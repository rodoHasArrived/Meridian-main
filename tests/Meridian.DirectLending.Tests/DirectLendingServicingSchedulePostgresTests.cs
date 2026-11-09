using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;

namespace Meridian.DirectLending.Tests;

/// <summary>
/// Migration 011 end to end on PostgreSQL: the fixing store's revisions, a scheduled reset from a
/// stored fixing, deferred PIK accrual and capitalization, the normalized projection tables, and
/// rebuild from the retained event history.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DirectLendingServicingSchedulePostgresTests
{
    private static readonly DateOnly Origination = new(2026, 3, 23); // Monday

    [DirectLendingDatabaseFact]
    public async Task IndexFixingStore_KeepsRevisionsAndServesTheLatest()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;

        var first = await db.IndexFixingStore.RecordAsync(new RecordIndexFixingRequest("sofr", Origination, DeclaredRate.BasisPoints(431m), "feed"), 0.0431m);
        var second = await db.IndexFixingStore.RecordAsync(new RecordIndexFixingRequest("SOFR ", Origination, DeclaredRate.Percent(4.32m), "correction"), 0.0432m);

        first.Revision.Should().Be(1);
        second.Revision.Should().Be(2);
        var effective = await db.IndexFixingStore.GetEffectiveAsync("Sofr", Origination);
        effective!.Revision.Should().Be(2);
        effective.RateFraction.Should().Be(0.0432m);
        effective.DeclaredRate.Should().Be(DeclaredRate.Percent(4.32m));
        (await db.IndexFixingStore.ListAsync("SOFR")).Should().ContainSingle();
        (await db.IndexFixingStore.GetEffectiveAsync("SOFR", Origination.AddDays(1))).Should().BeNull();
    }

    [DirectLendingDatabaseFact]
    public async Task ScheduledResetAccrualAndCapitalization_PersistProjectionsAndRebuild()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
            return;

        var loan = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(loan!.LoanId, new ActivateLoanRequest(Origination));
        await db.Service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(1_000_000m, Origination, Origination, "wire"));

        // No fixing: fail closed.
        var missing = await db.CommandService.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(Origination));
        missing.Error!.Code.Should().Be(DirectLendingErrorCode.FixingMissing);

        // Lookback 2 business days from Monday 2026-03-23 -> Thursday 2026-03-19.
        var fixing = (await db.CommandService.RecordIndexFixingAsync(new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 3, 19), DeclaredRate.Percent(4.5m), "feed"))).Value!;
        (await db.IndexFixingStore.GetEffectiveAsync("SOFR", fixing.ObservationDate)).Should().Be(fixing, "the returned DTO round-trips exactly");
        var reset = await db.CommandService.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(Origination));
        reset.IsSuccess.Should().BeTrue(reset.Error?.Message);
        reset.Value!.CurrentRateReset!.AllInRate.Should().Be(0.07m); // 4.5% + 250 bp

        // Re-applying the same scheduled reset is a no-op.
        (await db.CommandService.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(Origination))).IsSuccess.Should().BeTrue();

        for (var date = Origination.AddDays(1); date <= new DateOnly(2026, 4, 23); date = date.AddDays(1))
        {
            var accrual = await db.CommandService.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(date));
            accrual.IsSuccess.Should().BeTrue(accrual.Error?.Message);
        }

        // 1,000,000 x 7% / 360 = 194.44 per day; PIK 40% = 77.78 per day; 31 days.
        var capitalization = await db.CommandService.CapitalizePikAsync(loan.LoanId, new CapitalizePikRequest(new DateOnly(2026, 4, 23)));
        capitalization.IsSuccess.Should().BeTrue(capitalization.Error?.Message);
        capitalization.Value!.Balances.PrincipalOutstanding.Should().Be(1_000_000m + 31 * 77.78m);
        capitalization.Value.Balances.InterestAccruedUnpaid.Should().Be(31 * (194.44m - 77.78m));
        capitalization.Value.PikInterestAccruedUnpaid.Should().Be(0m);

        // Normalized projections written in the same transaction.
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync();
            (await ScalarAsync(connection, $"select count(*) from {db.Schema}.loan_rate_reset_projection where loan_id = @loan_id and reset_kind = 'Scheduled'", loan.LoanId))
                .Should().Be(1L);
            (await ScalarAsync(connection, $"select amount from {db.Schema}.loan_pik_capitalization_projection where loan_id = @loan_id", loan.LoanId))
                .Should().Be(31 * 77.78m);
            (await ScalarAsync(connection, $"select count(*) from {db.Schema}.accrual_entry_projection where loan_id = @loan_id and pik_capitalization_deferred", loan.LoanId))
                .Should().Be(31L);
            (await ScalarAsync(connection, $"select last_pik_capitalization_date from {db.Schema}.loan_servicing_projection where loan_id = @loan_id", loan.LoanId))
                .Should().Be(new DateOnly(2026, 4, 23));
            (await ScalarAsync(connection, $"select rate_reset_schedule_json ->> 'indexName' from {db.Schema}.loan_terms_version where loan_id = @loan_id", loan.LoanId))
                .Should().Be("SOFR");
        }

        // Projection readers return the policies and schedule state.
        var projectedTerms = await db.QueryService.GetTermsVersionProjectionsAsync(loan.LoanId);
        projectedTerms.Single().Terms.RateResetSchedule.Should().BeEquivalentTo(loan.CurrentTerms.RateResetSchedule);
        projectedTerms.Single().Terms.PikCapitalization.Should().BeEquivalentTo(loan.CurrentTerms.PikCapitalization);
        var projectedServicing = await db.QueryService.GetServicingProjectionAsync(loan.LoanId);
        projectedServicing!.LastPikCapitalizationDate.Should().Be(new DateOnly(2026, 4, 23));
        var projectedReset = projectedServicing.RateResetHistory.Should().ContainSingle().Subject;
        projectedReset.FixingObservationDate.Should().Be(new DateOnly(2026, 3, 19));
        projectedReset.FixingId.Should().Be(fixing.FixingId);
        projectedReset.FixingRevision.Should().Be(1);

        // The retained history rebuilds to the stored state.
        var history = await db.QueryService.GetHistoryAsync(loan.LoanId);
        var rebuilt = db.Rebuilder.Rebuild(loan.LoanId, history);
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(capitalization.Value.Balances);
        rebuilt.Servicing.PikInterestAccruedUnpaid.Should().Be(0m);
        rebuilt.Servicing.CurrentRateReset.Should().BeEquivalentTo(capitalization.Value.CurrentRateReset);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, Guid loanId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("loan_id", loanId);
        return await command.ExecuteScalarAsync();
    }

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Fabrikam Unitranche",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Fabrikam Borrower", null),
            EffectiveDate: Origination,
            Terms: new DirectLendingTermsDto(
                OriginationDate: Origination,
                MaturityDate: new DateOnly(2029, 3, 23),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Floating,
                FixedAnnualRate: null,
                InterestIndexName: "SOFR",
                SpreadBps: 250m,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Monthly,
                AmortizationType: AmortizationType.Bullet,
                CommitmentFeeRate: null,
                DefaultRateSpreadBps: null,
                PrepaymentAllowed: true,
                CovenantsJson: null,
                SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(
                    DirectLendingPostgresTestDatabase.TestSecurityId,
                    DirectLendingPostgresTestDatabase.TestSecuritySymbol,
                    "integration-test-security-master",
                    "integration-test-approval",
                    "integration-test-ledger-map"))
            {
                RateResetSchedule = new FloatingRateResetSchedule(
                    "SOFR", RateResetFrequency.Quarterly, Origination, 2, LookbackDayKind.Business, DeclaredRate.BasisPoints(250m)),
                PikCapitalization = new PikCapitalizationPolicy(
                    PikElection.Always, DeclaredRate.Percent(40m), PikCapitalizationFrequency.OnInterestPaymentDate, new DateOnly(2026, 4, 23))
            });
}
