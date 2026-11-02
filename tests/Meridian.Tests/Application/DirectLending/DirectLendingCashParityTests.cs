using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;
using Xunit;

namespace Meridian.Tests.Application.DirectLending;

public sealed class DirectLendingCashParityTests
{
    private static readonly DateOnly PaymentDate = new(2026, 4, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessPrincipal_WithDrawdownsBookedOutOfOrder_UsesTheSameOldestLotAsFSharpAndReplay(bool manual)
    {
        var terms = Terms(ExcessPaymentApplication.PrincipalPrepayment) with { PrepaymentPenaltyRate = 0.02m };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(300_000m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3), "later-booked-first"));
        await service.BookDrawdownAsync(loanId, new(200_000m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3), "earlier-booked-second"));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var request = new ApplyMixedPaymentRequest(100_000m, PaymentDate,
            manual ? new PaymentBreakdownDto(0m, 0m, 0m, 0m, 25_000m) : null, "fifo-excess");
        var canonical = DirectLendingAggregateInterop.ApplyMixedPayment(before, terms, request);

        var actual = (await service.ApplyMixedPaymentAsync(loanId, request))!;

        actual.DrawdownLots.Single(lot => lot.ExternalRef == "earlier-booked-second").RemainingPrincipal.Should().Be(100_000m);
        actual.DrawdownLots.Single(lot => lot.ExternalRef == "later-booked-first").RemainingPrincipal.Should().Be(300_000m);
        actual.DrawdownLots.Should().BeEquivalentTo(canonical.Servicing.DrawdownLots, options => options.WithStrictOrdering());
        actual.Balances.PrincipalOutstanding.Should().Be(400_000m);
        actual.Balances.PenaltyAccruedUnpaid.Should().Be(manual ? 1_500m : 2_000m,
            "the assessment applies only to excess principal, after the base allocation");
        actual.Balances.Should().BeEquivalentTo(canonical.Servicing.Balances);
        actual.ServicingRevision.Should().Be(before.ServicingRevision + 2);

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId))!;
        rebuilt.Servicing.DrawdownLots.Should().BeEquivalentTo(actual.DrawdownLots, options => options.WithStrictOrdering());
        rebuilt.Servicing.Balances.Should().BeEquivalentTo(actual.Balances);
        rebuilt.Servicing.ServicingRevision.Should().Be(actual.ServicingRevision);
        rebuilt.Servicing.RevisionHistory.Select(item => item.Notes)
            .Should().Equal(actual.RevisionHistory.Select(item => item.Notes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepaidConsumption_UsesTheContractualEffectiveRateBoundsLikeFSharpAndReplay(bool useFloor)
    {
        var terms = Terms(ExcessPaymentApplication.PrepaidInterest) with
        {
            FixedAnnualRate = 0.08m,
            EffectiveRateFloor = useFloor ? 0.10m : null,
            EffectiveRateCap = useFloor ? null : 0.06m
        };
        var (service, loanId) = await CreateAsync(terms);
        await service.BookDrawdownAsync(loanId, new(500_000m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), "bounded-interest"));
        await service.ApplyMixedPaymentAsync(loanId, new(300m, PaymentDate, null, "prepaid-bounded-interest"));
        var before = (await service.GetServicingStateAsync(loanId))!;
        var request = new PostDailyAccrualRequest(PaymentDate.AddDays(1));
        var canonical = DirectLendingAggregateInterop.PostDailyAccrual(before, terms, request);

        var actual = (await service.PostDailyAccrualAsync(loanId, request))!;
        var after = (await service.GetServicingStateAsync(loanId))!;

        // 500,000 * 6% / 360 = 83.333...; 500,000 * 10% / 360 = 138.888....
        var expectedInterest = useFloor ? 138.89m : 83.33m;
        actual.AnnualRateApplied.Should().Be(useFloor ? 0.10m : 0.06m);
        actual.InterestAmount.Should().Be(expectedInterest);
        actual.PikInterestAmount.Should().Be(0m);
        actual.PrepaidInterestConsumed.Should().Be(expectedInterest);
        actual.PrepaidInterestReleases.Should().BeEquivalentTo(new[]
        {
            new PrepaidInterestReleaseDto(before.Balances.PrepaidInterestLots!.Single().CashTransactionId, expectedInterest)
        });
        after.Balances.InterestAccruedUnpaid.Should().Be(0m);
        after.Balances.PrepaidInterest.Should().Be(useFloor ? 161.11m : 216.67m);
        actual.AnnualRateApplied.Should().Be(canonical.Entry.AnnualRateApplied);
        actual.InterestAmount.Should().Be(canonical.Entry.InterestAmount);
        actual.PrepaidInterestConsumed.Should().Be(canonical.Entry.PrepaidInterestConsumed);
        after.Balances.Should().BeEquivalentTo(canonical.Servicing.Balances);

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(loanId, await service.GetHistoryAsync(loanId))!;
        rebuilt.Servicing.Balances.Should().BeEquivalentTo(after.Balances);
        rebuilt.Servicing.AccrualEntries.Single().AnnualRateApplied.Should().Be(actual.AnnualRateApplied);
        rebuilt.Servicing.AccrualEntries.Single().PrepaidInterestConsumed.Should().Be(expectedInterest);
    }

    private static DirectLendingTermsDto Terms(ExcessPaymentApplication application) =>
        DirectLendingTermsHashCharacterizationTests.FixedBulletTerms() with
        {
            CommitmentAmount = 1_000_000m,
            FixedAnnualRate = 0.08m,
            CommitmentFeeRate = null,
            InterestOnlyMonths = 12,
            ExcessPaymentApplication = application
        };

    private static async Task<(InMemoryDirectLendingService Service, Guid LoanId)> CreateAsync(DirectLendingTermsDto terms)
    {
        var service = new InMemoryDirectLendingService();
        var loan = await service.CreateLoanAsync(new(null, "Synthetic parity facility",
            new(Guid.NewGuid(), "Synthetic borrower", null), terms.OriginationDate, terms));
        await service.ActivateLoanAsync(loan.LoanId, new(terms.OriginationDate));
        return (service, loan.LoanId);
    }
}
