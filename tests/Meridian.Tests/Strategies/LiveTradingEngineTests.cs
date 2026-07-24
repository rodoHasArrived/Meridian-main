using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Closed-circuit coverage for the live trading engine: promoted runs must actually execute —
/// live events drive strategy callbacks, strategy orders reach the order manager, and fills
/// flow back into the strategy and the run's recorded metrics. It also protects the terminal
/// cleanup path when a market-data feed completes without an operator stop request.
/// </summary>
public sealed class LiveTradingEngineTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);

    // ---- Full trading loop ----

    [Fact]
    public async Task TryLaunchAsync_PaperRun_DrivesFeedIntoStrategyAndRoutesOrders()
    {
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var orderManager = new RecordingOrderManager(fillPrice: 101.5m);
        await using var engine = CreateEngine(repository, hub, cache, orderManager);

        var run = CreatePaperRun(
            strategyId: BuyAndHoldLiveStrategy.CatalogId,
            parameters: new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "5" });
        await repository.RecordRunAsync(run);

        var launch = await engine.TryLaunchAsync(run);

        launch.Launched.Should().BeTrue(launch.Reason);
        engine.ActiveRunIds.Should().Contain(run.RunId);

        await PublishTradesUntilAsync(hub, cache, "AAPL", 101.5m, () => !orderManager.Requests.IsEmpty);

        orderManager.Requests.Should().NotBeEmpty("the strategy should have placed an order from the live feed");
        var request = orderManager.Requests.First();
        request.Symbol.Should().Be("AAPL");
        request.Side.Should().Be(ExecutionSdk.OrderSide.Buy);
        request.Quantity.Should().Be(5m);
        request.StrategyId.Should().Be(BuyAndHoldLiveStrategy.CatalogId);
        request.ClientOrderId.Should().StartWith($"mlt-{run.RunId}-",
            "engine orders must carry the run identity so fills route back to the owning session");

        var stopped = await engine.StopRunAsync(run.RunId);

        stopped.Should().BeTrue();
        var recorded = await ((IStrategyRepository)repository).GetRunByIdAsync(run.RunId);
        recorded.Should().NotBeNull();
        recorded!.EndedAt.Should().NotBeNull("stopping a run must record its terminal state");
        recorded.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        recorded.Metrics.Should().NotBeNull("completed live runs must retain summary metrics for promotion evaluation");
        recorded.Metrics!.Metrics.TotalTrades.Should().Be(1, "the buy-and-hold entry fill must be recorded");
        recorded.Metrics.Fills.Should().ContainSingle(fill => fill.Symbol == "AAPL" && fill.FilledQuantity == 5);
    }

    [Fact]
    public async Task EngineShutdown_LeavesRunOpenForResume()
    {
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var orderManager = new RecordingOrderManager(fillPrice: 50m);
        var engine = CreateEngine(repository, hub, cache, orderManager);

        var run = CreatePaperRun(
            strategyId: BuyAndHoldLiveStrategy.CatalogId,
            parameters: new Dictionary<string, string> { ["symbols"] = "MSFT" });
        await repository.RecordRunAsync(run);
        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();

        await engine.DisposeAsync();

        var recorded = await ((IStrategyRepository)repository).GetRunByIdAsync(run.RunId);
        recorded!.EndedAt.Should().BeNull("host shutdown must leave the run open so a restarted engine resumes it");
    }

    [Fact]
    public async Task Scenario_MarketFeedTermination_CompletesRunAndRemovesItFromEngine()
    {
        var repository = new InMemoryRunRepository();
        await using var engine = CreateEngine(
            repository,
            new CompletedMarketEventFeed(),
            new LiveMarketDataCache(),
            new RecordingOrderManager(50m));
        var run = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "MSFT" });
        await repository.RecordRunAsync(run);

        var launch = await engine.TryLaunchAsync(run);
        var completed = await repository.CompletedRun.Task.WaitAsync(WaitBudget);

        launch.Launched.Should().BeTrue(launch.Reason);
        completed.RunId.Should().Be(run.RunId);
        completed.EndedAt.Should().NotBeNull("a completed market feed must finalize the live run");
        completed.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        await WaitUntilAsync(() => !engine.ActiveRunIds.Contains(run.RunId));
    }

    // ---- Launch gating ----

    [Fact]
    public async Task TryLaunchAsync_UnknownStrategy_DefersWithCatalogGuidance()
    {
        var repository = new InMemoryRunRepository();
        await using var engine = CreateEngine(repository, new LiveMarketEventHub(), new LiveMarketDataCache(), new RecordingOrderManager(10m));
        var run = CreatePaperRun("no-such-strategy", new Dictionary<string, string> { ["symbols"] = "AAPL" });

        var launch = await engine.TryLaunchAsync(run);

        launch.Launched.Should().BeFalse();
        launch.Reason.Should().Contain("No live strategy implementation is registered");
        launch.Reason.Should().Contain(BuyAndHoldLiveStrategy.CatalogId);
        engine.ActiveRunIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TryLaunchAsync_LiveRun_IsDeferredUntilLiveExecutionIsEnabled()
    {
        var repository = new InMemoryRunRepository();
        await using var engine = CreateEngine(repository, new LiveMarketEventHub(), new LiveMarketDataCache(), new RecordingOrderManager(10m));
        var run = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" }) with
        {
            RunType = RunType.Live,
            Engine = "BrokerLive"
        };

        var launch = await engine.TryLaunchAsync(run);

        launch.Launched.Should().BeFalse();
        launch.Reason.Should().Contain("AllowLiveRuns");
        engine.ActiveRunIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TryLaunchAsync_WithoutUniverse_Defers()
    {
        var repository = new InMemoryRunRepository();
        await using var engine = CreateEngine(repository, new LiveMarketEventHub(), new LiveMarketDataCache(), new RecordingOrderManager(10m));
        var run = CreatePaperRun(BuyAndHoldLiveStrategy.CatalogId, parameters: null);

        var launch = await engine.TryLaunchAsync(run);

        launch.Launched.Should().BeFalse();
        launch.Reason.Should().Contain("symbols");
    }

    // ---- Startup resume sweep ----

    [Fact]
    public async Task ResumePendingRunsAsync_ActivatesOpenRunsAndSkipsFinishedOnes()
    {
        var repository = new InMemoryRunRepository();
        await using var engine = CreateEngine(repository, new LiveMarketEventHub(), new LiveMarketDataCache(), new RecordingOrderManager(10m));

        var openRun = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        var finishedRun = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "MSFT" }) with
        {
            EndedAt = DateTimeOffset.UtcNow
        };
        var backtestRun = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest);
        await repository.RecordRunAsync(openRun);
        await repository.RecordRunAsync(finishedRun);
        await repository.RecordRunAsync(backtestRun);

        var resumed = await engine.ResumePendingRunsAsync();

        resumed.Should().Be(1);
        engine.ActiveRunIds.Should().BeEquivalentTo([openRun.RunId]);
    }

    // ---- Promotion integration ----

    [Fact]
    public async Task PromotionApproval_HandsNewRunToTheRunLauncher()
    {
        var store = new StrategyRunStore();
        var launcher = new RecordingRunLauncher();
        var service = new PromotionService(
            store,
            new BacktestToLivePromoter(),
            new InMemoryPromotionRecordStore(),
            NullLogger<PromotionService>.Instance,
            runLauncher: launcher);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

        result.Success.Should().BeTrue(result.Reason);
        launcher.LaunchedRuns.Should().ContainSingle(
            "approving a promotion must hand the new run to the execution engine");
        launcher.LaunchedRuns[0].RunId.Should().Be(result.NewRunId);
        launcher.LaunchedRuns[0].RunType.Should().Be(RunType.Paper);
    }

    // ------------------------------------------------------------------ //
    // Helpers                                                            //
    // ------------------------------------------------------------------ //

    private static BacktestResult BuildPassingResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 3, 1),
            Symbols: ["SPY"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 110_000m,
            GrossPnl: 10_000m,
            NetPnl: 9_500m,
            TotalReturn: 0.10m,
            AnnualizedReturn: 0.25m,
            SharpeRatio: 1.5d,
            SortinoRatio: 2.0d,
            CalmarRatio: 3.0d,
            MaxDrawdown: 2_000m,
            MaxDrawdownPercent: 0.02m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 2.0d,
            WinRate: 0.65d,
            TotalTrades: 20,
            WinningTrades: 13,
            LosingTrades: 7,
            TotalCommissions: 500m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.22d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }

    private static LiveTradingEngine CreateEngine(
        InMemoryRunRepository repository,
        ILiveMarketEventFeed feed,
        LiveMarketDataCache cache,
        RecordingOrderManager orderManager)
    {
        var gateway = new PaperTradingGateway(
            NullLogger<PaperTradingGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);
        return new LiveTradingEngine(
            LiveStrategyCatalog.CreateDefault(),
            feed,
            cache,
            gateway,
            orderManager,
            new PaperTradingPortfolio(100_000m),
            repository,
            options: new LiveTradingEngineOptions(),
            loggerFactory: NullLoggerFactory.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // The engine removes its completed background task after session finalization, so
        // poll the externally observable registry rather than synchronizing on internals.
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < WaitBudget)
        {
            await Task.Delay(25);
        }

        condition().Should().BeTrue("the completed session must be removed from the active-run registry");
    }

    private static StrategyRunEntry CreatePaperRun(
        string strategyId,
        IReadOnlyDictionary<string, string>? parameters) =>
        StrategyRunEntry.Start(strategyId, strategyId, RunType.Paper) with
        {
            ParameterSet = parameters
        };

    private static async Task PublishTradesUntilAsync(
        LiveMarketEventHub hub,
        LiveMarketDataCache cache,
        string symbol,
        decimal price,
        Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        var sequence = 0L;
        while (!condition() && stopwatch.Elapsed < WaitBudget)
        {
            var trade = new Trade(
                DateTimeOffset.UtcNow,
                symbol,
                price,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: ++sequence);
            cache.RecordTrade(symbol, trade);
            hub.Publish(new LiveMarketEvent(trade.Timestamp, symbol, trade));
            await Task.Delay(25);
        }

        // Give the session loop a moment to finish routing after the condition flips.
        await Task.Delay(50);
    }

    private sealed class InMemoryRunRepository : IStrategyRepository
    {
        private readonly ConcurrentDictionary<string, StrategyRunEntry> _runs = new(StringComparer.Ordinal);

        public TaskCompletionSource<StrategyRunEntry> CompletedRun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            _runs[entry.RunId] = entry;
            return Task.CompletedTask;
        }

        public Task RecordLifecycleEventAsync(
            StrategyRunEntry entry,
            StrategyRunLifecycleEventType eventType,
            CancellationToken ct = default)
        {
            var recorded = entry with { LastLifecycleEvent = eventType };
            _runs[recorded.RunId] = recorded;
            if (eventType == StrategyRunLifecycleEventType.Completed)
            {
                CompletedRun.TrySetResult(recorded);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var run in _runs.Values.Where(run => run.StrategyId == strategyId).OrderBy(run => run.StartedAt))
                yield return run;

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var run in _runs.Values.OrderBy(run => run.StartedAt))
                yield return run;

            await Task.CompletedTask;
        }

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            Task.FromResult(_runs.Values
                .Where(run => run.StrategyId == strategyId)
                .OrderBy(run => run.StartedAt)
                .LastOrDefault());
    }

    private sealed class RecordingOrderManager(decimal fillPrice) : ExecutionSdk.IOrderManager
    {
        public ConcurrentQueue<ExecutionSdk.OrderRequest> Requests { get; } = new();

        public Task<ExecutionSdk.OrderResult> PlaceOrderAsync(
            ExecutionSdk.OrderRequest request,
            CancellationToken ct = default)
        {
            Requests.Enqueue(request);
            var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");
            var state = new ExecutionSdk.OrderState
            {
                OrderId = orderId,
                Symbol = request.Symbol,
                Side = request.Side,
                Type = request.Type,
                Quantity = request.Quantity,
                FilledQuantity = request.Quantity,
                Status = ExecutionSdk.OrderStatus.Filled,
                AverageFillPrice = fillPrice,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                StrategyId = request.StrategyId
            };
            return Task.FromResult(new ExecutionSdk.OrderResult
            {
                Success = true,
                OrderId = orderId,
                OrderState = state
            });
        }

        public Task<ExecutionSdk.OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.OrderResult { Success = true, OrderId = orderId });

        public Task<ExecutionSdk.OrderResult> ModifyOrderAsync(
            string orderId,
            ExecutionSdk.OrderModification modification,
            CancellationToken ct = default) =>
            Task.FromResult(new ExecutionSdk.OrderResult { Success = true, OrderId = orderId });

        public IReadOnlyList<ExecutionSdk.OrderState> GetOpenOrders() => [];

        public ExecutionSdk.OrderState? GetOrder(string orderId) => null;

        public Task CancelAllAsync(CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<ExecutionSdk.OrderState> GetCompletedOrders(int take = 20) => [];
    }

    private sealed class CompletedMarketEventFeed : ILiveMarketEventFeed
    {
        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(
            IReadOnlyCollection<string> symbols,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingRunLauncher : IPromotedRunLauncher
    {
        public List<StrategyRunEntry> LaunchedRuns { get; } = [];

        public Task<RunLaunchResult> TryLaunchAsync(StrategyRunEntry run, CancellationToken ct = default)
        {
            LaunchedRuns.Add(run);
            return Task.FromResult(RunLaunchResult.Success());
        }
    }

    private sealed class InMemoryPromotionRecordStore : IPromotionRecordStore
    {
        private readonly List<StrategyPromotionRecord> _records = [];

        public Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StrategyPromotionRecord>>(_records.ToArray());
    }
}
