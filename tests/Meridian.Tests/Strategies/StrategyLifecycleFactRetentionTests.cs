using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Trading;
using Meridian.Storage.Operations;
using Meridian.Strategies.Models;
using Meridian.Strategies.Serialization;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyLifecycleFactRetentionTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "Meridian.Tests", "strategy-lifecycle-facts", Guid.NewGuid().ToString("N"));

    public StrategyLifecycleFactRetentionTests() => Directory.CreateDirectory(_dataRoot);

    [Fact]
    public void EveryEmittedPolicyFact_HasAnExplicitRetainedEventMapping()
    {
        Func<string, StrategyTransitionDto>[] commands =
        [
            StrategyLifecycleInterop.EvaluateStart,
            StrategyLifecycleInterop.EvaluateWarmupCompleted,
            StrategyLifecycleInterop.EvaluatePause,
            StrategyLifecycleInterop.EvaluateStop,
            StrategyLifecycleInterop.EvaluateStopCompleted,
            state => StrategyLifecycleInterop.EvaluateFail(state, "existing fault", "new fault")
        ];
        var run = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "mapping-contract");
        var mappedFacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in Enum.GetNames<StrategyStatus>())
        {
            foreach (var command in commands)
            {
                var verdict = command(state);
                if (!verdict.IsValid)
                    continue;

                // Exclude the persistence fallback, which can carry facts from any external
                // action, so each policy fact must have a specific command-event association.
                var factsForAllEvents = Enum.GetValues<StrategyRunLifecycleEventType>()
                    .Where(eventType => eventType != StrategyRunLifecycleEventType.EvidencePersistenceFailed)
                    .SelectMany(eventType => StrategyLifecycleFactMapping.Attach(run, eventType, verdict).LifecycleFacts)
                    .ToArray();
                factsForAllEvents.Distinct(StringComparer.Ordinal).Should().BeEquivalentTo(verdict.EmittedFacts,
                    "every emitted policy fact must have an explicit lifecycle event association");
                mappedFacts.UnionWith(factsForAllEvents);
            }
        }

        mappedFacts.Should().Contain("strategy-resume-requested");
        mappedFacts.Should().Contain("strategy-stopped");
        mappedFacts.Should().Contain("strategy-faulted");
    }

    [Fact]
    public async Task DurableHistory_PreservesPolicyOrderAndRetriesAfterUnconfirmedAppendExactlyOnce()
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var unconfirmed = new CommitThenThrowStore(history);
        var store = new StrategyRunStore(unconfirmed);
        var run = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "ordered-facts");
        var start = StrategyLifecycleInterop.EvaluateStart(nameof(StrategyStatus.Registered));
        var warmup = StrategyLifecycleInterop.EvaluateWarmupCompleted(nameof(StrategyStatus.WarmingUp));
        var stop = StrategyLifecycleInterop.EvaluateStop(nameof(StrategyStatus.Running));
        var stopped = StrategyLifecycleInterop.EvaluateStopCompleted(nameof(StrategyStatus.Stopping));

        var requested = StrategyLifecycleFactMapping.Attach(
            run.RequestStart(), StrategyRunLifecycleEventType.StartRequested, start);
        await store.RecordRunAsync(requested);
        var started = StrategyLifecycleFactMapping.Attach(
            requested.Started(), StrategyRunLifecycleEventType.Started, start, warmup, warmup);
        Func<Task> unconfirmedAppend = () => store.RecordRunAsync(started);
        await unconfirmedAppend.Should().ThrowAsync<IOException>().WithMessage("*acknowledgment*");
        await store.RecordRunAsync(started);
        await store.RecordRunAsync(started);

        var stopRequested = StrategyLifecycleFactMapping.Attach(
            started.RequestStop(), StrategyRunLifecycleEventType.StopRequested, stop);
        await store.RecordRunAsync(stopRequested);
        var completed = StrategyLifecycleFactMapping.Attach(
            stopRequested.Complete(metrics: null), StrategyRunLifecycleEventType.Completed, stopped);
        await store.RecordRunAsync(completed);
        await store.RecordRunAsync(started);
        (await store.GetRunByIdAsync(run.RunId))!.LastLifecycleEvent
            .Should().Be(StrategyRunLifecycleEventType.Completed, "a delayed retry must not roll back the projection");

        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        await restarted.RecordRunAsync(completed);
        var replayed = await restarted.GetRunByIdAsync(run.RunId);
        replayed!.LifecycleFacts.Should().Equal("strategy-stopped");
        unconfirmed.StartedAppendCount.Should().Be(1);
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{run.RunId}"
        });
        records.Select(record => record.EventType).Should().Equal(
            "StartRequested", "Started", "StopRequested", "Completed");
        records.SelectMany(ReadFacts).Should().Equal(
            "strategy-start-requested", "strategy-warmup-entered",
            "strategy-warmup-completed", "strategy-running-entered",
            "strategy-stop-requested", "strategy-stopping-entered", "strategy-stopped");
    }

    [Theory]
    [InlineData("strategy-stopped", "*does not belong*")]
    [InlineData("future-unmapped-fact", "*no retained event mapping*")]
    public async Task DurableHistory_RejectsFactsWithoutMatchingRetainedEvent(string fact, string expectedError)
    {
        var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        var run = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "invalid-facts") with
        {
            LifecycleFacts = [fact]
        };

        Func<Task> append = () => store.RecordRunAsync(run);

        await append.Should().ThrowAsync<InvalidOperationException>().WithMessage(expectedError);
    }

    [Theory]
    [InlineData(StrategyRunStatus.Failed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunStatus.Cancelled, OperationTerminalState.Blocked)]
    public async Task PersistenceFailure_DoesNotEraseFailedOrCancelledTerminalOutcome(
        StrategyRunStatus terminalStatus,
        OperationTerminalState expectedState)
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var store = new StrategyRunStore(history);
        var started = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "failed-persistence");
        await store.RecordRunAsync(started);
        var cleanupFailure = new InvalidOperationException("Strategy cleanup failed.");
        var terminal = terminalStatus == StrategyRunStatus.Failed
            ? started.Fail(cleanupFailure)
            : started.Cancel(cleanupFailure);
        var fallback = terminal.EvidencePersistenceFailed(
            new AggregateException(cleanupFailure, new IOException("Terminal append failed.")),
            "Terminal strategy failure and final persistence failure are both retained.");
        fallback = StrategyLifecycleFactMapping.Attach(
            fallback, StrategyRunLifecycleEventType.EvidencePersistenceFailed,
            StrategyLifecycleInterop.EvaluateFail(nameof(StrategyStatus.Stopping), "", cleanupFailure.Message));

        await store.RecordRunAsync(fallback);

        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
        var replayed = await restarted.GetRunByIdAsync(started.RunId);
        replayed!.TerminalStatus.Should().Be(terminalStatus);
        replayed.ExceptionMessage.Should().Contain("cleanup failed").And.Contain("append failed");
        replayed.LifecycleFacts.Should().Equal("strategy-faulted");
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{started.RunId}"
        });
        records[^1].TerminalOutcome!.State.Should().Be(expectedState);
        records[^1].TerminalOutcome!.Issues.Should().ContainSingle()
            .Which.Severity.Should().Be(OperationIssueSeverity.Error);
        VerifiedOperationOutcomeValidator.Validate(records[^1].TerminalOutcome!).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySnapshot_WithoutFactsDeserializesToEmptyEvidence(bool useGeneratedMetadata)
    {
        const string snapshot = """
            {"RunId":"legacy","StrategyId":"strategy","StrategyName":"Strategy",
             "RunType":1,"StartedAt":"2026-10-15T00:00:00+00:00","EndedAt":null,"Metrics":null}
            """;

        var entry = useGeneratedMetadata
            ? JsonSerializer.Deserialize<StrategyRunEntry>(snapshot, StrategyRunPersistenceJson.Options)
            : JsonSerializer.Deserialize<StrategyRunEntry>(snapshot);

        entry!.LifecycleFacts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_AcceptsHistoricalWarningForFailedPersistenceOnlyInLegacySnapshot(bool retainFactsField)
    {
        var history = new FileOperationalCaseHistoryStore(_dataRoot);
        var store = new StrategyRunStore(history);
        var failure = StrategyRunEntry.Start("strategy", "Strategy", RunType.Paper, "legacy-warning")
            .Fail(new InvalidOperationException("Cleanup failed."))
            .EvidencePersistenceFailed(new IOException("Append failed."), "Failure could not be fully retained.");
        await store.RecordRunAsync(failure);
        var template = (await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = $"strategy-run:{failure.RunId}"
        })).Single();
        var snapshot = JsonNode.Parse(template.Data["strategyRunSnapshotJson"])!.AsObject();
        if (!retainFactsField)
            snapshot.Remove("lifecycleFacts");
        var snapshotJson = snapshot.ToJsonString();
        var legacyEventId = $"strategy-run:{failure.RunId}:{failure.LastLifecycleEvent}:{Sha256Digest.ComputeUtf8(snapshotJson)}";
        var legacyOutcome = template.TerminalOutcome! with
        {
            OperationId = legacyEventId,
            State = OperationTerminalState.CompletedWithWarnings,
            Postconditions = template.TerminalOutcome!.Postconditions
                .Select(condition => condition with { State = OperationPostconditionState.Satisfied }).ToArray(),
            Issues = template.TerminalOutcome!.Issues
                .Select(issue => issue with { Severity = OperationIssueSeverity.Warning, IsBlocking = false }).ToArray()
        };
        var legacyHistory = new FileOperationalCaseHistoryStore(Path.Combine(_dataRoot, "legacy"));
        await legacyHistory.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = template.CaseId,
            CaseType = template.CaseType,
            HistoryEventId = legacyEventId,
            EventType = template.EventType,
            OccurredAtUtc = template.OccurredAtUtc,
            ActorId = template.ActorId,
            Reason = template.Reason,
            CorrelationId = template.CorrelationId,
            InputHashSha256 = template.InputHashSha256,
            Transition = template.Transition,
            Data = new Dictionary<string, string>(template.Data)
            {
                ["strategyRunSnapshotJson"] = snapshotJson
            },
            Evidence = template.Evidence,
            TerminalOutcome = legacyOutcome
        });
        var replay = new StrategyRunStore(legacyHistory);

        if (retainFactsField)
        {
            Func<Task> read = async () => await replay.GetRunByIdAsync(failure.RunId);
            await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*terminal receipt is not bound*");
        }
        else
        {
            var retained = await replay.GetRunByIdAsync(failure.RunId);
            retained!.TerminalStatus.Should().Be(StrategyRunStatus.Failed);
            retained.LifecycleFacts.Should().BeEmpty();
        }
    }

    private static string[] ReadFacts(OperationalCaseHistoryRecord record)
    {
        using var snapshot = JsonDocument.Parse(record.Data["strategyRunSnapshotJson"]);
        return snapshot.RootElement.GetProperty("lifecycleFacts").EnumerateArray()
            .Select(fact => fact.GetString()!).ToArray();
    }

    public void Dispose() => Directory.Delete(_dataRoot, recursive: true);

    private sealed class CommitThenThrowStore(IOperationalCaseHistoryStore inner) : IOperationalCaseHistoryStore
    {
        public int StartedAppendCount { get; private set; }

        public async ValueTask<OperationalCaseHistoryRecord> AppendAsync(
            OperationalCaseHistoryAppendRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.AppendAsync(request, cancellationToken);
            if (request.EventType == nameof(StrategyRunLifecycleEventType.Started))
            {
                StartedAppendCount++;
                if (StartedAppendCount == 1)
                    throw new IOException("Lost append acknowledgment after durable commit.");
            }

            return result;
        }

        public ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
            OperationalCaseHistoryQuery query,
            CancellationToken cancellationToken = default) => inner.ReadAsync(query, cancellationToken);
    }
}
