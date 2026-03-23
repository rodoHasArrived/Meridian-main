using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationRunRepository
{
    Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default);
}
