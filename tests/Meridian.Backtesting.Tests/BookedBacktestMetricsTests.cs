using FluentAssertions;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;

namespace Meridian.Backtesting.Tests;

public sealed class BookedBacktestMetricsTests
{
    private static readonly DateTimeOffset Start = new(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2024, 1, 2);

    [Fact]
    public void ShortCoverAndLongSale_CountBookedOutcomesIncludingTheirFees()
    {
        var request = Request();
        var portfolio = Portfolio(request);
        FillEvent[] fills =
        [
            Fill("SHORT", -10, 100m, 3m),
            Fill("SHORT", 10, 90m, 3m, minute: 1),
            Fill("LONG", 10, 100m, 2m, minute: 2),
            Fill("LONG", -10, 95m, 2m, minute: 3)
        ];
        Apply(portfolio, fills);

        var metrics = Compute(portfolio, fills, request);

        metrics.TotalTrades.Should().Be(2);
        metrics.WinningTrades.Should().Be(1);
        metrics.LosingTrades.Should().Be(1);
        metrics.WinRate.Should().Be(0.5);
        metrics.ProfitFactor.Should().BeApproximately(94d / 54d, 1e-12);
        metrics.NetPnl.Should().Be(40m);
        metrics.SymbolAttribution.Values.Sum(value => value.RealizedPnl).Should().Be(50m);
        portfolio.GetClosedLots().Sum(lot => lot.EntryCommission + lot.ExitCommission).Should().Be(10m);
    }

    [Fact]
    public void AccountsWithTheSameOrderId_KeepSeparateClosingOutcomes()
    {
        var first = FinancialAccount.CreateDefaultBrokerage(10_000m, 0d, 0d);
        var second = first with { AccountId = "account-b" };
        var request = Request([first, second]);
        var portfolio = Portfolio(request);
        var closingOrder = Guid.NewGuid();
        FillEvent[] fills =
        [
            Fill("SPY", 10, 100m, account: first.AccountId),
            Fill("SPY", 10, 120m, minute: 1, account: second.AccountId),
            Fill("SPY", -10, 110m, minute: 2, account: second.AccountId, order: closingOrder),
            Fill("SPY", -10, 120m, minute: 3, account: first.AccountId, order: closingOrder)
        ];
        Apply(portfolio, fills);

        var metrics = Compute(portfolio, fills, request);

        metrics.TotalTrades.Should().Be(2);
        metrics.WinRate.Should().Be(0.5);
        metrics.ProfitFactor.Should().Be(2d);
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(100m);
    }

    [Fact]
    public void SpecificLotSelection_UsesRetainedClosureInsteadOfReplayingFifo()
    {
        var account = FinancialAccount.CreateDefaultBrokerage(10_000m, 0d, 0d) with
        {
            Rules = new FinancialAccountRules(LotSelection: LotSelectionMethod.SpecificId)
        };
        var request = Request([account]);
        var portfolio = Portfolio(request);
        var fills = new List<FillEvent> { Fill("SPY", 10, 100m), Fill("SPY", 10, 120m, minute: 1) };
        Apply(portfolio, fills);
        var selectedLot = portfolio.GetOpenLots().Last();
        var close = Fill("SPY", -10, 110m, minute: 2) with { TargetLotId = selectedLot.LotId };
        Apply(portfolio, [close]);
        fills.Add(close);

        var metrics = Compute(portfolio, fills, request);

        metrics.TotalTrades.Should().Be(1);
        metrics.LosingTrades.Should().Be(1);
        metrics.WinningTrades.Should().Be(0);
        metrics.SymbolAttribution["SPY"].RealizedPnl.Should().Be(-100m);
    }

    [Fact]
    public void PartialClosingFills_AggregateBookedSlicesIntoOneOrderOutcome()
    {
        var request = Request();
        var portfolio = Portfolio(request);
        var closeOrder = Guid.NewGuid();
        FillEvent[] fills =
        [
            Fill("SPY", 2, 100m, 2m),
            Fill("SPY", -1, 90m, 2m, minute: 1, order: closeOrder),
            Fill("SPY", -1, 120m, 2m, minute: 2, order: closeOrder)
        ];
        Apply(portfolio, fills);

        var metrics = Compute(portfolio, fills, request);

        portfolio.GetClosedLots().Should().HaveCount(2);
        metrics.TotalTrades.Should().Be(1);
        metrics.WinningTrades.Should().Be(1);
        metrics.NetPnl.Should().Be(4m);
    }

    [Fact]
    public void BreakevenClosure_CountsAsACompletedTrade()
    {
        var request = Request();
        var portfolio = Portfolio(request);
        FillEvent[] fills = [Fill("SPY", 10, 100m), Fill("SPY", -10, 100m, minute: 1)];
        Apply(portfolio, fills);

        var metrics = Compute(portfolio, fills, request);

        metrics.TotalTrades.Should().Be(1);
        metrics.WinningTrades.Should().Be(0);
        metrics.LosingTrades.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossingFill_AllocatesItsFeeBetweenTheClosureAndOppositeNewLot(bool startsShort)
    {
        var request = Request();
        var portfolio = Portfolio(request);
        FillEvent[] fills = startsShort
            ? [Fill("SPY", -1, 100m, 1m), Fill("SPY", 3, 90m, 3m, minute: 1),
                Fill("SPY", -2, 100m, 2m, minute: 2)]
            : [Fill("SPY", 1, 100m, 1m), Fill("SPY", -3, 110m, 3m, minute: 1),
                Fill("SPY", 2, 100m, 2m, minute: 2)];
        Apply(portfolio, fills);

        var metrics = Compute(portfolio, fills, request);

        metrics.TotalTrades.Should().Be(2);
        metrics.WinningTrades.Should().Be(2);
        metrics.NetPnl.Should().Be(24m);
        portfolio.GetClosedLots().Sum(lot => lot.EntryCommission + lot.ExitCommission).Should().Be(6m);
        portfolio.GetOpenLots().Should().BeEmpty();
    }

    [Fact]
    public void ReverseSplitAndRename_IncludeFractionalDisposalAndSuccessorClosure()
    {
        var request = Request();
        var ledger = new BacktestLedger();
        var portfolio = Portfolio(request, ledger);
        var entry = Fill("OLD", 3, 100m, 3m);
        Apply(portfolio, [entry]);
        portfolio.ApplyAssetEvent(new AssetEvent(Start.AddMinutes(1), "OLD", AssetEventType.Merger,
            PositionFactor: 0.5m, TargetSymbol: "NEW", ReferencePrice: 160m));
        var exit = Fill("NEW", -1, 250m, 4m, minute: 2);
        Apply(portfolio, [exit]);

        var metrics = Compute(portfolio, [entry, exit], request);

        // Cash in lieu: 80 cash - 100 basis - 1 acquisition fee = -21.
        // Successor disposal: 250 proceeds - 200 retained basis - 2 entry fee - 4 exit fee = 44.
        metrics.TotalTrades.Should().Be(2);
        metrics.WinRate.Should().Be(0.5);
        metrics.ProfitFactor.Should().BeApproximately(44d / 21d, 1e-12);
        metrics.NetPnl.Should().Be(23m);
        metrics.SymbolAttribution["OLD"].RealizedPnl.Should().Be(-20m);
        metrics.SymbolAttribution["NEW"].RealizedPnl.Should().Be(50m);
        metrics.SymbolAttribution.Values.Sum(value => value.RealizedPnl).Should().Be(
            ledger.GetBalance(LedgerAccounts.RealizedGainFor(request.DefaultBrokerageAccountId))
            - ledger.GetBalance(LedgerAccounts.RealizedLossFor(request.DefaultBrokerageAccountId)));
    }

    [Theory]
    [InlineData(5, 50)]
    [InlineData(-5, -50)]
    public void CashAndStockMerger_AttributesCashConsiderationOrFeesToTheBookedSourceSymbol(
        int cashPerShare, int pnl)
    {
        var request = Request();
        var ledger = new BacktestLedger();
        var portfolio = Portfolio(request, ledger);
        var entry = Fill("OLD", 10, 100m);
        Apply(portfolio, [entry]);
        portfolio.ApplyAssetEvent(new AssetEvent(Start.AddMinutes(1), "OLD", AssetEventType.Merger,
            CashPerShare: cashPerShare, TargetSymbol: "NEW", ReferencePrice: 100m));
        var exit = Fill("NEW", -10, 100m, minute: 2);
        Apply(portfolio, [exit]);

        var metrics = Compute(portfolio, [entry, exit], request);

        metrics.SymbolAttribution["OLD"].RealizedPnl.Should().Be(pnl);
        metrics.SymbolAttribution["NEW"].RealizedPnl.Should().Be(0m);
        var postedPnl = ledger.GetBalance(LedgerAccounts.CorporateActionIncomeFor(request.DefaultBrokerageAccountId))
            - ledger.GetBalance(LedgerAccounts.CorporateActionExpenseFor(request.DefaultBrokerageAccountId));
        metrics.SymbolAttribution.Values.Sum(value => value.RealizedPnl).Should().Be(postedPnl);
        metrics.GrossPnl.Should().Be(postedPnl);
        metrics.NetPnl.Should().Be(pnl);
        metrics.TotalTrades.Should().Be(1, "the per-share cash payment is not an additional lot disposal");
    }

    [Theory]
    [InlineData(false, 17)]
    [InlineData(true, -23)]
    public void AllFractionTransformation_PreservesDisposalWhenNoPositionRemains(bool isShort, int netPnl)
    {
        var request = Request();
        var portfolio = Portfolio(request);
        var fill = Fill("OLD", isShort ? -1 : 1, 100m, 3m);
        Apply(portfolio, [fill]);
        portfolio.ApplyAssetEvent(new AssetEvent(Start.AddMinutes(1), "OLD", AssetEventType.ReverseSplit,
            PositionFactor: 0.5m, TargetSymbol: "NEW", ReferencePrice: 240m));

        var metrics = Compute(portfolio, [fill], request);

        portfolio.GetClosedLots().Should().BeEmpty();
        portfolio.GetCurrentPositions().Should().BeEmpty();
        metrics.TotalTrades.Should().Be(1);
        metrics.NetPnl.Should().Be(netPnl);
        metrics.SymbolAttribution["OLD"].RealizedPnl.Should().Be(isShort ? -20m : 20m);
        metrics.WinningTrades.Should().Be(isShort ? 0 : 1);
        metrics.LosingTrades.Should().Be(isShort ? 1 : 0);
    }

    [Fact]
    public void CompositeSuccessor_CarriesAllSourceEntryFeesThroughSpecificLotClosure()
    {
        var account = FinancialAccount.CreateDefaultBrokerage(10_000m, 0d, 0d) with
        {
            Rules = new FinancialAccountRules(LotSelection: LotSelectionMethod.SpecificId)
        };
        var request = Request([account]);
        var portfolio = Portfolio(request);
        var fills = new List<FillEvent> { Fill("XYZ", 3, 100m, 3m), Fill("XYZ", 1, 200m, 2m, minute: 1) };
        Apply(portfolio, fills);
        portfolio.ApplyAssetEvent(new AssetEvent(Start.AddMinutes(2), "XYZ", AssetEventType.ReverseSplit,
            PositionFactor: 0.5m, ReferencePrice: 300m));
        var composite = portfolio.GetOpenLots().Single(lot => lot.BasisComponents.Count == 2);
        composite.EntryCommission.Should().Be(3m);
        var exit = Fill("XYZ", -1, 330m, 1m, minute: 3) with { TargetLotId = composite.LotId };
        Apply(portfolio, [exit]);
        fills.Add(exit);

        var metrics = Compute(portfolio, fills, request);

        var closed = portfolio.GetClosedLots().Should().ContainSingle().Subject;
        closed.RealizedPnl.Should().Be(30m);
        closed.EntryCommission.Should().Be(3m);
        closed.ExitCommission.Should().Be(1m);
        portfolio.GetOpenLots().Single().EntryCommission.Should().Be(2m);
        metrics.SymbolAttribution["XYZ"].RealizedPnl.Should().Be(30m);
        metrics.WinningTrades.Should().Be(1);
    }

    [Fact]
    public void SuccessorClosure_UsesExactBookedBasisRatherThanRoundedEntryQuote()
    {
        var request = Request();
        var portfolio = Portfolio(request);
        var entry = Fill("XYZ", 4, 25m);
        Apply(portfolio, [entry]);
        portfolio.ApplyAssetEvent(new AssetEvent(Start.AddMinutes(1), "XYZ", AssetEventType.Merger,
            PositionFactor: 0.75m, ReferencePrice: 40m));
        var exit = Fill("XYZ", -3, 40m, minute: 2);
        Apply(portfolio, [exit]);

        var metrics = Compute(portfolio, [entry, exit], request);

        portfolio.GetClosedLots().Sum(lot => lot.RealizedPnl).Should().Be(20m);
        metrics.SymbolAttribution["XYZ"].RealizedPnl.Should().Be(20m);
        metrics.GrossPnl.Should().Be(20m);
    }

    [Fact]
    public void OppositePositionsAcrossAccounts_KeepUnrealizedAttributionWhenQuantityNetsToZero()
    {
        var first = FinancialAccount.CreateDefaultBrokerage(10_000m, 0d, 0d);
        var second = first with { AccountId = "account-b" };
        var request = Request([first, second]);
        var portfolio = Portfolio(request);
        FillEvent[] fills = [Fill("SPY", 10, 100m, account: first.AccountId),
            Fill("SPY", -10, 120m, minute: 1, account: second.AccountId)];
        Apply(portfolio, fills);
        portfolio.UpdateLastPrice("SPY", 110m);

        var metrics = Compute(portfolio, fills, request);

        portfolio.GetCurrentPositions().Should().BeEmpty();
        metrics.SymbolAttribution["SPY"].UnrealizedPnl.Should().Be(200m);
        metrics.TotalTrades.Should().Be(0);
    }

    private static BacktestRequest Request(IReadOnlyList<FinancialAccount>? accounts = null) =>
        new(From: Date, To: Date, InitialCash: accounts?.Sum(account => account.InitialCash) ?? 10_000m,
            Accounts: accounts);

    private static SimulatedPortfolio Portfolio(BacktestRequest request, BacktestLedger? ledger = null) =>
        new(request.ResolveAccounts(), request.DefaultBrokerageAccountId, new FixedCommissionModel(0m), ledger, Start);

    private static FillEvent Fill(string symbol, long quantity, decimal price, decimal commission = 0m,
        int minute = 0, string? account = null, Guid? order = null) =>
        new(Guid.NewGuid(), order ?? Guid.NewGuid(), symbol, quantity, price, commission, Start.AddMinutes(minute), account);

    private static void Apply(SimulatedPortfolio portfolio, IEnumerable<FillEvent> fills)
    {
        foreach (var fill in fills)
        {
            portfolio.ProcessFill(fill);
            portfolio.UpdateLastPrice(fill.Symbol, fill.FillPrice);
        }
    }

    private static BacktestMetrics Compute(SimulatedPortfolio portfolio, IReadOnlyList<FillEvent> fills,
        BacktestRequest request)
    {
        var snapshot = portfolio.TakeSnapshot(Start.AddHours(1), Date);
        return BacktestMetricsEngine.Compute([snapshot], snapshot.DayCashFlows, fills, request);
    }
}
