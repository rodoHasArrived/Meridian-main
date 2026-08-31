using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Storage.Operations;
using Xunit;

namespace Meridian.Tests.Storage.Operations;

public sealed class FileOperationalCaseHistoryStoreTests : IDisposable
{
    private const string InputHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "meridian-case-history-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppendAsync_AssignsSequenceAndHashChain_AndReplaysAfterRestart()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);

        var first = await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        var second = await store.AppendAsync(CreateRequest("event-2", "case-2", "Started"));
        var third = await store.AppendAsync(CreateRequest("event-3", "case-1", "Completed") with
        {
            Data = ExpectedPredecessor(first),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Completed",
                TransitionedAtUtc = first.OccurredAtUtc
            }
        });

        first.Sequence.Should().Be(1);
        first.PreviousRecordHashSha256.Should().BeNull();
        OperationalCaseHistoryHashing.HasValidRecordHash(first).Should().BeTrue();
        second.Sequence.Should().Be(2);
        second.PreviousRecordHashSha256.Should().Be(first.RecordHashSha256);
        third.Sequence.Should().Be(3);
        third.PreviousRecordHashSha256.Should().Be(second.RecordHashSha256);

        var restarted = new FileOperationalCaseHistoryStore(_dataRoot);
        var replay = await restarted.ReadAsync(new OperationalCaseHistoryQuery { CaseId = "case-1" });

        replay.Select(static record => record.HistoryEventId).Should().Equal("event-1", "event-3");
        replay.Should().OnlyContain(static record =>
            OperationalCaseHistoryHashing.HasValidRecordHash(record));
    }

    [Fact]
    public async Task AppendAsync_RejectsDuplicateEventIdWithoutChangingHistory()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));

        var action = () => store.AppendAsync(
            CreateRequest("event-1", "case-2", "Started")).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already retained*");
        var replay = await store.ReadAsync(new OperationalCaseHistoryQuery());
        replay.Should().ContainSingle();
    }

    [Fact]
    public async Task AppendAsync_WhenIdenticalEventIsReplayed_ReturnsRetainedRecord()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-1", "case-1", "Started");

        var first = await store.AppendAsync(request);
        var replayed = await store.AppendAsync(request);

        replayed.Should().BeEquivalentTo(first);
        var history = await store.ReadAsync(new OperationalCaseHistoryQuery());
        history.Should().ContainSingle();
    }

    [Fact]
    public async Task AppendAsync_SnapshotsMutableRequestBeforeWaitingForProcessLock()
    {
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            TimeProvider.System,
            TimeSpan.FromSeconds(2));
        Directory.CreateDirectory(Path.GetDirectoryName(store.HistoryPath)!);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] = "0",
            ["mutable"] = "before"
        };
        var request = CreateRequest("event-1", "case-1", "Started") with { Data = data };

        Task<OperationalCaseHistoryRecord> appendTask;
        using (var processLock = new FileStream(
                   store.HistoryPath + ".lock",
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            appendTask = store.AppendAsync(request).AsTask();
            data["mutable"] = "after";
        }

        var retained = await appendTask;
        retained.Data["mutable"].Should().Be("before");
    }

    [Fact]
    public async Task AppendAsync_RejectsStaleExpectedCasePredecessorUnderAppendLock()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var first = await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        var expectedPredecessor = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] =
                first.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey] =
                first.RecordHashSha256
        };
        var pause = CreateRequest("event-2", "case-1", "Paused") with
        {
            Data = expectedPredecessor,
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Paused",
                TransitionedAtUtc = first.OccurredAtUtc
            }
        };
        var staleStop = CreateRequest("event-3", "case-1", "Stopped") with
        {
            Data = new Dictionary<string, string>(expectedPredecessor, StringComparer.Ordinal),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Stopped",
                TransitionedAtUtc = first.OccurredAtUtc
            }
        };

        await store.AppendAsync(pause);
        var action = () => store.AppendAsync(staleStop).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected predecessor sequence*actual*");
    }

    [Fact]
    public async Task AppendAsync_RejectsStatefulEventWithoutExpectedCasePredecessor()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-1", "case-1", "Started") with
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Stateful operational case-history events must provide*expectedPreviousCaseSequence*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_ConcurrentInstancesWithSameCaseHead_OnlyOneTransitionCommits()
    {
        var firstStore = new FileOperationalCaseHistoryStore(_dataRoot);
        var secondStore = new FileOperationalCaseHistoryStore(_dataRoot);
        var started = await firstStore.AppendAsync(CreateRequest("event-started", "case-1", "Started"));
        var expectedPredecessor = ExpectedPredecessor(started);
        var pause = CreateRequest("event-paused", "case-1", "Paused") with
        {
            Data = expectedPredecessor,
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Paused",
                TransitionedAtUtc = started.OccurredAtUtc
            }
        };
        var stop = CreateRequest("event-stopped", "case-1", "Stopped") with
        {
            Data = new Dictionary<string, string>(expectedPredecessor, StringComparer.Ordinal),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Stopped",
                TransitionedAtUtc = started.OccurredAtUtc
            }
        };

        var errors = await Task.WhenAll(
            Record.ExceptionAsync(() => firstStore.AppendAsync(pause).AsTask()),
            Record.ExceptionAsync(() => secondStore.AppendAsync(stop).AsTask()));

        errors.Count(static exception => exception is null).Should().Be(1);
        var conflict = errors.Single(static exception => exception is not null);
        conflict.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("expected predecessor sequence");
        var history = await firstStore.ReadAsync(new OperationalCaseHistoryQuery { CaseId = "case-1" });
        history.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendAsync_AssignmentOnlyEventPreservesEffectiveTransitionState()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var started = await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        var assigned = await store.AppendAsync(CreateRequest("event-2", "case-1", "Assigned") with
        {
            Data = ExpectedPredecessor(started),
            Transition = null,
            Assignment = new OperationalCaseAssignment
            {
                PreviousAssigneeId = null,
                AssigneeId = "operator-2",
                AssignedBy = "operator-1",
                AssignedAtUtc = started.OccurredAtUtc
            }
        });
        var completedRequest = CreateRequest("event-3", "case-1", "Completed") with
        {
            Data = ExpectedPredecessor(assigned),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Completed",
                TransitionedAtUtc = started.OccurredAtUtc
            }
        };

        var completed = await store.AppendAsync(completedRequest);

        completed.Sequence.Should().Be(3);
        completed.Transition!.PreviousState.Should().Be("Started");
    }

    [Fact]
    public async Task AppendAsync_RejectsStalePreviousAssigneeUnderAppendLock()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var started = await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        var assigned = await store.AppendAsync(CreateRequest("event-2", "case-1", "Assigned") with
        {
            Data = ExpectedPredecessor(started),
            Transition = null,
            Assignment = new OperationalCaseAssignment
            {
                PreviousAssigneeId = null,
                AssigneeId = "operator-2",
                AssignedBy = "operator-1",
                AssignedAtUtc = started.OccurredAtUtc
            }
        });
        var staleReassignment = CreateRequest("event-3", "case-1", "Reassigned") with
        {
            Data = ExpectedPredecessor(assigned),
            Transition = null,
            Assignment = new OperationalCaseAssignment
            {
                PreviousAssigneeId = "stale-operator",
                AssigneeId = "operator-3",
                AssignedBy = "operator-1",
                AssignedAtUtc = started.OccurredAtUtc
            }
        };

        var action = () => store.AppendAsync(staleReassignment).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected previous assignee 'stale-operator'*retained assignee is 'operator-2'*");
    }

    [Fact]
    public async Task ReadAsync_RejectsTailRollbackBehindExternalChainHead()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        await store.AppendAsync(CreateRequest("event-2", "case-2", "Started"));
        await store.AppendAsync(CreateRequest("event-3", "case-3", "Started"));
        var lines = await File.ReadAllLinesAsync(store.HistoryPath);
        await File.WriteAllLinesAsync(store.HistoryPath, lines.Take(2));

        var action = () => store.ReadAsync(new OperationalCaseHistoryQuery()).AsTask();

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*truncated or rolled back*sequence 3*");
    }

    [Fact]
    public async Task AppendAsync_WhenProcessLockRemainsHeld_TimesOutWithRecoveryGuidance()
    {
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(75));
        Directory.CreateDirectory(Path.GetDirectoryName(store.HistoryPath)!);
        using var processLock = new FileStream(
            store.HistoryPath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var action = () => store.AppendAsync(
            CreateRequest("event-1", "case-1", "Started")).AsTask();

        await action.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Verify that no stalled Meridian process retains the lock, then retry*");
    }

    [Fact]
    public async Task ReadAndAppend_RejectTamperedHistoryWithoutTruncatingIt()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var started = await store.AppendAsync(CreateRequest("event-1", "case-1", "Started"));
        await store.AppendAsync(CreateRequest("event-2", "case-1", "Completed") with
        {
            Data = ExpectedPredecessor(started),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = "Started",
                CurrentState = "Completed",
                TransitionedAtUtc = started.OccurredAtUtc
            }
        });
        var retained = await File.ReadAllTextAsync(store.HistoryPath);
        var tampered = retained.Replace("Started", "Altered", StringComparison.Ordinal);
        await File.WriteAllTextAsync(store.HistoryPath, tampered);

        var read = () => store.ReadAsync(new OperationalCaseHistoryQuery()).AsTask();
        var append = () => store.AppendAsync(
            CreateRequest("event-3", "case-1", "RecoveryAttempted")).AsTask();

        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*record hash is invalid*");
        await append.Should().ThrowAsync<InvalidDataException>().WithMessage("*record hash is invalid*");
        (await File.ReadAllTextAsync(store.HistoryPath)).Should().Be(tampered);
    }

    [Fact]
    public async Task ConcurrentStoreInstances_SerializeTheGlobalChain()
    {
        var firstStore = new FileOperationalCaseHistoryStore(_dataRoot);
        var secondStore = new FileOperationalCaseHistoryStore(_dataRoot);

        var writes = Enumerable.Range(1, 20)
            .Select(index => (index % 2 == 0 ? firstStore : secondStore)
                .AppendAsync(CreateRequest($"event-{index}", $"case-{index}", "Started"))
                .AsTask());

        await Task.WhenAll(writes);

        var replay = await firstStore.ReadAsync(new OperationalCaseHistoryQuery());
        replay.Should().HaveCount(20);
        replay.Select(static record => record.Sequence).Should().Equal(Enumerable.Range(1, 20).Select(static value => (long)value));
        replay.Should().OnlyContain(static record =>
            OperationalCaseHistoryHashing.HasValidRecordHash(record));
    }

    [Fact]
    public async Task ReadAsync_RejectsBlankOrMalformedRecords()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(store.HistoryPath)!);
        await File.WriteAllTextAsync(store.HistoryPath, "{not-json}\n");

        var action = () => store.ReadAsync(new OperationalCaseHistoryQuery()).AsTask();

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*could not be parsed*");
    }

    [Fact]
    public async Task AppendAsync_RejectsTerminalReceiptFromAnotherOperation()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateTerminalRequest() with
        {
            TerminalOutcome = CreateTerminalRequest().TerminalOutcome! with
            {
                CorrelationId = "unrelated-correlation"
            }
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*correlation identity must match*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("OccurredAtUtc")]
    [InlineData("Transition.TransitionedAtUtc")]
    [InlineData("Assignment.AssignedAtUtc")]
    [InlineData("Retry.AttemptedAtUtc")]
    [InlineData("Exception.OccurredAtUtc")]
    [InlineData("Approval.DecidedAtUtc")]
    [InlineData("Evidence.CapturedAtUtc")]
    [InlineData("RecoveryAttempt.StartedAtUtc")]
    [InlineData("RecoveryAttempt.CompletedAtUtc")]
    public async Task AppendAsync_NonUtcCaseHistoryTimestamp_RejectsBeforePersistence(
        string timestampField)
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-non-utc", "case-1", "Started");
        var occurredAtUtc = request.OccurredAtUtc;
        var sameInstantWithNonZeroOffset = occurredAtUtc.ToOffset(TimeSpan.FromHours(-7));
        request = timestampField switch
        {
            "OccurredAtUtc" => request with
            {
                OccurredAtUtc = sameInstantWithNonZeroOffset
            },
            "Transition.TransitionedAtUtc" => request with
            {
                Transition = request.Transition! with
                {
                    TransitionedAtUtc = sameInstantWithNonZeroOffset
                }
            },
            "Assignment.AssignedAtUtc" => request with
            {
                Assignment = new OperationalCaseAssignment
                {
                    AssigneeId = "operator-2",
                    AssignedBy = "operator-1",
                    AssignedAtUtc = sameInstantWithNonZeroOffset
                }
            },
            "Retry.AttemptedAtUtc" => request with
            {
                Retries =
                [
                    new OperationalCaseRetry
                    {
                        Attempt = 1,
                        AttemptedAtUtc = sameInstantWithNonZeroOffset,
                        Reason = "Retry after a transient storage failure."
                    }
                ]
            },
            "Exception.OccurredAtUtc" => request with
            {
                Exceptions =
                [
                    new OperationalCaseException
                    {
                        ExceptionType = "InvalidOperationException",
                        Message = "The operation failed.",
                        OccurredAtUtc = sameInstantWithNonZeroOffset
                    }
                ]
            },
            "Approval.DecidedAtUtc" => request with
            {
                Approvals =
                [
                    new OperationalCaseApproval
                    {
                        ApprovalId = "approval-1",
                        Decision = "Approved",
                        DecidedBy = "operator-1",
                        DecidedAtUtc = sameInstantWithNonZeroOffset,
                        Reason = "Approved after evidence review."
                    }
                ]
            },
            "Evidence.CapturedAtUtc" => request with
            {
                Evidence =
                [
                    new OperationEvidenceReference(
                        "evidence-1",
                        "lifecycle-receipt",
                        "Retained lifecycle receipt.",
                        ContentHashSha256: InputHash,
                        CapturedAtUtc: sameInstantWithNonZeroOffset)
                ]
            },
            "RecoveryAttempt.StartedAtUtc" => request with
            {
                RecoveryAttempts =
                [
                    new OperationalCaseRecoveryAttempt
                    {
                        RecoveryActionId = "recovery-1",
                        Attempt = 1,
                        StartedAtUtc = sameInstantWithNonZeroOffset,
                        Result = "Started"
                    }
                ]
            },
            "RecoveryAttempt.CompletedAtUtc" => request with
            {
                RecoveryAttempts =
                [
                    new OperationalCaseRecoveryAttempt
                    {
                        RecoveryActionId = "recovery-1",
                        Attempt = 1,
                        StartedAtUtc = occurredAtUtc.AddMinutes(-1),
                        CompletedAtUtc = sameInstantWithNonZeroOffset,
                        Result = "Completed"
                    }
                ]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(timestampField), timestampField, null)
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*must be UTC*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("StartedAtUtc")]
    [InlineData("CompletedAtUtc")]
    public async Task AppendAsync_NonUtcTerminalOutcomeTimestamp_RejectsBeforePersistence(
        string timestampField)
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateTerminalRequest();
        var outcome = request.TerminalOutcome!;
        var sameInstantWithNonZeroOffset = (timestampField == "StartedAtUtc"
                ? outcome.StartedAtUtc
                : outcome.CompletedAtUtc)
            .ToOffset(TimeSpan.FromHours(-7));
        request = request with
        {
            TerminalOutcome = timestampField == "StartedAtUtc"
                ? outcome with { StartedAtUtc = sameInstantWithNonZeroOffset }
                : outcome with { CompletedAtUtc = sameInstantWithNonZeroOffset }
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*StartedAtUtc and CompletedAtUtc must be UTC timestamps*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_NonUtcPersistenceClock_RejectsBeforePersistence()
    {
        var nonUtcNow = new DateTimeOffset(2026, 7, 19, 5, 0, 0, TimeSpan.FromHours(-7));
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            new FixedTimeProvider(nonUtcNow));

        var action = () => store.AppendAsync(
            CreateRequest("event-non-utc-clock", "case-1", "Started")).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*persistence clock returned a non-UTC timestamp*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_RetainedNonUtcPersistenceTimestamp_RejectsCorruptHistory()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var retained = await store.AppendAsync(
            CreateRequest("event-1", "case-1", "Started"));
        var nonUtcRecord = retained with
        {
            PersistedAtUtc = retained.PersistedAtUtc.ToOffset(TimeSpan.FromHours(-7)),
            RecordHashSha256 = string.Empty
        };
        nonUtcRecord = nonUtcRecord with
        {
            RecordHashSha256 = OperationalCaseHistoryHashing.ComputeRecordHashSha256(nonUtcRecord)
        };
        var json = JsonSerializer.Serialize(
            nonUtcRecord,
            OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord);
        await File.WriteAllTextAsync(store.HistoryPath, json + Environment.NewLine);
        File.Delete(store.ChainHeadPath);

        var action = () => store.ReadAsync(new OperationalCaseHistoryQuery()).AsTask();

        await action.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*persisted timestamp is not UTC*");
    }

    [Fact]
    public async Task AppendAsync_RejectsInvalidNestedHistoryBeforePersistence()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-invalid", "case-1", "Started") with
        {
            Transition = new OperationalCaseStateTransition
            {
                CurrentState = string.Empty,
                TransitionedAtUtc = default
            }
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>();
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_RejectsEvidenceCapturedAfterTheEnclosingEvent()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-invalid", "case-1", "Started");
        request = request with
        {
            Evidence =
            [
                new OperationEvidenceReference(
                    "future-evidence",
                    "lifecycle-receipt",
                    "Evidence that claims to come from the future.",
                    ContentHashSha256: InputHash,
                    CapturedAtUtc: request.OccurredAtUtc.AddSeconds(1))
            ]
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be captured after the enclosing history event*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_RejectsDescriptionOnlyEvidenceWithoutLocatorOrHash()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-message-only", "case-1", "Started") with
        {
            Evidence =
            [
                new OperationEvidenceReference(
                    "message-only",
                    "operator-message",
                    "A message without durable evidence cannot certify history.",
                    CapturedAtUtc: new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero))
            ]
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*durably locatable*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_RejectsNestedReferencesToMissingEvidence()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateRequest("event-invalid", "case-1", "Failed") with
        {
            Exceptions =
            [
                new OperationalCaseException
                {
                    ExceptionType = "InvalidOperationException",
                    Message = "The operation failed.",
                    OccurredAtUtc = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero),
                    EvidenceIds = ["missing-evidence"]
                }
            ]
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*references missing evidence*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_RejectsTerminalEvidenceChangedByEnclosingRecord()
    {
        var store = new FileOperationalCaseHistoryStore(_dataRoot);
        var request = CreateTerminalRequest();
        request = request with
        {
            Evidence =
            [
                request.Evidence[0] with
                {
                    Description = "Different evidence content under the same identifier."
                }
            ]
        };

        var action = () => store.AppendAsync(request).AsTask();

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*retained unchanged*");
        File.Exists(store.HistoryPath).Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_CancellationRaisedAfterHistoryCommit_DoesNotCancelCheckpointRepair()
    {
        using var callerCancellation = new CancellationTokenSource();
        var checkpointTokens = new List<CancellationToken>();
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            TimeProvider.System,
            chainHeadWriter: async (path, content, checkpointToken) =>
            {
                checkpointTokens.Add(checkpointToken);
                callerCancellation.Cancel();
                await File.WriteAllTextAsync(path, content, checkpointToken);
            });

        var retained = await store.AppendAsync(
            CreateRequest("event-post-commit-cancellation", "case-1", "Started"),
            callerCancellation.Token);

        retained.Sequence.Should().Be(1);
        checkpointTokens.Should().ContainSingle();
        checkpointTokens[0].CanBeCanceled.Should().BeFalse();
        callerCancellation.IsCancellationRequested.Should().BeTrue();
        (await store.ReadAsync(new OperationalCaseHistoryQuery())).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(retained);
    }

    [Fact]
    public async Task AppendAsync_TransientCheckpointFailure_RepairsWithoutReportingCommittedAppendAsFailed()
    {
        var checkpointAttempts = 0;
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            TimeProvider.System,
            chainHeadWriter: async (path, content, checkpointToken) =>
            {
                checkpointAttempts++;
                checkpointToken.CanBeCanceled.Should().BeFalse();
                if (checkpointAttempts == 1)
                {
                    throw new IOException("Simulated transient checkpoint failure.");
                }

                await File.WriteAllTextAsync(path, content, checkpointToken);
            });

        var retained = await store.AppendAsync(
            CreateRequest("event-checkpoint-repair", "case-1", "Started"));

        checkpointAttempts.Should().Be(2);
        (await store.ReadAsync(new OperationalCaseHistoryQuery())).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(retained);
    }

    [Fact]
    public async Task AppendAsync_PersistentCheckpointFailure_ReportsRecordAsAlreadyCommitted()
    {
        var checkpointAttempts = 0;
        var store = new FileOperationalCaseHistoryStore(
            _dataRoot,
            TimeProvider.System,
            chainHeadWriter: (_, _, checkpointToken) =>
            {
                checkpointAttempts++;
                checkpointToken.CanBeCanceled.Should().BeFalse();
                throw new IOException("Simulated persistent checkpoint failure.");
            });

        var action = () => store.AppendAsync(
            CreateRequest("event-committed-without-checkpoint", "case-1", "Started")).AsTask();

        var failure = await action.Should().ThrowAsync<OperationalCaseHistoryPostCommitException>();
        failure.Which.Message.Should().Contain("is committed");
        failure.Which.Message.Should().Contain("Do not assume the append was rolled back");
        failure.Which.CommittedRecord.HistoryEventId.Should().Be("event-committed-without-checkpoint");
        checkpointAttempts.Should().Be(2);

        var retained = await new FileOperationalCaseHistoryStore(_dataRoot)
            .ReadAsync(new OperationalCaseHistoryQuery());
        retained.Should().ContainSingle()
            .Which.HistoryEventId.Should().Be("event-committed-without-checkpoint");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private static OperationalCaseHistoryAppendRequest CreateRequest(
        string eventId,
        string caseId,
        string eventType) =>
        new()
        {
            CaseId = caseId,
            CaseType = "strategy-run",
            HistoryEventId = eventId,
            EventType = eventType,
            OccurredAtUtc = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero),
            ActorId = "operator-1",
            Reason = $"Record {eventType}.",
            CorrelationId = $"correlation-{caseId}",
            InputHashSha256 = InputHash,
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] = "0"
            },
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = eventType == "Started" ? null : "Running",
                CurrentState = eventType,
                TransitionedAtUtc = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero)
            }
        };

    private static OperationalCaseHistoryAppendRequest CreateTerminalRequest()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var evidence = new OperationEvidenceReference(
            "evidence-1",
            "lifecycle-receipt",
            "Retained lifecycle receipt.",
            ContentHashSha256: InputHash,
            CapturedAtUtc: occurredAt);
        var outcome = new VerifiedOperationOutcome(
            "event-terminal",
            "strategy-run.Completed",
            OperationTerminalState.Succeeded,
            occurredAt.AddMinutes(-1),
            occurredAt,
            1,
            "correlation-case-1",
            InputHash,
            [new OperationPostcondition(
                "completed",
                "The operation completed.",
                OperationPostconditionState.Satisfied,
                Required: true,
                EvidenceIds: [evidence.EvidenceId])],
            [evidence],
            [],
            [],
            []);

        return CreateRequest("event-terminal", "case-1", "Completed") with
        {
            Evidence = [evidence],
            TerminalOutcome = outcome
        };
    }

    private static IReadOnlyDictionary<string, string> ExpectedPredecessor(
        OperationalCaseHistoryRecord record) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] =
                record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey] =
                record.RecordHashSha256
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
