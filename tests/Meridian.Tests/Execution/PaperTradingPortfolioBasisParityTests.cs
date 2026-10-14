using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Backtesting;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioBasisParityTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(PositionLotSelectionMethod.Fifo, false)]
    [InlineData(PositionLotSelectionMethod.Lifo, false)]
    [InlineData(PositionLotSelectionMethod.Hifo, false)]
    [InlineData(PositionLotSelectionMethod.Fifo, true)]
    [InlineData(PositionLotSelectionMethod.Lifo, true)]
    [InlineData(PositionLotSelectionMethod.Hifo, true)]
    public void FractionalClosingFills_ExhaustTheExactOpeningBasis(
        PositionLotSelectionMethod method, bool shortPosition)
    {
        // Each partial quantity-times-price product rounds away the final decimal quantum.
        // The closing journal must still relieve the exact basis retained by the opening fill.
        const decimal openingBasis = 1.0000000000000000000000000001m;
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(2m, ledger, lotSelectionMethod: method);
        var openingSide = shortPosition ? OrderSide.Sell : OrderSide.Buy;
        var closingSide = shortPosition ? OrderSide.Buy : OrderSide.Sell;
        var account = shortPosition
            ? LedgerAccounts.ShortSecuritiesPayable("BASIS")
            : LedgerAccounts.Securities("BASIS");
        portfolio.ApplyFill(Fill(openingSide, 1m, openingBasis, 0));

        portfolio.ApplyFill(Fill(closingSide, 0.3m, 1m, 1));
        portfolio.ApplyFill(Fill(closingSide, 0.3m, 1m, 2));
        portfolio.ApplyFill(Fill(closingSide, 0.4m, 1m, 3));

        var lines = ledger.Journal.SelectMany(entry => entry.Lines)
            .Where(line => line.Account == account).ToArray();
        var relievedBasis = lines.Sum(line => shortPosition ? line.Debit : line.Credit);
        relievedBasis.Should().Be(openingBasis);
        lines.Sum(line => line.Debit - line.Credit).Should().Be(0m);
        portfolio.RealisedPnl.Should().Be(shortPosition ? openingBasis - 1m : 1m - openingBasis);
        portfolio.GetPositionLots("BASIS").Should().BeEmpty();
        portfolio.Positions.Should().NotContainKey("BASIS");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialCloseThenAdditionalFill_PreservesBasisInTheRemainingPosition(bool shortPosition)
    {
        const decimal openingBasis = 1.0000000000000000000000000001m;
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(2m, ledger);
        var openingSide = shortPosition ? OrderSide.Sell : OrderSide.Buy;
        var closingSide = shortPosition ? OrderSide.Buy : OrderSide.Sell;
        portfolio.ApplyFill(Fill(openingSide, 1m, openingBasis, 0));
        portfolio.ApplyFill(Fill(closingSide, 0.3m, 1m, 1));
        portfolio.ApplyFill(Fill(openingSide, 0.3m, 1m, 2));

        portfolio.Positions["BASIS"].AverageCostBasis.Should().Be(openingBasis);
        portfolio.UnrealisedPnl.Should().Be(shortPosition ? openingBasis - 1m : 1m - openingBasis);
        portfolio.ApplyFill(Fill(closingSide, 1m, 1m, 3));
        portfolio.RealisedPnl.Should().Be(shortPosition ? openingBasis - 1m : 1m - openingBasis);
        Balance(ledger, shortPosition).Should().Be(0m);
    }

    [Theory]
    [InlineData(PositionLotSelectionMethod.Fifo, false)]
    [InlineData(PositionLotSelectionMethod.Lifo, false)]
    [InlineData(PositionLotSelectionMethod.Hifo, false)]
    [InlineData(PositionLotSelectionMethod.Fifo, true)]
    [InlineData(PositionLotSelectionMethod.Lifo, true)]
    [InlineData(PositionLotSelectionMethod.Hifo, true)]
    public async Task NativeSplit_AfterPartialRelief_PreservesEveryRemainingLotBasis(
        PositionLotSelectionMethod method, bool shortPosition)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var adjuster = Adjuster(Action("StockSplit", splitRatio: 3m));
        var portfolio = new PaperTradingPortfolio(10m, ledger, adjuster, method);
        var openingSide = shortPosition ? OrderSide.Sell : OrderSide.Buy;
        var closingSide = shortPosition ? OrderSide.Buy : OrderSide.Sell;
        portfolio.ApplyFill(Fill(openingSide, 1m, 1m, 0));
        portfolio.ApplyFill(Fill(openingSide, 1m, 2m, 1));
        var originalIds = portfolio.GetPositionLots("BASIS").Select(lot => lot.LotId).ToArray();
        portfolio.ApplyFill(Fill(closingSide, 0.3m, 1m, 2));

        await portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);

        var lots = portfolio.GetPositionLots("BASIS");
        lots.Select(lot => lot.LotId).Should().Equal(originalIds);
        lots.Sum(lot => lot.OpenQuantity).Should().Be(5.1m);
        portfolio.ApplyFill(Fill(closingSide, 1.1m, 1m, 3));
        portfolio.ApplyFill(Fill(closingSide, 4m, 1m, 4));

        Balance(ledger, shortPosition).Should().Be(0m);
        portfolio.RealisedPnl.Should().Be(shortPosition ? -2.4m : 2.4m);
        portfolio.GetPositionLots("BASIS").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeThreeForOneSplit_FinalCloseRelievesTheUnitQuoteRemainder(bool shortPosition)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(2m, ledger, Adjuster(Action("StockSplit", splitRatio: 3m)));
        portfolio.ApplyFill(Fill(shortPosition ? OrderSide.Sell : OrderSide.Buy, 1m, 1m, 0));
        await portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);

        for (var index = 1; index <= 3; index++)
            portfolio.ApplyFill(Fill(shortPosition ? OrderSide.Buy : OrderSide.Sell, 1m, 1m / 3m, index));

        Balance(ledger, shortPosition).Should().Be(0m);
        var loss = 1m - 3m * (1m / 3m);
        portfolio.RealisedPnl.Should().Be(shortPosition ? loss : -loss);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePaydown_ApportionsTheExactRemainingBasisAcrossLots(bool shortPosition)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(10m, ledger, Adjuster(Action("PrincipalPaydown", distributionRatio: 0.4m)));
        var openingSide = shortPosition ? OrderSide.Sell : OrderSide.Buy;
        var closingSide = shortPosition ? OrderSide.Buy : OrderSide.Sell;
        portfolio.ApplyFill(Fill(openingSide, 1m, 1m, 0));
        portfolio.ApplyFill(Fill(openingSide, 1m, 2m, 1));

        await portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);
        portfolio.ApplyFill(Fill(closingSide, 1m, 1m, 2));
        portfolio.ApplyFill(Fill(closingSide, 1m, 1m, 3));

        // This compatibility projection does not post the separate paydown journal.
        Balance(ledger, shortPosition).Should().Be(shortPosition ? -0.8m : 0.8m);
        portfolio.RealisedPnl.Should().Be(shortPosition ? 0.2m : -0.2m);
    }

    [Fact]
    public async Task NativeSplit_BelowQuantityPrecision_RefusesWithoutDiscardingBasis()
    {
        var portfolio = new PaperTradingPortfolio(1m, corporateActionAdjuster: Adjuster(Action("StockSplit", splitRatio: 0.1m)));
        portfolio.ApplyFill(Fill(OrderSide.Buy, 0.0000000000000000000000000001m, 1m, 0));
        var original = portfolio.GetPositionLots("BASIS");

        Func<Task> apply = () => portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);

        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*decimal precision*");
        portfolio.GetPositionLots("BASIS").Should().BeEquivalentTo(original);
        portfolio.RealisedPnl.Should().Be(0m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSplitThenPaydown_ReducesRetainedTotalWithoutRebuildingTheUnitQuote(bool shortPosition)
    {
        const decimal openingBasis = 1.0000000000000000000000000001m;
        var ledger = new Meridian.Ledger.Ledger();
        var adjuster = Adjuster(Action("StockSplit", splitRatio: 3m),
            Action("PrincipalPaydown", distributionRatio: 0.1m, day: 7));
        var portfolio = new PaperTradingPortfolio(2m, ledger, adjuster);
        portfolio.ApplyFill(Fill(shortPosition ? OrderSide.Sell : OrderSide.Buy, 1m, openingBasis, 0));

        await portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);
        portfolio.ApplyFill(Fill(shortPosition ? OrderSide.Buy : OrderSide.Sell, 3m, 1m, 1));

        var account = shortPosition ? LedgerAccounts.ShortSecuritiesPayable("BASIS") : LedgerAccounts.Securities("BASIS");
        var relieved = ledger.Journal.SelectMany(entry => entry.Lines).Where(line => line.Account == account)
            .Sum(line => shortPosition ? line.Debit : line.Credit);
        relieved.Should().Be(openingBasis - 0.3m);
        portfolio.GetPositionLots("BASIS").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdjustmentAwaitingCompletion_DoesNotReplaceANewerLotBook(bool replacePosition)
    {
        var adjuster = new DeferredAdjuster();
        var portfolio = new PaperTradingPortfolio(10m, corporateActionAdjuster: adjuster);
        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 1m, 0));
        var pending = portfolio.ApplyCorporateActionsAsync("BASIS", OpenedAt);
        await adjuster.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Both paths return to the same quantity and unit cost, but have newer lot identity.
        portfolio.ApplyFill(Fill(replacePosition ? OrderSide.Sell : OrderSide.Buy, 1m, 1m, 1));
        portfolio.ApplyFill(Fill(replacePosition ? OrderSide.Buy : OrderSide.Sell, 1m, 1m, 2));
        var currentIds = portfolio.GetPositionLots("BASIS").Select(lot => lot.LotId).ToArray();
        adjuster.Completion.SetResult(new PositionCorporateActionAdjustment("BASIS", 1m, 3m, 1m, 1m / 3m, 1)
        {
            AdjustedTotalCostBasis = 1m
        });
        await pending;

        var lots = portfolio.GetPositionLots("BASIS");
        lots.Sum(lot => lot.OpenQuantity).Should().Be(1m);
        lots.Select(lot => lot.LotId).Should().Equal(currentIds);
    }

    [Fact]
    public async Task RetainedBasisOverload_PreservesLegacyAdjusterCompatibilityWithoutClaimingExactEvidence()
    {
        ILivePositionCorporateActionAdjuster adjuster = new LegacyAdjuster();
        var result = await adjuster.AdjustPositionAsync("BASIS", 1m, 1m, 1m, OpenedAt);

        result.AdjustedQuantity.Should().Be(2m);
        result.AdjustedTotalCostBasis.Should().BeNull();
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("AdjustedTotalCostBasis");
    }

    private static decimal Balance(Meridian.Ledger.Ledger ledger, bool shortPosition)
    {
        var account = shortPosition ? LedgerAccounts.ShortSecuritiesPayable("BASIS") : LedgerAccounts.Securities("BASIS");
        return ledger.Journal.SelectMany(entry => entry.Lines).Where(line => line.Account == account)
            .Sum(line => line.Debit - line.Credit);
    }

    private static CorporateActionAdjustmentService Adjuster(params CorporateActionDto[] actions)
    {
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(SecurityId));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorporateActionDto>>(actions));
        return new CorporateActionAdjustmentService(query, resolver, NullLogger<CorporateActionAdjustmentService>.Instance);
    }

    private static CorporateActionDto Action(string eventType, decimal? splitRatio = null, decimal? distributionRatio = null, int day = 6) =>
        new(Guid.NewGuid(), SecurityId, eventType, new DateOnly(2026, 1, day), null, null, "USD", splitRatio,
            null, distributionRatio, null, null, null, null, LifecycleState: CorporateActionLifecycleStates.Confirmed);

    private sealed class LegacyAdjuster : ILivePositionCorporateActionAdjuster
    {
        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(string ticker, decimal quantity,
            decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default) =>
            Task.FromResult(new PositionCorporateActionAdjustment(ticker, quantity, quantity * 2m, costBasis, costBasis / 2m, 1));
    }

    private sealed class DeferredAdjuster : ILivePositionCorporateActionAdjuster
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PositionCorporateActionAdjustment> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(string ticker, decimal quantity,
            decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default) =>
            throw new InvalidOperationException("The paper portfolio must supply its retained total basis.");

        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(string ticker, decimal quantity,
            decimal costBasis, decimal totalCostBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
        {
            totalCostBasis.Should().Be(1m);
            Started.SetResult();
            return Completion.Task;
        }
    }

    private static ExecutionReport Fill(OrderSide side, decimal quantity, decimal price, int sequence) => new()
    {
        OrderId = $"basis-fill-{sequence}",
        ReportType = ExecutionReportType.Fill,
        Symbol = "BASIS",
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Timestamp = OpenedAt.AddMinutes(sequence)
    };
}
