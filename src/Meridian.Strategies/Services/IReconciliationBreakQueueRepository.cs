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
}

public enum ReconciliationBreakQueueTransitionStatus : byte
{
    Success = 0,
    NotFound = 1,
    InvalidTransition = 2
}

public sealed record ReconciliationBreakQueueTransitionResult(
    ReconciliationBreakQueueTransitionStatus Status,
    ReconciliationBreakQueueItem? Item,
    string? Error = null);

public sealed record ReconciliationBreakQueueAuditEvent(
    string EventId,
    string BreakId,
    string EventType,
    ReconciliationBreakQueueStatus? PreviousStatus,
    ReconciliationBreakQueueStatus NewStatus,
    DateTimeOffset OccurredAt,
    string? AssignedTo,
    string? ReviewedBy,
    string? ResolvedBy,
    string? Note);
