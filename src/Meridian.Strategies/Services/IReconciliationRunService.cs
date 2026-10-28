using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>Runs strategy reconciliation and retrieves retained results and run history.</summary>
public interface IReconciliationRunService
{
    Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default);
}
