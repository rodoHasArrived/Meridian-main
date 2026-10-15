using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Trading;
using Meridian.Storage.Operations;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleExecutionFactTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "Meridian.Tests", "strategy-execution-facts", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ActivationRecoveryAndResume_PreserveOrderedPolicyEvidenceAcrossDurableRetries()
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var store = new StrategyRunStore(history);
        var start = StrategyLifecycleInterop.EvaluateStart(nameof(StrategyStatus.Registered));
        var warmup = StrategyLifecycleInterop.EvaluateWarmupCompleted(nameof(StrategyStatus.WarmingUp));
        var resume = StrategyLifecycleInterop.EvaluateStart(nameof(StrategyStatus.Paused));
        var requested = StrategyLifecycleFactMapping.Attach(
            StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "execution-facts").RequestStart(),
            StrategyRunLifecycleEventType.StartRequested, start);
        await store.RecordRunAsync(requested);
        var activated = StrategyLifecycleFactMapping.Attach(
            requested.Activated(), StrategyRunLifecycleEventType.Activated, start, warmup);
        await store.RecordRunAsync(activated);
        var suspended = activated.Suspended();
        suspended.LifecycleFacts.Should().BeEmpty("host suspension establishes no F# state transition");
        await store.RecordRunAsync(suspended);
        var recoveryRequested = StrategyLifecycleFactMapping.Attach(
            suspended.RequestStart(), StrategyRunLifecycleEventType.StartRequested, start);
        await store.RecordRunAsync(recoveryRequested);
        var recovered = StrategyLifecycleFactMapping.Attach(
            recoveryRequested.Recovered(), StrategyRunLifecycleEventType.Recovered, warmup);
        await store.RecordRunAsync(recovered);
        await store.RecordRunAsync(recovered.PauseRequested());
        var paused = StrategyLifecycleFactMapping.Attach(
            recovered.Paused(), StrategyRunLifecycleEventType.Paused,
            StrategyLifecycleInterop.EvaluatePause(nameof(StrategyStatus.Running)));
        await store.RecordRunAsync(paused);
        var resumeRequested = StrategyLifecycleFactMapping.Attach(
            paused.RequestStart(), StrategyRunLifecycleEventType.StartRequested, resume);
        await store.RecordRunAsync(resumeRequested);
        var resumed = StrategyLifecycleFactMapping.Attach(
            resumeRequested.Resumed(), StrategyRunLifecycleEventType.Resumed, resume, resume);
        await store.RecordRunAsync(resumed);
        var stopRequested = StrategyLifecycleFactMapping.Attach(
            resumed.RequestStop(), StrategyRunLifecycleEventType.StopRequested,
            StrategyLifecycleInterop.EvaluateStop(nameof(StrategyStatus.Running)));
        await store.RecordRunAsync(stopRequested);
        var completed = StrategyLifecycleFactMapping.Attach(
            stopRequested.Complete(metrics: null), StrategyRunLifecycleEventType.Completed,
            StrategyLifecycleInterop.EvaluateStopCompleted(nameof(StrategyStatus.Stopping)));
        await store.RecordRunAsync(completed);

        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        await restarted.RecordRunAsync(activated);
        await restarted.RecordRunAsync(recovered);
        await restarted.RecordRunAsync(resumed);
        (await restarted.GetRunByIdAsync(requested.RunId))!.LastLifecycleEvent
            .Should().Be(StrategyRunLifecycleEventType.Completed);
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{requested.RunId}"
        });
        records.Select(record => record.EventType).Should().Equal(
            "StartRequested", "Activated", "Suspended", "StartRequested", "Recovered",
            "PauseRequested", "Paused", "StartRequested", "Resumed", "StopRequested", "Completed");
        records.SelectMany(record => ReadFacts(record.Data["strategyRunSnapshotJson"])).Should().Equal(
            "strategy-start-requested", "strategy-warmup-entered",
            "strategy-warmup-completed", "strategy-running-entered",
            "strategy-start-requested", "strategy-warmup-entered",
            "strategy-warmup-completed", "strategy-running-entered", "strategy-paused",
            "strategy-resume-requested", "strategy-running-entered",
            "strategy-stop-requested", "strategy-stopping-entered", "strategy-stopped");
    }

    [Theory]
    [InlineData(StrategyRunLifecycleEventType.Activated)]
    [InlineData(StrategyRunLifecycleEventType.Recovered)]
    [InlineData(StrategyRunLifecycleEventType.Resumed)]
    [InlineData(StrategyRunLifecycleEventType.Suspended)]
    public async Task ExecutionEvent_CanRetainPersistenceWarningWithoutLosingFacts(
        StrategyRunLifecycleEventType eventType)
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var store = new StrategyRunStore(history);
        var run = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, $"warning-{eventType}");
        var warmup = StrategyLifecycleInterop.EvaluateWarmupCompleted(nameof(StrategyStatus.WarmingUp));
        var entry = eventType == StrategyRunLifecycleEventType.Suspended
            ? run.Suspended()
            : StrategyLifecycleFactMapping.Attach(run.Activated(), eventType, warmup);
        await store.RecordRunAsync(entry);
        var warning = entry.EvidencePersistenceFailed(
            new IOException("Suspension evidence acknowledgment failed."),
            "The completed external action requires evidence reconciliation.");

        await store.RecordRunAsync(warning);

        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        var retained = await restarted.GetRunByIdAsync(run.RunId);
        retained!.LifecycleFacts.Should().Equal(entry.LifecycleFacts);
        retained.EndedAt.Should().BeNull();
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{run.RunId}"
        });
        records[^1].TerminalOutcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static IEnumerable<string> ReadFacts(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        return document.RootElement.GetProperty("lifecycleFacts").EnumerateArray()
            .Select(fact => fact.GetString()!).ToArray();
    }
}
