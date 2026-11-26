using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>Runs strategy reconciliation and retrieves retained results and run history.</summary>
public interface IReconciliationRunService
{
    Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default);

    /// <summary>Runs within trusted request scope. Legacy implementations without scoped reads fail closed.</summary>
    Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, StrategyRunReadScope scope, CancellationToken ct = default)
        => Task.FromResult<ReconciliationRunDetail?>(null);

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, StrategyRunReadScope scope, CancellationToken ct = default)
        => Task.FromResult<ReconciliationRunDetail?>(null);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default);

    Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, StrategyRunReadScope scope, CancellationToken ct = default)
        => Task.FromResult<ReconciliationRunDetail?>(null);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, StrategyRunReadScope scope, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>([]);
}
