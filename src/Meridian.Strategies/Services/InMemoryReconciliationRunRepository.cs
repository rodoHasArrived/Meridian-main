using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed class InMemoryReconciliationRunRepository : IReconciliationRunRepository
{
    private readonly List<ReconciliationRunDetail> _runs = [];
    private readonly Lock _lock = new();

    public Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var index = _runs.FindIndex(run => run.Summary.ReconciliationRunId == detail.Summary.ReconciliationRunId);
            if (index >= 0)
            {
                _runs[index] = detail;
            }
            else
            {
                _runs.Add(detail);
            }
        }

        return Task.CompletedTask;
    }

    public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<ReconciliationRunDetail?>(
                _runs.FirstOrDefault(run => string.Equals(run.Summary.ReconciliationRunId, reconciliationRunId, StringComparison.Ordinal)));
        }
    }

    public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<ReconciliationRunDetail?>(
                _runs
                    .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                    .OrderByDescending(static run => run.Summary.CreatedAt)
                    .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationRunSummary>>(
                _runs
                    .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                    .OrderByDescending(static run => run.Summary.CreatedAt)
                    .Select(static run => run.Summary)
                    .ToArray());
        }
    }
}
