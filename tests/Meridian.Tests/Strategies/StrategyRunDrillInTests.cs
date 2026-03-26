using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Tests for the Track C drill-in methods added to <see cref="StrategyRunReadService"/>:
/// equity curve, fill list, and attribution.
/// </summary>
public sealed class StrategyRunDrillInTests
{
    // -----------------------------------------------------------------------
    // Equity curve
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEquityCurveAsync_ReturnsCorrectCurveWithDrawdown()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithMultipleSnapshots("curve-run-1", initialEquity: 100_000m);
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var curve = await service.GetEquityCurveAsync("curve-run-1");

        curve.Should().NotBeNull();
        curve!.RunId.Should().Be("curve-run-1");
        curve.Points.Should().HaveCount(3);

        // First point has zero drawdown (it is the peak)
        curve.Points[0].DrawdownFromPeak.Should().Be(0m);
        curve.Points[0].DrawdownFromPeakPercent.Should().Be(0m);

        // Second point (equity dropped) should show positive drawdown
        curve.Points[1].DrawdownFromPeak.Should().BeGreaterThan(0m);
        curve.Points[1].DrawdownFromPeakPercent.Should().BeGreaterThan(0m);

        // Third point recovered or worsened — either way drawdown is non-negative
        curve.Points[2].DrawdownFromPeak.Should().BeGreaterThanOrEqualTo(0m);

        curve.MaxDrawdown.Should().BeGreaterThanOrEqualTo(0m);
        curve.SharpeRatio.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetEquityCurveAsync_ReturnsNull_WhenRunNotFound()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());

        var curve = await service.GetEquityCurveAsync("no-such-run");

        curve.Should().BeNull();
    }

    [Fact]
    public async Task GetEquityCurveAsync_ReturnsNull_WhenRunHasNoMetrics()
    {
        var store = new StrategyRunStore();
        var incompleteRun = StrategyRunEntry.Start("strat-x", "Strategy X", RunType.Backtest);
        await store.RecordRunAsync(incompleteRun);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());

        var curve = await service.GetEquityCurveAsync(incompleteRun.RunId);

        curve.Should().BeNull();
    }

    [Fact]
    public async Task GetEquityCurveAsync_InitialAndFinalEquity_AreCorrect()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithMultipleSnapshots("curve-run-2", initialEquity: 50_000m);
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var curve = await service.GetEquityCurveAsync("curve-run-2");

        curve!.InitialEquity.Should().Be(curve.Points[0].TotalEquity);
        curve.FinalEquity.Should().Be(curve.Points[^1].TotalEquity);
    }

    // -----------------------------------------------------------------------
    // Fills
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetFillsAsync_ReturnsAllFillsSortedByTime()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithFills("fills-run-1", fillCount: 4);
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var summary = await service.GetFillsAsync("fills-run-1");

        summary.Should().NotBeNull();
        summary!.RunId.Should().Be("fills-run-1");
        summary.TotalFills.Should().Be(4);
        summary.Fills.Should().HaveCount(4);
        summary.Fills.Should().BeInAscendingOrder(static f => f.FilledAt);
    }

    [Fact]
    public async Task GetFillsAsync_TotalCommissions_MatchesSumOfIndividualFills()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithFills("fills-run-2", fillCount: 3);
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var summary = await service.GetFillsAsync("fills-run-2");

        var expectedCommissions = summary!.Fills.Sum(static f => f.Commission);
        summary.TotalCommissions.Should().Be(expectedCommissions);
    }

    [Fact]
    public async Task GetFillsAsync_ReturnsNull_WhenRunNotFound()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());

        var result = await service.GetFillsAsync("phantom-run");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFillsAsync_ReturnsEmptyFills_WhenRunHasNoFills()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithFills("fills-run-3", fillCount: 0);
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var summary = await service.GetFillsAsync("fills-run-3");

        summary.Should().NotBeNull();
        summary!.TotalFills.Should().Be(0);
        summary.Fills.Should().BeEmpty();
        summary.TotalCommissions.Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // Attribution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttributionAsync_ReturnsSymbolBreakdown()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithAttribution("attr-run-1");
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var attribution = await service.GetAttributionAsync("attr-run-1");

        attribution.Should().NotBeNull();
        attribution!.RunId.Should().Be("attr-run-1");
        attribution.BySymbol.Should().NotBeEmpty();
        attribution.BySymbol.Should().AllSatisfy(entry => entry.Symbol.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task GetAttributionAsync_TotalPnl_MatchesSymbolBreakdown()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithAttribution("attr-run-2");
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var attribution = await service.GetAttributionAsync("attr-run-2");

        attribution!.TotalRealizedPnl.Should().Be(attribution.BySymbol.Sum(static a => a.RealizedPnl));
        attribution.TotalUnrealizedPnl.Should().Be(attribution.BySymbol.Sum(static a => a.UnrealizedPnl));
        attribution.TotalCommissions.Should().Be(attribution.BySymbol.Sum(static a => a.Commissions));
    }

    [Fact]
    public async Task GetAttributionAsync_BySymbol_TotalPnlIsRealizedPlusUnrealized()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithAttribution("attr-run-3");
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var attribution = await service.GetAttributionAsync("attr-run-3");

        foreach (var entry in attribution!.BySymbol)
        {
            entry.TotalPnl.Should().Be(entry.RealizedPnl + entry.UnrealizedPnl);
        }
    }

    [Fact]
    public async Task GetAttributionAsync_ReturnsNull_WhenRunNotFound()
    {
        var store = new StrategyRunStore();
        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());

        var result = await service.GetAttributionAsync("ghost-run");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAttributionAsync_OrdersSymbolsByTotalPnlDescending()
    {
        var store = new StrategyRunStore();
        var run = BuildRunWithAttribution("attr-run-4");
        await store.RecordRunAsync(run);

        var service = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var attribution = await service.GetAttributionAsync("attr-run-4");

        var totals = attribution!.BySymbol.Select(static a => a.TotalPnl).ToList();
        totals.Should().BeInDescendingOrder();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static StrategyRunEntry BuildRunWithMultipleSnapshots(string runId, decimal initialEquity)
    {
        var startedAt = new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.Zero);

        // Day 1: peak, Day 2: drawdown, Day 3: partial recovery
        var equities = new[] { initialEquity, initialEquity * 0.95m, initialEquity * 0.98m };
        var snapshots = equities
            .Select((eq, i) => BuildSnapshot(startedAt.AddDays(i), eq))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(2).UtcDateTime),
            Symbols: ["SPY"],
            InitialCash: initialEquity);

        var metrics = new BacktestMetrics(
            InitialCapital: initialEquity,
            FinalEquity: equities[^1],
            GrossPnl: equities[^1] - initialEquity + 50m,
            NetPnl: equities[^1] - initialEquity,
            TotalReturn: (equities[^1] - initialEquity) / initialEquity,
            AnnualizedReturn: 0.06m,
            SharpeRatio: 1.1,
            SortinoRatio: 1.3,
            CalmarRatio: 0.8,
            MaxDrawdown: initialEquity * 0.05m,
            MaxDrawdownPercent: 0.05m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 1.4,
            WinRate: 0.55,
            TotalTrades: 6,
            WinningTrades: 4,
            LosingTrades: 2,
            TotalCommissions: 50m,
            TotalMarginInterest: 10m,
            TotalShortRebates: 2m,
            Xirr: 0.07,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["SPY"] = new("SPY", equities[^1] - initialEquity, 0m, 6, 50m, 8m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            Snapshots: snapshots,
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(30),
            TotalEventsProcessed: 300);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "curve-strat",
            StrategyName: "Curve Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(3),
            Metrics: result);
    }

    private static PortfolioSnapshot BuildSnapshot(DateTimeOffset ts, decimal equity)
    {
        return new PortfolioSnapshot(
            Timestamp: ts,
            Date: DateOnly.FromDateTime(ts.UtcDateTime),
            Cash: equity * 0.3m,
            MarginBalance: 0m,
            LongMarketValue: equity * 0.7m,
            ShortMarketValue: 0m,
            TotalEquity: equity,
            DailyReturn: 0m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);
    }

    private static StrategyRunEntry BuildRunWithFills(string runId, int fillCount)
    {
        var startedAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);

        var fills = Enumerable.Range(0, fillCount)
            .Select(i => new FillEvent(
                FillId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                Symbol: "MSFT",
                FilledQuantity: 10L,
                FillPrice: 350m + i,
                Commission: 1.00m,
                FilledAt: startedAt.AddMinutes(i * 5),
                AccountId: "default-brokerage"))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(1).UtcDateTime),
            Symbols: ["MSFT"],
            InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 101_000m, GrossPnl: 1_010m, NetPnl: 1_000m,
            TotalReturn: 0.01m, AnnualizedReturn: 0.12m, SharpeRatio: 1.5, SortinoRatio: 1.8,
            CalmarRatio: 0.9, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 2,
            ProfitFactor: 2.0, WinRate: 0.70, TotalTrades: fillCount, WinningTrades: fillCount,
            LosingTrades: 0, TotalCommissions: fillCount * 1.00m, TotalMarginInterest: 0m,
            TotalShortRebates: 0m, Xirr: 0.10,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
            {
                ["MSFT"] = new("MSFT", 1_000m, 0m, fillCount, fillCount * 1.00m, 0m)
            });

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MSFT" },
            Snapshots: [],
            CashFlows: [],
            Fills: fills,
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 100);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "fill-strat",
            StrategyName: "Fill Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(1),
            Metrics: result);
    }

    private static StrategyRunEntry BuildRunWithAttribution(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

        var attribution = new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>
        {
            ["AAPL"] = new("AAPL", 5_000m, 1_200m, 8, 45m, 20m),
            ["MSFT"] = new("MSFT", 3_000m, -200m, 4, 22m, 10m),
            ["SPY"]  = new("SPY",  1_000m, 500m, 2, 10m, 5m)
        };

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddMonths(1).UtcDateTime),
            Symbols: ["AAPL", "MSFT", "SPY"],
            InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 110_500m, GrossPnl: 10_577m, NetPnl: 10_500m,
            TotalReturn: 0.105m, AnnualizedReturn: 0.42m, SharpeRatio: 2.1, SortinoRatio: 2.5,
            CalmarRatio: 1.2, MaxDrawdown: 1_500m, MaxDrawdownPercent: 0.015m, MaxDrawdownRecoveryDays: 4,
            ProfitFactor: 3.2, WinRate: 0.75, TotalTrades: 14, WinningTrades: 11, LosingTrades: 3,
            TotalCommissions: 77m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0.40,
            SymbolAttribution: attribution);

        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL", "MSFT", "SPY" },
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(2),
            TotalEventsProcessed: 500);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "attr-strat",
            StrategyName: "Attribution Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddMonths(1),
            Metrics: result);
    }
}
