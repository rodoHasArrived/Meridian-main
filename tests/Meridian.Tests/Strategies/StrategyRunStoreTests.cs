using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunStoreTests
{
    [Fact]
    public async Task GetRunByIdAsync_ReturnsRecordedRun()
    {
        var store = new StrategyRunStore();
        var expected = CreateRun("run-a", "strategy-1", RunType.Backtest, startedAt: new DateTimeOffset(2026, 4, 20, 14, 0, 0, TimeSpan.Zero));

        await store.RecordRunAsync(expected);

        var actual = await store.GetRunByIdAsync("run-a");

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GetRunsByIdsAsync_ReturnsRequestedRunsInInputOrder()
    {
        var store = new StrategyRunStore();
        var runA = CreateRun("run-a", "strategy-1", RunType.Backtest, new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero));
        var runB = CreateRun("run-b", "strategy-1", RunType.Paper, new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero));
        var runC = CreateRun("run-c", "strategy-2", RunType.Live, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero), endedAt: null);

        await store.RecordRunAsync(runA);
        await store.RecordRunAsync(runB);
        await store.RecordRunAsync(runC);

        var results = await store.GetRunsByIdsAsync(["run-c", "missing", "run-a"]);

        results.Select(static run => run.RunId).Should().Equal("run-c", "run-a");
    }

    [Fact]
    public async Task QueryRunsAsync_FiltersAndOrdersByLastUpdatedDescending()
    {
        var store = new StrategyRunStore();
        var olderCompleted = CreateRun(
            "run-old",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var newestCompleted = CreateRun(
            "run-new",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var runningPaper = CreateRun(
            "run-running",
            "strategy-1",
            RunType.Paper,
            new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
            endedAt: null);

        await store.RecordRunAsync(olderCompleted);
        await store.RecordRunAsync(newestCompleted);
        await store.RecordRunAsync(runningPaper);

        var results = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            RunTypes: [RunType.Backtest],
            Status: StrategyRunStatus.Completed,
            Limit: 10));

        results.Select(static run => run.RunId).Should().Equal("run-new", "run-old");
    }

    [Fact]
    public async Task RecordRunAsync_ReplacesExistingRunAcrossIndexes()
    {
        var store = new StrategyRunStore();
        var original = CreateRun(
            "run-replace",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var updated = original with
        {
            EndedAt = new DateTimeOffset(2026, 4, 20, 11, 30, 0, TimeSpan.Zero),
            TerminalStatus = StrategyRunStatus.Failed
        };

        await store.RecordRunAsync(original);
        await store.RecordRunAsync(updated);

        var byId = await store.GetRunByIdAsync("run-replace");
        var byStrategy = new List<StrategyRunEntry>();
        await foreach (var run in store.GetRunsAsync("strategy-1"))
        {
            byStrategy.Add(run);
        }

        var queried = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            Status: StrategyRunStatus.Failed,
            Limit: 10));

        byId.Should().Be(updated);
        byStrategy.Should().ContainSingle();
        byStrategy[0].Should().Be(updated);
        queried.Should().ContainSingle().Which.Should().Be(updated);
    }

    [Fact]
    public async Task DurableStore_ReplaysStartedAndCompletedRunAfterRestart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Backtest,
                "run-durable",
                datasetReference: "dataset:prices",
                engine: "MeridianNative",
                parameterSet: new Dictionary<string, string> { ["lookback"] = "20" });

            await store.RecordRunAsync(started);
            await store.RecordRunAsync(started.Complete(metrics: null));

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var replayed = await restarted.GetRunByIdAsync("run-durable");
            var events = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseType = StrategyRunStore.CaseType
            });

            replayed.Should().NotBeNull();
            replayed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
            replayed.EndedAt.Should().NotBeNull();
            replayed.InputHashSha256.Should().Be(started.InputHashSha256);
            events.Select(static item => item.EventType).Should().Equal("Started", "Completed");
            events.Should().OnlyContain(static item =>
                item.Data.ContainsKey("strategyRunSnapshotJson") &&
                item.Data["schemaVersion"] == StrategyRunStore.SnapshotSchemaVersion);
            events[0].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey]
                .Should().Be("0");
            events[1].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey]
                .Should().Be(events[0].Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            events[1].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey]
                .Should().Be(events[0].RecordHashSha256);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(StrategyRunLifecycleEventType.Started, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.Paused, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.Completed, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.StartFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.PauseFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.Failed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.StopFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.Cancelled, OperationTerminalState.Blocked)]
    [InlineData(StrategyRunLifecycleEventType.EvidencePersistenceFailed, OperationTerminalState.CompletedWithWarnings)]
    public async Task DurableStore_RetainsValidTerminalOutcome(
        StrategyRunLifecycleEventType eventType,
        OperationTerminalState expectedState)
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, $"run-{eventType}");
            var exception = new InvalidOperationException($"Simulated {eventType} failure.");
            var terminal = eventType switch
            {
                StrategyRunLifecycleEventType.Started => started,
                StrategyRunLifecycleEventType.Paused => started.Paused(),
                StrategyRunLifecycleEventType.Completed => started.Complete(metrics: null),
                StrategyRunLifecycleEventType.StartFailed => started.StartFailed(exception),
                StrategyRunLifecycleEventType.PauseFailed => started.PauseFailed(exception),
                StrategyRunLifecycleEventType.Failed => started.Fail(exception),
                StrategyRunLifecycleEventType.StopFailed => started.StopFailed(exception),
                StrategyRunLifecycleEventType.Cancelled => started.Cancel(exception),
                StrategyRunLifecycleEventType.EvidencePersistenceFailed => started.EvidencePersistenceFailed(
                    exception,
                    "External action completed; final evidence append failed."),
                _ => throw new ArgumentOutOfRangeException(nameof(eventType))
            };

            await store.RecordLifecycleEventAsync(terminal, eventType);

            var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            });
            retained.Should().ContainSingle();
            retained[0].TerminalOutcome.Should().NotBeNull();
            retained[0].TerminalOutcome!.State.Should().Be(expectedState);
            VerifiedOperationOutcomeValidator.Validate(retained[0].TerminalOutcome!).Should().BeEmpty();
            if (eventType == StrategyRunLifecycleEventType.EvidencePersistenceFailed)
            {
                retained[0].TerminalOutcome!.Recovery.Should().ContainSingle()
                    .Which.Should().Match<OperationRecoveryAction>(action =>
                        action.ActionId == "reconcile-external-state" &&
                        !action.Retryable &&
                        action.Guidance.Contains("Do not repeat", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void ComputeInputHash_IsDeterministicAcrossParameterInsertionOrder()
    {
        var first = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            null,
            "MeridianNative",
            new Dictionary<string, string> { ["window"] = "20", ["threshold"] = "0.5" });
        var second = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            null,
            "MeridianNative",
            new Dictionary<string, string> { ["threshold"] = "0.5", ["window"] = "20" });

        first.Should().Be(second).And.HaveLength(64);
    }

    [Fact]
    public void ComputeInputHash_BindsRunLineageAndOperationalScope()
    {
        var baseline = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            "feed",
            "MeridianNative",
            new Dictionary<string, string> { ["window"] = "20" },
            parentRunId: "parent-1",
            portfolioId: "portfolio-1",
            ledgerReference: "ledger-1",
            auditReference: "audit-1",
            fundProfileId: "fund-1");

        var mutations = new[]
        {
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-2", "portfolio-1", "ledger-1", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-2", "ledger-1", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-2", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-1", "audit-2", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-1", "audit-1", "fund-2")
        };

        mutations.Should().OnlyContain(hash => !string.Equals(hash, baseline, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DurableStore_RejectsOperationalScopeMutationAfterFirstLifecycleEvent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var started = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-scope-mutation") with
            {
                ParentRunId = "parent-1",
                FundProfileId = "fund-1",
                InputHashSha256 = null
            };
            await store.RecordRunAsync(started);

            var changed = started.Complete(metrics: null) with
            {
                PortfolioId = "different-portfolio",
                InputHashSha256 = null
            };
            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*immutable run identity changed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DurableStore_RejectsSuppliedInputHashThatDoesNotMatchCanonicalInputs()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var run = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-forged-hash") with
            {
                InputHashSha256 = new string('a', 64)
            };

            var action = () => store.RecordRunAsync(run);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*does not match the canonical hash*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task QueryRunsAsync_ProjectsPausedAndStoppedLifecycleStates()
    {
        var store = new StrategyRunStore();
        var paused = StrategyRunEntry.Start(
            "strategy-paused",
            "Paused Strategy",
            RunType.Paper,
            "run-paused").Paused();
        var stopped = StrategyRunEntry.Start(
            "strategy-stopped",
            "Stopped Strategy",
            RunType.Paper,
            "run-stopped").Complete(metrics: null) with
        {
            TerminalStatus = StrategyRunStatus.Stopped
        };
        await store.RecordRunAsync(paused);
        await store.RecordRunAsync(stopped);

        var pausedRuns = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            Status: StrategyRunStatus.Paused));
        var stoppedRuns = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            Status: StrategyRunStatus.Stopped));

        pausedRuns.Should().ContainSingle().Which.RunId.Should().Be("run-paused");
        stoppedRuns.Should().ContainSingle().Which.RunId.Should().Be("run-stopped");
    }

    [Fact]
    public async Task Replay_RejectsSucceededReceiptBoundToFailedLifecycleEvent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var failed = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-outcome-binding").StartFailed(new InvalidOperationException("start failed"));
            var snapshotJson = JsonSerializer.Serialize(failed);
            var snapshotHash = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
            var eventId = $"strategy-run:{failed.RunId}:{failed.LastLifecycleEvent}:{snapshotHash}";
            var eventAt = failed.LifecycleEventAtUtc!.Value;
            var evidence = new OperationEvidenceReference(
                "outcome-binding-evidence",
                "test",
                "Synthetic receipt used to verify lifecycle binding.",
                ContentHashSha256: failed.InputHashSha256,
                CapturedAtUtc: eventAt);
            var wronglySucceeded = new VerifiedOperationOutcome(
                eventId,
                $"strategy-run.{StrategyRunLifecycleEventType.StartFailed}",
                OperationTerminalState.Succeeded,
                failed.StartedAt,
                eventAt,
                1,
                failed.CorrelationId,
                failed.InputHashSha256,
                [new OperationPostcondition(
                    "synthetic-success",
                    "Synthetic success receipt.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: [evidence.EvidenceId])],
                [evidence],
                [],
                [],
                []);
            await history.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = $"strategy-run:{failed.RunId}",
                CaseType = StrategyRunStore.CaseType,
                HistoryEventId = eventId,
                EventType = failed.LastLifecycleEvent.ToString(),
                OccurredAtUtc = eventAt,
                ActorId = failed.ActorId!,
                Reason = failed.Reason!,
                CorrelationId = failed.CorrelationId!,
                InputHashSha256 = failed.InputHashSha256!,
                Data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["schemaVersion"] = StrategyRunStore.SnapshotSchemaVersion,
                    ["strategyRunSnapshotJson"] = snapshotJson,
                    [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] = "0"
                },
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = null,
                    CurrentState = failed.LastLifecycleEvent.ToString(),
                    TransitionedAtUtc = eventAt
                },
                Evidence = [evidence],
                TerminalOutcome = wronglySucceeded
            });
            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));

            var action = () => restarted.GetRunByIdAsync(failed.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*terminal receipt is not bound*lifecycle event 'StartFailed'*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsRegressionAfterCompletedRun()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-regression") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            var completedAt = startedAt.AddMinutes(5);
            var completed = started.Complete(metrics: null) with
            {
                EndedAt = completedAt,
                LifecycleEventAtUtc = completedAt
            };
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);
            var regressed = started.Paused() with
            {
                LifecycleEventAtUtc = completedAt.AddMinutes(1)
            };

            var action = () => store.RecordLifecycleEventAsync(
                regressed,
                StrategyRunLifecycleEventType.Paused);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Completed*Paused*not allowed*");
            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidCompletedToPausedRegression()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 13, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-regression") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            var completedAt = startedAt.AddMinutes(5);
            var completed = started.Complete(metrics: null) with
            {
                EndedAt = completedAt,
                LifecycleEventAtUtc = completedAt
            };
            await writer.RecordRunAsync(started);
            await writer.RecordRunAsync(completed);
            var regressed = started.Paused() with
            {
                LifecycleEventAtUtc = completedAt.AddMinutes(1)
            };
            await AppendRawSnapshotAsync(
                history,
                regressed,
                StrategyRunLifecycleEventType.Completed);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*Completed*Paused*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidLifecycleTimestampBeforeRunStart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 14, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-time") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            await writer.RecordRunAsync(started);
            var outOfOrder = started.Paused() with
            {
                LifecycleEventAtUtc = startedAt.AddSeconds(-1)
            };
            await AppendRawSnapshotAsync(
                history,
                outOfOrder,
                StrategyRunLifecycleEventType.Started);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*timestamp precedes the run start*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RepeatedCanonicalTerminalSnapshotIsIdempotent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-idempotent");
            var completed = started.Complete(metrics: null);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);

            await store.RecordRunAsync(completed);

            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsChangedRepeatedTerminalSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-terminal-mutation");
            var completed = started.Complete(metrics: null);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);
            var changed = completed with
            {
                Reason = "Changed after terminal retention.",
                RetainedEvidenceReferences = ["evidence:changed-after-completion"]
            };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Completed*Completed*not allowed*");
            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidChangedRepeatedTerminalSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-terminal");
            var completed = started.Complete(metrics: null);
            await writer.RecordRunAsync(started);
            await writer.RecordRunAsync(completed);
            var changed = completed with
            {
                Reason = "Forged changed terminal snapshot.",
                RetainedEvidenceReferences = ["evidence:forged-terminal-change"]
            };
            await AppendRawSnapshotAsync(
                history,
                changed,
                StrategyRunLifecycleEventType.Completed);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*Completed*Completed*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsChangedRepeatedRecoveryAttemptSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-recovery-mutation");
            var paused = started.Paused();
            var recovery = paused.RecoveryAttempted(started.RunId, attemptNumber: 2);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(paused);
            await store.RecordRunAsync(recovery);
            var changed = recovery with { Reason = "Changed recovery evidence." };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*RecoveryAttempted*RecoveryAttempted*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static async Task AppendRawSnapshotAsync(
        FileOperationalCaseHistoryStore history,
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType previousEvent)
    {
        var eventAt = entry.LifecycleEventAtUtc!.Value;
        var snapshotJson = JsonSerializer.Serialize(entry);
        var snapshotHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        var caseId = $"strategy-run:{entry.RunId}";
        var predecessor = (await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = caseId,
            CaseType = StrategyRunStore.CaseType
        })).LastOrDefault();
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = StrategyRunStore.SnapshotSchemaVersion,
            ["strategyRunSnapshotJson"] = snapshotJson,
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] =
                (predecessor?.Sequence ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (predecessor is not null)
        {
            data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey] =
                predecessor.RecordHashSha256;
        }

        await history.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = caseId,
            CaseType = StrategyRunStore.CaseType,
            HistoryEventId = $"strategy-run:{entry.RunId}:{entry.LastLifecycleEvent}:{snapshotHash}",
            EventType = entry.LastLifecycleEvent.ToString(),
            OccurredAtUtc = eventAt,
            ActorId = entry.ActorId!,
            Reason = entry.Reason!,
            CorrelationId = entry.CorrelationId!,
            InputHashSha256 = entry.InputHashSha256!,
            Data = data,
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = previousEvent.ToString(),
                CurrentState = entry.LastLifecycleEvent.ToString(),
                TransitionedAtUtc = eventAt
            }
        });
    }

    private static StrategyRunEntry CreateRun(
        string runId,
        string strategyId,
        RunType runType,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null,
        StrategyRunStatus? terminalStatus = null)
    {
        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyId,
            RunType: runType,
            StartedAt: startedAt,
            EndedAt: endedAt,
            Metrics: null,
            PortfolioId: $"{strategyId}-{runId}-portfolio",
            LedgerReference: $"{strategyId}-{runId}-ledger",
            AuditReference: $"{runId}-audit",
            Engine: runType.ToString(),
            TerminalStatus: terminalStatus);
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "meridian-strategy-run-store-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
