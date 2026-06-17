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

    Task<ReconciliationBreakQueueItem?> RebuildSnapshotFromAuditAsync(string breakId, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBreakQueueItem?>(null);

    Task<ReconciliationBulkCaseworkResult?> GetBulkCaseworkResultAsync(string bulkActionIdOrIdempotencyKey, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBulkCaseworkResult?>(null);

    Task<ReconciliationBreakQueueTransitionResult> ApplyCaseworkCommandAsync(ReconciliationCaseworkCommand command, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBreakQueueTransitionResult(
            ReconciliationBreakQueueTransitionStatus.ValidationFailed,
            Item: null,
            Error: "Reconciliation casework commands are not supported by this repository."));

    Task<ReconciliationBulkCaseworkResult> ApplyBulkCaseworkAsync(ReconciliationBulkCaseworkRequest request, CancellationToken ct = default)
        => Task.FromResult(new ReconciliationBulkCaseworkResult(
            BulkActionId: request.CommandId,
            IdempotencyKey: request.IdempotencyKey,
            DryRun: request.DryRun,
            RequestedCount: request.BreakIds.Count,
            SucceededCount: 0,
            FailedCount: request.BreakIds.Count,
            Results: request.BreakIds.Select(breakId => new ReconciliationBulkCaseworkCaseResult(
                breakId,
                Succeeded: false,
                WouldSucceed: false,
                Error: "Reconciliation casework commands are not supported by this repository.",
                Item: null)).ToArray()));
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
    MissingRootCause = 7,
    MissingResolutionCode = 8,
    ResolverSignerConflict = 9,
    ConcurrencyConflict = 10,
    InvalidTaxonomy = 11,
    MaterialActionRequiresHumanOperator = 12
}

public sealed record ReconciliationCaseValidationProblem(
    string CurrentState,
    string RequestedState,
    IReadOnlyList<string> MissingFields,
    string Message);

public sealed record ReconciliationBreakQueueTransitionResult(
    ReconciliationBreakQueueTransitionStatus Status,
    ReconciliationBreakQueueItem? Item,
    string? Error = null,
    ReconciliationBreakQueueTransitionErrorCode ErrorCode = ReconciliationBreakQueueTransitionErrorCode.None,
    ReconciliationCaseValidationProblem? Validation = null);

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
    int SchemaVersion = 1,
    long Sequence = 0,
    string? CausationId = null,
    string? BeforePayloadHash = null,
    string? AfterPayloadHash = null);
