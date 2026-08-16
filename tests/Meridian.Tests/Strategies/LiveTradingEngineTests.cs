using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Execution;
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
    private const string CanonicalPromotionVaultReference =
        "evidence://evidence-vault/ev-0123456789abcdef01234567";
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
    public async Task Scenario_FractionalBrokerFill_FailsRunClosedWithoutTruncatingQuantity()
    {
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var executionGateway = new ControllableExecutionGateway();
        await using var oms = new OrderManagementSystem(
            executionGateway,
            NullLogger<OrderManagementSystem>.Instance);
        await using var engine = CreateEngine(repository, hub, cache, oms);
        var run = CreatePaperRun(
            strategyId: BuyAndHoldLiveStrategy.CatalogId,
            parameters: new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "1" });
        await repository.RecordRunAsync(run);

        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await PublishTradesUntilAsync(
            hub,
            cache,
            "AAPL",
            101.5m,
            () => !executionGateway.Requests.IsEmpty);
        var request = executionGateway.Requests.Should().ContainSingle().Subject;

        await executionGateway.PublishAsync(new ExecutionSdk.ExecutionReport
        {
            OrderId = request.ClientOrderId!,
            ClientOrderId = request.ClientOrderId,
            ReportType = ExecutionSdk.ExecutionReportType.PartialFill,
            Symbol = request.Symbol,
            Side = request.Side,
            OrderStatus = ExecutionSdk.OrderStatus.PartiallyFilled,
            OrderQuantity = request.Quantity,
            FilledQuantity = 0.5m,
            FillPrice = 101.5m,
            Commission = 0m,
            Timestamp = DateTimeOffset.UtcNow
        });

        var failed = await repository.FailedRun.Task.WaitAsync(WaitBudget);
        failed.RunId.Should().Be(run.RunId);
        failed.TerminalStatus.Should().Be(Meridian.Contracts.Workstation.StrategyRunStatus.Failed);
        failed.ExceptionMessage.Should().Contain("fractional quantity");
        failed.Metrics.Should().BeNull(
            "a 0.5-unit broker fill must never be recorded as a silently truncated zero-unit strategy fill");
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
    public async Task SynchronousFallback_BurstBeyondInboxCapacity_DoesNotSelfDeadlock()
    {
        const int orderCount = 8;
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var orderManager = new RecordingOrderManager(fillPrice: 42m);
        var catalog = new LiveStrategyCatalog()
            .Register(BurstOrderLiveStrategy.CatalogId, context =>
                new BurstOrderLiveStrategy(context.StrategyId, orderCount));
        await using var engine = CreateEngine(
            repository,
            hub,
            cache,
            orderManager,
            catalog,
            new LiveTradingEngineOptions { FillReportQueueCapacity = 1 });
        var run = CreatePaperRun(
            BurstOrderLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);

        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await PublishTradesUntilAsync(
            hub,
            cache,
            "AAPL",
            42m,
            () => orderManager.Requests.Count == orderCount);

        orderManager.Requests.Should().HaveCount(orderCount,
            "synchronous fills are processed inline on the sole event loop rather than written to its own full inbox");
        (await engine.StopRunAsync(run.RunId)).Should().BeTrue();
        var recorded = await ((IStrategyRepository)repository).GetRunByIdAsync(run.RunId);
        recorded!.Metrics!.Metrics.TotalTrades.Should().Be(orderCount);
    }

    [Fact]
    public async Task LateFillForRetiredRun_FailsThatRunAndPumpContinuesForAnotherRun()
    {
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var gateway = new ControllableExecutionGateway();
        var retiredStrategy = new RecordingOrderLiveStrategy("retired-run-strategy");
        var healthyStrategy = new RecordingOrderLiveStrategy("healthy-run-strategy");
        var catalog = new LiveStrategyCatalog()
            .Register(retiredStrategy.StrategyId, _ => retiredStrategy)
            .Register(healthyStrategy.StrategyId, _ => healthyStrategy);
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);
        await using var engine = CreateEngine(repository, hub, cache, oms, catalog);
        var retiredRun = CreatePaperRun(
            retiredStrategy.StrategyId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        var healthyRun = CreatePaperRun(
            healthyStrategy.StrategyId,
            new Dictionary<string, string> { ["symbols"] = "MSFT" });
        await repository.RecordRunAsync(retiredRun);
        await repository.RecordRunAsync(healthyRun);

        (await engine.TryLaunchAsync(retiredRun)).Launched.Should().BeTrue();
        (await engine.TryLaunchAsync(healthyRun)).Launched.Should().BeTrue();
        await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () =>
            gateway.Requests.Any(request => request.ClientOrderId!.StartsWith(
                BuildRunOrderPrefix(retiredRun.RunId),
                StringComparison.Ordinal)));
        await PublishTradesUntilAsync(hub, cache, "MSFT", 200m, () =>
            gateway.Requests.Any(request => request.ClientOrderId!.StartsWith(
                BuildRunOrderPrefix(healthyRun.RunId),
                StringComparison.Ordinal)));
        var retiredRequest = FindRequest(gateway, retiredRun.RunId);
        var healthyRequest = FindRequest(gateway, healthyRun.RunId);

        (await engine.StopRunAsync(retiredRun.RunId)).Should().BeTrue();
        await WaitUntilAsync(() => !engine.ActiveRunIds.Contains(retiredRun.RunId));

        await gateway.PublishAsync(BuildFill(retiredRequest));
        await gateway.PublishAsync(BuildFill(healthyRequest));

        var failed = await repository.FailedRun.Task.WaitAsync(WaitBudget);
        failed.RunId.Should().Be(retiredRun.RunId);
        failed.ExceptionMessage.Should().Contain(retiredRequest.ClientOrderId);
        var healthyFill = await healthyStrategy.FillReceived.Task.WaitAsync(WaitBudget);
        healthyFill.Symbol.Should().Be("MSFT",
            "a retired run's late fill must not terminate the sole pump for other runs");
    }

    [Fact]
    public async Task SaturatedRunInbox_DoesNotBlockFillDeliveryToAnotherRun()
    {
        const int blockedOrderCount = 5;
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var gateway = new ControllableExecutionGateway();
        var blockedStrategy = new BlockingFillLiveStrategy("blocked-run-strategy", blockedOrderCount);
        var healthyStrategy = new RecordingOrderLiveStrategy("isolated-run-strategy");
        var catalog = new LiveStrategyCatalog()
            .Register(blockedStrategy.StrategyId, _ => blockedStrategy)
            .Register(healthyStrategy.StrategyId, _ => healthyStrategy);
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);
        await using var engine = CreateEngine(
            repository,
            hub,
            cache,
            oms,
            catalog,
            new LiveTradingEngineOptions { FillReportQueueCapacity = 1 });
        var blockedRun = CreatePaperRun(
            blockedStrategy.StrategyId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        var healthyRun = CreatePaperRun(
            healthyStrategy.StrategyId,
            new Dictionary<string, string> { ["symbols"] = "MSFT" });
        await repository.RecordRunAsync(blockedRun);
        await repository.RecordRunAsync(healthyRun);

        (await engine.TryLaunchAsync(blockedRun)).Launched.Should().BeTrue();
        (await engine.TryLaunchAsync(healthyRun)).Launched.Should().BeTrue();
        await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () =>
            gateway.Requests.Count(request => request.ClientOrderId!.StartsWith(
                BuildRunOrderPrefix(blockedRun.RunId),
                StringComparison.Ordinal)) == blockedOrderCount);
        await PublishTradesUntilAsync(hub, cache, "MSFT", 200m, () =>
            gateway.Requests.Any(request => request.ClientOrderId!.StartsWith(
                BuildRunOrderPrefix(healthyRun.RunId),
                StringComparison.Ordinal)));
        var blockedRequests = gateway.Requests
            .Where(request => request.ClientOrderId!.StartsWith(
                BuildRunOrderPrefix(blockedRun.RunId),
                StringComparison.Ordinal))
            .ToArray();
        var healthyRequest = FindRequest(gateway, healthyRun.RunId);

        try
        {
            await gateway.PublishAsync(BuildFill(blockedRequests[0]));
            await blockedStrategy.FillHandlerEntered.Task.WaitAsync(WaitBudget);
            foreach (var request in blockedRequests.Skip(1))
            {
                await gateway.PublishAsync(BuildFill(request));
            }
            await gateway.PublishAsync(BuildFill(healthyRequest));

            var healthyFill = await healthyStrategy.FillReceived.Task.WaitAsync(WaitBudget);
            healthyFill.Symbol.Should().Be("MSFT",
                "the sole OMS pump must continue while another run's event loop and inbox are saturated");
            (await repository.FailedRun.Task.WaitAsync(WaitBudget)).RunId.Should().Be(blockedRun.RunId);
        }
        finally
        {
            blockedStrategy.Release();
        }
    }

    [Fact]
    public async Task EngineShutdown_DrainsDequeuedOmsFillIntoSessionBeforeClosingIt()
    {
        var repository = new InMemoryRunRepository();
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var gateway = new ControllableExecutionGateway();
        var handoff = new BlockingTradeEventPublisher();
        var strategy = new RecordingOrderLiveStrategy("shutdown-drain-strategy");
        var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: handoff);
        await using var engine = CreateEngine(repository, hub, cache, oms, catalog);
        var run = CreatePaperRun(
            strategy.StrategyId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);

        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => !gateway.Requests.IsEmpty);
        var request = FindRequest(gateway, run.RunId);

        try
        {
            await gateway.PublishAsync(BuildFill(request));
            await handoff.Entered.Task.WaitAsync(WaitBudget);

            var shutdown = engine.DisposeAsync().AsTask();
            shutdown.IsCompleted.Should().BeFalse(
                "the engine must keep its session and report workers alive while a dequeued OMS fill is in flight");

            handoff.Release();
            await shutdown.WaitAsync(WaitBudget);

            var delivered = await strategy.FillReceived.Task.WaitAsync(WaitBudget);
            delivered.Symbol.Should().Be("AAPL");
        }
        finally
        {
            handoff.Release();
        }
    }

    [Fact]
    public async Task TryLaunchAsync_RacingDispose_CannotCommitRunAfterAdmissionCloses()
    {
        var repository = new InMemoryRunRepository();
        var catalog = new BlockingLiveStrategyCatalog();
        var engine = CreateEngine(
            repository,
            new LiveMarketEventHub(),
            new LiveMarketDataCache(),
            new RecordingOrderManager(10m),
            catalog);
        var run = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);

        var launch = Task.Run(() => engine.TryLaunchAsync(run));
        await catalog.Entered.Task.WaitAsync(WaitBudget);
        var shutdown = engine.DisposeAsync().AsTask();

        catalog.Release();

        var launchResult = await launch.WaitAsync(WaitBudget);
        await shutdown.WaitAsync(WaitBudget);
        launchResult.Launched.Should().BeFalse();
        engine.ActiveRunIds.Should().BeEmpty(
            "a launch that had not committed when shutdown closed admission must not escape the shutdown snapshot");
    }

    [Fact]
    public async Task HostedStopAsync_CancellationLeavesOwnedCleanupRunningAndObserved()
    {
        var repository = new InMemoryRunRepository();
        var catalog = new BlockingLiveStrategyCatalog();
        var engine = CreateEngine(
            repository,
            new LiveMarketEventHub(),
            new LiveMarketDataCache(),
            new RecordingOrderManager(10m),
            catalog);
        var hosted = new global::Meridian.LiveTradingEngineHostedService(
            engine,
            NullLogger<global::Meridian.LiveTradingEngineHostedService>.Instance);
        var run = CreatePaperRun(
            BuyAndHoldLiveStrategy.CatalogId,
            new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);
        var launch = Task.Run(() => engine.TryLaunchAsync(run));
        await catalog.Entered.Task.WaitAsync(WaitBudget);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        try
        {
            Func<Task> stop = () => hosted.StopAsync(cancelled.Token);
            await stop.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            catalog.Release();
        }

        (await launch.WaitAsync(WaitBudget)).Launched.Should().BeFalse();
        await hosted.StopAsync(CancellationToken.None).WaitAsync(WaitBudget);
        engine.ActiveRunIds.Should().BeEmpty();
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
        var approvalChecklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        var run = StrategyRunEntry.StartWithEvidence(
                "s1",
                "Strategy One",
                RunType.Backtest,
                runId: "promotion-launch-source",
                engine: "MeridianNative",
                retainedEvidenceReferences: [CanonicalPromotionVaultReference])
            .Complete(BuildPassingResult());
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: approvalChecklist,
            EvidenceReferences: approvalChecklist
                .Select(item => $"{item}:{CanonicalPromotionVaultReference}")
                .ToArray()));

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
        ExecutionSdk.IOrderManager orderManager,
        ILiveStrategyCatalog? catalog = null,
        LiveTradingEngineOptions? options = null)
    {
        // `Meridian.Execution` and `Meridian.Execution.Adapters` both declare `PaperTradingGateway`
        // and this file imports both. Only the Adapters one implements `IOrderGateway`, which is
        // what `LiveTradingEngine` takes; the other implements `IExecutionGateway`.
        var gateway = new Meridian.Execution.Adapters.PaperTradingGateway(
            NullLogger<Meridian.Execution.Adapters.PaperTradingGateway>.Instance,
            securityMaster: null,
            options: null,
            liveFeed: cache);
        return new LiveTradingEngine(
            catalog ?? LiveStrategyCatalog.CreateDefault(),
            feed,
            cache,
            gateway,
            orderManager,
            new PaperTradingPortfolio(100_000m),
            repository,
            options: options ?? new LiveTradingEngineOptions(),
            loggerFactory: NullLoggerFactory.Instance);
    }

    private static string BuildRunOrderPrefix(string runId) => $"mlt-{runId}-";

    private static ExecutionSdk.OrderRequest FindRequest(
        ControllableExecutionGateway gateway,
        string runId) =>
        gateway.Requests.Single(request => request.ClientOrderId!.StartsWith(
            BuildRunOrderPrefix(runId),
            StringComparison.Ordinal));

    private static ExecutionSdk.ExecutionReport BuildFill(ExecutionSdk.OrderRequest request) => new()
    {
        OrderId = request.ClientOrderId!,
        ClientOrderId = request.ClientOrderId,
        ReportType = ExecutionSdk.ExecutionReportType.Fill,
        Symbol = request.Symbol,
        Side = request.Side,
        OrderStatus = ExecutionSdk.OrderStatus.Filled,
        OrderQuantity = request.Quantity,
        FilledQuantity = request.Quantity,
        FillPrice = 123m,
        Commission = 0m,
        Timestamp = DateTimeOffset.UtcNow
    };

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

    private sealed class BurstOrderLiveStrategy(string strategyId, int orderCount) : LiveStrategyBase
    {
        public const string CatalogId = "burst-orders";
        private int _submitted;

        public override string StrategyId { get; } = strategyId;

        public override string Name => "Burst orders";

        public override void OnTrade(Trade trade, IBacktestContext ctx)
        {
            if (Interlocked.Exchange(ref _submitted, 1) != 0)
            {
                return;
            }

            for (var order = 0; order < orderCount; order++)
            {
                ctx.PlaceMarketOrder(trade.Symbol, 1);
            }
        }
    }

    private class RecordingOrderLiveStrategy(string strategyId) : LiveStrategyBase
    {
        private int _submitted;

        public override string StrategyId { get; } = strategyId;

        public override string Name => "Recording live strategy";

        public TaskCompletionSource<FillEvent> FillReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void OnTrade(Trade trade, IBacktestContext ctx)
        {
            if (Interlocked.Exchange(ref _submitted, 1) == 0)
            {
                ctx.PlaceMarketOrder(trade.Symbol, 1);
            }
        }

        public override void OnOrderFill(FillEvent fill, IBacktestContext ctx)
            => FillReceived.TrySetResult(fill);
    }

    private sealed class BlockingFillLiveStrategy(string strategyId, int orderCount)
        : RecordingOrderLiveStrategy(strategyId)
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);
        private int _fillEntered;
        private int _ordersSubmitted;

        public TaskCompletionSource FillHandlerEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void OnTrade(Trade trade, IBacktestContext ctx)
        {
            if (Interlocked.Exchange(ref _ordersSubmitted, 1) != 0)
            {
                return;
            }

            for (var order = 0; order < orderCount; order++)
            {
                ctx.PlaceMarketOrder(trade.Symbol, 1);
            }
        }

        public override void OnOrderFill(FillEvent fill, IBacktestContext ctx)
        {
            if (Interlocked.Exchange(ref _fillEntered, 1) == 0)
            {
                FillHandlerEntered.TrySetResult();
                _release.Wait();
            }

            base.OnOrderFill(fill, ctx);
        }

        public void Release() => _release.Set();
    }

    private sealed class BlockingLiveStrategyCatalog : ILiveStrategyCatalog
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public IReadOnlyCollection<string> StrategyIds => [BuyAndHoldLiveStrategy.CatalogId];

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryCreate(
            string strategyId,
            IReadOnlyDictionary<string, string>? parameters,
            out Meridian.Strategies.Interfaces.ILiveStrategy? strategy,
            out string? failureReason)
        {
            Entered.TrySetResult();
            _release.Wait();
            strategy = new BuyAndHoldLiveStrategy(strategyId, quantityPerSymbol: 1);
            failureReason = null;
            return true;
        }

        public void Release() => _release.Set();
    }

    private sealed class BlockingTradeEventPublisher : ITradeEventPublisher
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Publish(TradeExecutedEvent tradeEvent)
        {
            Entered.TrySetResult();
            _release.Wait();
        }

        public void Release() => _release.Set();
    }

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

        public TaskCompletionSource<StrategyRunEntry> FailedRun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            else if (eventType == StrategyRunLifecycleEventType.Failed)
            {
                FailedRun.TrySetResult(recorded);
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

        public Task<ExecutionSdk.KillSwitchSweepResult> CancelAllAsync(CancellationToken ct = default) =>
            Task.FromResult(ExecutionSdk.KillSwitchSweepResult.Empty);

        public IReadOnlyList<ExecutionSdk.OrderState> GetCompletedOrders(int take = 20) => [];
    }

    private sealed class ControllableExecutionGateway :
        ExecutionSdk.IExecutionGateway,
        ExecutionSdk.IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionSdk.ExecutionReport> _reports =
            Channel.CreateUnbounded<ExecutionSdk.ExecutionReport>();

        public ConcurrentQueue<ExecutionSdk.OrderRequest> Requests { get; } = new();

        public string GatewayId => "fractional-fill-test";

        public bool IsConnected => true;

        public ExecutionSdk.ExecutionMode ExecutionMode => ExecutionSdk.ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionSdk.ExecutionReport> SubmitOrderAsync(
            ExecutionSdk.OrderRequest request,
            CancellationToken ct = default)
        {
            Requests.Enqueue(request);
            var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");
            return Task.FromResult(new ExecutionSdk.ExecutionReport
            {
                OrderId = orderId,
                ClientOrderId = request.ClientOrderId,
                ReportType = ExecutionSdk.ExecutionReportType.New,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = ExecutionSdk.OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                FilledQuantity = 0m,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        public Task<ExecutionSdk.ExecutionReport> CancelOrderAsync(
            string orderId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionSdk.ExecutionReport> ModifyOrderAsync(
            string orderId,
            ExecutionSdk.OrderModification modification,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionSdk.ExecutionReport> StreamExecutionReportsAsync(
            CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishAsync(ExecutionSdk.ExecutionReport report) =>
            _reports.Writer.WriteAsync(report);
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
        private readonly SemaphoreSlim _decisionGate = new(1, 1);

        public Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StrategyPromotionRecord>>(_records.ToArray());

        public async Task<PromotionDecisionReservation> ReserveFirstDecisionAsync(
            StrategyPromotionRecord record,
            CancellationToken ct = default)
        {
            await _decisionGate.WaitAsync(ct);
            var existing = _records.FirstOrDefault(candidate =>
                candidate.SourceRunId == record.SourceRunId &&
                candidate.SourceRunType == record.SourceRunType &&
                candidate.TargetRunType == record.TargetRunType);
            var wasAppended = existing is null;
            if (wasAppended)
            {
                _records.Add(record);
            }

            return new PromotionDecisionReservation(
                existing ?? record,
                wasAppended,
                () =>
                {
                    _decisionGate.Release();
                    return ValueTask.CompletedTask;
                });
        }
    }
}
