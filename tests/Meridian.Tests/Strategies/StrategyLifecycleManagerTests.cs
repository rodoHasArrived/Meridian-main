using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleManagerTests
{
    [Fact]
    public async Task PauseAsync_WhenStrategyIsRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.PauseAsync("strategy-1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_WhenStrategyAlreadyRunning_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Running));

        var action = () => manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartAsync_WhenStrategyIsRegistered_RecordsRun()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var outcome = await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        repository.RecordedRuns.Should().HaveCount(2);
        repository.RecordedRuns.Select(static run => run.LastLifecycleEvent).Should().Equal(
            StrategyRunLifecycleEventType.StartRequested,
            StrategyRunLifecycleEventType.Started);
        repository.RecordedRuns[^1].StrategyId.Should().Be("strategy-1");
        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ //
    // StopAsync                                                            //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StopAsync_WhenStrategyIsRunning_RecordsCompletedRun()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var outcome = await manager.StopAsync("strategy-1");

        // Start intent/success, stop intent, then completion.
        repository.RecordedRuns.Should().HaveCount(4);
        repository.RecordedRuns[^1].StrategyId.Should().Be("strategy-1");
        repository.RecordedRuns[^1].EndedAt.Should().NotBeNull();
        repository.RecordedRuns[^1].TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
        repository.RecordedRuns[^2].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StopRequested);
        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.StopAsync("unknown-strategy");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopAsync_WhenStrategyIsRegisteredButNotStarted_ThrowsInvalidOperationException()
    {
        // A registered-but-not-started strategy cannot be stopped (invalid lifecycle transition)
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.StopAsync("strategy-1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    // ------------------------------------------------------------------ //
    // PauseAsync                                                           //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PauseAsync_WhenStrategyIsRunning_ReturnsSucceededReceipt()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var outcome = await manager.PauseAsync("strategy-1");

        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        repository.RecordedRuns.TakeLast(2).Select(static run => run.LastLifecycleEvent).Should().Equal(
            StrategyRunLifecycleEventType.PauseRequested,
            StrategyRunLifecycleEventType.Paused);
    }

    [Fact]
    public async Task PauseAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.PauseAsync("unknown-strategy");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    // ------------------------------------------------------------------ //
    // GetStatuses                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public void GetStatuses_WhenNoStrategiesRegistered_ReturnsEmptyDictionary()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var statuses = manager.GetStatuses();

        statuses.Should().BeEmpty();
    }

    [Fact]
    public void GetStatuses_WithMultipleRegisteredStrategies_ReturnsAllStatuses()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-a", StrategyStatus.Registered));
        manager.Register(new StubLiveStrategy("strategy-b", StrategyStatus.Registered));

        var statuses = manager.GetStatuses();

        statuses.Should().HaveCount(2);
        statuses.Should().ContainKey("strategy-a");
        statuses.Should().ContainKey("strategy-b");
        statuses["strategy-a"].Should().Be(StrategyStatus.Registered);
        statuses["strategy-b"].Should().Be(StrategyStatus.Registered);
    }

    [Fact]
    public async Task GetStatuses_AfterStart_ReflectsRunningStatus()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var statuses = manager.GetStatuses();
        statuses["strategy-1"].Should().Be(StrategyStatus.Running);
    }

    // ------------------------------------------------------------------ //
    // Register                                                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_WhenStrategyNotRegistered_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.StartAsync("unknown-strategy", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Register_WhenCalledTwiceForSameId_ReplacesExistingEntry()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var statuses = manager.GetStatuses();

        // Re-registering keeps only one entry under the same ID
        statuses.Should().HaveCount(1);
    }

    // ------------------------------------------------------------------ //
    // DisposeAsync                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DisposeAsync_WhenRunningStrategyExists_StopsItGracefully()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new StubLiveStrategy("strategy-1", StrategyStatus.Registered);
        manager.Register(strategy);
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        // Should not throw even when disposing with a running strategy
        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
        strategy.Status.Should().Be(StrategyStatus.Stopped);
    }

    [Fact]
    public async Task DisposeAsync_WhenNoStrategiesRegistered_CompletesWithoutError()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------ //
    // Concurrency                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_ConcurrentCallsForDifferentStrategies_AllComplete()
    {
        // Registering and starting many distinct strategies concurrently should not corrupt
        // internal state — each strategy should end up in Running state with a recorded run.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        const int count = 20;
        for (var i = 0; i < count; i++)
            manager.Register(new StubLiveStrategy($"strategy-{i}", StrategyStatus.Registered));

        var tasks = Enumerable
            .Range(0, count)
            .Select(i => manager.StartAsync($"strategy-{i}", new StubExecutionContext(), RunType.Paper))
            .ToArray();

        await Task.WhenAll(tasks);

        var statuses = manager.GetStatuses();
        statuses.Should().HaveCount(count);
        statuses.Values.Should().AllSatisfy(s => s.Should().Be(StrategyStatus.Running));
        repository.RecordedRuns.Should().HaveCount(count * 2);
    }

    [Fact]
    public async Task Register_ConcurrentRegistrations_KeepsLatestEntry()
    {
        // Registering the same strategy ID from many threads concurrently should not throw;
        // the final registered entry should be present.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        const string strategyId = "shared-strategy";
        var tasks = Enumerable
            .Range(0, 50)
            .Select(_ => Task.Run(() => manager.Register(new StubLiveStrategy(strategyId, StrategyStatus.Registered))))
            .ToArray();

        var act = () => Task.WhenAll(tasks);

        await act.Should().NotThrowAsync("concurrent registrations for the same ID must not corrupt state");

        var statuses = manager.GetStatuses();
        statuses.Should().ContainKey(strategyId);
    }

    [Fact]
    public async Task GetStatuses_CalledConcurrentlyWithRegistrations_DoesNotThrow()
    {
        // GetStatuses must not throw when called concurrently while strategies are being added.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var registerTasks = Enumerable
            .Range(0, 30)
            .Select(i => Task.Run(() => manager.Register(new StubLiveStrategy($"s-{i}", StrategyStatus.Registered))))
            .ToArray();
        var readTasks = Enumerable
            .Range(0, 30)
            .Select(_ => Task.Run(() => manager.GetStatuses()))
            .ToArray();

        var act = () => Task.WhenAll(registerTasks.Concat(readTasks));

        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------ //
    // Error paths                                                          //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task StartAsync_WhenRepositoryThrows_PropagatesException()
    {
        // When the repository fails to persist the run, StartAsync should surface the error.
        var repository = new FailingStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

        var action = () => manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*repository*");
    }

    [Fact]
    public async Task StartAsync_WhenStrategyThrows_ReturnsRetainedFailedOutcome()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new ThrowingOnStartStrategy("strategy-throws"));

        var outcome = await manager.StartAsync(
            "strategy-throws",
            new StubExecutionContext(),
            RunType.Paper);

        repository.RecordedRuns.Should().HaveCount(2);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StartFailed);
        repository.RecordedRuns[^1].ExceptionType.Should().Contain(nameof(InvalidOperationException));
        repository.RecordedRuns[^1].ExceptionMessage.Should().Be("Simulated start failure.");
        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_WhenStrategyThrows_ReturnsRetainedFailedOutcome()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new ThrowingOnPauseStrategy("strategy-throws"));
        await manager.StartAsync("strategy-throws", new StubExecutionContext(), RunType.Paper);

        var outcome = await manager.PauseAsync("strategy-throws");

        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.PauseFailed);
        repository.RecordedRuns[^1].ExceptionMessage.Should().Be("Simulated pause failure.");
        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_WhenStrategyThrows_ReturnsRetainedFailedOutcome()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new ThrowingOnStopStrategy("strategy-throws"));
        await manager.StartAsync("strategy-throws", new StubExecutionContext(), RunType.Paper);

        var outcome = await manager.StopAsync("strategy-throws");

        repository.RecordedRuns.TakeLast(2).Select(static run => run.LastLifecycleEvent)
            .Should().Equal(
                StrategyRunLifecycleEventType.StopRequested,
                StrategyRunLifecycleEventType.StopFailed);
        repository.RecordedRuns[^1].ExceptionMessage.Should().Be("Simulated stop failure.");
        outcome.State.Should().Be(OperationTerminalState.Failed);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_AfterPause_RetainsRecoveryParentAndAttempt()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);
        var originalRunId = repository.RecordedRuns[0].RunId;
        await manager.PauseAsync("strategy-1");

        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        repository.RecordedRuns[^3].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.RecoveryAttempted);
        repository.RecordedRuns[^2].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.StartRequested);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Started);
        repository.RecordedRuns[^1].RecoveryParentRunId.Should().Be(originalRunId);
        repository.RecordedRuns[^1].AttemptNumber.Should().Be(2);
    }

    [Fact]
    public async Task PauseAsync_WhenFinalEvidenceAppendFails_ReturnsWarningAndReconcilesWithoutRepeatingPause()
    {
        var repository = new FailOnceOnEventRepository(StrategyRunLifecycleEventType.Paused);
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new StubLiveStrategy("strategy-1", StrategyStatus.Registered);
        manager.Register(strategy);
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var warning = await manager.PauseAsync("strategy-1");

        warning.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        VerifiedOperationOutcomeValidator.Validate(warning).Should().BeEmpty();
        warning.Recovery.Should().ContainSingle().Which.Should().Match<OperationRecoveryAction>(action =>
            action.ActionId == "reconcile-external-state" &&
            !action.Retryable &&
            action.Guidance.Contains("Do not repeat", StringComparison.Ordinal));
        strategy.PauseCallCount.Should().Be(1);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(
            StrategyRunLifecycleEventType.EvidencePersistenceFailed);

        var reconciled = await manager.ReconcileExternalStateAsync("strategy-1");

        reconciled.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(reconciled).Should().BeEmpty();
        strategy.PauseCallCount.Should().Be(1);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Paused);
    }

    [Fact]
    public async Task StopAsync_WhenFinalEvidenceAppendFails_ReturnsWarningAndReconcilesWithoutRepeatingStop()
    {
        var repository = new FailOnceOnEventRepository(StrategyRunLifecycleEventType.Completed);
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new StubLiveStrategy("strategy-1", StrategyStatus.Registered);
        manager.Register(strategy);
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var warning = await manager.StopAsync("strategy-1");

        warning.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
        VerifiedOperationOutcomeValidator.Validate(warning).Should().BeEmpty();
        warning.Recovery.Should().ContainSingle().Which.Should().Match<OperationRecoveryAction>(action =>
            action.ActionId == "reconcile-external-state" && !action.Retryable);
        strategy.StopCallCount.Should().Be(1);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(
            StrategyRunLifecycleEventType.EvidencePersistenceFailed);

        var reconciled = await manager.ReconcileExternalStateAsync("strategy-1");

        reconciled.State.Should().Be(OperationTerminalState.Succeeded);
        VerifiedOperationOutcomeValidator.Validate(reconciled).Should().BeEmpty();
        strategy.StopCallCount.Should().Be(1);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
    }

    [Fact]
    public async Task StartAndPause_WithDurableRepository_RetainIntentBeforeSucceededReceipts()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-strategy-lifecycle-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var manager = new StrategyLifecycleManager(
                new StrategyRunStore(history),
                NullLogger<StrategyLifecycleManager>.Instance);
            manager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));

            var started = await manager.StartAsync(
                "strategy-1",
                new StubExecutionContext(),
                RunType.Paper);
            var paused = await manager.PauseAsync("strategy-1");

            started.State.Should().Be(OperationTerminalState.Succeeded);
            paused.State.Should().Be(OperationTerminalState.Succeeded);
            var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseType = StrategyRunStore.CaseType
            });
            retained.Select(static record => record.EventType).Should().Equal(
                StrategyRunLifecycleEventType.StartRequested.ToString(),
                StrategyRunLifecycleEventType.Started.ToString(),
                StrategyRunLifecycleEventType.PauseRequested.ToString(),
                StrategyRunLifecycleEventType.Paused.ToString());
            retained[0].TerminalOutcome.Should().BeNull();
            retained[1].TerminalOutcome!.State.Should().Be(OperationTerminalState.Succeeded);
            retained[2].TerminalOutcome.Should().BeNull();
            retained[3].TerminalOutcome!.State.Should().Be(OperationTerminalState.Succeeded);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PauseAsync_AfterManagerRestart_RehydratesRetainedCurrentRun()
    {
        var repository = new InMemoryStrategyRepository();
        var firstManager = new StrategyLifecycleManager(
            repository,
            NullLogger<StrategyLifecycleManager>.Instance);
        firstManager.Register(new StubLiveStrategy("strategy-1", StrategyStatus.Registered));
        await firstManager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);
        var restartedStrategy = new StubLiveStrategy("strategy-1", StrategyStatus.Running);
        var restartedManager = new StrategyLifecycleManager(
            repository,
            NullLogger<StrategyLifecycleManager>.Instance);
        restartedManager.Register(restartedStrategy);

        var outcome = await restartedManager.PauseAsync("strategy-1");

        outcome.State.Should().Be(OperationTerminalState.Succeeded);
        restartedStrategy.PauseCallCount.Should().Be(1);
        repository.RecordedRuns.TakeLast(2).Select(static run => run.LastLifecycleEvent)
            .Should().Equal(
                StrategyRunLifecycleEventType.PauseRequested,
                StrategyRunLifecycleEventType.Paused);
    }

    [Fact]
    public async Task PauseAsync_ConcurrentCommandsForSameStrategy_InvokeExternalEffectOnce()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new CoordinatedPauseStrategy("strategy-1");
        manager.Register(strategy);
        await manager.StartAsync("strategy-1", new StubExecutionContext(), RunType.Paper);

        var firstPause = manager.PauseAsync("strategy-1");
        await strategy.PauseEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondPause = manager.PauseAsync("strategy-1");
        strategy.ReleasePause.TrySetResult();

        (await firstPause).State.Should().Be(OperationTerminalState.Succeeded);
        var secondAction = () => secondPause;
        await secondAction.Should().ThrowAsync<InvalidOperationException>();
        strategy.PauseCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_WhenAdapterReportsFailureAfterExternalStart_ReconcilesWithoutRepeatingStart()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        var strategy = new ExternalStartThenThrowStrategy("strategy-1");
        manager.Register(strategy);

        var failed = await manager.StartAsync(
            "strategy-1",
            new StubExecutionContext(),
            RunType.Paper);
        var reconciled = await manager.ReconcileExternalStateAsync("strategy-1");

        failed.State.Should().Be(OperationTerminalState.Failed);
        failed.Recovery.Should().ContainSingle().Which.Should().Match<OperationRecoveryAction>(action =>
            action.ActionId == "inspect-and-reconcile" &&
            !action.Retryable &&
            action.Guidance.Contains("Do not retry", StringComparison.Ordinal));
        reconciled.State.Should().Be(OperationTerminalState.Succeeded);
        strategy.StartCallCount.Should().Be(1);
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Started);
        repository.RecordedRuns[^1].EndedAt.Should().BeNull();
        repository.RecordedRuns[^1].TerminalStatus.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_WhenExternalCancellationIsObserved_ReturnsBlockedReceipt()
    {
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(new CancellingOnStartStrategy("strategy-1"));

        var outcome = await manager.StartAsync(
            "strategy-1",
            new StubExecutionContext(),
            RunType.Paper);

        outcome.State.Should().Be(OperationTerminalState.Blocked);
        VerifiedOperationOutcomeValidator.Validate(outcome).Should().BeEmpty();
        repository.RecordedRuns[^1].LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Cancelled);
    }

    [Fact]
    public async Task DisposeAsync_WhenOneStrategyStopThrows_ContinuesAndDisposesRemainder()
    {
        // DisposeAsync must swallow individual strategy errors and keep draining.
        var repository = new InMemoryStrategyRepository();
        var manager = new StrategyLifecycleManager(repository, NullLogger<StrategyLifecycleManager>.Instance);

        var throwingStrategy = new ThrowingOnStopStrategy("strategy-throws");
        var normalStrategy = new StubLiveStrategy("strategy-ok", StrategyStatus.Registered);

        manager.Register(throwingStrategy);
        manager.Register(normalStrategy);
        await manager.StartAsync("strategy-throws", new StubExecutionContext(), RunType.Paper);
        await manager.StartAsync("strategy-ok", new StubExecutionContext(), RunType.Paper);

        // Must not throw even though one strategy's StopAsync will throw.
        var action = () => manager.DisposeAsync().AsTask();

        await action.Should().NotThrowAsync();
        normalStrategy.Status.Should().Be(StrategyStatus.Stopped);
    }

    // ------------------------------------------------------------------ //
    // Helpers                                                               //
    // ------------------------------------------------------------------ //

    private sealed class InMemoryStrategyRepository : IStrategyRepository
    {
        public List<StrategyRunEntry> RecordedRuns { get; } = [];

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            RecordedRuns.Add(entry);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var run in RecordedRuns.Where(run => run.StrategyId == strategyId))
                yield return run;

            await Task.CompletedTask;
        }

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            Task.FromResult(RecordedRuns.LastOrDefault(run => run.StrategyId == strategyId));
    }

    private sealed class FailOnceOnEventRepository(
        StrategyRunLifecycleEventType eventToFail) : IStrategyRepository
    {
        private int _failureRemaining = 1;

        public List<StrategyRunEntry> RecordedRuns { get; } = [];

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            if (entry.LastLifecycleEvent == eventToFail &&
                Interlocked.Exchange(ref _failureRemaining, 0) == 1)
            {
                throw new InvalidOperationException($"Simulated {eventToFail} persistence failure.");
            }

            RecordedRuns.Add(entry);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var run in RecordedRuns.Where(run => run.StrategyId == strategyId))
            {
                ct.ThrowIfCancellationRequested();
                yield return run;
            }

            await Task.CompletedTask;
        }

        public Task<StrategyRunEntry?> GetLatestRunAsync(
            string strategyId,
            CancellationToken ct = default) =>
            Task.FromResult(RecordedRuns.LastOrDefault(run => run.StrategyId == strategyId));

        public Task<StrategyRunEntry?> GetRunByIdAsync(
            string runId,
            CancellationToken ct = default) =>
            Task.FromResult(RecordedRuns.LastOrDefault(run => run.RunId == runId));
    }

    private sealed class StubLiveStrategy(string strategyId, StrategyStatus initialStatus) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = initialStatus;
        public int PauseCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int StartCallCount { get; private set; }

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            StartCallCount++;
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            PauseCallCount++;
            Status = StrategyStatus.Paused;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            Status = StrategyStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class CoordinatedPauseStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;
        public int PauseCallCount { get; private set; }
        public TaskCompletionSource PauseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePause { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public async Task PauseAsync(CancellationToken ct = default)
        {
            PauseCallCount++;
            PauseEntered.TrySetResult();
            await ReleasePause.Task.WaitAsync(ct);
            Status = StrategyStatus.Paused;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            Status = StrategyStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class ExternalStartThenThrowStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;
        public int StartCallCount { get; private set; }

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            StartCallCount++;
            Status = StrategyStatus.Running;
            throw new InvalidOperationException("External start completed before adapter acknowledgement failed.");
        }

        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class CancellingOnStartStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status => StrategyStatus.Registered;
        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default) =>
            throw new OperationCanceledException("Simulated external cancellation.");
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class StubExecutionContext : IExecutionContext
    {
        public IOrderGateway Gateway => throw new NotImplementedException();
        public ILiveFeedAdapter Feed => throw new NotImplementedException();
        public IPortfolioState Portfolio => throw new NotImplementedException();
        public IReadOnlySet<string> Universe { get; } = new HashSet<string>();
        public DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;
        public Meridian.Ledger.IReadOnlyLedger? Ledger => null;
    }

    /// <summary>Repository that always throws on <see cref="RecordRunAsync"/>.</summary>
    private sealed class FailingStrategyRepository : IStrategyRepository
    {
        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated repository failure.");

#pragma warning disable CS1998 // async method body has no awaits
        public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
#pragma warning restore CS1998

        public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default) =>
            Task.FromResult<StrategyRunEntry?>(null);
    }

    /// <summary>Strategy whose <see cref="StopAsync"/> always throws.</summary>
    private sealed class ThrowingOnStopStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default)
        {
            Status = StrategyStatus.Paused;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated stop failure.");

        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class ThrowingOnStartStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated start failure.");

        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class ThrowingOnPauseStrategy(string strategyId) : ILiveStrategy
    {
        public string Name => strategyId;
        public string StrategyId => strategyId;
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated pause failure.");

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
