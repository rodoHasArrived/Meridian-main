using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.DirectLending.Tests;

/// <summary>
/// End-to-end golden scenario on the in-memory service: a floating SOFR loan with a quarterly reset
/// schedule (2 business-day lookback, 1% index floor) and 50% PIK capitalized quarterly. The event
/// history it records must rebuild to the same servicing state.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class DirectLendingServicingScheduleScenarioTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);

    [Fact]
    public async Task QuarterlyResetAndQuarterlyCapitalization_ProduceTheGoldenBalancesAndRebuild()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(Origination));
        await service.BookDrawdownAsync(loan.LoanId, new BookDrawdownRequest(1_000_000m, Origination, Origination, "wire"));

        // Fixings observed two weekdays before each reset date. 2026-01-15 is a Thursday -> 01-13.
        await service.RecordIndexFixingAsync(new RecordIndexFixingRequest("sofr", new DateOnly(2026, 1, 13), DeclaredRate.Percent(0.5m), "feed"));
        // 2026-04-15 is a Wednesday -> 04-13. Recorded twice: the correction (revision 2) wins.
        await service.RecordIndexFixingAsync(new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 4, 13), DeclaredRate.BasisPoints(400m), "feed"));
        var corrected = await service.RecordIndexFixingAsync(new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 4, 13), DeclaredRate.Percent(4.25m), "feed-correction"));
        corrected!.Revision.Should().Be(2);

        // First reset: max(0.5%, 1% floor) + 250 bp = 3.5%.
        var first = await service.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(Origination));
        first!.CurrentRateReset!.AllInRate.Should().Be(0.035m);
        first.CurrentRateReset.FixingObservationDate.Should().Be(new DateOnly(2026, 1, 13));

        // Accrue 2026-01-16 through 2026-04-15, capitalizing on the 2026-04-15 policy date.
        for (var date = Origination.AddDays(1); date <= new DateOnly(2026, 4, 15); date = date.AddDays(1))
        {
            if (date == new DateOnly(2026, 4, 15))
            {
                var second = await service.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(date));
                second!.CurrentRateReset!.AllInRate.Should().Be(0.0675m); // 4.25% corrected fixing + 250 bp
            }

            await service.PostDailyAccrualAsync(loan.LoanId, new PostDailyAccrualRequest(date));
        }

        var beforeCapitalization = await service.GetServicingStateAsync(loan.LoanId);
        // 89 days at 3.5%: 1,000,000 x 3.5% / 360 = 97.2222, posted as 97.22 per day (rounded to
        // cents, as the F# aggregate does); PIK 50% = 48.61 and cash 48.61 per day.
        // 2026-04-15 accrues at 6.75%: 187.50 per day, PIK 93.75, cash 93.75.
        // PIK = cash = 89 x 48.61 + 93.75 = 4,420.04; total interest = 8,840.08.
        beforeCapitalization!.PikInterestAccruedUnpaid.Should().Be(4_420.04m);
        beforeCapitalization.Balances.InterestAccruedUnpaid.Should().Be(4_420.04m);
        beforeCapitalization.Balances.PrincipalOutstanding.Should().Be(1_000_000m);

        var capitalized = await service.CapitalizePikAsync(loan.LoanId, new CapitalizePikRequest(new DateOnly(2026, 4, 15)));
        capitalized!.Balances.PrincipalOutstanding.Should().Be(1_000_000m + beforeCapitalization.PikInterestAccruedUnpaid);
        capitalized.PikInterestAccruedUnpaid.Should().Be(0m);
        capitalized.PikCapitalizations.Should().ContainSingle()
            .Which.Amount.Should().Be(beforeCapitalization.PikInterestAccruedUnpaid);
        capitalized.RateResetHistory.Should().HaveCount(2);

        // Capitalizing the same date again is idempotent.
        var again = await service.CapitalizePikAsync(loan.LoanId, new CapitalizePikRequest(new DateOnly(2026, 4, 15)));
        again!.Balances.PrincipalOutstanding.Should().Be(capitalized.Balances.PrincipalOutstanding);

        // The retained history rebuilds to the same servicing state.
        var history = await service.GetHistoryAsync(loan.LoanId);
        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loan.LoanId, history);
        rebuilt!.Servicing.Balances.Should().BeEquivalentTo(capitalized.Balances);
        rebuilt.Servicing.PikInterestAccruedUnpaid.Should().Be(capitalized.PikInterestAccruedUnpaid);
        rebuilt.Servicing.LastPikCapitalizationDate.Should().Be(new DateOnly(2026, 4, 15));
        rebuilt.Servicing.CurrentRateReset.Should().BeEquivalentTo(capitalized.CurrentRateReset);
        rebuilt.Servicing.RateResetHistory.Should().BeEquivalentTo(capitalized.RateResetHistory);
        rebuilt.Servicing.PikCapitalizations.Should().BeEquivalentTo(
            capitalized.PikCapitalizations,
            options => options.Excluding(item => item.RecordedAt));
    }

    [Fact]
    public async Task ScheduledReset_FailsClosedWhenTheFixingIsMissing()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildRequest());
        await service.ActivateLoanAsync(loan.LoanId, new ActivateLoanRequest(Origination));

        var act = () => service.ApplyScheduledRateResetAsync(loan.LoanId, new ApplyScheduledRateResetRequest(Origination));

        (await act.Should().ThrowAsync<DirectLendingCommandException>())
            .Which.Error.Code.Should().Be(DirectLendingErrorCode.FixingMissing);
    }

    [Fact]
    public async Task SchedulePreview_ListsResetsWithFixingAvailabilityAndCapitalizationDates()
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(BuildRequest());
        await service.RecordIndexFixingAsync(new RecordIndexFixingRequest("SOFR", new DateOnly(2026, 4, 13), DeclaredRate.Percent(4m), null));

        var preview = await service.GetServicingSchedulePreviewAsync(loan.LoanId, Origination, new DateOnly(2026, 7, 31));

        preview!.RateResets.Select(item => item.ResetDate).Should().Equal(Origination, new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 15));
        preview.RateResets.Select(item => item.FixingAvailable).Should().Equal(false, true, false);
        preview.PikCapitalizationDates.Should().Equal(new DateOnly(2026, 4, 15), new DateOnly(2026, 7, 15));
    }

    [Fact]
    public async Task CreateLoan_RejectsAScheduleWhoseUnitsDoNotNormalize()
    {
        var service = new InMemoryDirectLendingService();
        var request = BuildRequest();
        var invalid = request with
        {
            Terms = request.Terms with
            {
                RateResetSchedule = request.Terms.RateResetSchedule! with { Cap = DeclaredRate.Percent(-2m) }
            }
        };

        var act = () => service.CreateLoanAsync(invalid);

        await act.Should().ThrowAsync<DirectLendingCommandException>();
    }

    private static CreateLoanRequest BuildRequest() =>
        new(
            LoanId: Guid.NewGuid(),
            FacilityName: "Contoso Unitranche",
            Borrower: new BorrowerInfoDto(Guid.NewGuid(), "Contoso Holdings", null),
            EffectiveDate: Origination,
            Terms: new DirectLendingTermsDto(
                OriginationDate: Origination,
                MaturityDate: new DateOnly(2031, 1, 15),
                CommitmentAmount: 1_000_000m,
                BaseCurrency: CurrencyCode.USD,
                RateTypeKind: RateTypeKind.Floating,
                FixedAnnualRate: null,
                InterestIndexName: "SOFR",
                SpreadBps: 250m,
                FloorRate: null,
                CapRate: null,
                DayCountBasis: DayCountBasis.Act360,
                PaymentFrequency: PaymentFrequency.Quarterly,
                AmortizationType: AmortizationType.Bullet,
                CommitmentFeeRate: null,
                DefaultRateSpreadBps: null,
                PrepaymentAllowed: true,
                CovenantsJson: null)
            {
                RateResetSchedule = new FloatingRateResetSchedule(
                    "SOFR",
                    RateResetFrequency.Quarterly,
                    Origination,
                    2,
                    LookbackDayKind.Business,
                    DeclaredRate.BasisPoints(250m),
                    Floor: DeclaredRate.Percent(1m)),
                PikCapitalization = new PikCapitalizationPolicy(
                    PikElection.Always,
                    DeclaredRate.Percent(50m),
                    PikCapitalizationFrequency.Quarterly,
                    new DateOnly(2026, 4, 15))
            });
}
