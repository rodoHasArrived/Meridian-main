using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class FactoredAmortizationLedgerBridgeTests
{
    private static readonly Guid Security = Guid.Parse("dddddddd-1111-2222-3333-000000000001");
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 1, 11);
    private const string Ticker = "POOL";

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task DatedFactors_UsePrePaydownFace_AndRespectPartialDisposals(int parBasis)
    {
        var history = History(parBasis, partialSale: true);
        var ledger = new DomainLedger();
        var bridge = Bridge(Projection());
        await bridge.PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End));
        // 5 days at 8, 2 days at 6, then 3 days at 3 after the half disposal.
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(61m);
        ledger.Journal.Should().ContainSingle().Which.IsBalanced.Should().BeTrue();
        ledger.Journal.Should().NotContain(entry => entry.Metadata.ActivityType == "PrincipalPaydown");
        (await bridge.PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End))).Should().Be(0);
    }

    [Fact]
    public async Task ConsecutiveWindows_TieToWholeCoupon_AfterAHoldingChange()
    {
        var projection = Projection(scale: 0.0001m);
        var history = History(100, partialSale: true);
        var bridge = Bridge(projection);
        var full = new DomainLedger();
        await bridge.PostProjectedCashFlowsAsync(Security, Ticker, full, Context(history, Start, End));
        var daily = new DomainLedger();
        for (var day = Start; day <= End; day = day.AddDays(1))
            await bridge.PostProjectedCashFlowsAsync(Security, Ticker, daily,
                Context(history.Where(holding => holding.Date <= day).ToArray(), day, day));
        full.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(0.01m);
        daily.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker))
            .Should().Be(full.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)));
        daily.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public async Task FinalPaydown_EarnsLastDay_EvenWhenClosingFactorIsZero()
    {
        var original = Projection().Schedule.Single();
        var row = original with
        {
            Factor = 0m,
            PrincipalAmount = 60_000m,
            InterestSegments = [original.InterestSegments![0], original.InterestSegments[1] with { FactorAfter = 0m }]
        };
        var history = History(100).Select(holding => holding.Date == End
            ? holding with { Lot = holding.Lot.WithFactor(0m) } : holding).ToArray();
        var ledger = new DomainLedger();
        await Bridge(Projection() with { Schedule = [row] }).PostProjectedCashFlowsAsync(Security, Ticker, ledger,
            Context(history, Start, End));
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(70m);
        ledger.Journal.Should().ContainSingle();
    }

    [Fact]
    public async Task AcquisitionDay_IsExcluded_AndBookedFactorIsNotAppliedTwice()
    {
        var bought = Start.AddDays(7);
        var lot = new FaceValueLot("late", Security, bought, 6_000m, 1m, bookedFactor: 0.6m, parBasis: 1m);
        var history = Enumerable.Range(7, 4).Select(day => new FaceValueAccrualHolding(Start.AddDays(day), lot, 1m, 0.6m,
            AmortizationMethod: BondAmortizationMethod.StraightLine)).ToArray();
        var ledger = new DomainLedger();
        await Bridge(Projection()).PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End));
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(18m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrContradictoryEvidence_PostsNothing(bool missing)
    {
        var projection = Projection();
        if (missing)
            projection = projection with { Schedule = [projection.Schedule.Single() with { InterestSegments = null }] };
        var history = History(100).Select(holding => missing ? holding : holding with { AccrualFactor = 0.5m }).ToArray();
        var ledger = new DomainLedger();
        var act = () => Bridge(projection).PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End));
        await act.Should().ThrowAsync<InvalidOperationException>();
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosingFactorMismatch_BlocksBeforeAnyJournal()
    {
        var history = History(100).Select(holding => holding.Date == Start.AddDays(5)
            ? holding with { Lot = holding.Lot.WithFactor(0.8m) } : holding).ToArray();
        var ledger = new DomainLedger();
        var act = () => Bridge(Projection()).PostProjectedCashFlowsAsync(Security, Ticker, ledger,
            Context(history, Start, End));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closing factor*");
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task AmortizedThrough_ExcludesAlreadyRecognizedPremium()
    {
        var history = History(100).Select(holding => holding with
        {
            Lot = new FaceValueLot(holding.Lot.LotId, Security, holding.Lot.AcquiredDate,
                holding.Lot.OriginalFace, 110m, holding.Lot.BookedFactor, 100m, holding.Lot.CurrentFactor)
        }).ToArray();
        var ledger = new DomainLedger();
        await Bridge(Projection(0m)).PostProjectedCashFlowsAsync(Security, Ticker, ledger,
            Context(history, Start, End) with { AmortizedThrough = End });
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task NonTerminatingBalanceRatio_DoesNotRejectAValidPrincipalSegment()
    {
        var ratio = 2m / 3m;
        var projection = Projection();
        var row = projection.Schedule.Single() with
        {
            InterestAmount = 0.25m,
            Factor = ratio,
            InterestSegments =
            [
                new(Start, Start.AddDays(5), 3m, 0.15m, 1m, Start, End) { FactorAfter = ratio },
                new(Start.AddDays(5), End, 2m, 0.10m, ratio, Start, End) { FactorAfter = ratio }
            ]
        };
        projection = projection with { Schedule = [row], TermsUsed = projection.TermsUsed! with { PrincipalFace = 3m } };
        var history = Enumerable.Range(0, 11).Select(offset => new FaceValueAccrualHolding(Start.AddDays(offset),
            new FaceValueLot("thirds", Security, Start.AddDays(-1), 3m, 100m,
                currentFactor: offset < 5 ? 1m : ratio), 1m, offset <= 5 ? 1m : ratio,
                AmortizationMethod: BondAmortizationMethod.StraightLine)).ToArray();
        var ledger = new DomainLedger();
        await Bridge(projection).PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End));
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(0.25m);
    }

    [Fact]
    public async Task SelectedProjection_IsUsedWithoutRereadingTheSource()
    {
        var source = Substitute.For<ISecurityMasterCashFlowService>();
        var projection = Projection();
        var bridge = new SecurityMasterAmortizationLedgerBridge(source,
            NullLogger<SecurityMasterAmortizationLedgerBridge>.Instance);
        var ledger = new DomainLedger();
        await bridge.PostProjectedCashFlowsAsync(Security, Ticker, ledger,
            Context(History(100), Start, End) with { RetainedProjection = projection });
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(70m);
        source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(BondAmortizationMethod.NoAmortization, 0)]
    [InlineData(BondAmortizationMethod.StraightLine, 709.09)]
    public async Task RetainedPolicy_PreservesCouponsAndControlsPremiumAcrossConsecutiveWindows(
        BondAmortizationMethod method, decimal expectedPremium)
    {
        var history = History(100).Select(holding => holding with
        {
            Lot = new FaceValueLot(holding.Lot.LotId, Security, holding.Lot.AcquiredDate,
                holding.Lot.OriginalFace, 110m, holding.Lot.BookedFactor, 100m, holding.Lot.CurrentFactor),
            AmortizationMethod = method
        }).ToArray();
        var bridge = Bridge(Projection());
        var full = new DomainLedger();
        await bridge.PostProjectedCashFlowsAsync(Security, Ticker, full, Context(history, Start, End));
        var daily = new DomainLedger();
        for (var day = Start; day <= End; day = day.AddDays(1))
            await bridge.PostProjectedCashFlowsAsync(Security, Ticker, daily,
                Context(history.Where(holding => holding.Date <= day).ToArray(), day, day));

        // Premium is 800 for six of eleven lifetime days, then 600 for five days.
        full.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-expectedPremium);
        full.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(70m);
        daily.GetBalance(LedgerAccounts.Securities(Ticker)).Should().Be(-expectedPremium);
        daily.GetBalance(LedgerAccounts.AccruedInterestReceivable(Ticker)).Should().Be(70m);
        full.Journal.Should().OnlyContain(entry => entry.IsBalanced);
        daily.Journal.Should().OnlyContain(entry => entry.IsBalanced);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BondAmortizationMethod.ConstantYield)]
    [InlineData(BondAmortizationMethod.AuctionRate)]
    [InlineData(BondAmortizationMethod.PurchasedCreditImpaired)]
    public async Task MissingOrUnsupportedRetainedPolicy_PostsNothing(BondAmortizationMethod? method)
    {
        var history = History(100).Select(holding => holding with { AmortizationMethod = method }).ToArray();
        var ledger = new DomainLedger();
        var act = () => Bridge(Projection()).PostProjectedCashFlowsAsync(Security, Ticker, ledger, Context(history, Start, End));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit supported lot amortization policy*");
        ledger.Journal.Should().BeEmpty();
    }

    private static AmortizationLedgerPostingContext Context(IReadOnlyList<FaceValueAccrualHolding> history, DateOnly from, DateOnly through)
        => new(AccrualWindow: new(from, through), AccrualHoldings: history);

    private static FaceValueAccrualHolding[] History(int parBasis, bool partialSale = false)
        => Enumerable.Range(0, 11).Select(offset =>
        {
            var day = Start.AddDays(offset);
            var factor = offset <= 5 ? 0.8m : 0.6m;
            var lot = new FaceValueLot("pool", Security, Start.AddDays(-1), 8_000m, parBasis,
                bookedFactor: 0.8m, parBasis: parBasis, currentFactor: offset < 5 ? 0.8m : 0.6m);
            return new FaceValueAccrualHolding(day, lot, partialSale && offset >= 8 ? 0.5m : 1m, factor,
                AmortizationMethod: BondAmortizationMethod.StraightLine);
        }).ToArray();

    private static StructuredCashFlowProjectionDto Projection(decimal scale = 1m)
    {
        var row = new StructuredCashFlowScheduleEntry(new(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), 0m, 700m * scale, 0.6m)
        {
            RowVersion = 2,
            InterestMeasuredFrom = Start,
            InterestMeasuredTo = End,
            InterestSegments =
            [
                new(Start, Start.AddDays(5), 80_000m, 400m * scale, 0.8m, Start, End) { FactorAfter = 0.6m },
                new(Start.AddDays(5), End, 60_000m, 300m * scale, 0.6m, Start, End) { FactorAfter = 0.6m }
            ]
        };
        return new(Security, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base,
            DateTimeOffset.UtcNow, [row], StructuredCashFlowStaleness.Fresh,
            TermsUsed: StructuredCashFlowTerms.Empty with { PrincipalFace = 100_000m, MaturityDate = End, DayCountConvention = "ACT/365" });
    }

    private static SecurityMasterAmortizationLedgerBridge Bridge(StructuredCashFlowProjectionDto projection)
    {
        var source = Substitute.For<ISecurityMasterCashFlowService>();
        source.GetProjectionAsync(Security, StructuredCashFlowScenario.Base, Arg.Any<CancellationToken>()).Returns(projection);
        return new(source, NullLogger<SecurityMasterAmortizationLedgerBridge>.Instance);
    }
}
