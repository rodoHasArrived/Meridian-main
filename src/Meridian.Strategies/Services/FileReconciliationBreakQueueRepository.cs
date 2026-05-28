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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Dictionary<string, ReconciliationBreakQueueItem>? _items;
    private readonly Dictionary<string, ReconciliationBulkCaseworkResult> _bulkResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReconciliationCaseWorkflowService _workflowService = new ReconciliationCaseWorkflowService();

    public FileReconciliationBreakQueueRepository(string dataDirectory, ILogger<FileReconciliationBreakQueueRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
            if (_bulkResults.TryGetValue(request.IdempotencyKey, out var cached))
            {
                return cached;
            }

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

            _items[item.BreakId] = StampComputedFields(item, item.DetectedAt);
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: item.BreakId,
                EventType: "CaseCreated",
                PreviousStatus: null,
                NewStatus: item.Status,
                PreviousLifecycleState: null,
                NewLifecycleState: item.LifecycleState,
                OccurredAt: item.DetectedAt,
                AssignedTo: item.AssignedTo,
                ReviewedBy: item.ReviewedBy,
                ResolvedBy: item.ResolvedBy,
                Note: item.ResolutionNote,
                ExceptionRoute: item.ExceptionRoute,
                ToleranceBand: item.ToleranceBand,
                RequiredSignoffRole: item.RequiredSignoffRole,
                SignoffStatus: item.SignoffStatus,
                ExternalAccountId: item.ExternalAccountId,
                CustodianId: item.CustodianId,
                UpstreamSyncCursor: item.UpstreamSyncCursor,
                Actor: item.AssignedTo ?? item.ReviewedBy ?? item.ResolvedBy,
                BeforePayload: null,
                AfterPayload: JsonSerializer.Serialize(item, _jsonOptions)), ct).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            _items![item.BreakId] = item;
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
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions)), ct).ConfigureAwait(false);

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

            var now = DateTimeOffset.UtcNow;
            var approval = _workflowService.Apply(item, new ReconciliationCaseTransitionCommand(request.BreakId, ReconciliationCaseTransitionAction.RequestApproval, request.ResolvedBy, request.OperatorRationale, ["resolution-note"]), now);
            if (approval.Status != ReconciliationBreakQueueTransitionStatus.Success || approval.Item is null)
            {
                return approval;
            }
            var updated = request.Status == ReconciliationBreakQueueStatus.Dismissed
                ? approval.Item with
                {
                    Status = request.Status,
                    ResolvedBy = request.ResolvedBy,
                    ResolvedAt = now,
                    ResolutionNote = request.ResolutionNote,
                    SignoffStatus = "dismissed",
                    SignoffHistory = (item.SignoffHistory ?? []).Concat(
                    [
                        new ReconciliationCaseSignoffRecord(request.ResolvedBy, item.RequiredSignoffRole ?? "operator", request.Status.ToString(), request.OperatorRationale, now)
                    ]).ToArray()
                }
                : approval.Item with
                {
                    ResolvedBy = request.ResolvedBy,
                    ResolvedAt = now,
                    ResolutionNote = request.ResolutionNote,
                    SignoffStatus = "awaiting-approval"
                };

            updated = StampComputedFields(updated, now);
            _items[request.BreakId] = updated;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(
                EventId: Guid.NewGuid().ToString("N"),
                BreakId: request.BreakId,
                EventType: request.Status == ReconciliationBreakQueueStatus.Resolved ? "Resolved" : "Closed",
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
                AfterPayload: JsonSerializer.Serialize(updated, _jsonOptions)), ct).ConfigureAwait(false);

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

            var validation = ValidateCaseworkCommand(item, command);
            if (validation is not null)
            {
                return validation;
            }

            var now = DateTimeOffset.UtcNow;
            var next = ApplyCaseworkMutation(item, command, now);
            next = StampComputedFields(next, now);
            _items[command.BreakId] = next;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(CreateAudit(command, item, next, now), ct).ConfigureAwait(false);
            if (item.SlaState != next.SlaState || item.SlaBreached != next.SlaBreached)
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
        var distinctIds = request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(request.MaxCaseCount).ToArray();
        var results = new List<ReconciliationBulkCaseworkCaseResult>();
        var bulkActionId = string.IsNullOrWhiteSpace(request.CommandId) ? Guid.NewGuid().ToString("N") : request.CommandId;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_bulkResults.TryGetValue(request.IdempotencyKey, out var cached))
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
                    ResolutionCode: request.ResolutionCode);
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
                if (item.SlaState != next.SlaState || item.SlaBreached != next.SlaBreached)
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
        _bulkResults[request.IdempotencyKey] = result;
        return result;
    }

    public async Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        if (!File.Exists(_auditPath))
        {
            return [];
        }

        var events = new List<ReconciliationBreakQueueAuditEvent>();

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
                if (auditEvent is not null && string.Equals(auditEvent.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(auditEvent);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt reconciliation break queue audit event in {Path}", _auditPath);
            }
        }

        return events
            .OrderBy(static entry => entry.OccurredAt)
            .ToArray();
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
        _items = loaded.ToDictionary(static item => item.BreakId, StringComparer.OrdinalIgnoreCase);
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
        var line = JsonSerializer.Serialize(auditEvent, _jsonOptions);
        await AtomicFileWriter.AppendLinesAsync(_auditPath, [line], ct).ConfigureAwait(false);
    }



    private static ReconciliationBreakQueueTransitionResult? ValidateCaseworkCommand(ReconciliationBreakQueueItem item, ReconciliationCaseworkCommand command)
    {
        if (command.ExpectedVersion != item.Version)
        {
            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Conflict, item, "Case version conflict.", ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);
        }

        return command.Action switch
        {
            ReconciliationCaseworkAction.Assign when string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignee is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.Investigating && string.IsNullOrWhiteSpace(item.AssignedTo) && string.IsNullOrWhiteSpace(command.Assignee)
                => Invalid(item, "Assignment is required before investigation.", ReconciliationBreakQueueTransitionErrorCode.MissingActor),
            ReconciliationCaseworkAction.TransitionStatus when command.Status == ReconciliationCaseLifecycleState.AwaitingEvidence && string.IsNullOrWhiteSpace(command.Note)
                => Invalid(item, "Evidence request note is required before awaiting evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.RootCauseCode) && string.IsNullOrWhiteSpace(command.RootCauseCode)
                => Invalid(item, "Root cause code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingRootCause),
            ReconciliationCaseworkAction.Resolve when string.IsNullOrWhiteSpace(item.ResolutionCode) && string.IsNullOrWhiteSpace(command.ResolutionCode)
                => Invalid(item, "Resolution code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingResolutionCode),
            ReconciliationCaseworkAction.SignOff when string.Equals(item.ResolvedBy, command.Actor, StringComparison.OrdinalIgnoreCase)
                => Invalid(item, "Resolver and signer must be different operators.", ReconciliationBreakQueueTransitionErrorCode.ResolverSignerConflict),
            ReconciliationCaseworkAction.Reopen when item.LifecycleState != ReconciliationCaseLifecycleState.SignedOff
                => Invalid(item, "Only signed-off cases can be reopened through privileged reopen.", ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed),
            ReconciliationCaseworkAction.Reopen when !command.Privileged || string.IsNullOrWhiteSpace(command.Reason)
                => Invalid(item, "Privileged reopen requires a reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason),
            _ => null
        };
    }

    private static ReconciliationBreakQueueTransitionResult Invalid(ReconciliationBreakQueueItem? item, string error, ReconciliationBreakQueueTransitionErrorCode code)
        => new(ReconciliationBreakQueueTransitionStatus.ValidationFailed, item, error, code);

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
                return item with { LifecycleState = command.Status ?? item.LifecycleState, Status = MapQueueStatus(command.Status ?? item.LifecycleState, item.Status), LifecycleRationale = command.Note, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.AddComment:
                comments.Add(new ReconciliationCaseComment(command.CommentId ?? Guid.NewGuid().ToString("N"), command.ParentCommentId, command.Actor, command.Actor, command.Visibility, command.Note ?? string.Empty, command.EvidenceLinks ?? [], now));
                evidence.AddRange(command.EvidenceLinks ?? []);
                return item with { Comments = comments.ToArray(), EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.EditComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { Body = command.Note ?? c.Body, EditedAt = now } : c).ToList();
                return item with { Comments = comments.ToArray(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.DeleteComment:
                comments = comments.Select(c => c.CommentId == command.CommentId ? c with { DeletedAt = now, DeletedBy = command.Actor } : c).ToList();
                return item with { Comments = comments.ToArray(), CommentCount = comments.Count(c => c.DeletedAt is null), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetRootCause:
                return item with { RootCauseCode = command.RootCauseCode, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.SetResolution:
                return item with { ResolutionCode = command.ResolutionCode, ResolutionNote = command.Note ?? item.ResolutionNote, LastUpdatedAt = now };
            case ReconciliationCaseworkAction.LinkEvidence:
                evidence.AddRange(command.EvidenceLinks ?? []);
                return item with { EvidenceLinks = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), EvidenceCount = evidence.Distinct(StringComparer.OrdinalIgnoreCase).Count(), LastUpdatedAt = now };
            case ReconciliationCaseworkAction.Resolve:
                return item with { LifecycleState = ReconciliationCaseLifecycleState.Resolved, Status = ReconciliationBreakQueueStatus.Resolved, RootCauseCode = command.RootCauseCode ?? item.RootCauseCode, ResolutionCode = command.ResolutionCode ?? item.ResolutionCode, ResolvedBy = command.Actor, ResolvedAt = now, ResolutionNote = command.Note ?? item.ResolutionNote, SignoffStatus = "ready-for-signoff", LastUpdatedAt = now };
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

    private static ReconciliationBreakQueueItem StampComputedFields(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        var sla = ReconciliationSlaCalculator.Compute(item, ReconciliationSlaCalculator.DefaultPolicyFor(item), now);
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
            Score = ComputeScore(item)
        };
    }

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
            Reason: command.Reason);

    private static string ToAuditEventType(ReconciliationCaseworkAction action)
        => action switch
        {
            ReconciliationCaseworkAction.Assign => "AssigneeChanged",
            ReconciliationCaseworkAction.ChangePriority => "PriorityChanged",
            ReconciliationCaseworkAction.TransitionStatus => "StatusChanged",
            ReconciliationCaseworkAction.AddComment => "CommentAdded",
            ReconciliationCaseworkAction.EditComment => "CommentEdited",
            ReconciliationCaseworkAction.DeleteComment => "CommentDeleted",
            ReconciliationCaseworkAction.SetRootCause => "RootCauseSet",
            ReconciliationCaseworkAction.SetResolution => "ResolutionSet",
            ReconciliationCaseworkAction.LinkEvidence => "EvidenceLinked",
            ReconciliationCaseworkAction.SignOff => "SignOff",
            ReconciliationCaseworkAction.Reopen => "Reopen",
            ReconciliationCaseworkAction.Resolve => "ResolutionSet",
            _ => action.ToString()
        };

    private static ReconciliationBreakScore ComputeScore(ReconciliationBreakQueueItem item)
    {
        var materiality = Math.Min(50m, Math.Abs(item.Variance));
        var ageHours = Math.Max(0d, (DateTimeOffset.UtcNow - item.DetectedAt).TotalHours);
        var ageComponent = Math.Min(25, (int)Math.Round(ageHours / 4d, MidpointRounding.AwayFromZero));
        var counterparty = string.IsNullOrWhiteSpace(item.Counterparty) ? 0 : 15;
        var recurring = item.StateTransitions?.Count(t => t.To == ReconciliationCaseLifecycleState.InReview) > 1 ? 10 : 0;
        var severityScore = (int)Math.Min(100, materiality + ageComponent + counterparty + recurring);
        var priorityScore = Math.Min(100, severityScore + (item.Severity == ReconciliationBreakSeverity.Critical ? 20 : item.Severity == ReconciliationBreakSeverity.High ? 10 : 0));
        return new ReconciliationBreakScore(severityScore, priorityScore, materiality, ageHours, counterparty, recurring, priorityScore >= 70, ComputeSlaDueAt(item), DateTimeOffset.UtcNow > ComputeSlaDueAt(item) ? DateTimeOffset.UtcNow : null);
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
