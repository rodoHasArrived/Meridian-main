using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed class LiveStrategyRunSessionCompletionTests
{
    [Fact]
    public async Task UncomputedXirr_RoundTripsThroughDurableHistoryWithoutBecomingZero()
    {
        using var fixture = new SessionFixture(new CleanupStrategy());

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        var result = (await fixture.ReloadAsync())!.Metrics!;
        double.IsNaN(result.Metrics.Xirr).Should().BeTrue("this session has no closed-trade IRR calculation");
        result.EngineMetadata!.CoverageWarnings.Should().Contain(warning => warning.Contains("XIRR", StringComparison.Ordinal));
        var history = await fixture.ReadHistoryAsync();
        using var snapshot = JsonDocument.Parse(history.Last().Data["strategyRunSnapshotJson"]);
        snapshot.RootElement.GetProperty("metrics").GetProperty("metrics").GetProperty("xirr").GetString().Should().Be("NaN");

        var invalidMetrics = result.Metrics with { SharpeRatio = double.NaN };
        var serializeOtherNonfiniteMetric = () => JsonSerializer.Serialize(invalidMetrics);
        serializeOtherNonfiniteMetric.Should().Throw<ArgumentException>("named-value support is scoped to XIRR");
    }

    [Theory]
    [InlineData("day-end")]
    [InlineData("finished")]
    [InlineData("stop")]
    [InlineData("stop-status")]
    public async Task CleanupFailure_ReturnsFailedReceiptAndRetainsMetricsAndFailure(string stage)
    {
        using var fixture = new SessionFixture(new CleanupStrategy(stage));

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        var retained = await fixture.ReloadAsync();
        retained!.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        retained.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
        retained.Metrics.Should().NotBeNull("valid trading evidence must survive a cleanup failure");
        retained.ExceptionMessage.Should().Contain(stage == "stop-status" ? "requires Stopped" : stage);
        fixture.Strategy.FinishedCalls.Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
        fixture.Strategy.DayEndCalls.Should().Be(1);
        var history = await fixture.ReadHistoryAsync();
        history.Last().TerminalOutcome!.State.Should().Be(OperationTerminalState.Failed);
        history.Should().NotContain(record => record.EventType.EndsWith("Completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultipleCleanupFailures_PreserveAllReasonsWithoutRepeatingCallbacks()
    {
        using var fixture = new SessionFixture(new CleanupStrategy("day-end", "finished", "stop"));

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Failed);
        var retained = await fixture.ReloadAsync();
        retained!.ExceptionMessage.Should().Contain("day-end").And.Contain("finished").And.Contain("stop");
        retained.ExceptionType.Should().Be(typeof(AggregateException).FullName);
        fixture.Strategy.DayEndCalls.Should().Be(1);
        fixture.Strategy.FinishedCalls.Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task TerminalPersistenceFailure_RetainsWarningAndMetricsWithoutRepeatingCleanup()
    {
        using var fixture = new SessionFixture(new CleanupStrategy(), terminalFailures: 1);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        var retained = await fixture.ReloadAsync();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.EvidencePersistenceFailed);
        retained.Metrics.Should().NotBeNull();
        retained.EndedAt.Should().NotBeNull();
        retained.ExceptionMessage.Should().Contain("terminal persistence failed");
        fixture.Strategy.FinishedCalls.Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
        (await fixture.ReadHistoryAsync()).Last().TerminalOutcome!.State.Should().Be(outcome.State);
    }

    [Fact]
    public async Task CleanupAndTerminalPersistenceFailure_KeepFailedOutcomeAndBothReasons()
    {
        using var fixture = new SessionFixture(new CleanupStrategy("stop"), terminalFailures: 1);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Failed);
        var retained = await fixture.ReloadAsync();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.EvidencePersistenceFailed);
        retained.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        retained.ExceptionMessage.Should().Contain("stop").And.Contain("terminal persistence failed");
        retained.Metrics.Should().NotBeNull();
        (await fixture.ReadHistoryAsync()).Last().TerminalOutcome!.State.Should().Be(OperationTerminalState.Failed);
    }

    [Fact]
    public async Task ReconcileStoppedStrategy_CannotEraseRetainedSessionCleanupFailure()
    {
        using var fixture = new SessionFixture(new CleanupStrategy("finished"), terminalFailures: 1);
        var failed = await fixture.Session.ExecuteAsync(CancellationToken.None);
        fixture.Strategy.Status.Should().Be(StrategyStatus.Stopped);
        var historyCount = (await fixture.ReadHistoryAsync()).Count;
        await using var manager = new StrategyLifecycleManager(fixture.Repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(fixture.Strategy);

        var reconciled = await manager.ReconcileExternalStateAsync(fixture.Strategy.StrategyId);

        reconciled.State.Should().Be(OperationTerminalState.Failed);
        reconciled.OperationId.Should().Be(failed!.OperationId);
        var retained = await fixture.ReloadAsync();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.EvidencePersistenceFailed);
        retained.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
        retained.ExceptionMessage.Should().Contain("finished").And.Contain("terminal persistence failed");
        retained.Metrics.Should().NotBeNull();
        (await fixture.ReadHistoryAsync()).Should().HaveCount(historyCount);
        fixture.Strategy.FinishedCalls.Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentOperatorStopIntent_JoinsRetainedIntentWithoutFailingCleanShutdown()
    {
        using var fixture = new SessionFixture(new CleanupStrategy(), concurrentStopIntent: true);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        var retained = await fixture.ReloadAsync();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.ExceptionMessage.Should().BeNull();
        retained.Metrics.Should().NotBeNull();
        (await fixture.ReadHistoryAsync()).Count(record => record.EventType.EndsWith("StopRequested", StringComparison.Ordinal))
            .Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task CommittedTerminalWriteWithLostResponse_VerifiesReceiptWithoutWarningOrDuplicateWrite()
    {
        using var fixture = new SessionFixture(new CleanupStrategy(), terminalFailures: 1, commitBeforeThrow: true);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        var retained = await fixture.ReloadAsync();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.Metrics.Should().NotBeNull();
        var history = await fixture.ReadHistoryAsync();
        history.Count(record => record.EventType.EndsWith("Completed", StringComparison.Ordinal)).Should().Be(1);
        history.Should().NotContain(record => record.EventType.EndsWith("EvidencePersistenceFailed", StringComparison.Ordinal));
        fixture.Strategy.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task CommittedWarningWithLostResponse_ReturnsVerifiedWarning()
    {
        using var fixture = new SessionFixture(new CleanupStrategy(), terminalFailures: 2, commitWarningBeforeThrow: true);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        (await fixture.ReloadAsync())!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.EvidencePersistenceFailed);
        (await fixture.ReadHistoryAsync()).Last().TerminalOutcome!.State.Should().Be(outcome.State);
        fixture.Strategy.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task TerminalAndWarningPersistenceFailures_PropagateBothFailuresWithoutRepeatingCleanup()
    {
        using var fixture = new SessionFixture(new CleanupStrategy("finished"), terminalFailures: 2);

        var action = () => fixture.Session.ExecuteAsync(CancellationToken.None);

        var failure = await action.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("neither its terminal transition nor recovery warning could be retained");
        failure.Which.InnerException.Should().BeOfType<AggregateException>();
        failure.Which.ToString().Should().Contain("finished").And.Contain("terminal persistence failed");
        fixture.Strategy.FinishedCalls.Should().Be(1);
        fixture.Strategy.StopCalls.Should().Be(1);
        (await fixture.ReloadAsync())!.EndedAt.Should().BeNull("no terminal write was retained");
    }

    [Fact]
    public async Task HostStopCannotDowngradePreviouslyRequestedOperatorCompletion()
    {
        using var fixture = new SessionFixture(new CleanupStrategy());
        fixture.Strategy.OnTradeAction = () =>
        {
            fixture.Session.RequestCompletion();
            fixture.Session.RequestStop(completeRun: false);
        };

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        (await fixture.ReloadAsync())!.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HostStopBeforeStartup_LeavesRunOpenWithoutInvokingUnstartedCleanup()
    {
        using var fixture = new SessionFixture(new CleanupStrategy());
        fixture.Session.RequestStop(completeRun: false);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome.Should().BeNull();
        fixture.Strategy.FinishedCalls.Should().Be(0);
        fixture.Strategy.StopCalls.Should().Be(0);
        (await fixture.ReadHistoryAsync()).Should().BeEmpty();
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"session-completion-{Guid.NewGuid():N}");
        private readonly StrategyRunEntry _run = StrategyRunEntry.Start("cleanup-strategy", "Cleanup strategy", RunType.Paper);
        private readonly FileOperationalCaseHistoryStore _history;

        public CleanupStrategy Strategy { get; }
        public LiveStrategyRunSession Session { get; }
        public IStrategyRepository Repository { get; }

        public SessionFixture(CleanupStrategy strategy, int terminalFailures = 0, bool commitBeforeThrow = false,
            bool commitWarningBeforeThrow = false, bool concurrentStopIntent = false)
        {
            Strategy = strategy;
            _history = new FileOperationalCaseHistoryStore(_dataRoot);
            var repository = new FailingTerminalRepository(new StrategyRunStore(_history), terminalFailures, commitBeforeThrow,
                commitWarningBeforeThrow, concurrentStopIntent);
            Repository = repository;
            var gateway = new Mock<IOrderGateway>();
            gateway.SetupGet(item => item.BrokerName).Returns("Paper");
            var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
                new PaperTradingPortfolio(100_000m), new HashSet<string> { "AAPL" });
            Session = new LiveStrategyRunSession(_run, strategy, context, new OneEventFeed(),
                Mock.Of<ExecutionSdk.IOrderManager>(), repository, auditTrail: null,
                NullLogger.Instance, useSynchronousFillFallback: false, fillReportQueueCapacity: 2);
        }

        public Task<StrategyRunEntry?> ReloadAsync() => new StrategyRunStore(
            new FileOperationalCaseHistoryStore(_dataRoot)).GetRunByIdAsync(_run.RunId);

        public Task<IReadOnlyList<OperationalCaseHistoryRecord>> ReadHistoryAsync() => _history.ReadAsync(
            new OperationalCaseHistoryQuery { CaseId = $"strategy-run:{_run.RunId}" }).AsTask();

        public void Dispose()
        {
            if (Directory.Exists(_dataRoot))
                Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private sealed class CleanupStrategy(params string[] failures) : BacktestStrategyBase, ILiveStrategy
    {
        public override string Name => "Cleanup strategy";
        public string StrategyId => "cleanup-strategy";
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;
        public int FinishedCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int DayEndCalls { get; private set; }
        public Action? OnTradeAction { get; set; }

        public override void OnTrade(Trade trade, IBacktestContext ctx) => OnTradeAction?.Invoke();

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            ThrowIfRequested("stop");
            if (!failures.Contains("stop-status"))
                Status = StrategyStatus.Stopped;
            return Task.CompletedTask;
        }

        public override void OnFinished(IBacktestContext ctx)
        {
            FinishedCalls++;
            ThrowIfRequested("finished");
        }

        public override void OnDayEnd(DateOnly date, IBacktestContext ctx)
        {
            DayEndCalls++;
            ThrowIfRequested("day-end");
        }

        private void ThrowIfRequested(string stage)
        {
            if (failures.Contains(stage))
                throw new InvalidOperationException($"{stage} cleanup failed");
        }
    }

    private sealed class OneEventFeed : ILiveMarketEventFeed
    {
        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var timestamp = DateTimeOffset.UtcNow;
            yield return new LiveMarketEvent(timestamp, "AAPL", new Trade(timestamp, "AAPL", 100m, 1, AggressorSide.Buy, 1));
            await Task.CompletedTask;
        }
    }

    private sealed class FailingTerminalRepository(
        StrategyRunStore inner,
        int terminalFailures,
        bool commitBeforeThrow,
        bool commitWarningBeforeThrow,
        bool concurrentStopIntent) : IStrategyRepository
    {
        private int _failuresRemaining = terminalFailures;

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) => inner.RecordRunAsync(entry, ct);

        public async Task RecordLifecycleEventAsync(StrategyRunEntry entry, StrategyRunLifecycleEventType eventType,
            CancellationToken ct = default)
        {
            if (concurrentStopIntent && eventType == StrategyRunLifecycleEventType.StopRequested)
            {
                // Model the operator committing intent between the session's read and append.
                await inner.RecordLifecycleEventAsync(entry with { ActorId = "operator", Reason = "Operator requested stop." }, eventType, ct);
            }

            await inner.RecordLifecycleEventAsync(entry, eventType, ct);
        }

        public async Task<VerifiedOperationOutcome?> RecordLifecycleEventWithOutcomeAsync(StrategyRunEntry entry,
            StrategyRunLifecycleEventType eventType, CancellationToken ct = default)
        {
            if (_failuresRemaining-- > 0)
            {
                if (commitBeforeThrow || commitWarningBeforeThrow && eventType == StrategyRunLifecycleEventType.EvidencePersistenceFailed)
                    await ((IStrategyRepository)inner).RecordLifecycleEventWithOutcomeAsync(entry, eventType, ct);
                throw new IOException("terminal persistence failed");
            }

            return await ((IStrategyRepository)inner).RecordLifecycleEventWithOutcomeAsync(entry, eventType, ct);
        }

        public Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default) => inner.GetRunByIdAsync(runId, ct);
        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) => inner.GetLatestRunAsync(strategyId, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default) => inner.GetRunsAsync(strategyId, ct);
        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) => inner.GetAllRunsAsync(ct);
    }
}
