using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Tests for <see cref="CashFlowProjectionService"/>.
/// </summary>
public sealed class CashFlowProjectionTests
{
    // -----------------------------------------------------------------------
    // Basic retrieval
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRunNotFound()
    {
        var store   = new StrategyRunStore();
        var service = new CashFlowProjectionService(store);

        var result = await service.GetAsync("no-such-run");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsEmptySummary_WhenRunHasNoCashFlows()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithNoFlows("cf-empty-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-empty-1");

        result.Should().NotBeNull();
        result!.RunId.Should().Be("cf-empty-1");
        result.TotalEntries.Should().Be(0);
        result.Entries.Should().BeEmpty();
        result.NetCashFlow.Should().Be(0m);
        result.Ladder.Buckets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsSummaryWithEntries_WhenRunHasCashFlows()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-mixed-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-mixed-1");

        result.Should().NotBeNull();
        result!.RunId.Should().Be("cf-mixed-1");
        result.TotalEntries.Should().BeGreaterThan(0);
        result.Entries.Should().HaveCount(result.TotalEntries);
    }

    // -----------------------------------------------------------------------
    // Totals consistency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_TotalInflowsAndOutflows_MatchEntrySums()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-totals-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-totals-1");

        var expectedInflows  = result!.Entries.Sum(static e => e.Amount > 0m ? e.Amount : 0m);
        var expectedOutflows = result.Entries.Sum(static e => e.Amount < 0m ? -e.Amount : 0m);

        result.TotalInflows.Should().Be(expectedInflows);
        result.TotalOutflows.Should().Be(expectedOutflows);
        result.NetCashFlow.Should().Be(expectedInflows - expectedOutflows);
    }

    [Fact]
    public async Task GetAsync_EntriesAreSortedByTimestamp()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-sort-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-sort-1");

        result!.Entries.Should().BeInAscendingOrder(static e => e.Timestamp);
    }

    // -----------------------------------------------------------------------
    // Cash ladder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_LadderNetPosition_EqualsTotalInflowsMinusOutflows_WithinWindow()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-ladder-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-ladder-1");

        // The ladder's net position equals sum of bucket netflows
        var bucketNetSum = result!.Ladder.Buckets.Sum(static b => b.NetFlow);
        result.Ladder.NetPosition.Should().Be(bucketNetSum);
    }

    [Fact]
    public async Task GetAsync_LadderTotals_EqualSumOfBuckets()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-ladder-totals-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-ladder-totals-1");

        var ladder = result!.Ladder;
        ladder.TotalProjectedInflows.Should().Be(ladder.Buckets.Sum(static b => b.ProjectedInflows));
        ladder.TotalProjectedOutflows.Should().Be(ladder.Buckets.Sum(static b => b.ProjectedOutflows));
    }

    [Fact]
    public async Task GetAsync_BucketDays_AffectsNumberOfBuckets()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-buckets-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);

        // Wide buckets = fewer buckets
        var wideBuckets  = await service.GetAsync("cf-buckets-1", bucketDays: 30);
        // Narrow buckets = more or equal buckets
        var narrowBuckets = await service.GetAsync("cf-buckets-1", bucketDays: 1);

        wideBuckets!.Ladder.BucketDays.Should().Be(30);
        narrowBuckets!.Ladder.BucketDays.Should().Be(1);
        narrowBuckets.Ladder.Buckets.Count.Should().BeGreaterThanOrEqualTo(wideBuckets.Ladder.Buckets.Count);
    }

    // -----------------------------------------------------------------------
    // Event kind mapping
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_DividendFlow_HasCorrectEventKind()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithDividendFlow("cf-dividend-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-dividend-1");

        result!.Entries.Should().ContainSingle(e => e.EventKind == "Dividend");
    }

    [Fact]
    public async Task GetAsync_CommissionFlow_HasCorrectEventKind()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithCommissionFlow("cf-commission-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-commission-1");

        result!.Entries.Should().ContainSingle(e => e.EventKind == "Commission");
    }

    // -----------------------------------------------------------------------
    // Optional parameters
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_AsOf_FiltersLadderEntries()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-asof-1");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);

        // asOf = far in the future → all flows are before asOf → ladder should be empty
        var futureResult = await service.GetAsync("cf-asof-1", asOf: DateTimeOffset.MaxValue);

        futureResult!.Ladder.Buckets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_RunId_IsPropagated()
    {
        var store = new StrategyRunStore();
        var run   = BuildRunWithMixedFlows("cf-runid-check");
        await store.RecordRunAsync(run);

        var service = new CashFlowProjectionService(store);
        var result  = await service.GetAsync("cf-runid-check");

        result!.RunId.Should().Be("cf-runid-check");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static StrategyRunEntry BuildRunWithNoFlows(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.Zero);
        var result    = BuildMinimalBacktestResult(startedAt, cashFlows: []);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "strat-cf",
            StrategyName: "CF Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(1),
            Metrics: result);
    }

    private static StrategyRunEntry BuildRunWithMixedFlows(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 2, 1, 9, 30, 0, TimeSpan.Zero);

        var cashFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(startedAt.AddHours(1),   -35_000m, "AAPL",  100L, 350m),
            new CommissionCashFlow(startedAt.AddHours(1), -1m,  "AAPL", Guid.NewGuid()),
            new DividendCashFlow(startedAt.AddDays(5),   230m,  "AAPL",  100L, 2.30m),
            new TradeCashFlow(startedAt.AddDays(10),   36_000m,  "AAPL", -100L, 360m),
            new CommissionCashFlow(startedAt.AddDays(10), -1m,  "AAPL", Guid.NewGuid()),
        };

        var result = BuildMinimalBacktestResult(startedAt, cashFlows: cashFlows);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "strat-cf-mixed",
            StrategyName: "CF Mixed Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(15),
            Metrics: result);
    }

    private static StrategyRunEntry BuildRunWithDividendFlow(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);

        var cashFlows = new CashFlowEntry[]
        {
            new DividendCashFlow(startedAt.AddDays(1), 50m, "MSFT", 100L, 0.50m)
        };

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "strat-div",
            StrategyName: "Dividend Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(5),
            Metrics: BuildMinimalBacktestResult(startedAt, cashFlows: cashFlows));
    }

    private static StrategyRunEntry BuildRunWithCommissionFlow(string runId)
    {
        var startedAt = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
        var orderId   = Guid.NewGuid();

        var cashFlows = new CashFlowEntry[]
        {
            new CommissionCashFlow(startedAt.AddHours(1), -1.50m, "SPY", orderId)
        };

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "strat-comm",
            StrategyName: "Commission Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(1),
            Metrics: BuildMinimalBacktestResult(startedAt, cashFlows: cashFlows));
    }

    private static BacktestResult BuildMinimalBacktestResult(
        DateTimeOffset startedAt,
        CashFlowEntry[] cashFlows)
    {
        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(startedAt.AddDays(15).UtcDateTime),
            Symbols: ["AAPL"],
            InitialCash: 100_000m);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 101_000m, GrossPnl: 1_010m, NetPnl: 1_000m,
            TotalReturn: 0.01m, AnnualizedReturn: 0.12m, SharpeRatio: 1.5, SortinoRatio: 1.8,
            CalmarRatio: 0.9, MaxDrawdown: 200m, MaxDrawdownPercent: 0.002m, MaxDrawdownRecoveryDays: 2,
            ProfitFactor: 2.0, WinRate: 0.70, TotalTrades: 2, WinningTrades: 1,
            LosingTrades: 1, TotalCommissions: 2m, TotalMarginInterest: 0m,
            TotalShortRebates: 0m, Xirr: 0.10,
            SymbolAttribution: new Dictionary<string, Meridian.Backtesting.Sdk.SymbolAttribution>());

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [],
            CashFlows: cashFlows,
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 100);
    }
}
