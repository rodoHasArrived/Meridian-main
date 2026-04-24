using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Storage;

/// <summary>
/// In-memory run store used for development and testing.
/// A production implementation would persist entries to JSONL using
/// <c>AtomicFileWriter</c> following the same pattern as <c>JsonlStorageSink</c>.
/// </summary>
public sealed class StrategyRunStore : IStrategyRepository
{
    private readonly Dictionary<string, StrategyRunEntry> _runsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StrategyRunEntry>> _runsByStrategy = new(StringComparer.Ordinal);
    private readonly List<StrategyRunEntry> _runsByLastUpdated = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_runsById.TryGetValue(entry.RunId, out var existing))
            {
                RemoveIndexedEntry(existing);
            }

            _runsById[entry.RunId] = entry;
            InsertIndexedEntry(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
#pragma warning disable CS1998 // Intentionally synchronous: in-memory store has no I/O operations to await
    public async IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
        string strategyId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<StrategyRunEntry> snapshot;
        lock (_lock)
        {
            snapshot = _runsByStrategy.TryGetValue(strategyId, out var runs)
                ? [.. runs]
                : [];
        }

        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
        }
    }
#pragma warning restore CS1998

    /// <inheritdoc/>
    public Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default)
    {
        StrategyRunEntry? latest;
        lock (_lock)
        {
            latest = _runsByStrategy.TryGetValue(strategyId, out var runs) && runs.Count > 0
                ? runs[^1]
                : null;
        }

        return Task.FromResult(latest);
    }

    /// <inheritdoc/>
    public Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ct.ThrowIfCancellationRequested();

        StrategyRunEntry? run;
        lock (_lock)
        {
            _runsById.TryGetValue(runId, out run);
        }

        return Task.FromResult(run);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<StrategyRunEntry>> GetRunsByIdsAsync(IReadOnlyCollection<string> runIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        ct.ThrowIfCancellationRequested();

        var results = new List<StrategyRunEntry>(runIds.Count);
        lock (_lock)
        {
            foreach (var runId in runIds)
            {
                if (string.IsNullOrWhiteSpace(runId))
                {
                    continue;
                }

                if (_runsById.TryGetValue(runId, out var run))
                {
                    results.Add(run);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<StrategyRunEntry>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<StrategyRunEntry>> QueryRunsAsync(StrategyRunRepositoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var runTypeFilter = query.RunTypes is { Count: > 0 }
            ? new HashSet<RunType>(query.RunTypes)
            : null;
        var limit = query.Limit <= 0 ? int.MaxValue : query.Limit;
        var results = new List<StrategyRunEntry>();

        lock (_lock)
        {
            foreach (var run in _runsByLastUpdated)
            {
                if (!string.IsNullOrWhiteSpace(query.StrategyId) &&
                    !string.Equals(run.StrategyId, query.StrategyId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (runTypeFilter is not null && !runTypeFilter.Contains(run.RunType))
                {
                    continue;
                }

                if (query.Status.HasValue &&
                    StrategyRunRepositoryOrdering.MapStatus(run) != query.Status.Value)
                {
                    continue;
                }

                results.Add(run);
                if (results.Count >= limit)
                {
                    break;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<StrategyRunEntry>>(results);
    }

    /// <inheritdoc/>
#pragma warning disable CS1998 // Intentionally synchronous: in-memory store has no I/O operations to await
    public async IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        List<StrategyRunEntry> snapshot;
        lock (_lock)
        {
            snapshot = _runsById.Values
                .OrderByDescending(static run => run.StartedAt)
                .ThenBy(static run => run.RunId, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var entry in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
        }
    }
#pragma warning restore CS1998

    private void InsertIndexedEntry(StrategyRunEntry entry)
    {
        if (!_runsByStrategy.TryGetValue(entry.StrategyId, out var strategyRuns))
        {
            strategyRuns = [];
            _runsByStrategy[entry.StrategyId] = strategyRuns;
        }

        InsertSorted(strategyRuns, entry, StrategyRunRepositoryOrdering.StartedAtAscending);
        InsertSorted(_runsByLastUpdated, entry, StrategyRunRepositoryOrdering.LastUpdatedDescending);
    }

    private void RemoveIndexedEntry(StrategyRunEntry entry)
    {
        if (_runsByStrategy.TryGetValue(entry.StrategyId, out var strategyRuns))
        {
            RemoveByRunId(strategyRuns, entry.RunId);
            if (strategyRuns.Count == 0)
            {
                _runsByStrategy.Remove(entry.StrategyId);
            }
        }

        RemoveByRunId(_runsByLastUpdated, entry.RunId);
    }

    private static void InsertSorted(List<StrategyRunEntry> target, StrategyRunEntry entry, IComparer<StrategyRunEntry> comparer)
    {
        var index = target.BinarySearch(entry, comparer);
        if (index < 0)
        {
            index = ~index;
        }

        target.Insert(index, entry);
    }

    private static void RemoveByRunId(List<StrategyRunEntry> target, string runId)
    {
        var index = target.FindIndex(run => string.Equals(run.RunId, runId, StringComparison.Ordinal));
        if (index >= 0)
        {
            target.RemoveAt(index);
        }
    }
}
