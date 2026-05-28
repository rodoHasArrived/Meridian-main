using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Services;

public sealed class FileReconciliationBreakQueueRepository : IReconciliationBreakQueueRepository
{
    private const int AuditSchemaVersion = 1;
    private static readonly ReconciliationSlaPolicy[] DefaultPolicies =
    [
        new("critical-default", null, null, ReconciliationBreakSeverity.Critical, ReconciliationCasePriority.Critical, "UTC", 4, 1, true, ReconciliationSlaStopPolicy.StopOnResolved),
        new("high-default", null, null, ReconciliationBreakSeverity.High, ReconciliationCasePriority.High, "UTC", 8, 2, true, ReconciliationSlaStopPolicy.StopOnResolved),
        new("medium-default", null, null, ReconciliationBreakSeverity.Medium, ReconciliationCasePriority.Normal, "UTC", 24, 4, true, ReconciliationSlaStopPolicy.StopOnResolved),
        new("low-default", null, null, ReconciliationBreakSeverity.Info, ReconciliationCasePriority.Low, "UTC", 48, 8, true, ReconciliationSlaStopPolicy.StopOnResolved)
    ];

    private readonly string _snapshotPath;
    private readonly string _auditPath;
    private readonly ILogger<FileReconciliationBreakQueueRepository> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Dictionary<string, ReconciliationBulkActionResult> _bulkResultsByKey = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ReconciliationBreakQueueItem>? _items;

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
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            IEnumerable<ReconciliationBreakQueueItem> items = _items!.Values.Select(item => NormalizeComputed(item, now));
            if (status.HasValue)
            {
                items = items.Where(item => item.Status == status.Value || IsLegacyStatusMatch(item, status.Value));
            }

            return items.OrderByDescending(static item => item.LastUpdatedAt).ToArray();
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
            return _items!.TryGetValue(breakId, out var item) ? NormalizeComputed(item, DateTimeOffset.UtcNow) : null;
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
            if (_items!.ContainsKey(item.BreakId)) return false;
            var now = item.DetectedAt == default ? DateTimeOffset.UtcNow : item.DetectedAt;
            var seeded = NormalizeComputed(Migrate(item) with { Version = Math.Max(1, item.Version), LastActivityAt = item.LastActivityAt ?? now }, now);
            _items[item.BreakId] = seeded;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(BuildAudit("CaseCreated", null, seeded, seeded.AssignedTo ?? seeded.ReviewedBy ?? seeded.ResolvedBy ?? "system", seeded.Reason, null, null, "repository", null, seeded.DetectedAt), ct).ConfigureAwait(false);
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
            _items![item.BreakId] = NormalizeComputed(Migrate(item), DateTimeOffset.UtcNow);
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.Remove(breakId)) return false;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default)
    {
        var assignment = await AssignAsync(
            new ReconciliationAssignRequest(request.BreakId, request.AssignedTo, request.AssignedTo, request.ReviewNote ?? "Review assignment."),
            request.ReviewedBy,
            source: "review-compatibility-wrapper",
            ct: ct).ConfigureAwait(false);
        if (assignment.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            return assignment;
        }

        return await TransitionStatusAsync(
            new ReconciliationStatusTransitionRequest(request.BreakId, ReconciliationCaseLifecycleState.Investigating, request.ReviewNote ?? "Review started.", ["review-note"]),
            request.ReviewedBy,
            source: "review-compatibility-wrapper",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default)
    {
        var rootCause = string.IsNullOrWhiteSpace(request.RootCauseCode) ? "OperatorReviewed" : request.RootCauseCode;
        var resolution = request.Status == ReconciliationBreakQueueStatus.Dismissed
            ? "DismissedFalsePositive"
            : string.IsNullOrWhiteSpace(request.ResolutionCode) ? "ResolvedByOperator" : request.ResolutionCode;
        var rootResult = await SetRootCauseAsync(new ReconciliationTaxonomyRequest(request.BreakId, rootCause, null, request.OperatorRationale, request.ExpectedVersion), request.ResolvedBy, source: "resolve-compatibility-wrapper", ct: ct).ConfigureAwait(false);
        if (rootResult.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            return rootResult;
        }

        var resolutionResult = await SetResolutionAsync(new ReconciliationTaxonomyRequest(request.BreakId, resolution, request.ResolutionNote, request.OperatorRationale), request.ResolvedBy, source: "resolve-compatibility-wrapper", ct: ct).ConfigureAwait(false);
        if (resolutionResult.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            return resolutionResult;
        }

        return await TransitionStatusAsync(
            new ReconciliationStatusTransitionRequest(request.BreakId, ReconciliationCaseLifecycleState.Resolved, request.OperatorRationale, ["resolution-note"]),
            request.ResolvedBy,
            source: "resolve-compatibility-wrapper",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> AssignAsync(ReconciliationAssignRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Assignee)) return Fail("Assignee is required.", ReconciliationBreakQueueTransitionErrorCode.MissingAssignee);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "AssigneeChanged", request.Reason, item => item with
        {
            AssignedTo = request.Assignee.Trim(),
            AssigneeId = request.Assignee.Trim(),
            AssigneeDisplayName = string.IsNullOrWhiteSpace(request.AssigneeDisplayName) ? request.Assignee.Trim() : request.AssigneeDisplayName.Trim(),
            AssignedBy = actor,
            AssignedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> ChangePriorityAsync(ReconciliationPriorityRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "PriorityChanged", request.Reason, item => item with { Priority = request.Priority }, ct).ConfigureAwait(false);

    public async Task<ReconciliationBreakQueueTransitionResult> SetRootCauseAsync(ReconciliationTaxonomyRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        if (!IsValidTaxonomy(request.Code)) return Fail("Root cause code is required and must use taxonomy-code characters.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "RootCauseSet", request.Reason, item => item with { RootCauseCode = request.Code.Trim() }, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> SetResolutionAsync(ReconciliationTaxonomyRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        if (!IsValidTaxonomy(request.Code)) return Fail("Resolution code is required and must use taxonomy-code characters.", ReconciliationBreakQueueTransitionErrorCode.InvalidTaxonomy);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "ResolutionSet", request.Reason, item => item with { ResolutionCode = request.Code.Trim(), ResolutionNote = request.Note ?? item.ResolutionNote }, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> AddCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? actorDisplayName = null, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return Fail("Comment body is required.", ReconciliationBreakQueueTransitionErrorCode.MissingReason);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "CommentAdded", request.Reason ?? "Comment added.", item =>
        {
            var now = DateTimeOffset.UtcNow;
            var comment = new ReconciliationCaseComment(Guid.NewGuid().ToString("N"), request.BreakId, NormalizeBlank(request.ParentCommentId), actor, actorDisplayName ?? actor, request.Body.Trim(), request.Visibility, request.EvidenceLinks, now);
            return item with { Comments = (item.Comments ?? []).Concat([comment]).ToArray(), CommentCount = (item.Comments ?? []).Count(c => c.DeletedAt is null) + 1, EvidenceLinks = MergeEvidence(item.EvidenceLinks, request.EvidenceLinks), EvidenceCount = MergeEvidence(item.EvidenceLinks, request.EvidenceLinks).Count };
        }, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationBreakQueueTransitionResult> EditCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => await MutateCommentAsync(request, actor, correlationId, commandId, source, "CommentEdited", c => c with { Body = request.Body, EditedAt = DateTimeOffset.UtcNow, EditedBy = actor, Version = c.Version + 1 }, ct).ConfigureAwait(false);

    public async Task<ReconciliationBreakQueueTransitionResult> DeleteCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => await MutateCommentAsync(request, actor, correlationId, commandId, source, "CommentDeleted", c => c with { DeletedAt = DateTimeOffset.UtcNow, DeletedBy = actor, DeleteReason = request.Reason, Version = c.Version + 1 }, ct).ConfigureAwait(false);

    public async Task<ReconciliationBreakQueueTransitionResult> TransitionStatusAsync(ReconciliationStatusTransitionRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Fail("Reason is required.", ReconciliationBreakQueueTransitionErrorCode.MissingReason);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, "StatusChanged", request.Reason, item =>
        {
            var validation = ValidateTransition(item, request, actor);
            if (validation is not null) throw new ReconciliationCaseValidationException(validation.Value.Message, validation.Value.Code);
            var now = DateTimeOffset.UtcNow;
            var lifecycle = NormalizeLifecycle(request.Status);
            var queueStatus = ToQueueStatus(lifecycle, item.Status);
            var transition = new ReconciliationCaseStateTransition(Guid.NewGuid().ToString("N"), NormalizeLifecycle(item.LifecycleState), lifecycle, actor, request.Reason, now, request.EvidenceLinks, item.StateTransitions?.LastOrDefault()?.EntryHash, Guid.NewGuid().ToString("N"));
            var resolvedBy = lifecycle == ReconciliationCaseLifecycleState.Resolved ? actor : item.ResolvedBy;
            var resolvedAt = lifecycle == ReconciliationCaseLifecycleState.Resolved ? now : item.ResolvedAt;
            var evidenceLinks = MergeEvidence(item.EvidenceLinks, request.EvidenceLinks);
            return item with
            {
                LifecycleState = lifecycle,
                Status = queueStatus,
                LifecycleRationale = request.Reason,
                StateTransitions = (item.StateTransitions ?? []).Concat([transition]).ToArray(),
                ReviewedBy = lifecycle == ReconciliationCaseLifecycleState.Investigating ? actor : item.ReviewedBy,
                ReviewedAt = lifecycle == ReconciliationCaseLifecycleState.Investigating ? now : item.ReviewedAt,
                ResolvedBy = resolvedBy,
                ResolvedAt = resolvedAt,
                SignOffBy = lifecycle == ReconciliationCaseLifecycleState.SignedOff ? actor : item.SignOffBy,
                SignOffAt = lifecycle == ReconciliationCaseLifecycleState.SignedOff ? now : item.SignOffAt,
                SignOffNote = lifecycle == ReconciliationCaseLifecycleState.SignedOff ? request.Reason : item.SignOffNote,
                ReopenedBy = lifecycle == ReconciliationCaseLifecycleState.Reopened ? actor : item.ReopenedBy,
                ReopenedAt = lifecycle == ReconciliationCaseLifecycleState.Reopened ? now : item.ReopenedAt,
                ReopenReason = lifecycle == ReconciliationCaseLifecycleState.Reopened ? request.Reason : item.ReopenReason,
                EvidenceLinks = evidenceLinks,
                EvidenceCount = evidenceLinks.Count
            };
        }, ct).ConfigureAwait(false);
    }

    public Task<ReconciliationBreakQueueTransitionResult> SignOffAsync(ReconciliationSignOffRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => TransitionStatusAsync(new ReconciliationStatusTransitionRequest(request.BreakId, ReconciliationCaseLifecycleState.SignedOff, request.Note, request.EvidenceLinks, request.ExpectedVersion), actor, correlationId, commandId, source, ct);

    public Task<ReconciliationBreakQueueTransitionResult> ReopenAsync(ReconciliationReopenRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => TransitionStatusAsync(new ReconciliationStatusTransitionRequest(request.BreakId, ReconciliationCaseLifecycleState.Reopened, request.Reason, null, request.ExpectedVersion), actor, correlationId, commandId, source, ct);

    public async Task<ReconciliationBulkActionResult> BulkActionAsync(ReconciliationBulkActionRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return new ReconciliationBulkActionResult(Guid.NewGuid().ToString("N"), string.Empty, request.DryRun, false, request.BreakIds.Count, 0, request.BreakIds.Count, [new ReconciliationBulkCaseResult(string.Empty, false, "Idempotency key is required.")]);
        }
        var cacheKey = BulkResultCacheKey(request.IdempotencyKey, request.DryRun);
        if (_bulkResultsByKey.TryGetValue(cacheKey, out var existing)) return existing;
        var distinctIds = request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(request.MaxCaseCount).ToArray();
        var bulkId = Guid.NewGuid().ToString("N");
        var cases = new List<ReconciliationBulkCaseResult>();
        await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(Guid.NewGuid().ToString("N"), bulkId, "BulkActionRequested", null, ReconciliationBreakQueueStatus.Open, null, ReconciliationCaseLifecycleState.Open, DateTimeOffset.UtcNow, null, null, null, request.Reason, Actor: actor, CorrelationId: correlationId, CommandId: commandId, Source: source, Reason: request.Reason, SchemaVersion: AuditSchemaVersion), ct).ConfigureAwait(false);
        foreach (var breakId in distinctIds)
        {
            var result = request.DryRun ? await ValidateBulkCaseAsync(breakId, request, actor, ct).ConfigureAwait(false) : await ExecuteBulkCaseAsync(breakId, request, actor, correlationId, commandId, source, ct).ConfigureAwait(false);
            cases.Add(result);
            await AppendAuditAsync(new ReconciliationBreakQueueAuditEvent(Guid.NewGuid().ToString("N"), breakId, result.Success ? "BulkActionCaseSucceeded" : "BulkActionCaseFailed", null, result.Item?.Status ?? ReconciliationBreakQueueStatus.Open, null, result.Item?.LifecycleState ?? ReconciliationCaseLifecycleState.Open, DateTimeOffset.UtcNow, result.Item?.AssignedTo, result.Item?.ReviewedBy, result.Item?.ResolvedBy, result.Error, Actor: actor, CorrelationId: correlationId, CommandId: commandId, Source: source, Reason: request.Reason, SchemaVersion: AuditSchemaVersion), ct).ConfigureAwait(false);
            if (!result.Success && !request.AllowPartialSuccess) break;
        }
        var response = new ReconciliationBulkActionResult(bulkId, request.IdempotencyKey, request.DryRun, true, distinctIds.Length, cases.Count(c => c.Success), cases.Count(c => !c.Success), cases);
        _bulkResultsByKey[cacheKey] = response;
        return response;
    }

    public Task<ReconciliationBulkActionResult?> GetBulkActionResultAsync(string idempotencyKeyOrActionId, CancellationToken ct = default)
    {
        var result = _bulkResultsByKey.Values.FirstOrDefault(r => string.Equals(r.BulkActionId, idempotencyKeyOrActionId, StringComparison.OrdinalIgnoreCase))
            ?? _bulkResultsByKey.Values
                .Where(r => string.Equals(r.IdempotencyKey, idempotencyKeyOrActionId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static r => r.DryRun)
                .FirstOrDefault();
        return Task.FromResult(result);
    }

    private static string BulkResultCacheKey(string idempotencyKey, bool dryRun)
        => (dryRun ? "dry-run:" : "execute:") + idempotencyKey;

    public async Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        if (!File.Exists(_auditPath)) return [];
        var events = new List<ReconciliationBreakQueueAuditEvent>();
        await using var stream = File.OpenRead(_auditPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var auditEvent = JsonSerializer.Deserialize<ReconciliationBreakQueueAuditEvent>(line, _jsonOptions);
                if (auditEvent is not null && string.Equals(auditEvent.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) events.Add(auditEvent);
            }
            catch (JsonException ex) { _logger.LogWarning(ex, "Skipping corrupt reconciliation break queue audit event in {Path}", _auditPath); }
        }
        return events.OrderBy(static entry => entry.OccurredAt).ToArray();
    }

    private async Task<ReconciliationBreakQueueTransitionResult> MutateCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? correlationId, string? commandId, string? source, string eventType, Func<ReconciliationCaseComment, ReconciliationCaseComment> update, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CommentId)) return Fail("CommentId is required.", ReconciliationBreakQueueTransitionErrorCode.MissingReason);
        return await MutateAsync(request.BreakId, request.ExpectedVersion, actor, correlationId, commandId, source, eventType, request.Reason ?? eventType, item =>
        {
            var comments = (item.Comments ?? []).Select(c => string.Equals(c.CommentId, request.CommentId, StringComparison.OrdinalIgnoreCase) ? update(c) : c).ToArray();
            if (!comments.Any(c => string.Equals(c.CommentId, request.CommentId, StringComparison.OrdinalIgnoreCase))) throw new ReconciliationCaseValidationException("Comment was not found.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
            return item with { Comments = comments, CommentCount = comments.Count(c => c.DeletedAt is null) };
        }, ct).ConfigureAwait(false);
    }

    private async Task<ReconciliationBreakQueueTransitionResult> MutateAsync(string breakId, int? expectedVersion, string actor, string? correlationId, string? commandId, string? source, string eventType, string? reason, Func<ReconciliationBreakQueueItem, ReconciliationBreakQueueItem> mutation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) return Fail("Actor is required.", ReconciliationBreakQueueTransitionErrorCode.MissingActor);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (!_items!.TryGetValue(breakId, out var item)) return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.NotFound, null, "Break was not found.");
            item = Migrate(item);
            if (expectedVersion.HasValue && expectedVersion.Value != item.Version) return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Conflict, item, "Case version conflict.", ReconciliationBreakQueueTransitionErrorCode.ConcurrencyConflict);
            ReconciliationBreakQueueItem updated;
            try { updated = mutation(item); }
            catch (ReconciliationCaseValidationException ex) { return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.InvalidTransition, item, ex.Message, ex.Code); }
            var now = DateTimeOffset.UtcNow;
            updated = NormalizeComputed(updated with { LastUpdatedAt = now, LastActivityAt = now, Version = item.Version + 1 }, now);
            _items[breakId] = updated;
            await PersistSnapshotAsync(ct).ConfigureAwait(false);
            await AppendAuditAsync(BuildAudit(eventType, item, updated, actor, reason, correlationId, commandId, source, reason, now), ct).ConfigureAwait(false);
            return new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.Success, updated);
        }
        finally { _gate.Release(); }
    }

    private async Task<ReconciliationBulkCaseResult> ValidateBulkCaseAsync(string breakId, ReconciliationBulkActionRequest request, string actor, CancellationToken ct)
    {
        var item = await GetByIdAsync(breakId, ct).ConfigureAwait(false);
        if (item is null) return new ReconciliationBulkCaseResult(breakId, false, "Break was not found.");
        try { _ = BuildBulkMutationRequest(breakId, request); return new ReconciliationBulkCaseResult(breakId, true, null, item); }
        catch (InvalidOperationException ex) { return new ReconciliationBulkCaseResult(breakId, false, ex.Message, item); }
    }

    private async Task<ReconciliationBulkCaseResult> ExecuteBulkCaseAsync(string breakId, ReconciliationBulkActionRequest request, string actor, string? correlationId, string? commandId, string? source, CancellationToken ct)
    {
        var result = request.Action switch
        {
            ReconciliationBulkActionType.Assign => await AssignAsync(new ReconciliationAssignRequest(breakId, request.Assignee ?? string.Empty, null, request.Reason ?? "Bulk assignment."), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.ChangePriority => await ChangePriorityAsync(new ReconciliationPriorityRequest(breakId, request.Priority ?? ReconciliationCasePriority.Normal, request.Reason ?? "Bulk priority change."), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.AddComment => await AddCommentAsync(new ReconciliationCommentMutationRequest(breakId, null, null, request.Comment ?? string.Empty, ReconciliationCommentVisibility.Internal, request.EvidenceLinks, request.Reason), actor, actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.TransitionStatus => await TransitionStatusAsync(new ReconciliationStatusTransitionRequest(breakId, request.Status ?? ReconciliationCaseLifecycleState.Investigating, request.Reason ?? "Bulk status transition.", request.EvidenceLinks), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.SetRootCause => await SetRootCauseAsync(new ReconciliationTaxonomyRequest(breakId, request.RootCauseCode ?? string.Empty, null, request.Reason ?? "Bulk root cause update."), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.SetResolution => await SetResolutionAsync(new ReconciliationTaxonomyRequest(breakId, request.ResolutionCode ?? string.Empty, request.ResolutionNote, request.Reason ?? "Bulk resolution update."), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.Resolve => await TransitionStatusAsync(new ReconciliationStatusTransitionRequest(breakId, ReconciliationCaseLifecycleState.Resolved, request.Reason ?? request.ResolutionNote ?? "Bulk resolve.", request.EvidenceLinks), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            ReconciliationBulkActionType.SignOff => await SignOffAsync(new ReconciliationSignOffRequest(breakId, request.Reason ?? "Bulk sign-off.", request.EvidenceLinks), actor, correlationId, commandId, source, ct).ConfigureAwait(false),
            _ => new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Unsupported bulk action.")
        };
        return new ReconciliationBulkCaseResult(breakId, result.Status == ReconciliationBreakQueueTransitionStatus.Success, result.Error, result.Item);
    }

    private static object BuildBulkMutationRequest(string breakId, ReconciliationBulkActionRequest request) => request.Action switch
    {
        ReconciliationBulkActionType.Assign when string.IsNullOrWhiteSpace(request.Assignee) => throw new InvalidOperationException("Assignee is required."),
        ReconciliationBulkActionType.AddComment when string.IsNullOrWhiteSpace(request.Comment) => throw new InvalidOperationException("Comment is required."),
        ReconciliationBulkActionType.SetRootCause when string.IsNullOrWhiteSpace(request.RootCauseCode) => throw new InvalidOperationException("Root cause code is required."),
        ReconciliationBulkActionType.SetResolution when string.IsNullOrWhiteSpace(request.ResolutionCode) => throw new InvalidOperationException("Resolution code is required."),
        _ => request
    };

    private static (string Message, ReconciliationBreakQueueTransitionErrorCode Code)? ValidateTransition(ReconciliationBreakQueueItem item, ReconciliationStatusTransitionRequest request, string actor)
    {
        var from = NormalizeLifecycle(item.LifecycleState);
        var to = NormalizeLifecycle(request.Status);
        if (to == ReconciliationCaseLifecycleState.Investigating && string.IsNullOrWhiteSpace(item.AssignedTo)) return ("Assignment is required before investigation.", ReconciliationBreakQueueTransitionErrorCode.MissingAssignee);
        if (to == ReconciliationCaseLifecycleState.AwaitingEvidence && (request.EvidenceLinks is null || request.EvidenceLinks.Count == 0) && string.IsNullOrWhiteSpace(request.Reason)) return ("Evidence request note is required before awaiting evidence.", ReconciliationBreakQueueTransitionErrorCode.MissingEvidence);
        if (to == ReconciliationCaseLifecycleState.Resolved && string.IsNullOrWhiteSpace(item.RootCauseCode)) return ("Root cause code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingRootCause);
        if (to == ReconciliationCaseLifecycleState.Resolved && string.IsNullOrWhiteSpace(item.ResolutionCode) && !string.Equals(item.ResolutionCode, "DismissedFalsePositive", StringComparison.OrdinalIgnoreCase)) return ("Resolution code is required before resolution.", ReconciliationBreakQueueTransitionErrorCode.MissingResolution);
        if (to == ReconciliationCaseLifecycleState.SignedOff && !string.Equals(from.ToString(), nameof(ReconciliationCaseLifecycleState.Resolved), StringComparison.Ordinal) && from != ReconciliationCaseLifecycleState.Resolved) return ("Only resolved cases can be signed off.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
        if (to == ReconciliationCaseLifecycleState.SignedOff && string.Equals(item.ResolvedBy, actor, StringComparison.OrdinalIgnoreCase)) return ("Signer must differ from resolver.", ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired);
        if (to == ReconciliationCaseLifecycleState.Reopened && from == ReconciliationCaseLifecycleState.SignedOff && string.IsNullOrWhiteSpace(request.Reason)) return ("Privileged reopen from signed-off requires a reason.", ReconciliationBreakQueueTransitionErrorCode.MissingReason);
        if (from == ReconciliationCaseLifecycleState.Open && to is not (ReconciliationCaseLifecycleState.Investigating or ReconciliationCaseLifecycleState.Reopened)) return ("Open cases must enter investigation before later lifecycle states.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
        if (from == ReconciliationCaseLifecycleState.SignedOff && to != ReconciliationCaseLifecycleState.Reopened) return ("Signed-off cases can only be reopened.", ReconciliationBreakQueueTransitionErrorCode.IllegalTransition);
        return null;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_items is not null) return;
        if (!File.Exists(_snapshotPath)) { _items = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.OrdinalIgnoreCase); return; }
        await using var stream = File.OpenRead(_snapshotPath);
        var snapshot = await JsonSerializer.DeserializeAsync<BreakQueueSnapshot>(stream, _jsonOptions, ct).ConfigureAwait(false);
        _items = (snapshot?.Items ?? []).Select(Migrate).ToDictionary(static item => item.BreakId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task PersistSnapshotAsync(CancellationToken ct)
    {
        var snapshot = new BreakQueueSnapshot(_items!.Values.OrderByDescending(static item => item.LastUpdatedAt).ToArray());
        await AtomicFileWriter.WriteAsync(_snapshotPath, JsonSerializer.Serialize(snapshot, _jsonOptions), ct).ConfigureAwait(false);
    }

    private async Task AppendAuditAsync(ReconciliationBreakQueueAuditEvent auditEvent, CancellationToken ct)
        => await AtomicFileWriter.AppendLinesAsync(_auditPath, [JsonSerializer.Serialize(auditEvent, _jsonOptions)], ct).ConfigureAwait(false);

    private ReconciliationBreakQueueAuditEvent BuildAudit(string eventType, ReconciliationBreakQueueItem? before, ReconciliationBreakQueueItem after, string actor, string? note, string? correlationId, string? commandId, string? source, string? reason, DateTimeOffset occurredAt)
        => new(Guid.NewGuid().ToString("N"), after.BreakId, eventType, before?.Status, after.Status, before?.LifecycleState, after.LifecycleState, occurredAt, after.AssignedTo, after.ReviewedBy, after.ResolvedBy, note, after.ExceptionRoute, after.ToleranceBand, after.RequiredSignoffRole, after.SignoffStatus, after.ExternalAccountId, after.CustodianId, after.UpstreamSyncCursor, actor, before is null ? null : JsonSerializer.Serialize(before, _jsonOptions), JsonSerializer.Serialize(after, _jsonOptions), correlationId, commandId, source, reason, AuditSchemaVersion);

    private static ReconciliationBreakQueueTransitionResult Fail(string error, ReconciliationBreakQueueTransitionErrorCode code)
        => new(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, error, code);

    private static bool IsLegacyStatusMatch(ReconciliationBreakQueueItem item, ReconciliationBreakQueueStatus requested)
        => requested == ReconciliationBreakQueueStatus.InReview && item.Status == ReconciliationBreakQueueStatus.Investigating;

    private static ReconciliationBreakQueueItem Migrate(ReconciliationBreakQueueItem item)
    {
        var lifecycle = NormalizeLifecycle(item.LifecycleState);
        if (item.Status == ReconciliationBreakQueueStatus.Dismissed && string.IsNullOrWhiteSpace(item.ResolutionCode)) item = item with { ResolutionCode = "DismissedFalsePositive" };
        return item with { LifecycleState = lifecycle, AssignedTo = item.AssignedTo ?? item.AssigneeId, AssigneeId = item.AssigneeId ?? item.AssignedTo, LastActivityAt = item.LastActivityAt ?? item.LastUpdatedAt };
    }

    private static ReconciliationCaseLifecycleState NormalizeLifecycle(ReconciliationCaseLifecycleState state)
        => state == ReconciliationCaseLifecycleState.InReview ? ReconciliationCaseLifecycleState.Investigating : state;

    private static ReconciliationBreakQueueStatus ToQueueStatus(ReconciliationCaseLifecycleState lifecycle, ReconciliationBreakQueueStatus current) => lifecycle switch
    {
        ReconciliationCaseLifecycleState.Open => ReconciliationBreakQueueStatus.Open,
        ReconciliationCaseLifecycleState.Investigating => ReconciliationBreakQueueStatus.InReview,
        ReconciliationCaseLifecycleState.AwaitingEvidence => ReconciliationBreakQueueStatus.AwaitingEvidence,
        ReconciliationCaseLifecycleState.Resolved => ReconciliationBreakQueueStatus.Resolved,
        ReconciliationCaseLifecycleState.SignedOff => ReconciliationBreakQueueStatus.SignedOff,
        ReconciliationCaseLifecycleState.Reopened => ReconciliationBreakQueueStatus.Reopened,
        ReconciliationCaseLifecycleState.LegacyTerminal => ReconciliationBreakQueueStatus.LegacyTerminal,
        _ => current
    };

    private static ReconciliationBreakQueueItem NormalizeComputed(ReconciliationBreakQueueItem item, DateTimeOffset now)
    {
        item = Migrate(item);
        var policy = ResolvePolicy(item);
        var due = item.SlaDueAt ?? AddBusinessHours(item.DetectedAt, policy.DueBusinessHours, policy);
        var warning = item.SlaWarningAt ?? due.AddHours(-policy.WarningBusinessHoursBeforeDue);
        var businessAgeHours = Math.Max(0, BusinessHoursBetween(item.DetectedAt, now, policy));
        var state = ComputeSlaState(item, now, due, warning, policy);
        var evidenceCount = item.EvidenceLinks?.Count ?? item.Comments?.SelectMany(c => c.EvidenceLinks ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? item.EvidenceCount;
        return item with
        {
            SlaPolicyId = item.SlaPolicyId ?? policy.PolicyId,
            SlaDueAt = due,
            SlaWarningAt = warning,
            SlaBreachedAt = state == ReconciliationSlaState.Breached ? item.SlaBreachedAt ?? now : item.SlaBreachedAt,
            SlaBreached = state == ReconciliationSlaState.Breached,
            SlaState = state,
            BusinessAgeHours = businessAgeHours,
            AgeBand = ToAgeBand(businessAgeHours),
            Score = ComputeScore(item, businessAgeHours, due),
            CommentCount = item.Comments?.Count(c => c.DeletedAt is null) ?? item.CommentCount,
            EvidenceCount = evidenceCount
        };
    }

    private static ReconciliationSlaState ComputeSlaState(ReconciliationBreakQueueItem item, DateTimeOffset now, DateTimeOffset due, DateTimeOffset warning, ReconciliationSlaPolicy policy)
    {
        var lifecycle = NormalizeLifecycle(item.LifecycleState);
        if (lifecycle == ReconciliationCaseLifecycleState.AwaitingEvidence && policy.PauseAwaitingEvidenceWithRequest && (item.EvidenceLinks?.Count > 0 || item.Comments?.Any(c => c.Visibility == ReconciliationCommentVisibility.CloseEvidence) == true)) return ReconciliationSlaState.Paused;
        if (policy.StopPolicy == ReconciliationSlaStopPolicy.StopOnResolved && lifecycle is ReconciliationCaseLifecycleState.Resolved or ReconciliationCaseLifecycleState.SignedOff) return ReconciliationSlaState.Stopped;
        if (policy.StopPolicy == ReconciliationSlaStopPolicy.StopOnSignedOff && lifecycle == ReconciliationCaseLifecycleState.SignedOff) return ReconciliationSlaState.Stopped;
        if (now > due) return ReconciliationSlaState.Breached;
        if (now >= warning) return ReconciliationSlaState.Warning;
        return ReconciliationSlaState.Running;
    }

    private static ReconciliationBreakScore ComputeScore(ReconciliationBreakQueueItem item, double businessAgeHours, DateTimeOffset? due)
    {
        var materiality = Math.Min(50m, Math.Abs(item.Variance));
        var ageComponent = Math.Min(25, (int)Math.Round(businessAgeHours / 4d, MidpointRounding.AwayFromZero));
        var counterparty = string.IsNullOrWhiteSpace(item.Counterparty) ? 0 : 15;
        var recurring = item.StateTransitions?.Count(t => NormalizeLifecycle(t.To) == ReconciliationCaseLifecycleState.Investigating) > 1 ? 10 : 0;
        var severityScore = (int)Math.Min(100, materiality + ageComponent + counterparty + recurring);
        var priorityScore = Math.Min(100, severityScore + (item.Priority == ReconciliationCasePriority.Critical ? 25 : item.Priority == ReconciliationCasePriority.High ? 15 : 0));
        return new ReconciliationBreakScore(severityScore, priorityScore, materiality, businessAgeHours, counterparty, recurring, priorityScore >= 70, due, item.SlaBreachedAt);
    }

    private static ReconciliationSlaPolicy ResolvePolicy(ReconciliationBreakQueueItem item)
        => DefaultPolicies.FirstOrDefault(p => p.Severity == item.Severity || p.Priority == item.Priority) ?? DefaultPolicies[^1];

    private static DateTimeOffset AddBusinessHours(DateTimeOffset start, int hours, ReconciliationSlaPolicy policy)
        => start.AddHours(hours); // UTC-aware placeholder; policy carries timezone/calendar for deterministic future calendars.

    private static double BusinessHoursBetween(DateTimeOffset start, DateTimeOffset end, ReconciliationSlaPolicy policy)
        => Math.Max(0, (end - start).TotalHours);

    private static ReconciliationCaseAgeBand ToAgeBand(double businessHours)
        => businessHours <= 8 ? ReconciliationCaseAgeBand.SameDay : businessHours <= 16 ? ReconciliationCaseAgeBand.OneToTwoBusinessDays : businessHours <= 40 ? ReconciliationCaseAgeBand.ThreeToFiveBusinessDays : ReconciliationCaseAgeBand.OlderThanFiveBusinessDays;

    private static bool IsValidTaxonomy(string? code)
        => !string.IsNullOrWhiteSpace(code) && code.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string? NormalizeBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> MergeEvidence(IReadOnlyList<string>? existing, IReadOnlyList<string>? added)
        => (existing ?? []).Concat(added ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed record BreakQueueSnapshot(IReadOnlyList<ReconciliationBreakQueueItem> Items);

    private sealed class ReconciliationCaseValidationException(string message, ReconciliationBreakQueueTransitionErrorCode code) : Exception(message)
    {
        public ReconciliationBreakQueueTransitionErrorCode Code { get; } = code;
    }
}
