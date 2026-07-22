using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Operations;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Operations;

/// <summary>
/// Crash-safe, append-only operational case-history storage under a shared Meridian data root.
/// </summary>
/// <remarks>
/// Every append verifies the complete retained chain before assigning the next global sequence and
/// predecessor hash. A process-wide semaphore and an operating-system file lock serialize the
/// read-tail/write sequence across store instances and workstation processes that share a data root.
/// </remarks>
public sealed class FileOperationalCaseHistoryStore : IOperationalCaseHistoryStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan DefaultLockAcquisitionTimeout = TimeSpan.FromSeconds(30);

    internal const string ExpectedPreviousCaseSequenceDataKey = "expectedPreviousCaseSequence";
    internal const string ExpectedPreviousCaseRecordHashDataKey = "expectedPreviousCaseRecordHashSha256";

    private const string ChainHeadSchema = "meridian.operational-case-history.head.v1";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lockAcquisitionTimeout;
    private readonly Func<string, string, CancellationToken, Task> _chainHeadWriter;

    /// <summary>Creates a store rooted at <c>&lt;dataRoot&gt;/operations/case-history.jsonl</c>.</summary>
    public FileOperationalCaseHistoryStore(string dataRoot)
        : this(dataRoot, TimeProvider.System, DefaultLockAcquisitionTimeout)
    {
    }

    internal FileOperationalCaseHistoryStore(
        string dataRoot,
        TimeProvider timeProvider,
        TimeSpan? lockAcquisitionTimeout = null,
        Func<string, string, CancellationToken, Task>? chainHeadWriter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _lockAcquisitionTimeout = lockAcquisitionTimeout ?? DefaultLockAcquisitionTimeout;
        if (_lockAcquisitionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lockAcquisitionTimeout),
                "The process-lock acquisition timeout must be positive.");
        }
        _chainHeadWriter = chainHeadWriter ?? AtomicFileWriter.WriteAsync;
        HistoryPath = Path.Combine(Path.GetFullPath(dataRoot), "operations", "case-history.jsonl");
    }

    /// <summary>The append-only JSONL file used by this store.</summary>
    public string HistoryPath { get; }

    private string LockPath => HistoryPath + ".lock";
    internal string ChainHeadPath => HistoryPath + ".head";

    /// <inheritdoc />
    public async ValueTask<OperationalCaseHistoryRecord> AppendAsync(
        OperationalCaseHistoryAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        var snapshot = SnapshotRequest(request);
        ValidateRequest(snapshot);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(cancellationToken).ConfigureAwait(false);
            var records = await LoadAndValidateAsync(cancellationToken).ConfigureAwait(false);

            var retainedEvent = records.FirstOrDefault(record =>
                string.Equals(record.HistoryEventId, snapshot.HistoryEventId, StringComparison.Ordinal));
            if (retainedEvent is not null)
            {
                if (MatchesRequest(retainedEvent, snapshot))
                {
                    if (records.Count > 0)
                    {
                        await RepairChainHeadAfterCommitAsync(records[^1]).ConfigureAwait(false);
                    }

                    return retainedEvent;
                }

                throw new InvalidOperationException(
                    $"Operational case-history event '{snapshot.HistoryEventId}' is already retained with different content.");
            }

            ValidateExpectedPredecessor(snapshot, records);
            var previous = records.Count == 0 ? null : records[^1];
            var persistedAtUtc = _timeProvider.GetUtcNow();
            if (persistedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    "The operational case-history persistence clock returned a non-UTC timestamp. " +
                    "Correct the clock provider before retrying the append.");
            }

            var record = CreateRecord(
                snapshot,
                previous?.Sequence + 1 ?? 1,
                previous?.RecordHashSha256,
                persistedAtUtc);
            record = record with
            {
                RecordHashSha256 = OperationalCaseHistoryHashing.ComputeRecordHashSha256(record)
            };

            var json = JsonSerializer.Serialize(
                record,
                OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord);
            await AtomicFileWriter.AppendLinesAsync(HistoryPath, [json], cancellationToken).ConfigureAwait(false);
            await RepairChainHeadAfterCommitAsync(record).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(
        OperationalCaseHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.AfterSequence is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                query.AfterSequence,
                "AfterSequence cannot be negative.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(cancellationToken).ConfigureAwait(false);
            var records = await LoadAndValidateAsync(cancellationToken).ConfigureAwait(false);
            return records
                .Where(record =>
                    (string.IsNullOrWhiteSpace(query.CaseId) ||
                     string.Equals(record.CaseId, query.CaseId, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(query.CaseType) ||
                     string.Equals(record.CaseType, query.CaseType, StringComparison.Ordinal)) &&
                    (!query.AfterSequence.HasValue || record.Sequence > query.AfterSequence.Value))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<OperationalCaseHistoryRecord>> LoadAndValidateAsync(CancellationToken ct)
    {
        if (!File.Exists(HistoryPath))
        {
            await ValidateChainHeadAsync([], ct).ConfigureAwait(false);
            return [];
        }

        var records = new List<OperationalCaseHistoryRecord>();
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        string? expectedPreviousHash = null;
        long expectedSequence = 1;
        var lineNumber = 0;

        await using var stream = new FileStream(
            HistoryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                throw Corrupt(lineNumber, "a blank record was retained");
            }

            OperationalCaseHistoryRecord record;
            try
            {
                record = JsonSerializer.Deserialize(
                    line,
                    OperationsContractsJsonContext.Default.OperationalCaseHistoryRecord)
                    ?? throw Corrupt(lineNumber, "the JSON record was null");
            }
            catch (JsonException exception)
            {
                throw Corrupt(lineNumber, "the JSON record could not be parsed", exception);
            }

            ValidateStoredRecord(record, lineNumber, expectedSequence, expectedPreviousHash, eventIds);
            records.Add(record);
            expectedSequence++;
            expectedPreviousHash = record.RecordHashSha256;
        }

        await ValidateChainHeadAsync(records, ct).ConfigureAwait(false);
        return records;
    }

    private static OperationalCaseHistoryAppendRequest SnapshotRequest(
        OperationalCaseHistoryAppendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            OperationsContractsJsonContext.Default.OperationalCaseHistoryAppendRequest);
        return JsonSerializer.Deserialize(
                   bytes,
                   OperationsContractsJsonContext.Default.OperationalCaseHistoryAppendRequest)
               ?? throw new ArgumentException(
                   "The operational case-history request could not be snapshotted.",
                   nameof(request));
    }

    private static OperationalCaseHistoryRecord CreateRecord(
        OperationalCaseHistoryAppendRequest request,
        long sequence,
        string? previousRecordHashSha256,
        DateTimeOffset persistedAtUtc) =>
        new()
        {
            CaseId = request.CaseId,
            CaseType = request.CaseType,
            HistoryEventId = request.HistoryEventId,
            EventType = request.EventType,
            Sequence = sequence,
            PreviousRecordHashSha256 = previousRecordHashSha256,
            RecordHashSha256 = string.Empty,
            OccurredAtUtc = request.OccurredAtUtc,
            PersistedAtUtc = persistedAtUtc,
            ActorId = request.ActorId,
            Reason = request.Reason,
            CorrelationId = request.CorrelationId,
            InputHashSha256 = request.InputHashSha256,
            Data = request.Data,
            Transition = request.Transition,
            Assignment = request.Assignment,
            Retries = request.Retries,
            Exceptions = request.Exceptions,
            Approvals = request.Approvals,
            Artifacts = request.Artifacts,
            Evidence = request.Evidence,
            RecoveryAttempts = request.RecoveryAttempts,
            TerminalOutcome = request.TerminalOutcome
        };

    private static bool MatchesRequest(
        OperationalCaseHistoryRecord retained,
        OperationalCaseHistoryAppendRequest request)
    {
        var candidate = CreateRecord(
            request,
            retained.Sequence,
            retained.PreviousRecordHashSha256,
            retained.PersistedAtUtc);
        return string.Equals(
            retained.RecordHashSha256,
            OperationalCaseHistoryHashing.ComputeRecordHashSha256(candidate),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateExpectedPredecessor(
        OperationalCaseHistoryAppendRequest request,
        IReadOnlyList<OperationalCaseHistoryRecord> records)
    {
        if (!request.Data.TryGetValue(ExpectedPreviousCaseSequenceDataKey, out var expectedSequenceText))
        {
            if (request.Transition is not null || request.Assignment is not null)
            {
                throw new ArgumentException(
                    $"Stateful operational case-history events must provide Data value " +
                    $"'{ExpectedPreviousCaseSequenceDataKey}' (use 0 only when the case has no retained predecessor).",
                    nameof(request));
            }

            return;
        }

        if (!long.TryParse(
                expectedSequenceText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expectedSequence) ||
            expectedSequence < 0)
        {
            throw new ArgumentException(
                $"Data value '{ExpectedPreviousCaseSequenceDataKey}' must be a non-negative integer.",
                nameof(request));
        }

        var predecessor = records.LastOrDefault(record =>
            string.Equals(record.CaseId, request.CaseId, StringComparison.Ordinal) &&
            string.Equals(record.CaseType, request.CaseType, StringComparison.Ordinal));
        var actualSequence = predecessor?.Sequence ?? 0;
        if (expectedSequence != actualSequence)
        {
            throw new InvalidOperationException(
                $"Operational case '{request.CaseId}' changed before event '{request.HistoryEventId}' could be retained: " +
                $"expected predecessor sequence {expectedSequence}, actual {actualSequence}. Refresh case history and retry from the retained state.");
        }

        var hasExpectedHash = request.Data.TryGetValue(
            ExpectedPreviousCaseRecordHashDataKey,
            out var expectedHash);
        if (expectedSequence == 0)
        {
            if (predecessor is not null || hasExpectedHash && !string.IsNullOrEmpty(expectedHash))
            {
                throw new InvalidOperationException(
                    $"Operational case '{request.CaseId}' expected no predecessor, but retained predecessor evidence exists.");
            }
        }
        else if (!hasExpectedHash ||
                 !string.Equals(
                     expectedHash,
                     predecessor?.RecordHashSha256,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Operational case '{request.CaseId}' predecessor hash changed before event " +
                $"'{request.HistoryEventId}' could be retained. Refresh case history and retry from the retained state.");
        }

        var previousTransitionState = records.LastOrDefault(record =>
            string.Equals(record.CaseId, request.CaseId, StringComparison.Ordinal) &&
            string.Equals(record.CaseType, request.CaseType, StringComparison.Ordinal) &&
            record.Transition is not null)?.Transition?.CurrentState;
        if (request.Transition is not null &&
            !string.Equals(
                request.Transition.PreviousState,
                previousTransitionState,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Operational case '{request.CaseId}' expected previous state " +
                $"'{request.Transition.PreviousState ?? "<none>"}', but the retained state is " +
                $"'{previousTransitionState ?? "<none>"}'. Refresh case history before retrying.");
        }

        var previousAssigneeId = records.LastOrDefault(record =>
            string.Equals(record.CaseId, request.CaseId, StringComparison.Ordinal) &&
            string.Equals(record.CaseType, request.CaseType, StringComparison.Ordinal) &&
            record.Assignment is not null)?.Assignment?.AssigneeId;
        if (request.Assignment is not null &&
            !string.Equals(
                request.Assignment.PreviousAssigneeId,
                previousAssigneeId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Operational case '{request.CaseId}' expected previous assignee " +
                $"'{request.Assignment.PreviousAssigneeId ?? "<unassigned>"}', but the retained assignee is " +
                $"'{previousAssigneeId ?? "<unassigned>"}'. Refresh case history before retrying.");
        }
    }

    private async Task ValidateChainHeadAsync(
        IReadOnlyList<OperationalCaseHistoryRecord> records,
        CancellationToken ct)
    {
        if (!File.Exists(ChainHeadPath))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(ChainHeadPath, Utf8NoBom, ct).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException(
                $"Operational case-history chain-head checkpoint '{ChainHeadPath}' could not be read.",
                exception);
        }

        if (lines.Length != 3 ||
            !string.Equals(lines[0], ChainHeadSchema, StringComparison.Ordinal) ||
            !long.TryParse(
                lines[1],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var checkpointSequence) ||
            checkpointSequence <= 0 ||
            lines[2] is not { Length: 64 } checkpointHash ||
            !checkpointHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"Operational case-history chain-head checkpoint '{ChainHeadPath}' is invalid.");
        }

        if (records.Count < checkpointSequence)
        {
            throw new InvalidDataException(
                $"Operational case history '{HistoryPath}' was truncated or rolled back below externally checkpointed sequence {checkpointSequence}.");
        }

        var checkpointRecord = records[(int)checkpointSequence - 1];
        if (!string.Equals(
                checkpointRecord.RecordHashSha256,
                checkpointHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Operational case history '{HistoryPath}' does not match externally checkpointed sequence {checkpointSequence}.");
        }
    }

    private async Task RepairChainHeadAfterCommitAsync(OperationalCaseHistoryRecord committedRecord)
    {
        try
        {
            await WriteChainHeadAsync(committedRecord, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception firstFailure)
        {
            try
            {
                await WriteChainHeadAsync(committedRecord, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception repairFailure)
            {
                throw new OperationalCaseHistoryPostCommitException(
                    committedRecord,
                    ChainHeadPath,
                    new AggregateException(firstFailure, repairFailure));
            }
        }
    }

    private Task WriteChainHeadAsync(OperationalCaseHistoryRecord record, CancellationToken ct)
    {
        var content = string.Join(
            '\n',
            ChainHeadSchema,
            record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record.RecordHashSha256);
        return _chainHeadWriter(ChainHeadPath, content, ct);
    }

    private void ValidateStoredRecord(
        OperationalCaseHistoryRecord record,
        int lineNumber,
        long expectedSequence,
        string? expectedPreviousHash,
        ISet<string> eventIds)
    {
        if (record.Sequence != expectedSequence)
        {
            throw Corrupt(
                lineNumber,
                $"sequence {record.Sequence} does not match expected sequence {expectedSequence}");
        }

        if (!string.Equals(
                record.PreviousRecordHashSha256,
                expectedPreviousHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Corrupt(lineNumber, "the predecessor hash does not match the retained chain");
        }

        bool hasValidHash;
        try
        {
            hasValidHash = OperationalCaseHistoryHashing.HasValidRecordHash(record);
        }
        catch (ArgumentException exception)
        {
            throw Corrupt(lineNumber, "the record hash inputs are invalid", exception);
        }

        if (!hasValidHash)
        {
            throw Corrupt(lineNumber, "the record hash is invalid");
        }

        if (!eventIds.Add(record.HistoryEventId))
        {
            throw Corrupt(lineNumber, $"event id '{record.HistoryEventId}' is duplicated");
        }

        if (record.PersistedAtUtc == default)
        {
            throw Corrupt(lineNumber, "the persisted timestamp is missing");
        }
        if (record.PersistedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Corrupt(lineNumber, "the persisted timestamp is not UTC");
        }

        try
        {
            ValidateRequest(new OperationalCaseHistoryAppendRequest
            {
                CaseId = record.CaseId,
                CaseType = record.CaseType,
                HistoryEventId = record.HistoryEventId,
                EventType = record.EventType,
                OccurredAtUtc = record.OccurredAtUtc,
                ActorId = record.ActorId,
                Reason = record.Reason,
                CorrelationId = record.CorrelationId,
                InputHashSha256 = record.InputHashSha256,
                Data = record.Data,
                Transition = record.Transition,
                Assignment = record.Assignment,
                Retries = record.Retries,
                Exceptions = record.Exceptions,
                Approvals = record.Approvals,
                Artifacts = record.Artifacts,
                Evidence = record.Evidence,
                RecoveryAttempts = record.RecoveryAttempts,
                TerminalOutcome = record.TerminalOutcome
            });
        }
        catch (ArgumentException exception)
        {
            throw Corrupt(lineNumber, "the retained record violates the case-history contract", exception);
        }
    }

    private static void ValidateRequest(OperationalCaseHistoryAppendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.CaseId, nameof(request.CaseId));
        RequireText(request.CaseType, nameof(request.CaseType));
        RequireText(request.HistoryEventId, nameof(request.HistoryEventId));
        RequireText(request.EventType, nameof(request.EventType));
        RequireText(request.ActorId, nameof(request.ActorId));
        RequireText(request.Reason, nameof(request.Reason));
        RequireText(request.CorrelationId, nameof(request.CorrelationId));

        ValidateRequiredUtcTimestamp(
            request.OccurredAtUtc,
            nameof(request.OccurredAtUtc),
            request);

        RequireHash(request.InputHashSha256, nameof(request.InputHashSha256));
        var dataErrors = OperationalCaseHistoryHashing.ValidateData(request.Data);
        if (dataErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Operational case-history data is invalid: {string.Join(" ", dataErrors)}",
                nameof(request));
        }
        ValidateUnique(request.Evidence.Select(static item => item.EvidenceId), "evidence id", request);
        ValidateUnique(request.Artifacts.Select(static item => item.ArtifactId), "artifact id", request);
        ValidateUnique(request.Approvals.Select(static item => item.ApprovalId), "approval id", request);
        ValidateUnique(
            request.RecoveryAttempts.Select(static item => item.RecoveryActionId),
            "recovery action id",
            request);

        var evidenceIds = request.Evidence
            .Select(static item => item.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var artifactIds = request.Artifacts
            .Select(static item => item.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);

        ValidateEvidenceAndArtifacts(request, evidenceIds, artifactIds);

        if (request.Transition is { } transition)
        {
            RequireText(transition.CurrentState, "Transition.CurrentState");
            ValidateEventTimestamp(
                transition.TransitionedAtUtc,
                request.OccurredAtUtc,
                "Transition.TransitionedAtUtc",
                request);
        }

        if (request.Assignment is { } assignment)
        {
            RequireText(assignment.AssignedBy, "Assignment.AssignedBy");
            if (string.IsNullOrWhiteSpace(assignment.PreviousAssigneeId) &&
                string.IsNullOrWhiteSpace(assignment.AssigneeId))
            {
                throw new ArgumentException(
                    "An assignment must identify either the previous or current assignee.",
                    nameof(request));
            }
            ValidateEventTimestamp(
                assignment.AssignedAtUtc,
                request.OccurredAtUtc,
                "Assignment.AssignedAtUtc",
                request);
        }

        foreach (var retry in request.Retries)
        {
            if (retry.Attempt <= 0)
            {
                throw new ArgumentException("Retry attempts must be positive.", nameof(request));
            }
            RequireText(retry.Reason, "Retry.Reason");
            ValidateEventTimestamp(
                retry.AttemptedAtUtc,
                request.OccurredAtUtc,
                "Retry.AttemptedAtUtc",
                request);
        }

        foreach (var exception in request.Exceptions)
        {
            RequireText(exception.ExceptionType, "Exception.ExceptionType");
            RequireText(exception.Message, "Exception.Message");
            ValidateEventTimestamp(
                exception.OccurredAtUtc,
                request.OccurredAtUtc,
                "Exception.OccurredAtUtc",
                request);
            ValidateReferences(
                "Exception",
                exception.EvidenceIds,
                [],
                evidenceIds,
                artifactIds,
                request);
        }

        foreach (var approval in request.Approvals)
        {
            RequireText(approval.ApprovalId, "Approval.ApprovalId");
            RequireText(approval.Decision, $"Approval[{approval.ApprovalId}].Decision");
            RequireText(approval.DecidedBy, $"Approval[{approval.ApprovalId}].DecidedBy");
            RequireText(approval.Reason, $"Approval[{approval.ApprovalId}].Reason");
            ValidateEventTimestamp(
                approval.DecidedAtUtc,
                request.OccurredAtUtc,
                $"Approval[{approval.ApprovalId}].DecidedAtUtc",
                request);
            ValidateReferences(
                $"Approval '{approval.ApprovalId}'",
                approval.EvidenceIds,
                [],
                evidenceIds,
                artifactIds,
                request);
        }

        foreach (var recovery in request.RecoveryAttempts)
        {
            if (recovery.Attempt <= 0)
            {
                throw new ArgumentException("Recovery attempts must be positive.", nameof(request));
            }
            RequireText(recovery.RecoveryActionId, "RecoveryAttempt.RecoveryActionId");
            RequireText(recovery.Result, $"RecoveryAttempt[{recovery.RecoveryActionId}].Result");
            ValidateEventTimestamp(
                recovery.StartedAtUtc,
                request.OccurredAtUtc,
                $"RecoveryAttempt[{recovery.RecoveryActionId}].StartedAtUtc",
                request);
            if (recovery.CompletedAtUtc is { } completedAtUtc)
            {
                ValidateEventTimestamp(
                    completedAtUtc,
                    request.OccurredAtUtc,
                    $"RecoveryAttempt[{recovery.RecoveryActionId}].CompletedAtUtc",
                    request);
                if (completedAtUtc < recovery.StartedAtUtc)
                {
                    throw new ArgumentException(
                        $"Recovery attempt '{recovery.RecoveryActionId}' completed before it started.",
                        nameof(request));
                }
            }
            ValidateReferences(
                $"Recovery attempt '{recovery.RecoveryActionId}'",
                recovery.EvidenceIds,
                recovery.ArtifactIds,
                evidenceIds,
                artifactIds,
                request);
        }

        if (request.TerminalOutcome is not null)
        {
            VerifiedOperationOutcomeValidator.ValidateAndThrow(request.TerminalOutcome);
            if (!string.Equals(
                    request.TerminalOutcome.OperationId,
                    request.HistoryEventId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Terminal outcome operation identity must match the enclosing history event.",
                    nameof(request));
            }

            if (!string.Equals(
                    request.TerminalOutcome.CorrelationId,
                    request.CorrelationId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Terminal outcome correlation identity must match the enclosing history event.",
                    nameof(request));
            }

            if (!string.Equals(
                    request.TerminalOutcome.InputHashSha256,
                    request.InputHashSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Terminal outcome input hash must match the enclosing history event.",
                    nameof(request));
            }

            if (request.TerminalOutcome.CompletedAtUtc < request.OccurredAtUtc)
            {
                throw new ArgumentException(
                    "Terminal outcome completion cannot precede the enclosing history event.",
                    nameof(request));
            }

            ValidateTerminalReferences(request);
        }
    }

    private static void ValidateTerminalReferences(OperationalCaseHistoryAppendRequest request)
    {
        var missingEvidence = request.TerminalOutcome!.Evidence
            .Where(terminal => !request.Evidence.Contains(terminal))
            .Select(static item => item.EvidenceId)
            .ToArray();
        var missingArtifacts = request.TerminalOutcome.Artifacts
            .Where(terminal => !request.Artifacts.Contains(terminal))
            .Select(static item => item.ArtifactId)
            .ToArray();

        if (missingEvidence.Length > 0 || missingArtifacts.Length > 0)
        {
            throw new ArgumentException(
                "Terminal outcome evidence and artifacts must be retained unchanged by the enclosing history event.",
                nameof(request));
        }
    }

    private static void ValidateEvidenceAndArtifacts(
        OperationalCaseHistoryAppendRequest request,
        IReadOnlySet<string> evidenceIds,
        IReadOnlySet<string> artifactIds)
    {
        foreach (var evidence in request.Evidence)
        {
            RequireText(evidence.EvidenceId, "Evidence.EvidenceId");
            RequireText(evidence.Kind, $"Evidence[{evidence.EvidenceId}].Kind");
            RequireText(evidence.Description, $"Evidence[{evidence.EvidenceId}].Description");
            ValidateOptionalHash(
                evidence.ContentHashSha256,
                $"Evidence[{evidence.EvidenceId}].ContentHashSha256",
                request);
            ValidateOptionalUri(evidence.Uri, $"Evidence[{evidence.EvidenceId}].Uri", request);
            if (string.IsNullOrWhiteSpace(evidence.Uri)
                && string.IsNullOrWhiteSpace(evidence.ContentHashSha256))
            {
                throw new ArgumentException(
                    $"Evidence '{evidence.EvidenceId}' must be durably locatable by URI or bound to retained content by SHA-256.",
                    nameof(request));
            }
            if (evidence.CapturedAtUtc is not { } capturedAtUtc)
            {
                throw new ArgumentException(
                    $"Evidence '{evidence.EvidenceId}' has an invalid captured timestamp.",
                    nameof(request));
            }

            if (capturedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"Evidence '{evidence.EvidenceId}' captured timestamp must be UTC.",
                    nameof(request));
            }

            var latestPermittedCapture = request.TerminalOutcome?.CompletedAtUtc ?? request.OccurredAtUtc;
            if (capturedAtUtc > latestPermittedCapture)
            {
                throw new ArgumentException(
                    $"Evidence '{evidence.EvidenceId}' cannot be captured after " +
                    $"{(request.TerminalOutcome is null ? "the enclosing history event" : "operation completion")}.",
                    nameof(request));
            }
        }

        foreach (var artifact in request.Artifacts)
        {
            RequireText(artifact.ArtifactId, "Artifact.ArtifactId");
            RequireText(artifact.FileName, $"Artifact[{artifact.ArtifactId}].FileName");
            RequireText(artifact.ContentType, $"Artifact[{artifact.ArtifactId}].ContentType");
            if (artifact.ByteLength <= 0)
            {
                throw new ArgumentException(
                    $"Artifact '{artifact.ArtifactId}' must retain non-empty bytes.",
                    nameof(request));
            }
            RequireHash(
                artifact.ContentHashSha256,
                $"Artifact[{artifact.ArtifactId}].ContentHashSha256");
            ValidateRequiredUri(artifact.Uri, $"Artifact[{artifact.ArtifactId}].Uri", request);
            ValidateOptionalUri(artifact.PreviewUri, $"Artifact[{artifact.ArtifactId}].PreviewUri", request);
        }
    }

    private static void ValidateEventTimestamp(
        DateTimeOffset value,
        DateTimeOffset eventOccurredAtUtc,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        ValidateRequiredUtcTimestamp(value, label, request);
        if (value > eventOccurredAtUtc)
        {
            throw new ArgumentException(
                $"{label} cannot occur after the enclosing history event.",
                nameof(request));
        }
    }

    private static void ValidateRequiredUtcTimestamp(
        DateTimeOffset value,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        if (value == default)
        {
            throw new ArgumentException($"{label} is required.", nameof(request));
        }
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"{label} must be UTC.", nameof(request));
        }
    }

    private static void ValidateReferences(
        string owner,
        IReadOnlyList<string>? ownerEvidenceIds,
        IReadOnlyList<string>? ownerArtifactIds,
        IReadOnlySet<string> evidenceIds,
        IReadOnlySet<string> artifactIds,
        OperationalCaseHistoryAppendRequest request)
    {
        foreach (var evidenceId in ownerEvidenceIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(evidenceId) || !evidenceIds.Contains(evidenceId))
            {
                throw new ArgumentException(
                    $"{owner} references missing evidence '{evidenceId}'.",
                    nameof(request));
            }
        }
        foreach (var artifactId in ownerArtifactIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(artifactId) || !artifactIds.Contains(artifactId))
            {
                throw new ArgumentException(
                    $"{owner} references missing artifact '{artifactId}'.",
                    nameof(request));
            }
        }
    }

    private static void ValidateOptionalHash(
        string? value,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        if (value is not null && (value.Length != 64 || !value.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException($"{label} must be a 64-character SHA-256 value.", nameof(request));
        }
    }

    private static void ValidateRequiredUri(
        string? value,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _))
        {
            throw new ArgumentException($"{label} must be a valid retained URI.", nameof(request));
        }
    }

    private static void ValidateOptionalUri(
        string? value,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        if (value is not null)
        {
            ValidateRequiredUri(value, label, request);
        }
    }

    private async Task<FileStream> AcquireProcessLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    LockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                if (stopwatch.Elapsed >= _lockAcquisitionTimeout)
                {
                    throw new TimeoutException(
                        $"Timed out after {_lockAcquisitionTimeout} acquiring operational case-history lock " +
                        $"'{LockPath}'. Verify that no stalled Meridian process retains the lock, then retry.",
                        exception);
                }

                await Task.Delay(LockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 11 or 32 or 33;
    }

    private InvalidDataException Corrupt(int lineNumber, string detail, Exception? inner = null) =>
        new($"Operational case history '{HistoryPath}' is corrupt at line {lineNumber}: {detail}.", inner);

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }

    private static void RequireHash(string? value, string name)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{name} must be a 64-character SHA-256 value.", name);
        }
    }

    private static void ValidateUnique(
        IEnumerable<string?> values,
        string label,
        OperationalCaseHistoryAppendRequest request)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                throw new ArgumentException(
                    $"Operational case history contains an empty or duplicate {label}.",
                    nameof(request));
            }
        }
    }
}

internal sealed class OperationalCaseHistoryPostCommitException : IOException
{
    public OperationalCaseHistoryPostCommitException(
        OperationalCaseHistoryRecord committedRecord,
        string chainHeadPath,
        Exception innerException)
        : base(
            $"Operational case-history record '{committedRecord.HistoryEventId}' at sequence " +
            $"{committedRecord.Sequence} is committed, but chain-head checkpoint '{chainHeadPath}' " +
            "could not be repaired. Do not assume the append was rolled back; read the retained " +
            "history and repair the checkpoint before retrying the operation.",
            innerException)
    {
        CommittedRecord = committedRecord;
    }

    public OperationalCaseHistoryRecord CommittedRecord { get; }
}
