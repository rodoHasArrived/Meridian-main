using FluentAssertions;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

public sealed class DirectLendingPostgresIntegrationTests
{
    [Fact]
    public async Task PostgresService_ShouldPersistSchemaVersionedHistoryAndSnapshots()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(250_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 24), "wire-1"));

        var history = await db.Service.GetHistoryAsync(created.LoanId);
        var snapshotCount = await db.CountSnapshotsAsync(created.LoanId);
        var servicing = await db.Service.GetServicingProjectionAsync(created.LoanId);

        history.Should().HaveCount(3);
        history.Should().OnlyContain(static item => item.EventSchemaVersion == 1);
        snapshotCount.Should().BeGreaterThanOrEqualTo(2);
        servicing.Should().NotBeNull();
        servicing!.Balances.PrincipalOutstanding.Should().Be(250_000m);
    }

    [Fact]
    public async Task QueryService_ShouldRebuildFromHistory_WhenLiveStateRowIsMissing()
    {
        await using var db = await DirectLendingPostgresTestDatabase.CreateOrSkipAsync();
        if (db is null)
        {
            return;
        }

        var created = await db.Service.CreateLoanAsync(BuildCreateRequest());
        await db.Service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(new DateOnly(2026, 3, 22)));
        await db.Service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(150_000m, new DateOnly(2026, 3, 22), new DateOnly(2026, 3, 22), "wire-2"));
        await db.DeleteLiveStateAsync(created.LoanId);

        var rebuilt = await db.QueryService.LoadAggregateAsync(created.LoanId);

        rebuilt.Should().NotBeNull();
        rebuilt!.AggregateVersion.Should().Be(3);
        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(150_000m);
        rebuilt.Servicing.DrawdownLots.Should().ContainSingle();
    }

    private static CreateLoanRequest BuildCreateRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Fabrikam Senior Secured Loan",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Fabrikam Borrower", Guid.NewGuid()),
            EffectiveDate: new DateOnly(2026, 3, 22),
            Terms: new DirectLendingTermsDto(
                OriginationDate: new DateOnly(2026, 3, 22),
                MaturityDate: new DateOnly(2029, 3, 22),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Fixed,
                FixedAnnualRate: 0.08m,
                InterestIndexName: null,
                SpreadBps: null,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.InterestOnly,
                CommitmentFeeRate: 0.03m,
                DefaultRateSpreadBps: 200m,
                PrepaymentAllowed: true,
                CovenantsJson: "{\"leverage\": \"<= 4.5x\"}"));
}
