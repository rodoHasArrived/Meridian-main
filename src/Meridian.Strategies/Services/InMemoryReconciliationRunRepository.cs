using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed class InMemoryReconciliationRunRepository : IReconciliationRunRepository
{
    private readonly List<ReconciliationRunDetail> _runs = [];
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly AsyncLocal<int> _leaseCallbackDepth = new();

    public async Task SaveAsync(ReconciliationRunDetail detail, CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        _ = await SaveWithFirstObservationContinuityAsync(detail, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDetail> SaveWithFirstObservationContinuityAsync(
        ReconciliationRunDetail detail,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentNullException.ThrowIfNull(detail);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalized = ReconciliationRunContinuity.UpsertAndNormalize(_runs, detail);
            _runs.Clear();
            _runs.AddRange(normalized);
            return _runs.Single(run =>
                string.Equals(
                    run.Summary.ReconciliationRunId,
                    detail.Summary.ReconciliationRunId,
                    StringComparison.Ordinal));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<TResult> ExecuteWithLatestForRunLeaseAsync<TResult>(
        string runId,
        Func<ReconciliationRunDetail?, CancellationToken, Task<TResult>> callback,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(callback);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var latest = FindLatest(runId);
            _leaseCallbackDepth.Value++;
            try
            {
                return await callback(latest, ct).ConfigureAwait(false);
            }
            finally
            {
                _leaseCallbackDepth.Value--;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ReconciliationRunDetail?> GetByIdAsync(
        string reconciliationRunId,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _runs.FirstOrDefault(run =>
                string.Equals(
                    run.Summary.ReconciliationRunId,
                    reconciliationRunId,
                    StringComparison.Ordinal));
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ReconciliationRunDetail?> GetLatestForRunAsync(
        string runId,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return FindLatest(runId);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(
        string runId,
        CancellationToken ct = default)
    {
        ThrowIfLeaseCallbackReentry();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _runs
                .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
                .OrderByDescending(static run => run.Summary.CreatedAt)
                .Select(static run => run.Summary)
                .ToArray();
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private ReconciliationRunDetail? FindLatest(string runId) =>
        _runs
            .Where(run => string.Equals(run.Summary.RunId, runId, StringComparison.Ordinal))
            .OrderByDescending(static run => run.Summary.CreatedAt)
            .FirstOrDefault();

    private void ThrowIfLeaseCallbackReentry()
    {
        if (_leaseCallbackDepth.Value > 0)
        {
            throw new InvalidOperationException(
                "A reconciliation repository lease callback cannot re-enter the repository.");
        }
    }
}
