using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Serialization;

namespace Meridian.Strategies.Storage;

/// <summary>
/// Strategy-run repository that projects the latest compatible read model from append-only
/// operational case history when a durable store is supplied.
/// </summary>
/// <remarks>
/// The parameterless constructor is retained for isolated tests and explicit in-memory callers.
/// Production composition must inject <see cref="IOperationalCaseHistoryStore"/>.
/// </remarks>
public sealed class StrategyRunStore : IStrategyRepository
{
    public const string CaseType = "strategy-run";
    public const string SnapshotSchemaVersion = "meridian.strategy-run-snapshot.v1";

    private const string SchemaVersionDataKey = "schemaVersion";
    private const string SnapshotDataKey = "strategyRunSnapshotJson";
    private const string ExpectedPreviousCaseSequenceDataKey = "expectedPreviousCaseSequence";
    private const string ExpectedPreviousCaseRecordHashDataKey = "expectedPreviousCaseRecordHashSha256";

    private readonly Dictionary<string, StrategyRunEntry> _runsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StrategyRunEntry>> _runsByStrategy = new(StringComparer.Ordinal);
    private readonly List<StrategyRunEntry> _runsByLastUpdated = [];
    private readonly HashSet<string> _persistedEventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetainedCaseVersion> _retainedVersionsByRunId = new(StringComparer.Ordinal);
    private readonly IOperationalCaseHistoryStore? _historyStore;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly Lock _lock = new();
    private long _lastReplayedSequence;

    /// <summary>Creates an isolated in-memory store for compatibility and tests.</summary>
    public StrategyRunStore()
    {
    }

    /// <summary>Creates a durable store backed by the shared operational case-history port.</summary>
    public StrategyRunStore(IOperationalCaseHistoryStore historyStore)
    {
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
    }

    /// <inheritdoc/>
    public async Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        if (_historyStore is null)
        {
            lock (_lock)
            {
                ApplyEntry(entry);
            }
            return;
        }

        var normalized = NormalizeOperationalMetadata(SnapshotEntry(entry));
        var snapshotJson = JsonSerializer.Serialize(normalized, StrategyRunPersistenceJson.Options);
        if (snapshotJson.Length > OperationalCaseHistoryHashing.MaxDataValueLength)
        {
            throw new InvalidOperationException(
                $"Strategy run '{entry.RunId}' snapshot contains {snapshotJson.Length} characters, which exceeds " +
                $"the durable case-history limit of {OperationalCaseHistoryHashing.MaxDataValueLength}. " +
                "Retain the oversized result as a hashed artifact before recording the run.");
        }

        await _persistenceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RefreshFromHistoryCoreAsync(ct).ConfigureAwait(false);

            StrategyRunEntry? previous;
            RetainedCaseVersion? previousVersion;
            lock (_lock)
            {
                _runsById.TryGetValue(normalized.RunId, out previous);
                _retainedVersionsByRunId.TryGetValue(normalized.RunId, out previousVersion);
            }

            var eventId = ComputeEventId(normalized, snapshotJson);
            if (_persistedEventIds.Contains(eventId))
            {
                lock (_lock)
                {
                    ApplyEntry(normalized);
                }
                return;
            }

            var progressionError = GetLifecycleProgressionError(previous, normalized);
            if (progressionError is not null)
            {
                throw new InvalidOperationException(
                    $"Strategy run '{normalized.RunId}' lifecycle update is invalid: {progressionError}");
            }

            var request = BuildAppendRequest(normalized, previous, previousVersion, eventId, snapshotJson);
            var retained = await _historyStore.AppendAsync(request, ct).ConfigureAwait(false);
            ApplyRetainedRecord(retained);
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    /// <inheritdoc/>
    public Task RecordLifecycleEventAsync(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType,
        CancellationToken ct = default) =>
        RecordRunAsync(entry with { LastLifecycleEvent = eventType }, ct);

    internal static VerifiedOperationOutcome? CreateLifecycleOutcome(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType)
    {
        var normalized = NormalizeOperationalMetadata(entry with { LastLifecycleEvent = eventType });
        var snapshotJson = JsonSerializer.Serialize(normalized, StrategyRunPersistenceJson.Options);
        var eventId = ComputeEventId(normalized, snapshotJson);
        var occurredAt = normalized.LifecycleEventAtUtc ?? normalized.EndedAt ?? normalized.StartedAt;
        return BuildTerminalOutcome(normalized, eventId, occurredAt, BuildEvidence(normalized));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
        string strategyId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);

        List<StrategyRunEntry> snapshot;
        lock (_lock)
        {
            snapshot = _runsByStrategy.TryGetValue(strategyId, out var runs)
                ? [.. runs]
                : [];
        }

        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    /// <inheritdoc/>
    public async Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            return _runsByStrategy.TryGetValue(strategyId, out var runs) && runs.Count > 0
                ? runs[^1]
                : null;
        }
    }

    /// <inheritdoc/>
    public async Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            return _runsById.GetValueOrDefault(runId);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StrategyRunEntry>> GetRunsByIdsAsync(
        IReadOnlyCollection<string> runIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);

        var results = new List<StrategyRunEntry>(runIds.Count);
        lock (_lock)
        {
            foreach (var runId in runIds)
            {
                if (!string.IsNullOrWhiteSpace(runId) && _runsById.TryGetValue(runId, out var run))
                {
                    results.Add(run);
                }
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StrategyRunEntry>> QueryRunsAsync(
        StrategyRunRepositoryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);

        var runTypeFilter = query.RunTypes is { Count: > 0 }
            ? new HashSet<RunType>(query.RunTypes)
            : null;
        var limit = query.Limit <= 0 ? int.MaxValue : query.Limit;
        var results = new List<StrategyRunEntry>();

        lock (_lock)
        {
            foreach (var run in _runsByLastUpdated)
            {
                if (!string.IsNullOrWhiteSpace(query.StrategyId) &&
                    !string.Equals(run.StrategyId, query.StrategyId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (runTypeFilter is not null && !runTypeFilter.Contains(run.RunType))
                {
                    continue;
                }

                if (query.Status.HasValue &&
                    StrategyRunRepositoryOrdering.MapStatus(run) != query.Status.Value)
                {
                    continue;
                }

                results.Add(run);
                if (results.Count >= limit)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await RefreshFromHistoryAsync(ct).ConfigureAwait(false);
        List<StrategyRunEntry> snapshot;
        lock (_lock)
        {
            snapshot = _runsById.Values
                .OrderByDescending(static run => run.StartedAt)
                .ThenBy(static run => run.RunId, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private async Task RefreshFromHistoryAsync(CancellationToken ct)
    {
        if (_historyStore is null)
        {
            return;
        }

        await _persistenceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RefreshFromHistoryCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    private async Task RefreshFromHistoryCoreAsync(CancellationToken ct)
    {
        if (_historyStore is null)
        {
            return;
        }

        var records = await _historyStore.ReadAsync(
            new OperationalCaseHistoryQuery
            {
                CaseType = CaseType,
                AfterSequence = _lastReplayedSequence
            },
            ct).ConfigureAwait(false);

        foreach (var record in records.OrderBy(static item => item.Sequence))
        {
            ApplyRetainedRecord(record);
        }
    }

    private void ApplyRetainedRecord(OperationalCaseHistoryRecord record)
    {
        if (!string.Equals(record.CaseType, CaseType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Strategy run replay received case type '{record.CaseType}' at sequence {record.Sequence}.");
        }

        if (!record.Data.TryGetValue(SchemaVersionDataKey, out var schemaVersion) ||
            !string.Equals(schemaVersion, SnapshotSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} has an unsupported snapshot schema.");
        }

        if (!record.Data.TryGetValue(SnapshotDataKey, out var snapshotJson) ||
            string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} is missing its retained snapshot.");
        }

        StrategyRunEntry entry;
        try
        {
            entry = JsonSerializer.Deserialize<StrategyRunEntry>(snapshotJson, StrategyRunPersistenceJson.Options)
                ?? throw new InvalidDataException(
                    $"Strategy run history sequence {record.Sequence} retained a null snapshot.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} contains an invalid snapshot.",
                exception);
        }

        var canonicalInputHash = ComputeCanonicalInputHash(entry);
        var legacyInputHash = ComputeLegacyInputHash(entry);
        if (!string.Equals(
                entry.InputHashSha256,
                canonicalInputHash,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                entry.InputHashSha256,
                legacyInputHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} retained an input hash that does not match " +
                "the canonical run inputs.");
        }

        if (!string.Equals(record.CaseId, BuildCaseId(entry.RunId), StringComparison.Ordinal) ||
            !string.Equals(record.HistoryEventId, ComputeEventId(entry, snapshotJson), StringComparison.Ordinal) ||
            !string.Equals(record.EventType, entry.LastLifecycleEvent.ToString(), StringComparison.Ordinal) ||
            !string.Equals(record.InputHashSha256, entry.InputHashSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(record.CorrelationId, entry.CorrelationId, StringComparison.Ordinal) ||
            !string.Equals(record.ActorId, entry.ActorId, StringComparison.Ordinal) ||
            record.OccurredAtUtc != entry.LifecycleEventAtUtc ||
            record.Transition is null ||
            !string.Equals(
                record.Transition.CurrentState,
                entry.LastLifecycleEvent.ToString(),
                StringComparison.Ordinal) ||
            record.Transition.TransitionedAtUtc != entry.LifecycleEventAtUtc)
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} does not match its retained snapshot identity.");
        }

        lock (_lock)
        {
            _runsById.TryGetValue(entry.RunId, out var previous);
            _retainedVersionsByRunId.TryGetValue(entry.RunId, out var previousVersion);
            if (!string.Equals(
                    record.Transition.PreviousState,
                    previous?.LastLifecycleEvent.ToString(),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Strategy run history sequence {record.Sequence} declares an invalid previous lifecycle state.");
            }

            var progressionError = GetLifecycleProgressionError(previous, entry);
            if (progressionError is not null)
            {
                throw new InvalidDataException(
                    $"Strategy run history sequence {record.Sequence} has an invalid lifecycle progression: " +
                    progressionError);
            }

            ValidateRetainedPredecessor(record, previousVersion);
            ValidateTerminalOutcomeBinding(record, entry);

            ApplyEntry(entry);
            _retainedVersionsByRunId[entry.RunId] = new RetainedCaseVersion(
                record.Sequence,
                record.RecordHashSha256);
            _persistedEventIds.Add(record.HistoryEventId);
            _lastReplayedSequence = Math.Max(_lastReplayedSequence, record.Sequence);
        }
    }

    private static OperationalCaseHistoryAppendRequest BuildAppendRequest(
        StrategyRunEntry entry,
        StrategyRunEntry? previous,
        RetainedCaseVersion? previousVersion,
        string eventId,
        string snapshotJson)
    {
        var occurredAt = entry.LifecycleEventAtUtc ?? entry.EndedAt ?? entry.StartedAt;
        var evidence = BuildEvidence(entry);
        var approvals = entry.ApprovalReferences
            .Distinct(StringComparer.Ordinal)
            .Select(reference => new OperationalCaseApproval
            {
                ApprovalId = reference,
                Decision = "Referenced",
                DecidedBy = entry.ActorId!,
                DecidedAtUtc = occurredAt,
                Reason = entry.Reason!,
                EvidenceIds = evidence.Select(static item => item.EvidenceId).ToArray()
            })
            .ToArray();
        var exceptions = string.IsNullOrWhiteSpace(entry.ExceptionType) &&
                         string.IsNullOrWhiteSpace(entry.ExceptionMessage)
            ? []
            : (IReadOnlyList<OperationalCaseException>)[new OperationalCaseException
            {
                ExceptionType = entry.ExceptionType ?? "System.Exception",
                Message = entry.ExceptionMessage ?? "Strategy lifecycle operation failed.",
                StackTrace = entry.ExceptionStackTrace,
                OccurredAtUtc = occurredAt,
                EvidenceIds = evidence.Select(static item => item.EvidenceId).ToArray()
            }];
        var retries = entry.AttemptNumber > 1
            ? (IReadOnlyList<OperationalCaseRetry>)[new OperationalCaseRetry
            {
                Attempt = entry.AttemptNumber,
                AttemptedAtUtc = occurredAt,
                Reason = entry.Reason!
            }]
            : [];
        var recoveryAttempts = entry.LastLifecycleEvent == StrategyRunLifecycleEventType.RecoveryAttempted
            ? (IReadOnlyList<OperationalCaseRecoveryAttempt>)[new OperationalCaseRecoveryAttempt
            {
                RecoveryActionId = entry.AttemptId!,
                Attempt = entry.AttemptNumber,
                StartedAtUtc = occurredAt,
                CompletedAtUtc = occurredAt,
                Result = entry.Reason!,
                EvidenceIds = evidence.Select(static item => item.EvidenceId).ToArray(),
                ArtifactIds = entry.RetainedArtifacts.Select(static item => item.ArtifactId).ToArray()
            }]
            : [];

        return new OperationalCaseHistoryAppendRequest
        {
            CaseId = BuildCaseId(entry.RunId),
            CaseType = CaseType,
            HistoryEventId = eventId,
            EventType = entry.LastLifecycleEvent.ToString(),
            OccurredAtUtc = occurredAt,
            ActorId = entry.ActorId!,
            Reason = entry.Reason!,
            CorrelationId = entry.CorrelationId!,
            InputHashSha256 = entry.InputHashSha256!,
            Data = BuildHistoryData(snapshotJson, previousVersion),
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = previous?.LastLifecycleEvent.ToString(),
                CurrentState = entry.LastLifecycleEvent.ToString(),
                TransitionedAtUtc = occurredAt
            },
            Retries = retries,
            Exceptions = exceptions,
            Approvals = approvals,
            Artifacts = entry.RetainedArtifacts,
            Evidence = evidence,
            RecoveryAttempts = recoveryAttempts,
            TerminalOutcome = BuildTerminalOutcome(entry, eventId, occurredAt, evidence)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildHistoryData(
        string snapshotJson,
        RetainedCaseVersion? previousVersion)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemaVersionDataKey] = SnapshotSchemaVersion,
            [SnapshotDataKey] = snapshotJson,
            [ExpectedPreviousCaseSequenceDataKey] =
                (previousVersion?.Sequence ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (previousVersion is not null)
        {
            data[ExpectedPreviousCaseRecordHashDataKey] = previousVersion.RecordHashSha256;
        }

        return data;
    }

    private static VerifiedOperationOutcome? BuildTerminalOutcome(
        StrategyRunEntry entry,
        string eventId,
        DateTimeOffset occurredAt,
        IReadOnlyList<OperationEvidenceReference> evidence)
    {
        var eventType = entry.LastLifecycleEvent;
        if (!RequiresTerminalOutcome(eventType))
        {
            return null;
        }

        var succeeded = eventType is StrategyRunLifecycleEventType.Started or
            StrategyRunLifecycleEventType.Paused or
            StrategyRunLifecycleEventType.Completed;
        var warning = eventType == StrategyRunLifecycleEventType.EvidencePersistenceFailed;
        var cancelled = eventType == StrategyRunLifecycleEventType.Cancelled;
        var state = succeeded
            ? OperationTerminalState.Succeeded
            : warning
                ? OperationTerminalState.CompletedWithWarnings
            : cancelled
                ? OperationTerminalState.Blocked
                : OperationTerminalState.Failed;
        var postcondition = new OperationPostcondition(
            "strategy-lifecycle-transition",
            $"Strategy lifecycle transition '{eventType}' completed with retained evidence.",
            succeeded || warning ? OperationPostconditionState.Satisfied : OperationPostconditionState.NotSatisfied,
            Required: true,
            EvidenceIds: evidence.Select(static item => item.EvidenceId).ToArray())
        {
            ArtifactIds = entry.RetainedArtifacts.Select(static item => item.ArtifactId).ToArray()
        };
        var issues = succeeded
            ? []
            : (IReadOnlyList<OperationIssue>)[new OperationIssue(
                warning ? "strategy-evidence-persistence-warning" : "strategy-lifecycle-failure",
                entry.ExceptionMessage ?? entry.Reason!,
                warning ? OperationIssueSeverity.Warning : OperationIssueSeverity.Error,
                entry.ExceptionType,
                evidence[0].EvidenceId)
            {
                IsBlocking = cancelled
            }];
        IReadOnlyList<OperationRecoveryAction> recovery = succeeded
            ? []
            : warning
                ? [new OperationRecoveryAction(
                    "reconcile-external-state",
                    "Reconcile external state",
                    "Do not repeat the lifecycle command because the external action already completed. " +
                    "Inspect the strategy's external state, then reconcile it with the retained run evidence.",
                    Retryable: false,
                    RequiresHumanAction: true,
                    Route: $"/api/strategies/{Uri.EscapeDataString(entry.StrategyId)}/reconcile")
                {
                    EvidenceIds = evidence.Select(static item => item.EvidenceId).ToArray(),
                    ArtifactIds = entry.RetainedArtifacts.Select(static item => item.ArtifactId).ToArray()
                }]
                : [new OperationRecoveryAction(
                    "inspect-and-reconcile",
                    "Inspect and reconcile",
                    "Do not retry the lifecycle command until the external strategy state has been inspected. " +
                    "If the action already took effect, reconcile that state with the retained run; otherwise " +
                    "correct the reported cause before issuing a new command.",
                    Retryable: false,
                    RequiresHumanAction: true,
                    Route: $"/api/strategies/{Uri.EscapeDataString(entry.StrategyId)}/reconcile")
                {
                    EvidenceIds = evidence.Select(static item => item.EvidenceId).ToArray(),
                    ArtifactIds = entry.RetainedArtifacts.Select(static item => item.ArtifactId).ToArray()
                }];

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            eventId,
            $"strategy-run.{eventType}",
            state,
            entry.StartedAt,
            occurredAt,
            entry.AttemptNumber,
            entry.CorrelationId,
            entry.InputHashSha256,
            [postcondition],
            evidence,
            entry.RetainedArtifacts,
            issues,
            recovery));
    }

    private static IReadOnlyList<OperationEvidenceReference> BuildEvidence(StrategyRunEntry entry)
    {
        var occurredAt = entry.LifecycleEventAtUtc ?? entry.EndedAt ?? entry.StartedAt;
        var lifecycleReceipt = $"{entry.RunId}|{entry.LastLifecycleEvent}|{occurredAt:O}|{entry.Reason}";
        var evidence = new List<OperationEvidenceReference>
        {
            new(
                $"strategy-run:{entry.RunId}:lifecycle:{HashText(lifecycleReceipt)[..16]}",
                "lifecycle-receipt",
                $"Retained {entry.LastLifecycleEvent} lifecycle receipt for strategy run {entry.RunId}.",
                ContentHashSha256: HashText(lifecycleReceipt),
                CapturedAtUtc: occurredAt)
        };
        AddEvidence(evidence, entry, "dataset", entry.DatasetReference);
        AddEvidence(evidence, entry, "feed", entry.FeedReference);
        AddEvidence(evidence, entry, "ledger", entry.LedgerReference);
        AddEvidence(evidence, entry, "audit", entry.AuditReference);
        AddEvidence(evidence, entry, "retained", entry.RetainedEvidenceReferences);
        AddEvidence(evidence, entry, "accounting-record", entry.AccountingRecordReferences);
        AddEvidence(evidence, entry, "paper-validation", entry.PaperValidationReferences);
        AddEvidence(evidence, entry, "governed-report", entry.GovernedReportReferences);
        AddEvidence(evidence, entry, "artifact-reference", entry.ArtifactReferences);
        return evidence;
    }

    private static void AddEvidence(
        ICollection<OperationEvidenceReference> target,
        StrategyRunEntry entry,
        string kind,
        string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            AddEvidence(target, entry, kind, [reference]);
        }
    }

    private static void AddEvidence(
        ICollection<OperationEvidenceReference> target,
        StrategyRunEntry entry,
        string kind,
        IEnumerable<string> references)
    {
        foreach (var reference in references
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Select(static value => value.Trim())
                     .Distinct(StringComparer.Ordinal))
        {
            var evidenceId = $"strategy-run:{entry.RunId}:{kind}:{HashText(reference)[..16]}";
            if (target.Any(item => string.Equals(item.EvidenceId, evidenceId, StringComparison.Ordinal)))
            {
                continue;
            }

            target.Add(new OperationEvidenceReference(
                evidenceId,
                kind,
                reference,
                Uri: Uri.TryCreate(reference, UriKind.RelativeOrAbsolute, out _) ? reference : null,
                CapturedAtUtc: entry.LifecycleEventAtUtc ?? entry.StartedAt));
        }
    }

    private static StrategyRunEntry NormalizeOperationalMetadata(StrategyRunEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.StrategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.StrategyName);

        var eventType = InferEventType(entry);
        var occurredAt = entry.LifecycleEventAtUtc ?? entry.EndedAt ?? entry.StartedAt;
        var inputHash = ComputeCanonicalInputHash(entry);
        if (!string.IsNullOrWhiteSpace(entry.InputHashSha256) &&
            !string.Equals(entry.InputHashSha256, inputHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Strategy run input hash does not match the canonical hash recomputed from its retained inputs.",
                nameof(entry));
        }

        return entry with
        {
            LastLifecycleEvent = eventType,
            LifecycleEventAtUtc = occurredAt,
            ActorId = string.IsNullOrWhiteSpace(entry.ActorId) ? "system" : entry.ActorId.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(entry.CorrelationId) ? entry.RunId : entry.CorrelationId.Trim(),
            Reason = string.IsNullOrWhiteSpace(entry.Reason) ? DefaultReason(eventType) : entry.Reason.Trim(),
            InputHashSha256 = inputHash.ToLowerInvariant(),
            AttemptId = string.IsNullOrWhiteSpace(entry.AttemptId) ? entry.RunId : entry.AttemptId.Trim(),
            AttemptNumber = Math.Max(1, entry.AttemptNumber)
        };
    }

    private static StrategyRunEntry SnapshotEntry(StrategyRunEntry entry)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, StrategyRunPersistenceJson.Options);
        return JsonSerializer.Deserialize<StrategyRunEntry>(bytes, StrategyRunPersistenceJson.Options)
               ?? throw new ArgumentException("The strategy run could not be snapshotted.", nameof(entry));
    }

    private static void ValidateRetainedPredecessor(
        OperationalCaseHistoryRecord record,
        RetainedCaseVersion? previousVersion)
    {
        if (!record.Data.TryGetValue(ExpectedPreviousCaseSequenceDataKey, out var expectedSequenceText))
        {
            return;
        }

        if (!long.TryParse(
                expectedSequenceText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expectedSequence) ||
            expectedSequence != (previousVersion?.Sequence ?? 0))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} declares an invalid predecessor sequence.");
        }

        var expectedHash = record.Data.GetValueOrDefault(ExpectedPreviousCaseRecordHashDataKey);
        if (!string.Equals(
                expectedHash,
                previousVersion?.RecordHashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} declares an invalid predecessor hash.");
        }
    }

    private static void ValidateTerminalOutcomeBinding(
        OperationalCaseHistoryRecord record,
        StrategyRunEntry entry)
    {
        var requiresOutcome = RequiresTerminalOutcome(entry.LastLifecycleEvent);
        if (!requiresOutcome)
        {
            if (record.TerminalOutcome is not null)
            {
                throw new InvalidDataException(
                    $"Strategy run history sequence {record.Sequence} binds a terminal receipt to intent event " +
                    $"'{entry.LastLifecycleEvent}'.");
            }

            return;
        }

        var outcome = record.TerminalOutcome
            ?? throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} is missing the terminal receipt for " +
                $"'{entry.LastLifecycleEvent}'.");
        var receiptErrors = VerifiedOperationOutcomeValidator.Validate(outcome);
        if (receiptErrors.Count > 0)
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} retained an invalid terminal receipt: " +
                string.Join(" ", receiptErrors));
        }
        var expectedState = ExpectedTerminalState(entry.LastLifecycleEvent);
        if (outcome.State != expectedState ||
            !string.Equals(outcome.OperationId, record.HistoryEventId, StringComparison.Ordinal) ||
            !string.Equals(
                outcome.OperationKind,
                $"strategy-run.{entry.LastLifecycleEvent}",
                StringComparison.Ordinal) ||
            !string.Equals(outcome.CorrelationId, record.CorrelationId, StringComparison.Ordinal) ||
            !string.Equals(
                outcome.InputHashSha256,
                record.InputHashSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Strategy run history sequence {record.Sequence} terminal receipt is not bound to lifecycle " +
                $"event '{entry.LastLifecycleEvent}'.");
        }
    }

    private static bool RequiresTerminalOutcome(StrategyRunLifecycleEventType eventType) =>
        eventType is StrategyRunLifecycleEventType.Started or
            StrategyRunLifecycleEventType.Paused or
            StrategyRunLifecycleEventType.Completed or
            StrategyRunLifecycleEventType.StartFailed or
            StrategyRunLifecycleEventType.PauseFailed or
            StrategyRunLifecycleEventType.StopFailed or
            StrategyRunLifecycleEventType.Failed or
            StrategyRunLifecycleEventType.Cancelled or
            StrategyRunLifecycleEventType.EvidencePersistenceFailed;

    private static OperationTerminalState ExpectedTerminalState(StrategyRunLifecycleEventType eventType) =>
        eventType switch
        {
            StrategyRunLifecycleEventType.Started or
            StrategyRunLifecycleEventType.Paused or
            StrategyRunLifecycleEventType.Completed => OperationTerminalState.Succeeded,
            StrategyRunLifecycleEventType.EvidencePersistenceFailed =>
                OperationTerminalState.CompletedWithWarnings,
            StrategyRunLifecycleEventType.Cancelled => OperationTerminalState.Blocked,
            StrategyRunLifecycleEventType.StartFailed or
            StrategyRunLifecycleEventType.PauseFailed or
            StrategyRunLifecycleEventType.StopFailed or
            StrategyRunLifecycleEventType.Failed => OperationTerminalState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Event is not terminal.")
        };

    private static string? GetLifecycleProgressionError(
        StrategyRunEntry? previous,
        StrategyRunEntry current)
    {
        if (current.StartedAt == default)
        {
            return "the run start timestamp is missing";
        }

        if (current.LifecycleEventAtUtc is not { } currentEventAt || currentEventAt == default)
        {
            return "the lifecycle event timestamp is missing";
        }

        if (currentEventAt < current.StartedAt)
        {
            return "the lifecycle event timestamp precedes the run start";
        }

        if (current.EndedAt is { } currentEndedAt &&
            (currentEndedAt < current.StartedAt || currentEndedAt > currentEventAt))
        {
            return "the run end timestamp falls outside the retained lifecycle interval";
        }

        if (RequiresEndedAt(current.LastLifecycleEvent) && current.EndedAt is null)
        {
            return $"terminal lifecycle event '{current.LastLifecycleEvent}' is missing the run end timestamp";
        }

        if (current.LastLifecycleEvent is (StrategyRunLifecycleEventType.Started or
                StrategyRunLifecycleEventType.StartRequested or
                StrategyRunLifecycleEventType.PauseRequested or
                StrategyRunLifecycleEventType.Paused or
                StrategyRunLifecycleEventType.PauseFailed or
                StrategyRunLifecycleEventType.StopFailed or
                StrategyRunLifecycleEventType.StopRequested) &&
            current.EndedAt is not null)
        {
            return $"non-terminal lifecycle event '{current.LastLifecycleEvent}' retains a run end timestamp";
        }

        if (previous is null)
        {
            return null;
        }

        var immutableScopeChanged =
            !string.Equals(previous.ParentRunId, current.ParentRunId, StringComparison.Ordinal) ||
            !string.Equals(previous.PortfolioId, current.PortfolioId, StringComparison.Ordinal) ||
            !string.Equals(previous.LedgerReference, current.LedgerReference, StringComparison.Ordinal) ||
            !string.Equals(previous.AuditReference, current.AuditReference, StringComparison.Ordinal) ||
            !string.Equals(previous.FundProfileId, current.FundProfileId, StringComparison.Ordinal);
        var inputHashChanged = !string.Equals(
            previous.InputHashSha256,
            current.InputHashSha256,
            StringComparison.OrdinalIgnoreCase);
        var isLegacyHashUpgrade =
            inputHashChanged &&
            string.Equals(
                previous.InputHashSha256,
                ComputeLegacyInputHash(previous),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                current.InputHashSha256,
                ComputeCanonicalInputHash(current),
                StringComparison.OrdinalIgnoreCase) &&
            !immutableScopeChanged;

        if (!string.Equals(previous.StrategyId, current.StrategyId, StringComparison.Ordinal) ||
            !string.Equals(previous.StrategyName, current.StrategyName, StringComparison.Ordinal) ||
            previous.RunType != current.RunType ||
            previous.StartedAt != current.StartedAt ||
            immutableScopeChanged ||
            (inputHashChanged && !isLegacyHashUpgrade) ||
            !string.Equals(previous.CorrelationId, current.CorrelationId, StringComparison.Ordinal))
        {
            return "immutable run identity changed after the first retained lifecycle event";
        }

        var previousEventAt = previous.LifecycleEventAtUtc ?? previous.EndedAt ?? previous.StartedAt;
        if (currentEventAt < previousEventAt)
        {
            return "the lifecycle event timestamp moved backwards";
        }

        if (!IsAllowedTransition(previous.LastLifecycleEvent, current.LastLifecycleEvent))
        {
            return $"transition '{previous.LastLifecycleEvent}' to '{current.LastLifecycleEvent}' is not allowed";
        }

        var reconcilesExternallyStartedRun =
            previous.LastLifecycleEvent == StrategyRunLifecycleEventType.StartFailed &&
            current.LastLifecycleEvent == StrategyRunLifecycleEventType.Started &&
            current.EndedAt is null;
        if (previous.EndedAt is { } previousEndedAt &&
            current.EndedAt != previousEndedAt &&
            !reconcilesExternallyStartedRun)
        {
            return "the retained run end timestamp changed after the run ended";
        }

        if (current.AttemptNumber < previous.AttemptNumber)
        {
            return "the recovery attempt number moved backwards";
        }

        return null;
    }

    private static string ComputeCanonicalInputHash(StrategyRunEntry entry) =>
        StrategyRunEntry.ComputeInputHash(
            entry.StrategyId,
            entry.StrategyName,
            entry.RunType,
            entry.DatasetReference,
            entry.FeedReference,
            entry.Engine,
            entry.ParameterSet,
            entry.ParentRunId,
            entry.PortfolioId,
            entry.LedgerReference,
            entry.AuditReference,
            entry.FundProfileId);

    private static string ComputeLegacyInputHash(StrategyRunEntry entry) =>
        StrategyRunEntry.ComputeLegacyInputHash(
            entry.StrategyId,
            entry.StrategyName,
            entry.RunType,
            entry.DatasetReference,
            entry.FeedReference,
            entry.Engine,
            entry.ParameterSet);

    private static bool RequiresEndedAt(StrategyRunLifecycleEventType eventType) =>
        eventType is StrategyRunLifecycleEventType.Completed or
            StrategyRunLifecycleEventType.StartFailed or
            StrategyRunLifecycleEventType.Failed or
            StrategyRunLifecycleEventType.Cancelled;

    private static bool IsAllowedTransition(
        StrategyRunLifecycleEventType previous,
        StrategyRunLifecycleEventType current)
    {
        return previous switch
        {
            StrategyRunLifecycleEventType.StartRequested => current is
                StrategyRunLifecycleEventType.Started or
                StrategyRunLifecycleEventType.StartFailed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed,
            StrategyRunLifecycleEventType.Started => current is
                StrategyRunLifecycleEventType.StartFailed or
                StrategyRunLifecycleEventType.PauseRequested or
                StrategyRunLifecycleEventType.Paused or
                StrategyRunLifecycleEventType.PauseFailed or
                StrategyRunLifecycleEventType.StopRequested or
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.Paused => current is
                StrategyRunLifecycleEventType.PauseRequested or
                StrategyRunLifecycleEventType.PauseFailed or
                StrategyRunLifecycleEventType.StopRequested or
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.PauseFailed => current is
                StrategyRunLifecycleEventType.PauseRequested or
                StrategyRunLifecycleEventType.Paused or
                StrategyRunLifecycleEventType.StopRequested or
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.StopRequested => current is
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.StopFailed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed,
            StrategyRunLifecycleEventType.PauseRequested => current is
                StrategyRunLifecycleEventType.Paused or
                StrategyRunLifecycleEventType.PauseFailed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed,
            StrategyRunLifecycleEventType.StopFailed => current is
                StrategyRunLifecycleEventType.StopRequested or
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.EvidencePersistenceFailed => current is
                StrategyRunLifecycleEventType.Started or
                StrategyRunLifecycleEventType.Paused or
                StrategyRunLifecycleEventType.Completed or
                StrategyRunLifecycleEventType.Failed or
                StrategyRunLifecycleEventType.Cancelled or
                StrategyRunLifecycleEventType.StopRequested,
            StrategyRunLifecycleEventType.StartFailed => current is
                StrategyRunLifecycleEventType.Started or
                StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.Completed or
            StrategyRunLifecycleEventType.Failed or
            StrategyRunLifecycleEventType.Cancelled =>
                current == StrategyRunLifecycleEventType.RecoveryAttempted,
            StrategyRunLifecycleEventType.RecoveryAttempted => false,
            _ => false
        };
    }

    private static StrategyRunLifecycleEventType InferEventType(StrategyRunEntry entry)
    {
        if (entry.LastLifecycleEvent != StrategyRunLifecycleEventType.Started)
        {
            return entry.LastLifecycleEvent;
        }

        return entry.TerminalStatus switch
        {
            StrategyRunStatus.Failed => StrategyRunLifecycleEventType.Failed,
            StrategyRunStatus.Cancelled => StrategyRunLifecycleEventType.Cancelled,
            _ when entry.EndedAt.HasValue => StrategyRunLifecycleEventType.Completed,
            _ => StrategyRunLifecycleEventType.Started
        };
    }

    private static string DefaultReason(StrategyRunLifecycleEventType eventType) => eventType switch
    {
        StrategyRunLifecycleEventType.StartRequested => "Strategy start requested.",
        StrategyRunLifecycleEventType.Started => "Strategy run started.",
        StrategyRunLifecycleEventType.StartFailed => "Strategy start failed.",
        StrategyRunLifecycleEventType.PauseRequested => "Strategy pause requested.",
        StrategyRunLifecycleEventType.Paused => "Strategy run paused.",
        StrategyRunLifecycleEventType.PauseFailed => "Strategy pause failed.",
        StrategyRunLifecycleEventType.StopRequested => "Strategy stop requested.",
        StrategyRunLifecycleEventType.StopFailed => "Strategy stop failed.",
        StrategyRunLifecycleEventType.Completed => "Strategy run completed.",
        StrategyRunLifecycleEventType.Failed => "Strategy run failed.",
        StrategyRunLifecycleEventType.Cancelled => "Strategy run was cancelled.",
        StrategyRunLifecycleEventType.RecoveryAttempted => "Strategy recovery attempted.",
        StrategyRunLifecycleEventType.EvidencePersistenceFailed =>
            "The external lifecycle action completed, but its final evidence could not be retained.",
        _ => "Strategy lifecycle changed."
    };

    private static string ComputeEventId(StrategyRunEntry entry, string snapshotJson) =>
        $"strategy-run:{entry.RunId}:{entry.LastLifecycleEvent}:{HashText(snapshotJson)}";

    private static string HashText(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string BuildCaseId(string runId) => $"strategy-run:{runId}";

    private sealed record RetainedCaseVersion(long Sequence, string RecordHashSha256);

    private void ApplyEntry(StrategyRunEntry entry)
    {
        if (_runsById.TryGetValue(entry.RunId, out var existing))
        {
            RemoveIndexedEntry(existing);
        }

        _runsById[entry.RunId] = entry;
        InsertIndexedEntry(entry);
    }

    private void InsertIndexedEntry(StrategyRunEntry entry)
    {
        if (!_runsByStrategy.TryGetValue(entry.StrategyId, out var strategyRuns))
        {
            strategyRuns = [];
            _runsByStrategy[entry.StrategyId] = strategyRuns;
        }

        InsertSorted(strategyRuns, entry, StrategyRunRepositoryOrdering.StartedAtAscending);
        InsertSorted(_runsByLastUpdated, entry, StrategyRunRepositoryOrdering.LastUpdatedDescending);
    }

    private void RemoveIndexedEntry(StrategyRunEntry entry)
    {
        if (_runsByStrategy.TryGetValue(entry.StrategyId, out var strategyRuns))
        {
            RemoveByRunId(strategyRuns, entry.RunId);
            if (strategyRuns.Count == 0)
            {
                _runsByStrategy.Remove(entry.StrategyId);
            }
        }

        RemoveByRunId(_runsByLastUpdated, entry.RunId);
    }

    private static void InsertSorted(
        List<StrategyRunEntry> target,
        StrategyRunEntry entry,
        IComparer<StrategyRunEntry> comparer)
    {
        var index = target.BinarySearch(entry, comparer);
        if (index < 0)
        {
            index = ~index;
        }

        target.Insert(index, entry);
    }

    private static void RemoveByRunId(List<StrategyRunEntry> target, string runId)
    {
        var index = target.FindIndex(run => string.Equals(run.RunId, runId, StringComparison.Ordinal));
        if (index >= 0)
        {
            target.RemoveAt(index);
        }
    }
}
