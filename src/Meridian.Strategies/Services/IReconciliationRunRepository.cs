using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationRunRepository
{
    Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default);

    /// <summary>
    /// Atomically upserts a run and recomputes first-observation continuity against the complete
    /// retained history for the strategy run. Implementations must serialize this operation with
    /// competing writers and return the exact persisted detail.
    /// </summary>
    Task<ReconciliationRunDetail> SaveWithFirstObservationContinuityAsync(
        ReconciliationRunDetail detail,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "This reconciliation repository does not implement atomic first-observation continuity. " +
            "Implement SaveWithFirstObservationContinuityAsync before using ReconciliationRunService writes.");

    /// <summary>
    /// Executes a callback while the latest retained snapshot for a strategy run is protected from
    /// competing repository writes. Implementations must hold the same mutation lease used by
    /// <see cref="SaveWithFirstObservationContinuityAsync"/> until the callback completes. The
    /// callback must not re-enter this repository; implementations should fail fast on re-entry.
    /// </summary>
    Task<TResult> ExecuteWithLatestForRunLeaseAsync<TResult>(
        string runId,
        Func<ReconciliationRunDetail?, CancellationToken, Task<TResult>> callback,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "This reconciliation repository does not implement leased latest-snapshot reads. " +
            "Implement ExecuteWithLatestForRunLeaseAsync before using reconciliation governance decisions.");

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default);
}
