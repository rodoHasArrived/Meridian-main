using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationRunService
{
    Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default);
}
