using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

public sealed partial class FileReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
{
    private const int MaximumBulkCaseCount = 100;
    private const int CurrentSnapshotSchemaVersion = 2;
    private readonly string _snapshotPath;
    private readonly string _auditPath;
    private readonly string _mutationLockPath;
    private readonly ILogger<FileReconciliationBreakQueueRepository> _logger;
    private readonly IReconciliationSlaPolicyProvider? _slaPolicyProvider;
    private readonly Func<string, string, CancellationToken, Task> _stateWriter;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions AuditValidationJsonOptions = CreateJsonOptions();
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

    private Dictionary<string, ReconciliationBreakQueueItem>? _items;
    private SnapshotStamp? _loadedSnapshotStamp;
    private readonly Dictionary<string, ReconciliationBulkCaseworkResult> _bulkResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _bulkResultIdsByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BulkCaseworkReceipt> _bulkReceipts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CaseworkCommandReceipt> _commandReceipts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ReconciliationBreakQueueAuditEvent> _auditEvents = [];
    private readonly IReconciliationCaseWorkflowService _workflowService = new ReconciliationCaseWorkflowService();

    public FileReconciliationBreakQueueRepository(
        string dataDirectory,
        ILogger<FileReconciliationBreakQueueRepository> logger,
        IReconciliationSlaPolicyProvider? slaPolicyProvider = null,
        Func<string, string, CancellationToken, Task>? stateWriter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _slaPolicyProvider = slaPolicyProvider;
        _stateWriter = stateWriter ?? ((path, content, ct) => AtomicFileWriter.WriteAsync(path, content, ct));

        Directory.CreateDirectory(dataDirectory);
        _snapshotPath = Path.Combine(dataDirectory, "reconciliation-break-queue.json");
        _auditPath = Path.Combine(dataDirectory, "reconciliation-break-queue-audit.jsonl");
        _mutationLockPath = Path.Combine(dataDirectory, "reconciliation-break-queue.lock");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            IEnumerable<ReconciliationBreakQueueItem> items = _items!.Values;
            if (status.HasValue)
            {
                items = items.Where(item => item.Status == status.Value);
            }

            return items
                .OrderByDescending(static item => item.LastUpdatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAgingByTeamAsync(
        string team, DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(team);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _items!.Values
                .Where(item => item.Status == ReconciliationBreakQueueStatus.Open
                    && string.Equals(item.Team, team, StringComparison.OrdinalIgnoreCase)
                    && item.SlaDueAt.HasValue
                    && item.SlaDueAt.Value < asOfUtc)
                .OrderBy(item => item.SlaDueAt!.Value)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return _items!.GetValueOrDefault(breakId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var state = CaptureState();
            if (_items!.TryGetValue(item.BreakId, out var existing))
            {
                // Casework mutates fields such as BlockedOutputs after creation. Compare a replay
                // with the retained creation/migration payload, not the current projection, so a
                // resolved case can be safely encountered again by deterministic source seeding.
                var existingInputHash = ComputeRetainedCreateInputHash(item.BreakId, existing);
                var incomingInputHash = ComputeCreateInputHash(item);
                var exactReplay = string.Equals(existingInputHash, incomingInputHash, StringComparison.Ordinal);
                await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                    EventId: Guid.NewGuid().ToString("N"),
                    BreakId: item.BreakId,
                    EventType: exactReplay ? "CreateReplayAccepted" : "CreateConflict",
                    PreviousStatus: existing.Status,
                    NewStatus: existing.Status,
                    PreviousLifecycleState: existing.LifecycleState,
                    NewLifecycleState: existing.LifecycleState,
                    OccurredAt: DateTimeOffset.UtcNow,
                    AssignedTo: existing.AssignedTo,
                    ReviewedBy: existing.ReviewedBy,
                    ResolvedBy: existing.ResolvedBy,
                    Note: exactReplay
                        ? "Exact create retry returned the retained reconciliation case without replacing casework state."
                        : "Create retry was rejected because the same break id was bound to materially different source or scope input.",
                    Actor: item.AssignedTo ?? item.ReviewedBy ?? item.ResolvedBy ?? "reconciliation-source",
                    BeforePayload: JsonSerializer.Serialize(existing, _jsonOptions),
                    AfterPayload: JsonSerializer.Serialize(existing, _jsonOptions),
                    CorrelationId: item.RunId,
                    CommandId: $"create:{item.BreakId}",
                    Source: item.SourceType,
                    Reason: exactReplay ? existingInputHash : $"existing:{existingInputHash};incoming:{incomingInputHash}"), ct).ConfigureAwait(false);
                try
                {
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    RestoreState(state);
                    throw;
                }

                if (!exactReplay)
                {
                    throw new InvalidOperationException(
                        $"Reconciliation break id '{item.BreakId}' is already bound to different source or scope input.");
                }
                return false;
            }

            var normalized = NormalizeLegacyCaseState(item);
            var created = StampComputedFields(normalized, normalized.DetectedAt);
            _items[item.BreakId] = created;
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: item.BreakId,
                EventType: "CaseCreated",
                PreviousStatus: null,
                NewStatus: created.Status,
                PreviousLifecycleState: null,
                NewLifecycleState: created.LifecycleState,
                OccurredAt: item.DetectedAt,
                AssignedTo: created.AssignedTo,
                ReviewedBy: created.ReviewedBy,
                ResolvedBy: created.ResolvedBy,
                Note: created.ResolutionNote,
                ExceptionRoute: created.ExceptionRoute,
                ToleranceBand: created.ToleranceBand,
                RequiredSignoffRole: created.RequiredSignoffRole,
                SignoffStatus: created.SignoffStatus,
                ExternalAccountId: created.ExternalAccountId,
                CustodianId: created.CustodianId,
                UpstreamSyncCursor: created.UpstreamSyncCursor,
                Actor: created.AssignedTo ?? created.ReviewedBy ?? created.ResolvedBy,
                BeforePayload: null,
                AfterPayload: JsonSerializer.Serialize(created, _jsonOptions),
                Source: created.SourceType,
                Reason: created.SourceReference), ct).ConfigureAwait(false);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreState(state);
                throw;
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> CreateOrMigrateAsync(ReconciliationBreakQueueItem item, string? previousBreakId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrWhiteSpace(previousBreakId)
            && !string.Equals(previousBreakId, item.BreakId, StringComparison.OrdinalIgnoreCase))
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
                ResetCachedState();
                await EnsureLoadedAsync(ct).ConfigureAwait(false);
                var state = CaptureState();

                // Re-key an existing case only when it still lives under the superseded id, no case
                // has already been created under the current id (which would win by dedupe), and the
                // stored case demonstrably identifies the SAME upstream break as the incoming item.
                // The latter guards Delta==null legacy-fingerprint collisions, where several distinct
                // breaks share one previousBreakId, from attaching one break's casework to another.
                if (!_items!.ContainsKey(item.BreakId)
                    && _items.TryGetValue(previousBreakId!, out var existing)
                    && LegacyCaseMatchesSource(existing, item))
                {
                    var migrated = existing with
                    {
                        BreakId = item.BreakId,
                        SourceFingerprint = item.SourceFingerprint
                    };
                    _items.Remove(previousBreakId!);
                    _items[item.BreakId] = migrated;
                    await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                        EventId: Guid.NewGuid().ToString("N"),
                        BreakId: migrated.BreakId,
                        EventType: "BreakIdMigrated",
                        PreviousStatus: existing.Status,
                        NewStatus: migrated.Status,
                        PreviousLifecycleState: existing.LifecycleState,
                        NewLifecycleState: migrated.LifecycleState,
                        OccurredAt: DateTimeOffset.UtcNow,
                        AssignedTo: migrated.AssignedTo,
                        ReviewedBy: migrated.ReviewedBy,
                        ResolvedBy: migrated.ResolvedBy,
                        Note: $"Re-keyed reconciliation case from superseded break id '{previousBreakId}' to '{migrated.BreakId}' after a statement fingerprint-input change.",
                        ExceptionRoute: migrated.ExceptionRoute,
                        ToleranceBand: migrated.ToleranceBand,
                        RequiredSignoffRole: migrated.RequiredSignoffRole,
                        SignoffStatus: migrated.SignoffStatus,
                        ExternalAccountId: migrated.ExternalAccountId,
                        CustodianId: migrated.CustodianId,
                        UpstreamSyncCursor: migrated.UpstreamSyncCursor,
                        Actor: migrated.AssignedTo ?? migrated.ReviewedBy ?? migrated.ResolvedBy,
                        BeforePayload: JsonSerializer.Serialize(existing, _jsonOptions),
                        AfterPayload: JsonSerializer.Serialize(migrated, _jsonOptions),
                        Source: migrated.SourceType,
                        Reason: migrated.SourceReference), ct).ConfigureAwait(false);
                    try
                    {
                        await PersistSnapshotAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        RestoreState(state);
                        throw;
                    }
                    return false;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        // No case to migrate → standard create-if-missing semantics (also a no-op when the current
        // BreakId already exists).
        return await CreateIfMissingAsync(item, ct).ConfigureAwait(false);
    }

    private static bool LegacyCaseMatchesSource(ReconciliationBreakQueueItem existing, ReconciliationBreakQueueItem incoming)
    {
        // Prefer the upstream source break id; fall back to the source reference. Only confirm a match
        // when both sides carry the identity, so a legacy case whose stored identity differs from (or
        // cannot be compared to) the incoming break is never re-keyed onto the wrong case.
        if (!string.IsNullOrWhiteSpace(existing.SourceBreakId) && !string.IsNullOrWhiteSpace(incoming.SourceBreakId))
        {
            return string.Equals(existing.SourceBreakId, incoming.SourceBreakId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(existing.SourceReference) && !string.IsNullOrWhiteSpace(incoming.SourceReference))
        {
            return string.Equals(existing.SourceReference, incoming.SourceReference, StringComparison.Ordinal);
        }

        return false;
    }

    public async Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var state = CaptureState();
            _items!.TryGetValue(item.BreakId, out var existing);
            var normalized = NormalizeLegacyCaseState(item);
            _items[item.BreakId] = normalized;
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: item.BreakId,
                EventType: existing is null ? "CaseCreated" : "CaseSaved",
                PreviousStatus: existing?.Status,
                NewStatus: normalized.Status,
                PreviousLifecycleState: existing?.LifecycleState,
                NewLifecycleState: normalized.LifecycleState,
                OccurredAt: DateTimeOffset.UtcNow,
                AssignedTo: normalized.AssignedTo,
                ReviewedBy: normalized.ReviewedBy,
                ResolvedBy: normalized.ResolvedBy,
                Note: normalized.ResolutionNote,
                Actor: normalized.AssignedTo ?? normalized.ReviewedBy ?? normalized.ResolvedBy ?? "repository-save",
                BeforePayload: existing is null ? null : JsonSerializer.Serialize(existing, _jsonOptions),
                AfterPayload: JsonSerializer.Serialize(normalized, _jsonOptions),
                CorrelationId: normalized.RunId,
                CommandId: $"save:{normalized.BreakId}:{normalized.Version}",
                Source: normalized.SourceType,
                Reason: normalized.SourceReference), ct).ConfigureAwait(false);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreState(state);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(breakId, out var existing))
            {
                return false;
            }

            var state = CaptureState();
            _items.Remove(breakId);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: breakId,
                EventType: "CaseDeleted",
                PreviousStatus: existing.Status,
                NewStatus: existing.Status,
                PreviousLifecycleState: existing.LifecycleState,
                NewLifecycleState: existing.LifecycleState,
                OccurredAt: DateTimeOffset.UtcNow,
                AssignedTo: existing.AssignedTo,
                ReviewedBy: existing.ReviewedBy,
                ResolvedBy: existing.ResolvedBy,
                Note: "Reconciliation case deleted from the active queue; audit evidence remains retained.",
                Actor: "repository-delete",
                BeforePayload: JsonSerializer.Serialize(existing, _jsonOptions),
                AfterPayload: null,
                CorrelationId: existing.RunId,
                CommandId: $"delete:{existing.BreakId}:{existing.Version}",
                Source: existing.SourceType,
                Reason: existing.SourceReference), ct).ConfigureAwait(false);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                RestoreState(state);
                throw;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.UtcNow;
        var inputHash = HashPayload($"meridian.reconciliation-start-review.v1\n{JsonSerializer.Serialize(request, _jsonOptions)}")!;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items!.TryGetValue(request.BreakId, out var item);
            var command = CreateLegacyStartReviewCommand(request, inputHash);
            var replay = await TryReplayRetainedLegacyCommandAsync(command, inputHash, startedAt, ct).ConfigureAwait(false);
            if (replay is not null)
            {
                return replay;
            }

            if (item is null)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.NotFound,
                    Item: null,
                    Error: "Break was not found.");
            }

            if (item.Status != ReconciliationBreakQueueStatus.Open)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                    Item: item,
                    Error: $"Cannot move break from {item.Status} to {ReconciliationBreakQueueStatus.InReview}.");
            }

            var now = DateTimeOffset.UtcNow;
            var transitioned = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(
                request.BreakId,
                ReconciliationCaseTransitionAction.StartReview,
                request.ReviewedBy,
                request.ReviewNote ?? "Review started.",
                ["review-note"]), now);
            if (transitioned.Status != ReconciliationBreakQueueTransitionStatus.Success || transitioned.Item is null)
            {
                return transitioned;
            }
            var updated = transitioned.Item with
            {
                AssignedTo = request.AssignedTo,
                ReviewedBy = request.ReviewedBy,
                ReviewedAt = now,
                ResolutionNote = request.ReviewNote,
                SignoffStatus = "in-review"
            };

            updated = StampComputedFields(updated, now);
            var state = CaptureState();
            _items[request.BreakId] = updated;
            if (!string.Equals(item.AssignedTo, updated.AssignedTo, StringComparison.OrdinalIgnoreCase))
            {
                await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                    EventId: Guid.NewGuid().ToString("N"),
                    BreakId: request.BreakId,
                    EventType: "Assigned",
                    PreviousStatus: item.Status,
                    NewStatus: updated.Status,
                    PreviousLifecycleState: item.LifecycleState,
                    NewLifecycleState: updated.LifecycleState,
                    OccurredAt: now,
                    AssignedTo: request.AssignedTo,
                    ReviewedBy: request.ReviewedBy,
                    ResolvedBy: null,
                    Note: request.ReviewNote,
                    ExceptionRoute: updated.ExceptionRoute,
                    ToleranceBand: updated.ToleranceBand,
                    RequiredSignoffRole: updated.RequiredSignoffRole,
                    SignoffStatus: updated.SignoffStatus,
                    ExternalAccountId: updated.ExternalAccountId,
                    CustodianId: updated.CustodianId,
                    UpstreamSyncCursor: updated.UpstreamSyncCursor,
                    Actor: request.ReviewedBy,
                    BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
                    AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions),
                    CorrelationId: command.CorrelationId,
                    CommandId: command.CommandId,
                    Source: command.Source,
                    Reason: command.Reason), ct).ConfigureAwait(false);
            }

            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: request.BreakId,
                EventType: "ReviewStarted",
                PreviousStatus: item.Status,
                NewStatus: updated.Status,
                PreviousLifecycleState: item.LifecycleState,
                NewLifecycleState: updated.LifecycleState,
                OccurredAt: now,
                AssignedTo: request.AssignedTo,
                ReviewedBy: request.ReviewedBy,
                ResolvedBy: null,
                Note: request.ReviewNote,
                ExceptionRoute: updated.ExceptionRoute,
                ToleranceBand: updated.ToleranceBand,
                RequiredSignoffRole: updated.RequiredSignoffRole,
                SignoffStatus: updated.SignoffStatus,
                ExternalAccountId: updated.ExternalAccountId,
                CustodianId: updated.CustodianId,
                UpstreamSyncCursor: updated.UpstreamSyncCursor,
                Actor: request.ReviewedBy,
                BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions),
                CorrelationId: command.CorrelationId,
                CommandId: command.CommandId,
                Source: command.Source,
                Reason: command.Reason), ct).ConfigureAwait(false);
            var retainedAudit = _auditEvents[^1];
            var successOutcome = CreateTransitionOutcome(
                command,
                inputHash,
                OperationTerminalState.Succeeded,
                startedAt,
                updated,
                null,
                retainedAudit);
            _commandReceipts[command.CommandId] = new CaseworkCommandReceipt(
                command.CommandId,
                command.BreakId,
                command.Action,
                inputHash,
                updated,
                successOutcome);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RestoreState(state);
                return CreateLegacyPersistenceFailure(
                    "reconciliation.casework.start-review",
                    request.BreakId,
                    inputHash,
                    startedAt,
                    item,
                    ex);
            }

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated)
            {
                Outcome = successOutcome
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.UtcNow;
        var inputHash = HashPayload($"meridian.reconciliation-resolve.v1\n{JsonSerializer.Serialize(request, _jsonOptions)}")!;
        if (request.Status is not ReconciliationBreakQueueStatus.Resolved and not ReconciliationBreakQueueStatus.Dismissed)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                Item: null,
                Error: "Resolve transition only supports Resolved or Dismissed.");
        }
        if (string.IsNullOrWhiteSpace(request.OperatorRationale))
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                Item: null,
                Error: "Operator rationale is required.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items!.TryGetValue(request.BreakId, out var item);
            var command = CreateLegacyResolveCommand(request, inputHash);
            var replay = await TryReplayRetainedLegacyCommandAsync(command, inputHash, startedAt, ct).ConfigureAwait(false);
            if (replay is not null)
            {
                return replay;
            }

            if (item is null)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.NotFound,
                    Item: null,
                    Error: "Break was not found.");
            }

            if (item.Status != ReconciliationBreakQueueStatus.InReview)
            {
                return new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.InvalidTransition,
                    Item: item,
                    Error: $"Cannot move break from {item.Status} to {request.Status}.");
            }

            if (request.ActionOrigin != OperationsActionOriginDto.HumanOperator)
            {
                var validation = Invalid(
                    item,
                    "Reviewed automation cannot resolve or dismiss reconciliation breaks; a human operator approval is required.",
                    ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator,
                    ["actionOrigin"],
                    ReconciliationCaseLifecycleState.Resolved);
                try
                {
                    await AppendMaterialActionDeniedAuditAsync(
                        item,
                        request.ResolvedBy,
                        request.ResolutionNote,
                        command.CommandId,
                        command.CorrelationId,
                        command.Source,
                        validation.Error ?? "Material action requires human operator origin.",
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return CreateLegacyPersistenceFailure(
                        "reconciliation.casework.resolve",
                        request.BreakId,
                        inputHash,
                        startedAt,
                        item,
                        ex);
                }
                return validation;
            }

            var now = DateTimeOffset.UtcNow;
            var approval = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(request.BreakId, ReconciliationCaseTransitionAction.RequestApproval, request.ResolvedBy, request.OperatorRationale, ["resolution-note"]), now);
            if (approval.Status != ReconciliationBreakQueueTransitionStatus.Success || approval.Item is null)
            {
                return approval;
            }
            var dispositionEvidenceHash = HashPayload(
                $"meridian.reconciliation-legacy-resolve.v1\n{JsonSerializer.Serialize(request, _jsonOptions)}")!;
            var updated = approval.Item with
            {
                Status = ReconciliationBreakQueueStatus.Resolved,
                LifecycleState = ReconciliationCaseLifecycleState.Resolved,
                ResolvedBy = request.ResolvedBy,
                ResolvedAt = now,
                ResolutionNote = request.ResolutionNote,
                RootCauseCode = approval.Item.RootCauseCode ?? (request.Status == ReconciliationBreakQueueStatus.Dismissed ? "DismissedFalsePositive" : null),
                ResolutionCode = approval.Item.ResolutionCode ?? (request.Status == ReconciliationBreakQueueStatus.Dismissed ? "DismissedFalsePositive" : "LegacyResolved"),
                SignoffStatus = request.Status == ReconciliationBreakQueueStatus.Dismissed ? "dismissed-false-positive" : "ready-for-signoff",
                Disposition = ReconciliationBreakDispositionDto.Resolved,
                DispositionReason = request.OperatorRationale,
                DispositionApprovedBy = request.ResolvedBy,
                DispositionEvidenceHash = dispositionEvidenceHash,
                DisposedAt = now,
                EvidenceLinks = (approval.Item.EvidenceLinks ?? [])
                    .Append($"urn:sha256:{dispositionEvidenceHash}")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                BlockedOutputs = [],
                SignoffHistory = (item.SignoffHistory ?? []).Concat(
                [
                    new ReconciliationCaseSignoffRecord(request.ResolvedBy, item.RequiredSignoffRole ?? "operator", request.Status == ReconciliationBreakQueueStatus.Dismissed ? "DismissedFalsePositive" : request.Status.ToString(), request.OperatorRationale, now)
                ]).ToArray()
            };

            updated = StampComputedFields(updated, now);
            var state = CaptureState();
            _items[request.BreakId] = updated;
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: request.BreakId,
                EventType: request.Status == ReconciliationBreakQueueStatus.Resolved ? "Resolved" : "Dismissed",
                PreviousStatus: item.Status,
                NewStatus: updated.Status,
                PreviousLifecycleState: item.LifecycleState,
                NewLifecycleState: updated.LifecycleState,
                OccurredAt: now,
                AssignedTo: updated.AssignedTo,
                ReviewedBy: updated.ReviewedBy,
                ResolvedBy: request.ResolvedBy,
                Note: request.ResolutionNote,
                ExceptionRoute: updated.ExceptionRoute,
                ToleranceBand: updated.ToleranceBand,
                RequiredSignoffRole: updated.RequiredSignoffRole,
                SignoffStatus: updated.SignoffStatus,
                ExternalAccountId: updated.ExternalAccountId,
                CustodianId: updated.CustodianId,
                UpstreamSyncCursor: updated.UpstreamSyncCursor,
                Actor: request.ResolvedBy,
                BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions),
                CorrelationId: command.CorrelationId,
                CommandId: command.CommandId,
                Source: command.Source,
                Reason: command.Reason), ct).ConfigureAwait(false);
            var retainedAudit = _auditEvents[^1];
            var successOutcome = CreateTransitionOutcome(
                command,
                inputHash,
                OperationTerminalState.Succeeded,
                startedAt,
                updated,
                null,
                retainedAudit);
            _commandReceipts[command.CommandId] = new CaseworkCommandReceipt(
                command.CommandId,
                command.BreakId,
                command.Action,
                inputHash,
                updated,
                successOutcome);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RestoreState(state);
                return CreateLegacyPersistenceFailure(
                    "reconciliation.casework.resolve",
                    request.BreakId,
                    inputHash,
                    startedAt,
                    item,
                    ex);
            }

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated)
            {
                Outcome = successOutcome
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(ReconciliationCaseworkCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var startedAt = DateTimeOffset.UtcNow;
        var inputHash = ComputeCommandInputHash(command);
        if (string.IsNullOrWhiteSpace(command.CommandId))
        {
            return BindTransitionOutcome(
                Invalid(null, "Command id is required.", ReconciliationBreakQueueTransitionErrorCode.InvalidRequest, ["commandId"]),
                command,
                inputHash,
                startedAt);
        }
        if (string.IsNullOrWhiteSpace(command.Actor))
        {
            return BindTransitionOutcome(
                Invalid(null, "Actor is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
                command,
                inputHash,
                startedAt);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_commandReceipts.TryGetValue(command.CommandId, out var receipt))
            {
                if (!receipt.LegacyUnverified &&
                    string.Equals(receipt.BreakId, command.BreakId, StringComparison.OrdinalIgnoreCase) &&
                    receipt.Action == command.Action &&
                    string.Equals(receipt.InputHashSha256, inputHash, StringComparison.Ordinal))
                {
                    var replayAuditCount = _auditEvents.Count;
                    await AppendCaseworkReplayAuditAsync(
                        command,
                        _items!.GetValueOrDefault(command.BreakId),
                        "CaseworkReplayAccepted",
                        "Exact command replay returned the retained terminal receipt without reapplying the transition.",
                        ct).ConfigureAwait(false);
                    try
                    {
                        await PersistSnapshotAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _auditEvents.RemoveRange(replayAuditCount, _auditEvents.Count - replayAuditCount);
                        return CreatePersistenceFailure(command, inputHash, startedAt, receipt.Result, ex);
                    }
                    var replay = new ReconciliationBreakQueueTransitionResult(
                        ReconciliationBreakQueueTransitionStatus.Success,
                        receipt.Result)
                    {
                        Outcome = receipt.Outcome ?? CreateTransitionOutcome(
                            command,
                            inputHash,
                            OperationTerminalState.Succeeded,
                            startedAt,
                            receipt.Result,
                            null,
                            null)
                    };
                    return replay;
                }

                var conflictAuditCount = _auditEvents.Count;
                const string conflictReason = "Command id was already used for a different reconciliation casework input.";
                await AppendCaseworkReplayAuditAsync(
                    command,
                    _items!.GetValueOrDefault(command.BreakId),
                    "CaseworkReplayConflict",
                    conflictReason,
                    ct).ConfigureAwait(false);
                try
                {
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _auditEvents.RemoveRange(conflictAuditCount, _auditEvents.Count - conflictAuditCount);
                    return CreatePersistenceFailure(command, inputHash, startedAt, _items!.GetValueOrDefault(command.BreakId), ex);
                }

                return BindTransitionOutcome(new ReconciliationBreakQueueTransitionResult(
                    ReconciliationBreakQueueTransitionStatus.Conflict,
                    _items!.GetValueOrDefault(command.BreakId),
                    conflictReason,
                    ReconciliationBreakQueueTransitionErrorCode.CommandIdConflict),
                    command,
                    inputHash,
                    startedAt);
            }

            if (!_items!.TryGetValue(command.BreakId, out var item))
            {
                return BindTransitionOutcome(
                    new ReconciliationBreakQueueTransitionResult(
                        ReconciliationBreakQueueTransitionStatus.NotFound,
                        null,
                        "Break was not found."),
                    command,
                    inputHash,
                    startedAt);
            }

            var successorValidation = ValidateSupersessionSuccessor(item, command, _items);
            if (successorValidation is not null)
            {
                return BindTransitionOutcome(
                    successorValidation,
                    command,
                    inputHash,
                    startedAt);
            }

            var validation = ValidateCaseworkCommand(item, command);
            if (validation is not null)
            {
                try
                {
                    await AppendCaseworkRejectedAuditAsync(item, command, validation, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return CreatePersistenceFailure(command, inputHash, startedAt, item, ex);
                }

                return BindTransitionOutcome(validation, command, inputHash, startedAt);
            }

            var now = DateTimeOffset.UtcNow;
            var next = ApplyCaseworkMutation(item, command, now);
            next = StampComputedFields(next, now);
            var auditCount = _auditEvents.Count;
            _items[command.BreakId] = next;
            var primaryAudit = CreateAudit(command, item, next, now);
            await AppendAuditAsync(primaryAudit, ct).ConfigureAwait(false);
            if (HasSlaChanged(item, next))
            {
                await AppendAuditAsync(CreateAudit(command, item, next, now) with { EventType = next.SlaState == ReconciliationCaseSlaState.Breached ? "SlaBreached" : "SlaChanged" }, ct).ConfigureAwait(false);
            }
            var retainedAudit = _auditEvents[auditCount];
            var successOutcome = CreateTransitionOutcome(
                command,
                inputHash,
                OperationTerminalState.Succeeded,
                startedAt,
                next,
                null,
                retainedAudit);
            _commandReceipts[command.CommandId] = new CaseworkCommandReceipt(
                command.CommandId,
                command.BreakId,
                command.Action,
                inputHash,
                next,
                successOutcome);

            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _items[command.BreakId] = item;
                _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
                _commandReceipts.Remove(command.CommandId);
                return CreatePersistenceFailure(command, inputHash, startedAt, item, ex);
            }
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.Success,
                next)
            {
                Outcome = successOutcome
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBulkCaseworkResult> ApplyBulkCaseworkAsync(ReconciliationBulkCaseworkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.UtcNow;
        var inputHash = ComputeBulkInputHash(request);
        var requestProblem = ValidateBulkRequest(request);
        if (requestProblem is not null)
        {
            return CreateRejectedBulkResult(request, inputHash, startedAt, requestProblem);
        }

        var breakIds = request.BreakIds.ToArray();
        var bulkActionId = request.CommandId;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
            ResetCachedState();
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var hasCommandResult = _bulkResults.ContainsKey(request.CommandId);
            var hasIdempotencyResult =
                _bulkResultIdsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var idempotentBulkActionId) &&
                _bulkResults.ContainsKey(idempotentBulkActionId);
            if (hasCommandResult || hasIdempotencyResult)
            {
                var retainedBulkActionId = hasCommandResult
                    ? request.CommandId
                    : idempotentBulkActionId!;
                if (hasCommandResult && hasIdempotencyResult &&
                    !string.Equals(request.CommandId, idempotentBulkActionId, StringComparison.OrdinalIgnoreCase))
                {
                    return await RetainBulkReplayConflictAsync(
                        request,
                        inputHash,
                        startedAt,
                        "The command id and idempotency key are already bound to different bulk actions.",
                        ct).ConfigureAwait(false);
                }

                if (_bulkReceipts.TryGetValue(retainedBulkActionId, out var retainedReceipt) &&
                    !retainedReceipt.LegacyUnverified &&
                    string.Equals(retainedReceipt.CommandId, request.CommandId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(retainedReceipt.IdempotencyKey, request.IdempotencyKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(retainedReceipt.InputHashSha256, inputHash, StringComparison.Ordinal))
                {
                    var replayAuditCount = _auditEvents.Count;
                    await AppendBulkReplayAuditAsync(
                        request,
                        "BulkActionReplayAccepted",
                        "Exact bulk replay returned the retained terminal receipt without reapplying case mutations.",
                        ct).ConfigureAwait(false);
                    try
                    {
                        await PersistSnapshotAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _auditEvents.RemoveRange(replayAuditCount, _auditEvents.Count - replayAuditCount);
                        return CreatePersistenceFailedBulkResult(request, inputHash, startedAt, _items!, ex);
                    }
                    return retainedReceipt.Result;
                }

                return await RetainBulkReplayConflictAsync(
                    request,
                    inputHash,
                    startedAt,
                    "The command id or idempotency key was already used for a different reconciliation bulk request.",
                    ct).ConfigureAwait(false);
            }

            var originalItems = new Dictionary<string, ReconciliationBreakQueueItem>(_items!, StringComparer.OrdinalIgnoreCase);
            var auditCount = _auditEvents.Count;
            var beforeBulkPayload = JsonSerializer.Serialize(request, _jsonOptions);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: bulkActionId,
                EventType: "BulkActionRequested",
                PreviousStatus: null,
                NewStatus: ReconciliationBreakQueueStatus.Open,
                PreviousLifecycleState: null,
                NewLifecycleState: ReconciliationCaseLifecycleState.Open,
                OccurredAt: DateTimeOffset.UtcNow,
                AssignedTo: request.Assignee,
                ReviewedBy: null,
                ResolvedBy: null,
                Note: request.Note,
                Actor: request.Actor,
                BeforePayload: beforeBulkPayload,
                CorrelationId: request.CorrelationId,
                CommandId: bulkActionId,
                Source: request.Source,
                Reason: request.Reason), ct).ConfigureAwait(false);

            var prepared = new List<PreparedBulkCasework>(breakIds.Length);
            var pendingSupersessionEdges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var breakId in breakIds)
            {
                if (!_items!.TryGetValue(breakId, out var item))
                {
                    prepared.Add(new PreparedBulkCasework(
                        breakId,
                        null,
                        null,
                        new ReconciliationBreakQueueTransitionResult(
                            ReconciliationBreakQueueTransitionStatus.NotFound,
                            null,
                            "Break was not found.")));
                    continue;
                }

                var command = new ReconciliationCaseworkCommand(
                    BreakId: breakId,
                    Action: request.Action,
                    Actor: request.Actor,
                    CommandId: $"{bulkActionId}:{breakId}",
                    CorrelationId: request.CorrelationId,
                    Source: request.Source,
                    ExpectedVersion: item.Version,
                    Reason: request.Reason,
                    Assignee: request.Assignee,
                    Priority: request.Priority,
                    Status: request.Status,
                    Note: request.Note,
                    RootCauseCode: request.RootCauseCode,
                    ResolutionCode: request.ResolutionCode,
                    EvidenceLinks: request.EvidenceLinks,
                    ActionOrigin: request.ActionOrigin,
                    ApprovalActor: request.ApprovalActor,
                    ApprovalReference: request.ApprovalReference,
                    SupersedingBreakId: request.SupersedingBreakId);
                var validation = ValidateSupersessionSuccessor(
                        item,
                        command,
                        _items,
                        pendingSupersessionEdges)
                    ?? ValidateCaseworkCommand(item, command);
                if (validation is null &&
                    command.Action == ReconciliationCaseworkAction.Supersede &&
                    !string.IsNullOrWhiteSpace(command.SupersedingBreakId))
                {
                    pendingSupersessionEdges[item.BreakId] = command.SupersedingBreakId;
                }
                prepared.Add(new PreparedBulkCasework(breakId, item, command, validation));
            }

            var hasValidationFailures = prepared.Any(static entry => entry.Validation is not null);
            var atomicBatchBlocked = hasValidationFailures && !request.AllowPartialSuccess;
            var results = new List<ReconciliationBulkCaseworkCaseResult>(prepared.Count);
            foreach (var entry in prepared)
            {
                if (entry.Validation is not null)
                {
                    await AppendBulkFailureAuditAsync(request, entry, bulkActionId, ct).ConfigureAwait(false);
                    results.Add(new ReconciliationBulkCaseworkCaseResult(
                        entry.BreakId,
                        Succeeded: false,
                        WouldSucceed: false,
                        Error: entry.Validation.Error,
                        Item: entry.Item));
                    continue;
                }

                if (atomicBatchBlocked)
                {
                    var error = "The case was not changed because another case failed validation and partial success was disabled.";
                    await AppendAuditAsync(CreateAudit(entry.Command!, entry.Item!, entry.Item!, DateTimeOffset.UtcNow) with
                    {
                        EventType = "BulkActionCaseBlocked",
                        Reason = error
                    }, ct).ConfigureAwait(false);
                    results.Add(new ReconciliationBulkCaseworkCaseResult(
                        entry.BreakId,
                        Succeeded: false,
                        WouldSucceed: true,
                        Error: error,
                        Item: entry.Item));
                    continue;
                }

                if (request.DryRun)
                {
                    await AppendAuditAsync(CreateAudit(entry.Command!, entry.Item!, entry.Item!, DateTimeOffset.UtcNow) with
                    {
                        EventType = "BulkActionCaseValidated",
                        Reason = "Dry-run validation succeeded; no case state was changed."
                    }, ct).ConfigureAwait(false);
                    results.Add(new ReconciliationBulkCaseworkCaseResult(
                        entry.BreakId,
                        Succeeded: false,
                        WouldSucceed: true,
                        Error: null,
                        Item: entry.Item));
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var next = StampComputedFields(ApplyCaseworkMutation(entry.Item!, entry.Command!, now), now);
                _items![entry.BreakId] = next;
                await AppendAuditAsync(CreateAudit(entry.Command!, entry.Item!, next, now) with { EventType = "BulkActionCaseSucceeded" }, ct).ConfigureAwait(false);
                if (HasSlaChanged(entry.Item!, next))
                {
                    await AppendAuditAsync(CreateAudit(entry.Command!, entry.Item!, next, now) with
                    {
                        EventType = next.SlaState == ReconciliationCaseSlaState.Breached ? "SlaBreached" : "SlaChanged"
                    }, ct).ConfigureAwait(false);
                }
                results.Add(new ReconciliationBulkCaseworkCaseResult(entry.BreakId, true, true, null, next));
            }

            var successCount = results.Count(result => request.DryRun ? result.WouldSucceed : result.Succeeded);
            var failureCount = results.Count - successCount;
            var outcome = CreateBulkOutcome(
                request,
                inputHash,
                startedAt,
                results,
                persistenceFailure: null,
                receiptRetained: true);
            var result = new ReconciliationBulkCaseworkResult(
                bulkActionId,
                request.IdempotencyKey,
                request.DryRun,
                breakIds.Length,
                successCount,
                failureCount,
                results.ToArray())
            {
                InputHashSha256 = inputHash,
                Outcome = outcome
            };
            _bulkResults[bulkActionId] = result;
            _bulkResultIdsByIdempotencyKey[request.IdempotencyKey] = bulkActionId;
            _bulkReceipts[bulkActionId] = new BulkCaseworkReceipt(
                bulkActionId,
                request.CommandId,
                request.IdempotencyKey,
                inputHash,
                result);
            try
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _items = originalItems;
                _auditEvents.RemoveRange(auditCount, _auditEvents.Count - auditCount);
                _bulkResults.Remove(bulkActionId);
                _bulkResultIdsByIdempotencyKey.Remove(request.IdempotencyKey);
                _bulkReceipts.Remove(bulkActionId);
                return CreatePersistenceFailedBulkResult(request, inputHash, startedAt, originalItems, ex);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(string bulkActionIdOrIdempotencyKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bulkActionIdOrIdempotencyKey);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_bulkResults.TryGetValue(bulkActionIdOrIdempotencyKey, out var byId))
            {
                return byId;
            }

            return _bulkResultIdsByIdempotencyKey.TryGetValue(bulkActionIdOrIdempotencyKey, out var bulkActionId) &&
                _bulkResults.TryGetValue(bulkActionId, out var byIdempotencyKey)
                    ? byIdempotencyKey
                    : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        ReconciliationBreakQueueAuditEvent[] all;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            all = _auditEvents.ToArray();
        }
        finally
        {
            _gate.Release();
        }

        // Follow "BreakIdMigrated" links so a re-keyed case surfaces its full pre-migration trail,
        // which remains stored immutably under the superseded break id (the migration event carries
        // the prior item — and thus the prior break id — in its BeforePayload).
        var relevantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { breakId };
        var pending = new Queue<string>();
        pending.Enqueue(breakId);
        while (pending.Count > 0)
        {
            var currentId = pending.Dequeue();
            foreach (var migration in all.Where(entry =>
                entry.EventType == "BreakIdMigrated"
                && string.Equals(entry.BreakId, currentId, StringComparison.OrdinalIgnoreCase)))
            {
                var previousId = TryReadBreakIdFromPayload(migration.BeforePayload);
                if (previousId is not null && relevantIds.Add(previousId))
                {
                    pending.Enqueue(previousId);
                }
            }
        }

        return all
            .Where(entry => relevantIds.Contains(entry.BreakId))
            .OrderBy(static entry => entry.Sequence)
            .ThenBy(static entry => entry.OccurredAt)
            .ToArray();
    }

    private static string? TryReadBreakIdFromPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("breakId", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }


    public async Task<ReconciliationBreakQueueItem?> RebuildSnapshotFromAuditAsync(string breakId, CancellationToken ct = default)
    {
        var history = await GetAuditHistoryAsync(breakId, ct).ConfigureAwait(false);
        ReconciliationBreakQueueItem? rebuilt = null;
        foreach (var auditEvent in history.OrderBy(static entry => entry.Sequence).ThenBy(static entry => entry.OccurredAt))
        {
            if (string.Equals(auditEvent.EventType, "CaseDeleted", StringComparison.Ordinal))
            {
                rebuilt = null;
                continue;
            }
            if (string.IsNullOrWhiteSpace(auditEvent.AfterPayload))
            {
                continue;
            }

            rebuilt = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(auditEvent.AfterPayload, _jsonOptions)
                ?? throw new InvalidDataException(
                    $"Reconciliation audit event '{auditEvent.EventId}' retained a null case snapshot.");
        }

        return rebuilt;
    }

}
