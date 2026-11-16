using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Live;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

/// <summary>
/// An operator stop racing a fill the OMS already accepted. The fill must reach the run, and the
/// run must end in exactly one valid terminal transition on the durable run store.
/// </summary>
public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task OperatorStop_WithOmsAcceptedFillInAccountingHandoff_DeliversFillAndStopsCleanly()
    {
        using var history = new RunHistoryFixture();
        var store = new StrategyRunStore(history.Store);
        var hub = new LiveMarketEventHub();
        var cache = new LiveMarketDataCache();
        var gateway = new ControllableExecutionGateway();
        var handoff = new BlockingTradeEventPublisher();
        var strategy = new RecordingOrderLiveStrategy("stop-fill-race-strategy");
        var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: handoff);
        await oms.StartAsync();
        var engine = CreateEngine(store, hub, cache, oms, catalog);
        var run = StrategyRunEntry.StartWithEvidence(
            strategy.StrategyId, strategy.Name, RunType.Paper, "stop-fill-race-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await store.RecordRunAsync(run);

        try
        {
            (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
            await WaitForLifecycleAsync(store, run.RunId, StrategyRunLifecycleEventType.Activated);
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => !gateway.Requests.IsEmpty);
            var request = FindRequest(gateway, run.RunId);

            // The OMS accepts the fill and blocks in its accounting handoff, before publishing the
            // report to the engine's subscription: exactly the window an operator stop can hit.
            await gateway.PublishAsync(BuildFill(request));
            await handoff.Entered.Task.WaitAsync(WaitBudget);
            oms.GetOrder(request.ClientOrderId!)!.Status.Should().Be(ExecutionSdk.OrderStatus.Filled);

            var stop = engine.StopRunAsync(run.RunId);
            // Without the accepted-fill drain the stop closes report admission and finishes here.
            await Task.WhenAny(stop, Task.Delay(TimeSpan.FromMilliseconds(500)));
            handoff.Release();

            (await stop.WaitAsync(WaitBudget)).Should().BeTrue();
            (await strategy.FillReceived.Task.WaitAsync(WaitBudget)).Symbol.Should().Be("AAPL");

            var retained = (await new StrategyRunStore(history.Store).GetRunByIdAsync(run.RunId))!;
            retained.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
            retained.TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
            retained.ExceptionMessage.Should().BeNull();
            retained.Metrics.Should().NotBeNull();
            retained.Metrics!.Fills.Should().ContainSingle(fill => fill.Symbol == "AAPL" && fill.FillPrice == 123m,
                "a fill the OMS accepted before the stop is part of the stopped run's metrics");
        }
        finally
        {
            handoff.Release();
            await engine.DisposeAsync();
        }

        var afterShutdown = (await new StrategyRunStore(history.Store).GetRunByIdAsync(run.RunId))!;
        afterShutdown.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed,
            "no undeliverable-fill failure may follow the clean stop");
    }

    [Fact]
    public async Task OperatorStop_RacingEngineRetainedFailure_KeepsThatSingleFailedTerminal()
    {
        using var history = new RunHistoryFixture();
        var repository = new ConcurrentFailureRepository(new StrategyRunStore(history.Store));
        var strategy = new RecordingOrderLiveStrategy("stop-failure-race-strategy");
        var run = StrategyRunEntry.StartWithEvidence(
            strategy.StrategyId, strategy.Name, RunType.Paper, "stop-failure-race-run",
            parameterSet: new Dictionary<string, string> { ["symbols"] = "AAPL" });
        await repository.RecordRunAsync(run);
        var engine = CreateEngine(
            repository, new LiveMarketEventHub(), new LiveMarketDataCache(), new RecordingOrderManager(100m),
            new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy));

        try
        {
            (await engine.TryLaunchAsync(run)).Launched.Should().BeTrue();
            await WaitForLifecycleAsync(repository, run.RunId, StrategyRunLifecycleEventType.Activated);

            // The session's terminal append loses a race with the engine retaining Failed for an
            // undeliverable fill. The retained failure must stand as the run's only terminal.
            var outcome = await engine.StopRunWithOutcomeAsync(run.RunId).WaitAsync(WaitBudget);

            outcome.Should().NotBeNull();
            outcome!.State.Should().NotBe(OperationTerminalState.Succeeded);
            var retained = (await new StrategyRunStore(history.Store).GetRunByIdAsync(run.RunId))!;
            retained.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
            retained.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
            retained.EndedAt.Should().Be(repository.InjectedFailure!.EndedAt,
                "the retained end timestamp cannot change after the run ended");
            retained.ExceptionMessage.Should().Be(ConcurrentFailureRepository.FailureMessage);
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    /// <summary>
    /// Durable store wrapper that retains an engine-style Failed transition immediately before the
    /// session's own terminal append, reproducing the undeliverable-fill race deterministically.
    /// </summary>
    private sealed class ConcurrentFailureRepository(StrategyRunStore inner) : IStrategyRepository
    {
        public const string FailureMessage = "Accepted execution report could not reach its strategy session.";
        private int _injected;

        public StrategyRunEntry? InjectedFailure { get; private set; }

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) =>
            inner.RecordRunAsync(entry, ct);

        public Task RecordLifecycleEventAsync(StrategyRunEntry entry,
            StrategyRunLifecycleEventType eventType, CancellationToken ct = default) =>
            inner.RecordLifecycleEventAsync(entry, eventType, ct);

        public async Task<VerifiedOperationOutcome?> RecordLifecycleEventWithOutcomeAsync(
            StrategyRunEntry entry,
            StrategyRunLifecycleEventType eventType,
            CancellationToken ct = default)
        {
            if (eventType == StrategyRunLifecycleEventType.Completed &&
                Interlocked.Exchange(ref _injected, 1) == 0)
            {
                var current = (await inner.GetRunByIdAsync(entry.RunId, ct))!;
                InjectedFailure = current.Fail(
                    new InvalidOperationException(FailureMessage),
                    "An accepted broker fill could not reach the live strategy session.");
                await inner.RecordLifecycleEventAsync(InjectedFailure, StrategyRunLifecycleEventType.Failed, ct);
            }

            return await inner.RecordLifecycleEventWithOutcomeAsync(entry, eventType, ct);
        }

        public Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default) =>
            inner.GetRunByIdAsync(runId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default) =>
            inner.GetRunsAsync(strategyId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) =>
            inner.GetAllRunsAsync(ct);

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            inner.GetLatestRunAsync(strategyId, ct);
    }
}
