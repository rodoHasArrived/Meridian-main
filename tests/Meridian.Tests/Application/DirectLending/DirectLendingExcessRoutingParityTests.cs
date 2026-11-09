using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// PRD-115 increment 3: the C# resolver (<see cref="DirectLendingServiceSupport.ResolveMixedPayment"/>,
/// used by the in-memory service) and the F# servicing aggregate (used by the Postgres service) must
/// route a payment's excess identically. The F# rule matrix lives in
/// <c>DirectLendingExcessRoutingTests.fs</c>; this pins the two lanes to each other.
/// </summary>
public sealed class DirectLendingExcessRoutingParityTests
{
    private static readonly DateOnly Origination = new(2026, 1, 15);
    private static readonly DateOnly PaymentDate = new(2026, 4, 1);

    public static TheoryData<ExcessPaymentApplication?, bool, decimal?, int, decimal, PaymentBreakdownDto?> Scenarios() => new()
    {
        // rule, prepayment allowed, penalty rate, interest-only months, amount, manual breakdown
        { null, true, 0.02m, 12, 1_500m, null },
        { ExcessPaymentApplication.HoldUnapplied, true, 0.02m, 0, 600_000m, new PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 400_000m) },
        { ExcessPaymentApplication.PrincipalPrepayment, true, 0.02m, 12, 251_000m, null },
        { ExcessPaymentApplication.PrincipalPrepayment, true, 0.0125m, 12, 1_234.57m, null },
        { ExcessPaymentApplication.PrincipalPrepayment, true, null, 0, 600_000m, new PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 400_000m) },
        { ExcessPaymentApplication.PrincipalPrepayment, false, 0.02m, 12, 1_500m, null },
        { ExcessPaymentApplication.PrincipalPrepayment, true, 0.02m, 0, 700_000m, null },
        { ExcessPaymentApplication.PrepaidInterest, true, 0.02m, 12, 1_600m, new PaymentBreakdownDto(1_000m, 0m, 0m, 0m, 0m) },
        { ExcessPaymentApplication.PrepaidInterest, true, 0.02m, 0, 1_000m, null },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void CSharpResolver_MatchesFSharpAggregate(
        ExcessPaymentApplication? rule, bool prepaymentAllowed, decimal? penaltyRate, int interestOnlyMonths, decimal amount, PaymentBreakdownDto? breakdown)
    {
        var terms = Terms(rule, prepaymentAllowed, penaltyRate, interestOnlyMonths);
        var servicing = LoanOwingInterest(terms);

        var fsharp = DirectLendingAggregateInterop.ApplyMixedPayment(
            servicing, terms, new ApplyMixedPaymentRequest(amount, PaymentDate, breakdown, "pay")).Resolution;
        var csharp = DirectLendingServiceSupport.ResolveMixedPayment(servicing, terms, amount, breakdown, PaymentDate);

        csharp.Should().Be(fsharp);
        (csharp.Breakdown.TotalAllocated + csharp.ExcessToPrincipal + csharp.ExcessToPrepaidInterest + csharp.UnappliedAmount)
            .Should().Be(amount, "every unit of cash is allocated or routed");
    }

    [Fact]
    public void CSharpWaterfall_DuringInterestOnlyPeriod_LeavesScheduledPrincipalAlone()
    {
        // Before PRD-115 the C# waterfall paid principal during the interest-only period while the
        // F# aggregate did not; the default rule now holds that cash as unapplied in both lanes.
        var terms = Terms(rule: null, prepaymentAllowed: true, penaltyRate: null, interestOnlyMonths: 12);

        var resolution = DirectLendingServiceSupport.ResolveMixedPayment(LoanOwingInterest(terms), terms, 5_000m, null, PaymentDate);

        resolution.Breakdown.ToPrincipal.Should().Be(0m);
        resolution.UnappliedAmount.Should().Be(4_000m);
    }

    [Fact]
    public async Task InMemoryService_AppliesRoutedExcessToBalancesLotsAndAllocations_AndReplaysIdentically()
    {
        var terms = Terms(ExcessPaymentApplication.PrincipalPrepayment, prepaymentAllowed: true, penaltyRate: 0.02m, interestOnlyMonths: 12);
        var service = new InMemoryDirectLendingService();
        var created = await service.CreateLoanAsync(new CreateLoanRequest(
            null, "Excess Routing Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms));
        await service.ActivateLoanAsync(created.LoanId, new ActivateLoanRequest(Origination));
        await service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(300_000m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), "wire-later"));
        await service.BookDrawdownAsync(created.LoanId, new BookDrawdownRequest(200_000m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "wire-earlier"));

        // Nothing is owed yet and it is the interest-only period, so the whole payment is excess.
        var servicing = await service.ApplyMixedPaymentAsync(created.LoanId, new ApplyMixedPaymentRequest(250_000m, PaymentDate, null, "pay"));

        servicing!.Balances.PrincipalOutstanding.Should().Be(250_000m);
        servicing.Balances.PenaltyAccruedUnpaid.Should().Be(5_000m);
        servicing.TotalDrawn.Should().Be(250_000m);
        servicing.DrawdownLots.Select(lot => lot.RemainingPrincipal).Should().Equal(0m, 250_000m);
        (await service.GetPaymentAllocationsAsync(created.LoanId))
            .Select(allocation => (allocation.TargetType, allocation.AllocatedAmount))
            .Should().Equal(("PrincipalPrepayment", 250_000m));

        var rebuilt = new DirectLendingEventRebuilder().Rebuild(created.LoanId, await service.GetHistoryAsync(created.LoanId));

        rebuilt!.Servicing.Balances.Should().Be(servicing.Balances);
        rebuilt.Servicing.DrawdownLots.Select(lot => lot.RemainingPrincipal).Should().Equal(0m, 250_000m);
    }

    private static DirectLendingTermsDto Terms(ExcessPaymentApplication? rule, bool prepaymentAllowed, decimal? penaltyRate, int interestOnlyMonths)
        => new(
            OriginationDate: Origination,
            MaturityDate: new DateOnly(2029, 1, 15),
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
            CommitmentFeeRate: null,
            DefaultRateSpreadBps: null,
            PrepaymentAllowed: prepaymentAllowed,
            CovenantsJson: null,
            InterestOnlyMonths: interestOnlyMonths,
            PrepaymentPenaltyRate: penaltyRate)
        {
            ExcessPaymentApplication = rule
        };

    /// <summary>Active loan with 200k drawn on 2026-02-01 and 300k on 2026-03-01, owing 1,000 of interest.</summary>
    private static LoanServicingStateDto LoanOwingInterest(DirectLendingTermsDto terms)
    {
        var created = DirectLendingAggregateInterop.CreateLoan(
            Guid.NewGuid(),
            new CreateLoanRequest(null, "Excess Routing Loan", new BorrowerInfoDto(Guid.NewGuid(), "Borrower", null), Origination, terms),
            new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        var activated = DirectLendingAggregateInterop.ActivateLoan(created.Contract, created.Servicing, new ActivateLoanRequest(Origination));
        var later = DirectLendingAggregateInterop.BookDrawdown(activated.Servicing, new BookDrawdownRequest(300_000m, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), "wire-later"));
        var earlier = DirectLendingAggregateInterop.BookDrawdown(later.Servicing, new BookDrawdownRequest(200_000m, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), "wire-earlier"));
        return earlier.Servicing with { Balances = earlier.Servicing.Balances with { InterestAccruedUnpaid = 1_000m } };
    }
}
