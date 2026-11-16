using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Events;
using Meridian.Execution.PaperMatching;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Storage.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    // ---- Stop while a settled fill is still on its way to the session ----

    /// <summary>
    /// The OMS marks an order filled and completes the fill's accounting handoff before it
    /// publishes the report, so a stop can arrive after the fill is real but before its report
    /// reaches the run. The stop must wait for that report instead of closing the session first:
    /// the run ends Stopped with the fill in its metrics rather than Failed over its own fill.
    /// The run store is file-backed so invalid lifecycle transitions are rejected, as in a host.
    /// </summary>
    [Fact]
    public async Task StopRun_WhileSettledFillAwaitsPublication_DeliversFillAndStopsCleanly()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "meridian-stop-fill", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var hub = new Meridian.Execution.Live.LiveMarketEventHub();
            var cache = new LiveMarketDataCache();
            var accounting = new HeldTradeEventPublisher();
            var paperGateway = new Meridian.Execution.PaperTradingGateway(
                NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
                liveFeed: cache,
                costOptions: new PaperTradingCostOptions { CommissionRate = 0m, CommissionMinimum = 0m });
            await using var oms = new OrderManagementSystem(
                paperGateway,
                NullLogger<OrderManagementSystem>.Instance,
                tradeEventPublisher: accounting);
            await oms.StartAsync();
            var strategy = new RecordingOrderLiveStrategy("stop-during-fill-strategy");
            var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
            var run = CreatePaperRun(strategy.StrategyId, new Dictionary<string, string> { ["symbols"] = "AAPL" });
            await store.RecordRunAsync(run);

            await using var engine = CreateEngine(store, hub, cache, oms, catalog);
            (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
            await WaitForLifecycleAsync(store, run.RunId, StrategyRunLifecycleEventType.Activated);

            // The order fills at the paper gateway and the OMS holds in its accounting handoff,
            // with the order already Filled and its report not yet published.
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => accounting.Held.Task.IsCompleted);
            oms.GetOpenOrders().Concat(oms.GetCompletedOrders()).Should().ContainSingle(order =>
                order.OrderId.StartsWith(BuildRunOrderPrefix(run.RunId), StringComparison.Ordinal)
                && order.Status == ExecutionSdk.OrderStatus.Filled);

            var stop = engine.StopRunAsync(run.RunId);
            // Give the stop time to reach report admission; before the fix it closed admission
            // here, so the report published below could no longer reach the session.
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            stop.IsCompleted.Should().BeFalse("the stop must wait for the settled fill's report");
            accounting.Release();

            (await stop.WaitAsync(WaitBudget)).Should().BeTrue();
            (await strategy.FillReceived.Task.WaitAsync(WaitBudget)).Symbol.Should().Be("AAPL");
            var retained = (await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot))
                .GetRunByIdAsync(run.RunId))!;
            retained.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed,
                retained.ExceptionMessage ?? retained.Reason);
            retained.TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
            retained.Metrics!.Fills.Should().ContainSingle(fill => fill.Symbol == "AAPL" && fill.FilledQuantity == 1,
                "the fill the OMS settled before the stop belongs to this run's results");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    /// <summary>
    /// A resumed run's orders can already carry fills the OMS knows from before this engine
    /// attached the run (as after a host restart). Those reports are never republished, so they
    /// must not hold the stop open until the drain timeout.
    /// </summary>
    [Fact]
    public async Task StopRun_WithFillSettledBeforeLaunch_DoesNotWaitForItsReport()
    {
        var repository = new InMemoryRunRepository();
        var hub = new Meridian.Execution.Live.LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        cache.RecordTrade("AAPL", new Meridian.Contracts.Domain.Models.Trade(
            DateTimeOffset.UtcNow, "AAPL", 100m, 100, Meridian.Contracts.Domain.Enums.AggressorSide.Buy, 1));
        var paperGateway = new Meridian.Execution.PaperTradingGateway(
            NullLogger<Meridian.Execution.PaperTradingGateway>.Instance,
            liveFeed: cache,
            costOptions: new PaperTradingCostOptions { CommissionRate = 0m, CommissionMinimum = 0m });
        await using var oms = new OrderManagementSystem(paperGateway, NullLogger<OrderManagementSystem>.Instance);
        await oms.StartAsync();
        var strategy = new RecordingOrderLiveStrategy("prior-fill-strategy");
        var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
        var run = CreatePaperRun(strategy.StrategyId, new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);

        // An order of this run filled before the engine attached it.
        var prior = await oms.PlaceOrderAsync(new ExecutionSdk.OrderRequest
        {
            Symbol = "AAPL",
            Side = ExecutionSdk.OrderSide.Buy,
            Type = ExecutionSdk.OrderType.Market,
            Quantity = 1m,
            ClientOrderId = $"{BuildRunOrderPrefix(run.RunId)}{Guid.NewGuid():N}"
        });
        prior.Success.Should().BeTrue(prior.ErrorMessage);
        oms.GetOrder(prior.OrderId)!.FilledQuantity.Should().Be(1m);

        await using var engine = CreateEngine(repository, hub, cache, oms, catalog,
            new LiveTradingEngineOptions { StopFillDrainTimeout = TimeSpan.FromSeconds(30) });
        (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
        await WaitForLifecycleAsync(repository, run.RunId, StrategyRunLifecycleEventType.Activated);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        (await engine.StopRunAsync(run.RunId).WaitAsync(WaitBudget)).Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "a fill settled before the run was attached is never republished to this engine");
    }

    /// <summary>Holds the OMS accounting handoff for the first fill until the test releases it.</summary>
    private sealed class HeldTradeEventPublisher : ITradeEventPublisher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public void Publish(TradeExecutedEvent tradeEvent)
            => throw new NotSupportedException("The OMS hands fills off through PublishAsync.");

        public async Task PublishAsync(TradeExecutedEvent tradeEvent)
        {
            Held.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }
}
