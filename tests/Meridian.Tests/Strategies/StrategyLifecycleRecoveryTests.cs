using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Interfaces;
using Meridian.Storage.Operations;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleRecoveryTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "Meridian.Tests", "strategy-lifecycle-recovery", Guid.NewGuid().ToString("N"));

    public StrategyLifecycleRecoveryTests() => Directory.CreateDirectory(_dataRoot);

    [Theory]
    [InlineData(StrategyRunLifecycleEventType.StartFailed)]
    [InlineData(StrategyRunLifecycleEventType.Cancelled)]
    public async Task DurableCleanup_PreservesTerminalAttemptIdentityAndReplaysAfterRestart(
        StrategyRunLifecycleEventType terminalEvent)
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var store = new StrategyRunStore(history);
        var terminal = await RetainTerminalAttemptAsync(store, terminalEvent);
        var requested = terminal.RequestStop();
        await store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StopRequested);
        var completed = requested.Complete(metrics: null) with
        {
            EndedAt = terminal.EndedAt,
            TerminalStatus = StrategyRunStatus.Stopped
        };
        await store.RecordLifecycleEventAsync(completed, StrategyRunLifecycleEventType.Completed);

        var restarted = NewStore();
        var replayed = await restarted.GetRunByIdAsync(terminal.RunId);
        replayed.Should().NotBeNull();
        replayed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        replayed.TerminalStatus.Should().Be(StrategyRunStatus.Stopped);
        replayed.EndedAt.Should().Be(terminal.EndedAt);
        replayed.StartedAt.Should().Be(terminal.StartedAt);
        replayed.AttemptId.Should().Be(terminal.AttemptId);
        replayed.CorrelationId.Should().Be(terminal.CorrelationId);
        replayed.InputHashSha256.Should().Be(terminal.InputHashSha256);
        replayed.ParameterSet.Should().BeEquivalentTo(terminal.ParameterSet);
        replayed.RetainedEvidenceReferences.Should().Equal(terminal.RetainedEvidenceReferences);

        var events = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{terminal.RunId}"
        });
        events.Select(static record => record.EventType).Should().Equal(
            "StartRequested", terminalEvent.ToString(), "StopRequested", "Completed");
        events[1].TerminalOutcome.Should().NotBeNull();
        events[1].TerminalOutcome!.State.Should().Be(terminalEvent == StrategyRunLifecycleEventType.StartFailed
            ? OperationTerminalState.Failed : OperationTerminalState.Blocked);
        events[^1].TerminalOutcome!.State.Should().Be(OperationTerminalState.Succeeded);
        events.Where(static record => record.TerminalOutcome is not null).Should()
            .OnlyContain(static record => VerifiedOperationOutcomeValidator.Validate(record.TerminalOutcome!).Count == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DurableCleanup_FailureOrCancellationCanBeRetriedWithoutReopeningRun(bool cancelCleanup)
    {
        var store = NewStore();
        var terminal = await RetainTerminalAttemptAsync(store, StrategyRunLifecycleEventType.StartFailed);
        var requested = terminal.RequestStop();
        await store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StopRequested);
        var failed = cancelCleanup
            ? requested.Cancel(new OperationCanceledException("Cleanup was cancelled.")) with { EndedAt = terminal.EndedAt }
            : requested.StopFailed(new InvalidOperationException("Cleanup failed."));
        await store.RecordLifecycleEventAsync(failed, failed.LastLifecycleEvent);

        var restarted = NewStore();
        var retained = (await restarted.GetRunByIdAsync(terminal.RunId))!;
        retained.LastLifecycleEvent.Should().Be(cancelCleanup
            ? StrategyRunLifecycleEventType.Cancelled : StrategyRunLifecycleEventType.StopFailed);
        retained.EndedAt.Should().Be(terminal.EndedAt);
        var retry = retained.RequestStop();
        await restarted.RecordLifecycleEventAsync(retry, StrategyRunLifecycleEventType.StopRequested);
        await restarted.RecordLifecycleEventAsync(retry.Complete(metrics: null) with
        {
            EndedAt = terminal.EndedAt,
            TerminalStatus = StrategyRunStatus.Stopped
        }, StrategyRunLifecycleEventType.Completed);

        var replayed = await NewStore().GetRunByIdAsync(terminal.RunId);
        replayed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        replayed.EndedAt.Should().Be(terminal.EndedAt);
    }

    [Theory]
    [InlineData(StrategyRunLifecycleEventType.StartFailed)]
    [InlineData(StrategyRunLifecycleEventType.Cancelled)]
    public async Task DurableCleanup_RejectsChangesToTerminalTimestampOrStatus(StrategyRunLifecycleEventType terminalEvent)
    {
        var store = NewStore();
        var terminal = await RetainTerminalAttemptAsync(store, terminalEvent);
        var requested = terminal.RequestStop();
        var mutations = new[]
        {
            requested with { EndedAt = null },
            requested with { EndedAt = terminal.StartedAt },
            requested with { TerminalStatus = StrategyRunStatus.Stopped }
        };

        foreach (var changed in mutations)
        {
            var append = () => store.RecordLifecycleEventAsync(changed, StrategyRunLifecycleEventType.StopRequested);
            await append.Should().ThrowAsync<InvalidOperationException>();
        }

        await store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StopRequested);
        var rewrittenCompletion = requested.Complete(metrics: null);
        var complete = () => store.RecordLifecycleEventAsync(rewrittenCompletion, StrategyRunLifecycleEventType.Completed);
        await complete.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained run end timestamp changed*");
    }

    [Fact]
    public async Task DurableCleanup_RejectsInputAndEvidenceMutation()
    {
        var store = NewStore();
        var terminal = await RetainTerminalAttemptAsync(store, StrategyRunLifecycleEventType.StartFailed);
        var requested = terminal.RequestStop();
        var mutations = new[]
        {
            requested with { StrategyName = "Changed strategy", InputHashSha256 = null },
            requested with { RetainedEvidenceReferences = ["evidence://different-run"], InputHashSha256 = null },
            requested with { ParameterSet = new Dictionary<string, string> { ["lookback"] = "100" }, InputHashSha256 = null },
            requested with { OutputMetadata = new Dictionary<string, string> { ["result"] = "overwritten" } }
        };

        foreach (var changed in mutations)
        {
            var append = () => store.RecordLifecycleEventAsync(changed, StrategyRunLifecycleEventType.StopRequested);
            await append.Should().ThrowAsync<InvalidOperationException>();
        }

        var wrongHash = () => store.RecordLifecycleEventAsync(
            requested with { InputHashSha256 = new string('0', 64) }, StrategyRunLifecycleEventType.StopRequested);
        await wrongHash.Should().ThrowAsync<ArgumentException>().WithMessage("*input hash does not match*");

        (await store.GetRunByIdAsync(terminal.RunId))!.LastLifecycleEvent
            .Should().Be(StrategyRunLifecycleEventType.StartFailed);
    }

    [Fact]
    public async Task DurableCleanup_RejectsAttemptLineageChangesOnRequestAndCompletion()
    {
        var store = NewStore();
        var terminal = await RetainTerminalAttemptAsync(store, StrategyRunLifecycleEventType.StartFailed);
        var requested = terminal.RequestStop();
        var invalidRequests = new[]
        {
            requested with { AttemptId = "different-attempt" },
            requested with { AttemptNumber = terminal.AttemptNumber + 1 },
            requested with { RecoveryParentRunId = "different-parent" }
        };
        foreach (var changed in invalidRequests)
        {
            var append = () => store.RecordLifecycleEventAsync(changed, StrategyRunLifecycleEventType.StopRequested);
            await append.Should().ThrowAsync<InvalidOperationException>();
        }

        await store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StopRequested);
        var completed = requested.Complete(metrics: null) with
        {
            EndedAt = terminal.EndedAt,
            TerminalStatus = StrategyRunStatus.Stopped
        };
        var invalidCompletions = new[]
        {
            completed with { AttemptId = "different-attempt" },
            completed with { AttemptNumber = terminal.AttemptNumber + 1 },
            completed with { RecoveryParentRunId = "different-parent" }
        };
        foreach (var changed in invalidCompletions)
        {
            var append = () => store.RecordLifecycleEventAsync(changed, StrategyRunLifecycleEventType.Completed);
            await append.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*cleanup attempt identity changed*");
        }
    }

    [Fact]
    public async Task DurableCleanup_DoesNotPermitOtherTerminalRunsToReopen()
    {
        foreach (var terminalEvent in new[] { StrategyRunLifecycleEventType.Completed, StrategyRunLifecycleEventType.Failed })
        {
            var store = NewStore();
            var run = StrategyRunEntry.Start("other-strategy", "Other Strategy", RunType.Paper, $"run-{terminalEvent}");
            var terminal = terminalEvent == StrategyRunLifecycleEventType.Completed
                ? run.Complete(metrics: null) : run.Fail(new InvalidOperationException("Execution failed."));
            await store.RecordLifecycleEventAsync(terminal, terminalEvent);
            var reopen = () => store.RecordLifecycleEventAsync(terminal.RequestStop(), StrategyRunLifecycleEventType.StopRequested);
            await reopen.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task DurableCleanup_RejectsEndTimestampOnAnActiveStopRequest()
    {
        var store = NewStore();
        var run = StrategyRunEntry.Start("active-strategy", "Active Strategy", RunType.Paper, "active-run");
        await store.RecordLifecycleEventAsync(run, StrategyRunLifecycleEventType.Started);
        var requested = run.RequestStop() with { EndedAt = run.StartedAt };
        var append = () => store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StopRequested);

        await append.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-terminal lifecycle event*retains a run end timestamp*");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Manager_DurableFaultRecoveryRetainsOriginalAttemptAndStartsANewRun(
        bool cancelWarmup, bool cancelCleanup)
    {
        var store = NewStore();
        var strategy = new RecoveryStrategy { CancelWarmup = cancelWarmup, CancelCleanup = cancelCleanup };
        var manager = new StrategyLifecycleManager(store, NullLogger<StrategyLifecycleManager>.Instance);
        manager.Register(strategy);

        var failed = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);
        failed.State.Should().Be(cancelWarmup ? OperationTerminalState.Blocked : OperationTerminalState.Failed);
        var failedRun = (await store.GetLatestRunAsync(strategy.StrategyId))!;
        var failedStop = await manager.StopAsync(strategy.StrategyId);
        failedStop.State.Should().Be(cancelCleanup ? OperationTerminalState.Blocked : OperationTerminalState.Failed);
        var retainedFailure = (await store.GetLatestRunAsync(strategy.StrategyId))!;
        retainedFailure.EndedAt.Should().Be(failedRun.EndedAt);
        retainedFailure.InputHashSha256.Should().Be(failedRun.InputHashSha256);

        strategy.FailCleanup = false;
        var stopped = await manager.StopAsync(strategy.StrategyId);
        stopped.State.Should().Be(OperationTerminalState.Succeeded);
        var completedRun = (await store.GetLatestRunAsync(strategy.StrategyId))!;
        completedRun.EndedAt.Should().Be(failedRun.EndedAt);
        completedRun.RunId.Should().Be(failedRun.RunId);
        strategy.FailWarmup = false;
        var started = await manager.StartAsync(strategy.StrategyId, Context(), RunType.Paper);
        started.State.Should().Be(OperationTerminalState.Succeeded);

        var replayedStore = NewStore();
        var recovered = (await replayedStore.GetLatestRunAsync(strategy.StrategyId))!;
        recovered.RunId.Should().NotBe(failedRun.RunId);
        recovered.RecoveryParentRunId.Should().Be(failedRun.RunId);
        recovered.AttemptNumber.Should().Be(2);
        recovered.EndedAt.Should().BeNull();
        (await replayedStore.GetRunByIdAsync(failedRun.RunId))!.EndedAt.Should().Be(failedRun.EndedAt);
    }

    private StrategyRunStore NewStore() => new(new FileOperationalCaseHistoryStore(_dataRoot));

    private static async Task<StrategyRunEntry> RetainTerminalAttemptAsync(
        StrategyRunStore store, StrategyRunLifecycleEventType terminalEvent)
    {
        var run = StrategyRunEntry.StartWithEvidence(
            "recovery-strategy", "Recovery Strategy", RunType.Paper, "recovery-run",
            datasetReference: "dataset:prices", engine: "BrokerPaper",
            parameterSet: new Dictionary<string, string> { ["lookback"] = "20" },
            retainedEvidenceReferences: ["evidence://strategy-runs/recovery-run"]);
        var requested = run.RequestStart();
        await store.RecordLifecycleEventAsync(requested, StrategyRunLifecycleEventType.StartRequested);
        var terminal = terminalEvent == StrategyRunLifecycleEventType.StartFailed
            ? requested.StartFailed(new InvalidOperationException("Warmup failed."))
            : requested.Cancel(new OperationCanceledException("Warmup was cancelled."));
        await store.RecordLifecycleEventAsync(terminal, terminalEvent);
        return terminal;
    }

    private static IExecutionContext Context() => Substitute.For<IExecutionContext>();

    public void Dispose() => Directory.Delete(_dataRoot, recursive: true);

    private sealed class RecoveryStrategy : LiveStrategyBase
    {
        public override string StrategyId => "manager-recovery-strategy";
        public override string Name => "Manager Recovery Strategy";
        public bool FailWarmup { get; set; } = true;
        public bool FailCleanup { get; set; } = true;
        public bool CancelWarmup { get; init; }
        public bool CancelCleanup { get; init; }

        protected override Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct)
        {
            if (FailWarmup)
            {
                throw CancelWarmup
                    ? (Exception)new OperationCanceledException("Warmup was cancelled.")
                    : new InvalidOperationException("Warmup failed.");
            }

            return Task.CompletedTask;
        }

        protected override Task OnStoppedAsync(CancellationToken ct)
        {
            if (FailCleanup)
            {
                throw CancelCleanup
                    ? (Exception)new OperationCanceledException("Cleanup was cancelled.")
                    : new InvalidOperationException("Cleanup failed.");
            }

            return Task.CompletedTask;
        }
    }
}
