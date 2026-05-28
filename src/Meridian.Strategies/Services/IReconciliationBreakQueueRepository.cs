using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationBreakQueueRepository
{
    Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAllAsync(ReconciliationBreakQueueStatus? status = null, CancellationToken ct = default);

    Task<ReconciliationBreakQueueItem?> GetByIdAsync(string breakId, CancellationToken ct = default);

    Task<bool> CreateIfMissingAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default);

    Task SaveAsync(ReconciliationBreakQueueItem item, CancellationToken ct = default);

    Task<bool> DeleteAsync(string breakId, CancellationToken ct = default);

    Task<ReconciliationBreakQueueTransitionResult> StartReviewAsync(ReviewReconciliationBreakRequest request, CancellationToken ct = default);

    Task<ReconciliationBreakQueueTransitionResult> ResolveAsync(ResolveReconciliationBreakRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationBreakQueueAuditEvent>> GetAuditHistoryAsync(string breakId, CancellationToken ct = default);

    Task<ReconciliationBreakQueueTransitionResult> AssignAsync(ReconciliationAssignRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Assignment is not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> ChangePriorityAsync(ReconciliationPriorityRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Priority changes are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> TransitionStatusAsync(ReconciliationStatusTransitionRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Status transitions are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> AddCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? actorDisplayName = null, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Comments are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> EditCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Comment edits are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> DeleteCommentAsync(ReconciliationCommentMutationRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Comment deletion is not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> SetRootCauseAsync(ReconciliationTaxonomyRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Root cause updates are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> SetResolutionAsync(ReconciliationTaxonomyRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Resolution updates are not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> SignOffAsync(ReconciliationSignOffRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Sign-off is not supported by this repository."));

    Task<ReconciliationBreakQueueTransitionResult> ReopenAsync(ReconciliationReopenRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(ReconciliationBreakQueueTransitionStatus.ValidationFailed, null, "Reopen is not supported by this repository."));

    Task<ReconciliationBulkActionResult> BulkActionAsync(ReconciliationBulkActionRequest request, string actor, string? correlationId = null, string? commandId = null, string? source = null, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBulkActionResult(Guid.NewGuid().ToString("N"), request.IdempotencyKey, request.DryRun, false, request.BreakIds.Count, 0, request.BreakIds.Count, request.BreakIds.Select(id => new ReconciliationBulkCaseResult(id, false, "Bulk actions are not supported by this repository.")).ToArray()));

    Task<ReconciliationBulkActionResult?> GetBulkActionResultAsync(string idempotencyKeyOrActionId, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBulkActionResult?>(null);
}

public enum ReconciliationBreakQueueTransitionStatus : byte
{
    Success = 0,
    NotFound = 1,
    InvalidTransition = 2,
    ValidationFailed = 3,
    Unauthorized = 4,
    Conflict = 5
}

public enum ReconciliationBreakQueueTransitionErrorCode : byte
{
    None = 0,
    MissingActor = 1,
    MissingReason = 2,
    MissingEvidence = 3,
    IllegalTransition = 4,
    DualReviewRequired = 5,
    ReopenNotAllowed = 6,
    MissingAssignee = 7,
    MissingRootCause = 8,
    MissingResolution = 9,
    ConcurrencyConflict = 10,
    InvalidTaxonomy = 11
}

public sealed record ReconciliationBreakQueueTransitionResult(
    ReconciliationBreakQueueTransitionStatus Status,
    ReconciliationBreakQueueItem? Item,
    string? Error = null,
    ReconciliationBreakQueueTransitionErrorCode ErrorCode = ReconciliationBreakQueueTransitionErrorCode.None);

public sealed record ReconciliationBreakQueueAuditEvent(
    string EventId,
    string BreakId,
    string EventType,
    ReconciliationBreakQueueStatus? PreviousStatus,
    ReconciliationBreakQueueStatus NewStatus,
    ReconciliationCaseLifecycleState? PreviousLifecycleState,
    ReconciliationCaseLifecycleState NewLifecycleState,
    DateTimeOffset OccurredAt,
    string? AssignedTo,
    string? ReviewedBy,
    string? ResolvedBy,
    string? Note,
    string? ExceptionRoute = null,
    decimal? ToleranceBand = null,
    string? RequiredSignoffRole = null,
    string? SignoffStatus = null,
    string? ExternalAccountId = null,
    string? CustodianId = null,
    string? UpstreamSyncCursor = null,
    string? Actor = null,
    string? BeforePayload = null,
    string? AfterPayload = null,
    string? CorrelationId = null,
    string? CommandId = null,
    string? Source = null,
    string? Reason = null,
    int SchemaVersion = 1);
