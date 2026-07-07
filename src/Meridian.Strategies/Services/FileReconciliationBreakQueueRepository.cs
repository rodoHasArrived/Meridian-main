using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

public sealed class FileReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
{
    private readonly string _snapshotPath;
    private readonly string _auditPath;
    private readonly ILogger<FileReconciliationBreakQueueRepository> _logger;
    private readonly IReconciliationSlaPolicyProvider? _slaPolicyProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Dictionary<string, ReconciliationBreakQueueItem>? _items;
    private readonly Dictionary<string, ReconciliationBulkCaseworkResult> _bulkResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _bulkResultIdsByIdempotencyKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReconciliationCaseWorkflowService _workflowService = new ReconciliationCaseWorkflowService();

    public FileReconciliationBreakQueueRepository(
        string dataDirectory,
        ILogger<FileReconciliationBreakQueueRepository> logger,
        IReconciliationSlaPolicyProvider? slaPolicyProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _slaPolicyProvider = slaPolicyProvider;

        Directory.CreateDirectory(dataDirectory);
        _snapshotPath = Path.Combine(dataDirectory, "reconciliation-break-queue.json");
        _auditPath = Path.Combine(dataDirectory, "reconciliation-break-queue-audit.jsonl");
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (_items!.ContainsKey(item.BreakId))
            {
                return false;
            }

            var normalized = NormalizeLegacyCaseState(item);
            var created = StampComputedFields(normalized, normalized.DetectedAt);
            _items[item.BreakId] = created;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
                await EnsureLoadedAsync(ct).ConfigureAwait(false);

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
                    await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items![item.BreakId] = NormalizeLegacyCaseState(item);
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.Remove(breakId))
            {
                return false;
            }

            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(request.BreakId, out var item))
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
            _items[request.BreakId] = updated;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
                    Source: updated.SourceType,
                    Reason: updated.SourceReference), ct).ConfigureAwait(false);
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
                Source: updated.SourceType,
                Reason: updated.SourceReference), ct).ConfigureAwait(false);

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(request.BreakId, out var item))
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
                await AppendMaterialActionDeniedAuditAsync(
                    item,
                    request.ResolvedBy,
                    request.ResolutionNote,
                    commandId: null,
                    correlationId: null,
                    source: "reconciliation-break-resolve",
                    validation.Error ?? "Material action requires human operator origin.",
                    ct).ConfigureAwait(false);
                return validation;
            }

            var now = DateTimeOffset.UtcNow;
            var approval = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(request.BreakId, ReconciliationCaseTransitionAction.RequestApproval, request.ResolvedBy, request.OperatorRationale, ["resolution-note"]), now);
            if (approval.Status != ReconciliationBreakQueueTransitionStatus.Success || approval.Item is null)
            {
                return approval;
            }
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
                SignoffHistory = (item.SignoffHistory ?? []).Concat(
                [
                    new ReconciliationCaseSignoffRecord(request.ResolvedBy, item.RequiredSignoffRole ?? "operator", request.Status == ReconciliationBreakQueueStatus.Dismissed ? "DismissedFalsePositive" : request.Status.ToString(), request.OperatorRationale, now)
                ]).ToArray()
            };

            updated = StampComputedFields(updated, now);
            _items[request.BreakId] = updated;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
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
                Source: updated.SourceType,
                Reason: updated.SourceReference), ct).ConfigureAwait(false);

            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(ReconciliationCaseworkCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Actor))
        {
            return Invalid(null, "Actor is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(command.BreakId, out var item))
            {
                return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.NotFound, null, "Break was not found.");
            }

            var idempotent = await TryGetIdempotentCommandResultAsync(command.CommandId, ct).ConfigureAwait(false);
            if (idempotent is not null)
            {
                return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, idempotent);
            }

            var validation = ValidateCaseworkCommand(item, command);
            if (validation is not null)
            {
                if (validation.ErrorCode == ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator)
                {
                    await AppendMaterialActionDeniedAuditAsync(
                        item,
                        command.Actor,
                        command.Note,
                        command.CommandId,
                        command.CorrelationId,
                        command.Source,
                        validation.Error ?? "Material action requires human operator origin.",
                        ct).ConfigureAwait(false);
                }

                return validation;
            }

            var now = DateTimeOffset.UtcNow;
            var next = ApplyCaseworkMutation(item, command, now);
            next = StampComputedFields(next, now);
            _items[command.BreakId] = next;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(CreateAudit(command, item, next, now), ct).ConfigureAwait(false);
            if (HasSlaChanged(item, next))
            {
                await AppendAuditAsync(CreateAudit(command, item, next, now) with { EventType = next.SlaState == ReconciliationCaseSlaState.Breached ? "SlaBreached" : "SlaChanged" }, ct).ConfigureAwait(false);
            }
            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, next);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReconciliationBulkCaseworkResult> ApplyBulkCaseworkAsync(ReconciliationBulkCaseworkRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return new ReconciliationBulkCaseworkResult(
                BulkActionId: string.IsNullOrWhiteSpace(request.CommandId) ? Guid.NewGuid().ToString("N") : request.CommandId,
                IdempotencyKey: request.IdempotencyKey,
                DryRun: request.DryRun,
                RequestedCount: request.BreakIds.Count,
                SucceededCount: 0,
                FailedCount: request.BreakIds.Count,
                Results: request.BreakIds.Select(breakId => new ReconciliationBulkCaseworkCaseResult(
                    breakId,
                    Succeeded: false,
                    WouldSucceed: false,
                    Error: "Idempotency key is required.",
                    Item: null)).ToArray());
        }

        var distinctIds = request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(request.MaxCaseCount).ToArray();
        var results = new List<ReconciliationBulkCaseworkCaseResult>();
        var bulkActionId = string.IsNullOrWhiteSpace(request.CommandId) ? Guid.NewGuid().ToString("N") : request.CommandId;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_bulkResultIdsByIdempotencyKey.TryGetValue(request.IdempotencyKey, out var cachedBulkActionId) &&
                _bulkResults.TryGetValue(cachedBulkActionId, out var cached))
            {
                return cached;
            }

            await EnsureLoadedAsync(ct).ConfigureAwait(false);
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

            foreach (var breakId in distinctIds)
            {
                if (!_items!.TryGetValue(breakId, out var item))
                {
                    await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                        EventId: Guid.NewGuid().ToString("N"),
                        BreakId: breakId,
                        EventType: "BulkActionCaseFailed",
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
                        CorrelationId: request.CorrelationId,
                        CommandId: $"{bulkActionId}:{breakId}",
                        Source: request.Source,
                        Reason: "Break was not found."), ct).ConfigureAwait(false);
                    results.Add(new ReconciliationBulkCaseworkCaseResult(breakId, false, false, "Break was not found.", null));
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
                    ActionOrigin: request.ActionOrigin);
                var validation = ValidateCaseworkCommand(item, command);
                if (validation is not null)
                {
                    await AppendAuditAsync(CreateAudit(command, item, item, DateTimeOffset.UtcNow) with { EventType = "BulkActionCaseFailed", Reason = validation.Error }, ct).ConfigureAwait(false);
                    results.Add(new ReconciliationBulkCaseworkCaseResult(breakId, false, false, validation.Error, item));
                    if (!request.AllowPartialSuccess)
                    {
                        break;
                    }
                    continue;
                }

                if (request.DryRun)
                {
                    results.Add(new ReconciliationBulkCaseworkCaseResult(breakId, false, true, null, item));
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var next = StampComputedFields(ApplyCaseworkMutation(item, command, now), now);
                _items[breakId] = next;
                await AppendAuditAsync(CreateAudit(command, item, next, now) with { EventType = "BulkActionCaseSucceeded" }, ct).ConfigureAwait(false);
                if (HasSlaChanged(item, next))
                {
                    await AppendAuditAsync(CreateAudit(command, item, next, now) with { EventType = next.SlaState == ReconciliationCaseSlaState.Breached ? "SlaBreached" : "SlaChanged" }, ct).ConfigureAwait(false);
                }
                results.Add(new ReconciliationBulkCaseworkCaseResult(breakId, true, true, null, next));
            }

            if (!request.DryRun)
            {
                await PersistSnapshotAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        var result = new ReconciliationBulkCaseworkResult(
            bulkActionId,
            request.IdempotencyKey,
            request.DryRun,
            distinctIds.Length,
            results.Count(r => r.Succeeded || (request.DryRun && r.WouldSucceed)),
            results.Count(r => !r.WouldSucceed),
            results.ToArray());
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _bulkResults[bulkActionId] = result;
            _bulkResultIdsByIdempotencyKey[request.IdempotencyKey] = bulkActionId;
        }
        finally
        {
            _gate.Release();
        }

        return result;
    }

    public async Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(string bulkActionIdOrIdempotencyKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bulkActionIdOrIdempotencyKey);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
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
        if (!File.Exists(_auditPath))
        {
            return [];
        }

        var all = new List<ReconciliationBreakQueueAuditEvent>();

        await using var stream = File.OpenRead(_auditPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var auditEvent = JsonSerializer.Deserialize<ReconciliationBreakQueueAuditEvent>(line, _jsonOptions);
                if (auditEvent is not null)
                {
                    all.Add(auditEvent);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt reconciliation break queue audit event in {Path}", _auditPath);
            }
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
            if (string.IsNullOrWhiteSpace(auditEvent.AfterPayload))
            {
                continue;
            }

            rebuilt = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(auditEvent.AfterPayload, _jsonOptions);
        }

        return rebuilt;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_items is not null)
        {
            return;
        }

        if (!File.Exists(_snapshotPath))
        {
            _items = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        var snapshot = await JsonSerializer.DeserializeAsync<BreakQueueSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
        var loaded = snapshot?.Items ?? [];
        _items = loaded
            .Select(NormalizeLegacyCaseState)
            .ToDictionary(static item => item.BreakId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new BreakQueueSnapshot(_items!.Values.OrderByDescending(static item => item.LastUpdatedAt).ToArray());
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }

    private async Task AppendAuditAsync(ReconciliationBreakQueueAuditEvent auditEvent, CancellationToken ct)
    {
        var sequenced = auditEvent with
        {
            Sequence = auditEvent.Sequence > 0 ? auditEvent.Sequence : await NextAuditSequenceAsync(ct).ConfigureAwait(false),
            BeforePayloadHash = auditEvent.BeforePayloadHash ?? HashPayload(auditEvent.BeforePayload),
            AfterPayloadHash = auditEvent.AfterPayloadHash ?? HashPayload(auditEvent.AfterPayload)
        };
        var line = JsonSerializer.Serialize(sequenced, _jsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [line], ct).ConfigureAwait(false);
    }

    private async Task AppendMaterialActionDeniedAuditAsync(
        ReconciliationBreakQueueItem item,
        string actor,
        string? note,
        string? commandId,
        string? correlationId,
        string source,
        string reason,
        CancellationToken ct)
        => await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: item.BreakId,
            EventType: "MaterialActionDenied",
            PreviousStatus: item.Status,
            NewStatus: item.Status,
            PreviousLifecycleState: item.LifecycleState,
            NewLifecycleState: item.LifecycleState,
            OccurredAt: DateTimeOffset.UtcNow,
            AssignedTo: item.AssignedTo,
            ReviewedBy: item.ReviewedBy,
            ResolvedBy: item.ResolvedBy,
            Note: note,
            ExceptionRoute: item.ExceptionRoute,
            ToleranceBand: item.ToleranceBand,
            RequiredSignoffRole: item.RequiredSignoffRole,
            SignoffStatus: item.SignoffStatus,
            ExternalAccountId: item.ExternalAccountId,
            CustodianId: item.CustodianId,
            UpstreamSyncCursor: item.UpstreamSyncCursor,
            Actor: actor,
            BeforePayload: JsonSerializer.Serialize(item, _jsonOptions),
            AfterPayload: JsonSerializer.Serialize(item, _jsonOptions),
            CorrelationId: correlationId,
            CommandId: commandId,
            Source: source,
            Reason: reason), ct).ConfigureAwait(false);

    private async Task<long> NextAuditSequenceAsync(CancellationToken ct)
    {
        if (!File.Exists(_auditPath))
        {
            return 1;
        }

        var count = 0L;
        await using var stream = File.OpenRead(_auditPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is not null)
        {
            count++;
        }

        return count + 1;
    }

    private async Task<ReconciliationBreakQueueItem?> TryGetIdempotentCommandResultAsync(string commandId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(commandId) || !File.Exists(_auditPath))
        {
            return null;
        }

        ReconciliationBreakQueueItem? item = null;
        await using var stream = File.OpenRead(_auditPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var auditEvent = JsonSerializer.Deserialize<ReconciliationBreakQueueAuditEvent>(line, _jsonOptions);
                if (auditEvent is null ||
                    !string.Equals(auditEvent.CommandId, commandId, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(auditEvent.AfterPayload))
                {
                    continue;
                }

                item = JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(auditEvent.AfterPayload, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt reconciliation break queue audit event in {Path}", _auditPath);
            }
        }

        return item;
    }

    private static string? HashPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static ReconciliationBreakQueueTransitionResult? ValidateCaseworkCommand(ReconciliationBreakQueueItem item, ReconciliationCaseworkCommand command)
    {
        if (command.ExpectedVersion != item.Version)
        {
            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Conflict, item, "Case version conflict.", ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);
        }

        return command.Action switch
        {
            _ when RequiresHumanOrigin(command) && command.ActionOrigin != OperationsActionOriginDto.HumanOperator
                => Invalid(item, "Reviewed automation cannot resolve, sign off, or reopen reconciliation cases; a human operator approval is required.", ReconciliationBreakQueueTransitionErrorCode.MaterialActionRequiresHumanOperator, ["actionOrigin"], RequestedLifecycle(command)),
            ReconciliationCaseworkAction.Assign when string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignee is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status is null
                => Invalid(item, "Target lifecycle status is required.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Reopened && (!command.Privileged || string.IsNullOrWhiteSpace(command.Reason))
                => Invalid(item, "Privileged reopen requires a reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["reason"], command.Status),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.SignedOff && string.Equals(item.ResolvedBy, command.Actor, StringComparison.OrdinalIgnoreCase) && (!command.Privileged || string.IsNullOrWhiteSpace(command.Reason))
                => Invalid(item, "Resolver and signer must be different operators unless privileged override and reason are supplied.", ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict, ["reason"], command.Status),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Investigating && string.IsNullOrWhiteSpace(item.AssignedTo) && string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignment is required before investigation.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.AwaitingEvidence && string.IsNullOrWhiteSpace(command.Note)
                => Invalid(item, "Evidence request note is required before awaiting evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence),
            ReconciliationCaseworkAction.TransitionStatus when !IsLegalLifecycleTransition(item.LifecycleState, command.Status.Value)
                => Invalid(item, $"Cannot transition case from {item.LifecycleState} to {command.Status}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Resolved && string.IsNullOrWhiteSpace(item.RootCauseCode) && string.IsNullOrWhiteSpace(command.RootCauseCode)
                => Invalid(item, "Root cause code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingRootCause),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Resolved && string.IsNullOrWhiteSpace(item.ResolutionCode) && string.IsNullOrWhiteSpace(command.ResolutionCode)
                => Invalid(item, "Resolution code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingResolutionCode),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Resolved && !IsKnownRootCause(command.RootCauseCode ?? item.RootCauseCode)
                => Invalid(item, "Root cause code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Resolved && !IsKnownResolution(command.ResolutionCode ?? item.ResolutionCode)
                => Invalid(item, "Resolution code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Resolved && !HasRequiredResolutionEvidence(command.ResolutionCode ?? item.ResolutionCode, command.EvidenceLinks, item.EvidenceLinks, command.Note)
                => Invalid(item, "Resolution-specific evidence is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["evidenceLinks"], command.Status),
            ReconciliationCaseworkAction.SetRootCause when !IsKnownRootCause(command.RootCauseCode)
                => Invalid(item, "Root cause code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.SetResolution when !IsKnownResolution(command.ResolutionCode)
                => Invalid(item, "Resolution code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.AddComment when command.StatusTransition.HasValue && !IsLegalLifecycleTransition(item.LifecycleState, command.StatusTransition.Value)
                => Invalid(item, $"Cannot transition case from {item.LifecycleState} to {command.StatusTransition} from comment.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition, requestedState: command.StatusTransition),
            ReconciliationCaseworkAction.EditComment when string.IsNullOrWhiteSpace(command.CommentId)
                => Invalid(item, "Comment id is required for comment edit.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["commentId"]),
            ReconciliationCaseworkAction.DeleteComment when string.IsNullOrWhiteSpace(command.CommentId) || string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Comment id and deletion reason are required for comment deletion.", ReconciliationBreakQueueTransitionErrorCode.MissingReason, ["commentId", "reason"]),
            ReconciliationCaseworkAction.Resolve when item.LifecycleState is not (ReconciliationCaseLifecycleState.Investigating or ReconciliationCaseLifecycleState.AwaitingEvidence or ReconciliationCaseLifecycleState.Reopened)
                => Invalid(item, $"Cannot resolve case from {item.LifecycleState}.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.RootCauseCode) && string.IsNullOrWhiteSpace(command.RootCauseCode)
                => Invalid(item, "Root cause code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingRootCause),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.ResolutionCode) && string.IsNullOrWhiteSpace(command.ResolutionCode)
                => Invalid(item, "Resolution code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingResolutionCode),
            ReconciliationCaseworkAction.Resolve when !IsKnownRootCause(command.RootCauseCode ?? item.RootCauseCode)
                => Invalid(item, "Root cause code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.Resolve when !IsKnownResolution(command.ResolutionCode ?? item.ResolutionCode)
                => Invalid(item, "Resolution code is not in the reconciliation taxonomy.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy),
            ReconciliationCaseworkAction.Resolve when !HasRequiredResolutionEvidence(command.ResolutionCode ?? item.ResolutionCode, command.EvidenceLinks, item.EvidenceLinks, command.Note)
                => Invalid(item, "Resolution-specific evidence is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence, ["evidenceLinks"]),
            ReconciliationCaseworkAction.SignOff when item.LifecycleState != ReconciliationCaseLifecycleState.Resolved
                => Invalid(item, "Only resolved cases can be signed off.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition),
            ReconciliationCaseworkAction.SignOff when string.Equals(item.ResolvedBy, command.Actor, StringComparison.OrdinalIgnoreCase) && (!command.Privileged || string.IsNullOrWhiteSpace(command.Reason))
                => Invalid(item, "Resolver and signer must be different operators unless privileged override and reason are supplied.", ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict, ["reason"]),
            ReconciliationCaseworkAction.Reopen when item.LifecycleState != ReconciliationCaseLifecycleState.SignedOff
                => Invalid(item, "Only signed-off cases can be reopened through privileged reopen.", ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed),
            ReconciliationCaseworkAction.Reopen when !command.Privileged || string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Privileged reopen requires a reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason),
            _ => null
        };
    }

    private static bool RequiresHumanOrigin(ReconciliationCaseworkCommand command)
        => command.Action is ReconciliationCaseworkAction.Resolve or ReconciliationCaseworkAction.SignOff or ReconciliationCaseworkAction.Reopen ||
           command is { Action: ReconciliationCaseworkAction.TransitionStatus, Status: ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.SignedOff or ReconciliationCaseLifecycleState.Reopened };

    private static ReconciliationCaseLifecycleState? RequestedLifecycle(ReconciliationCaseworkCommand command)
        => command.Action switch
        {
            ReconciliationCaseworkAction.Resolve => ReconciliationCaseLifecycleState.Resolved,
            ReconciliationCaseworkAction.SignOff => ReconciliationCaseLifecycleState.SignedOff,
            ReconciliationCaseworkAction.Reopen => ReconciliationCaseLifecycleState.Reopened,
            ReconciliationCaseworkAction.TransitionStatus => command.Status,
            _ => null
        };

    private static ReconciliationBreakQueueTransitionResult Invalid(
        ReconciliationBreakQueueItem? item,
        string error,
        ReconciliationBreakQueueTransitionErrorCode code,
        IReadOnlyList<string>? missingFields = null,
        ReconciliationCaseLifecycleState? requestedState = null)
        => new(
            ReconciliationBreakQueueTransitionStatus.ValidationFailed,
            item,
            error,
            code,
            new ReconciliationCaseValidationProblem(
                item?.LifecycleState.ToString() ?? "Unknown",
                requestedState?.ToString() ?? "Unknown",
                missingFields ?? [],
                error));

    private static bool IsKnownRootCause(string? code)
        => !string.IsNullOrWhiteSpace(code) && RootCauseCodes.Contains(code);

    private static bool IsKnownResolution(string? code)
        => !string.IsNullOrWhiteSpace(code) && ResolutionCodes.Contains(code);

    private static bool HasRequiredResolutionEvidence(
        string? resolutionCode,
        IReadOnlyList<string>? commandEvidence,
        IReadOnlyList<string>? existingEvidence,
        string? note)
    {
        if (string.IsNullOrWhiteSpace(resolutionCode) || !ResolutionEvidencePrefixes.TryGetValue(resolutionCode, out var prefixes))
        {
            return true;
        }

        var evidence = (commandEvidence ?? [])
            .Concat(existingEvidence ?? [])
            .Append(note ?? string.Empty);
        return evidence.Any(value => prefixes.Any(prefix => value.Contains(prefix, StringComparison.OrdinalIgnoreCase)));
    }


    private static readonly HashSet<string> RootCauseCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BrokerCashTiming",
        "CustodianPositionLag",
        "SecurityMasterMapping",
        "LedgerClassification",
        "AccrualTiming",
        "CorporateActionTiming",
        "DismissedFalsePositive"
    };

    private static readonly HashSet<string> ResolutionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LedgerAdjusted",
        "BrokerStatementCorrected",
        "SecurityMasterUpdated",
        "AccrualAdjusted",
        "EvidenceAccepted",
        "DismissedFalsePositive",
        "LegacyResolved"
    };

    private static readonly Dictionary<string, IReadOnlyList<string>> ResolutionEvidencePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LedgerAdjusted"] = ["ledger-event:", "journal:"],
        ["BrokerStatementCorrected"] = ["provider-record:", "statement:"],
        ["SecurityMasterUpdated"] = ["security-master:"],
        ["AccrualAdjusted"] = ["ledger-event:", "journal:"],
        ["EvidenceAccepted"] = ["report-pack:", "evidence:"]
    };

    public static ReconciliationTaxonomySnapshot Taxonomy { get; } = new(
        Version: 1,
        RootCauses: RootCauseCodes.Select(code => new ReconciliationTaxonomyValue(code, code, 1, true)).ToArray(),
        ResolutionCodes: ResolutionCodes.Select(code => new ReconciliationTaxonomyValue(
            code,
            code,
            1,
            true,
            ResolutionEvidencePrefixes.TryGetValue(code, out var prefixes) ? prefixes : null)).ToArray());

    private static bool IsLegalLifecycleTransition(ReconciliationCaseLifecycleState current, ReconciliationCaseLifecycleState next)
        => current == next || (current, next) switch
        {
            (ReconciliationCaseLifecycleState.Open, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Reopened, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Investigating, ReconciliationCaseLifecycleState.AwaitingEvidence) => true,
            (ReconciliationCaseLifecycleState.AwaitingEvidence, ReconciliationCaseLifecycleState.Investigating) => true,
            (ReconciliationCaseLifecycleState.Investigating, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.AwaitingEvidence, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.Reopened, ReconciliationCaseLifecycleState.Resolved) => true,
            (ReconciliationCaseLifecycleState.Resolved, ReconciliationCaseLifecycleState.SignedOff) => true,
            (ReconciliationCaseLifecycleState.SignedOff, ReconciliationCaseLifecycleState.Reopened) => true,
            _ => false
        };

    private static ReconciliationBreakQueueItem NormalizeLegacyCaseState(ReconciliationBreakQueueItem item)
    {
        var lifecycle = item.LifecycleState == ReconciliationCaseLifecycleState.InReview
            ? ReconciliationCaseLifecycleState.Investigating
            : item.LifecycleState;
        var status = item.Status;
        var resolutionCode = item.ResolutionCode;
        var rootCauseCode = item.RootCauseCode;
        var signoffStatus = item.SignoffStatus;

        if (item.Status == ReconciliationBreakQueueStatus.Dismissed)
        {
            lifecycle = ReconciliationCaseLifecycleState.Resolved;
            status = ReconciliationBreakQueueStatus.Resolved;
            resolutionCode ??= "DismissedFalsePositive";
            rootCauseCode ??= "DismissedFalsePositive";
            signoffStatus ??= "dismissed-false-positive";
        }

        return item with
        {
            LifecycleState = lifecycle,
            Status = status,
            ResolutionCode = resolutionCode,
            RootCauseCode = rootCauseCode,
            SignoffStatus = signoffStatus
        };
    }

    private static ReconciliationBreakQueueItem ApplyCaseworkMutation(ReconciliationBreakQueueItem item, ReconciliationCaseworkCommand command, DateTimeOffset now)
    {
        var comments = (item.Comments ?? []).ToList();
        var evidence = (item.EvidenceLinks ?? []).ToList();
        switch (command.Action)
        {
            case ReconciliationCaseworkAction.Assign:
                return item with { AssignedTo = command.Assignee, AssigneeId = command.Assignee, AssigneeDisplayName = command.Assignee, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.ChangePriority:
                return item with { Priority = command.Priority ?? item.Priority, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.TransitionStatus:
                var lifecycle = command.Status ?? item.LifecycleState;
                return item with
                {
                    LifecycleState = lifecycle,
                    Status = MapQueueStatus(lifecycle, item.Status),
                    AssignedTo = command.Assignee ?? item.AssignedTo,
                    AssigneeId = command.Assignee ?? item.AssigneeId,
                    AssigneeDisplayName = command.Assignee ?? item.AssigneeDisplayName,
                    RootCauseCode = command.RootCauseCode ?? item.RootCauseCode,
                    ResolutionCode = command.ResolutionCode ?? item.ResolutionCode,
                    EvidenceLinks = lifecycle == ReconciliationCaseLifecycleState.Resolved && command.EvidenceLinks is not null
                        ? evidence.Concat(command.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : item.EvidenceLinks,
                    EvidenceCount = lifecycle == ReconciliationCaseLifecycleState.Resolved && command.EvidenceLinks is not null
                        ? evidence.Concat(command.EvidenceLinks).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        : item.EvidenceCount,
                    ResolvedBy = lifecycle == ReconciliationCaseLifecycleState.Resolved ? command.Actor : item.ResolvedBy,
                    ResolvedAt = lifecycle == ReconciliationCaseLifecycleState.Resolved ? now : item.ResolvedAt,
                    LifecycleRationale = command.Note,
                    LastUpdatedAt = now
                };
            case ReconciliationCaseworkAction.AddComment:
                comments.Add(new ReconciliationCaseComment(
                    command.CommentId ?? Guid.NewGuid().ToString("N"),
                    command.ParentCommentId,
                    command.Actor,
                    command.Actor,
                    command.Visibility,
                    command.Note ?? string.Empty,
                    command.EvidenceLinks ?? [],
                    now,
                    Mentions: command.Mentions,
                    LinkedEvidenceIds: command.EvidenceLinks,
                    StatusTransition: command.StatusTransition));
                evidence.AddRange(command.EvidenceLinks ?? []);
                var commentLifecycle = command.StatusTransition ?? item.LifecycleState;
                return item with { LifecycleState = commentLifecycle, Status = MapQueueStatus(commentLifecycle, item.Status), Comments = comments.ToArray(), EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.EditComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { Body = command.Note ?? c.Body, EditedAt = now, PreviousTextHash = HashPayload(c.Body), EditReason = command.Reason } : c).ToList();
                return item with { Comments = comments.ToArray(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.DeleteComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { DeletedAt = now, DeletedBy = command.Actor, PreviousTextHash = HashPayload(c.Body), DeleteReason = command.Reason } : c).ToList();
                return item with { Comments = comments.ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetRootCause:
                return item with { RootCauseCode = command.RootCauseCode, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetResolution:
                return item with { ResolutionCode = command.ResolutionCode, ResolutionNote = command.Note ?? item.ResolutionNote, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.LinkEvidence:
                evidence.AddRange(command.EvidenceLinks ?? []);
                return item with { EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Resolve:
                evidence.AddRange(command.EvidenceLinks ?? []);
                var distinctEvidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Resolved, Status = ReconciliationBreakQueueStatus.Resolved, RootCauseCode = command.RootCauseCode ?? item.RootCauseCode, ResolutionCode = command.ResolutionCode ?? item.ResolutionCode, ResolvedBy = command.Actor, ResolvedAt = now, ResolutionNote = command.Note ?? item.ResolutionNote, EvidenceLinks = distinctEvidence, EvidenceCount = distinctEvidence.Length, SignoffStatus = "ready-for-signoff", LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SignOff:
                return item with { LifecycleState = ReconciliationCaseLifecycleState.SignedOff, Status = ReconciliationBreakQueueStatus.SignedOff, SignedOffBy = command.Actor, SignedOffAt = now, SignOffNote = command.Note, SignoffStatus = "signed-off", LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Reopen:
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Reopened, Status = ReconciliationBreakQueueStatus.Open, ReopenedBy = command.Actor, ReopenedAt = now, ReopenReason = command.Reason, LastUpdatedAt = now };
            default:
                return item;
        }
    }

    private static ReconciliationBreakQueueStatus MapQueueStatus(ReconciliationCaseLifecycleState lifecycle, ReconciliationBreakQueueStatus current)
    {
        if (lifecycle is ReconciliationCaseLifecycleState.Open or ReconciliationCaseLifecycleState.Reopened)
        {
            return ReconciliationBreakQueueStatus.Open;
        }

        if (lifecycle == ReconciliationCaseLifecycleState.InReview || lifecycle == ReconciliationCaseLifecycleState.Investigating || lifecycle == ReconciliationCaseLifecycleState.AwaitingEvidence)
        {
            return ReconciliationBreakQueueStatus.InReview;
        }

        if (lifecycle == ReconciliationCaseLifecycleState.Resolved)
        {
            return ReconciliationBreakQueueStatus.Resolved;
        }

        return lifecycle == ReconciliationCaseLifecycleState.SignedOff ? ReconciliationBreakQueueStatus.SignedOff : current;
    }

    private ReconciliationBreakQueueItem StampComputedFields(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        var policy = _slaPolicyProvider?.ResolvePolicy(item) ?? ReconciliationSlaCalculator.DefaultPolicyFor(item);
        var sla = ReconciliationSlaCalculator.Compute(item, policy, now);
        return item with
        {
            Version = item.Version + 1,
            SlaPolicyId = sla.PolicyId,
            SlaDueAt = sla.DueAt,
            SlaWarningAt = sla.WarningAt,
            SlaBreachedAt = sla.BreachedAt,
            SlaBreached = sla.State == ReconciliationCaseSlaState.Breached,
            SlaState = sla.State,
            AgeBand = sla.AgeBand,
            BusinessAgeHours = sla.BusinessAgeHours,
            LastActivityAt = now,
            Score = ComputeScore(item, now)
        };
    }


    private static bool HasSlaChanged(ReconciliationBreakQueueItem before, ReconciliationBreakQueueItem after)
        => before.SlaState != after.SlaState ||
           before.SlaBreached != after.SlaBreached ||
           before.SlaDueAt != after.SlaDueAt ||
           before.SlaWarningAt != after.SlaWarningAt ||
           before.SlaBreachedAt != after.SlaBreachedAt ||
           !string.Equals(before.SlaPolicyId, after.SlaPolicyId, StringComparison.OrdinalIgnoreCase);

    private ReconciliationBreakQueueAuditEvent CreateAudit(ReconciliationCaseworkCommand command, ReconciliationBreakQueueItem before, ReconciliationBreakQueueItem after, DateTimeOffset now)
        => new(
            EventId: Guid.NewGuid().ToString("N"),
            BreakId: command.BreakId,
            EventType: ToAuditEventType(command.Action),
            PreviousStatus: before.Status,
            NewStatus: after.Status,
            PreviousLifecycleState: before.LifecycleState,
            NewLifecycleState: after.LifecycleState,
            OccurredAt: now,
            AssignedTo: after.AssignedTo,
            ReviewedBy: after.ReviewedBy,
            ResolvedBy: after.ResolvedBy,
            Note: command.Note,
            ExceptionRoute: after.ExceptionRoute,
            ToleranceBand: after.ToleranceBand,
            RequiredSignoffRole: after.RequiredSignoffRole,
            SignoffStatus: after.SignoffStatus,
            ExternalAccountId: after.ExternalAccountId,
            CustodianId: after.CustodianId,
            UpstreamSyncCursor: after.UpstreamSyncCursor,
            Actor: command.Actor,
            BeforePayload: JsonSerializer.Serialize(before, _jsonOptions),
            AfterPayload: JsonSerializer.Serialize(after, _jsonOptions),
            CorrelationId: command.CorrelationId,
            CommandId: command.CommandId,
            Source: command.Source,
            Reason: command.Reason,
            CausationId: command.CausationId);

    private static string ToAuditEventType(ReconciliationCaseworkAction action)
        => action switch
        {
            ReconciliationCaseworkAction.Assign => "Assigned",
            ReconciliationCaseworkAction.ChangePriority => "PriorityChanged",
            ReconciliationCaseworkAction.TransitionStatus => "StatusChanged",
            ReconciliationCaseworkAction.AddComment => "CommentAdded",
            ReconciliationCaseworkAction.EditComment => "CommentEdited",
            ReconciliationCaseworkAction.DeleteComment => "CommentDeleted",
            ReconciliationCaseworkAction.SetRootCause => "RootCauseSet",
            ReconciliationCaseworkAction.SetResolution => "ResolutionSet",
            ReconciliationCaseworkAction.LinkEvidence => "EvidenceLinked",
            ReconciliationCaseworkAction.SignOff => "SignedOff",
            ReconciliationCaseworkAction.Reopen => "Reopen",
            ReconciliationCaseworkAction.Resolve => "ResolutionSet",
            _ => action.ToString()
        };

    private static ReconciliationBreakScore ComputeScore(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        var materiality = Math.Min(50m, Math.Abs(item.Variance));
        var ageHours = Math.Max(0d, (now - item.DetectedAt).TotalHours);
        var ageComponent = Math.Min(25, (int)Math.Round(ageHours / 4d, MidpointRounding.AwayFromZero));
        var counterparty = string.IsNullOrWhiteSpace(item.Counterparty) ? 0 : 15;
        var recurring = item.StateTransitions?.Count(t => t.To == ReconciliationCaseLifecycleState.InReview) > 1 ? 10 : 0;
        var severityScore = (int)Math.Min(100, materiality + ageComponent + counterparty + recurring);
        var priorityScore = Math.Min(100, severityScore + (item.Severity == ReconciliationBreakSeverity.Critical ? 20 : item.Severity == ReconciliationBreakSeverity.High ? 10 : 0));
        return new ReconciliationBreakScore(severityScore, priorityScore, materiality, ageHours, counterparty, recurring, priorityScore >= 70, ComputeSlaDueAt(item), now > ComputeSlaDueAt(item) ? now : null);
    }

    private static DateTimeOffset ComputeSlaDueAt(ReconciliationBreakQueueItem item)
    {
        var hours = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 48
        };
        return item.DetectedAt.AddHours(hours);
    }

    private static bool IsSlaBreached(ReconciliationBreakQueueItem item)
        => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview && DateTimeOffset.UtcNow > ComputeSlaDueAt(item);

    private sealed record BreakQueueSnapshot(IReadOnlyList<ReconciliationBreakQueueItem> Items);
}
