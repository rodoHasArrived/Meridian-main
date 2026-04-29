using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Persists and retrieves the run history for all registered strategies.
/// Implemented by <see cref="Storage.StrategyRunStore"/>.
/// </summary>
public interface IStrategyRepository
{
    /// <summary>Records a new or updated run entry.</summary>
    Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default);

    /// <summary>Retrieves all runs for <paramref name="strategyId"/> in ascending start order.</summary>
    IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Returns the most recent run entry for <paramref name="strategyId"/>, or <c>null</c>.</summary>
    Task<StrategyRunEntry?> GetLatestRunAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Returns all recorded strategy runs across all strategies.</summary>
    async IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>Returns the run with the given <paramref name="runId"/>, or <c>null</c>.</summary>
    async Task<StrategyRunEntry?> GetRunByIdAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                return run;
            }
        }

        return null;
    }

    /// <summary>Returns the runs matching the supplied run IDs.</summary>
    async Task<IReadOnlyList<StrategyRunEntry>> GetRunsByIdsAsync(IReadOnlyCollection<string> runIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        if (runIds.Count == 0)
        {
            return [];
        }

        var remaining = new HashSet<string>(
            runIds.Where(static runId => !string.IsNullOrWhiteSpace(runId)),
            StringComparer.Ordinal);
        if (remaining.Count == 0)
        {
            return [];
        }

        var results = new List<StrategyRunEntry>(remaining.Count);
        await foreach (var run in GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!remaining.Remove(run.RunId))
            {
                continue;
            }

            results.Add(run);
            if (remaining.Count == 0)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>Queries runs using workstation-oriented filters and ordering.</summary>
    async Task<IReadOnlyList<StrategyRunEntry>> QueryRunsAsync(StrategyRunRepositoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var runTypeFilter = query.RunTypes is { Count: > 0 }
            ? new HashSet<RunType>(query.RunTypes)
            : null;
        var results = new List<StrategyRunEntry>();

        await foreach (var run in GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
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
        }

        results.Sort(StrategyRunRepositoryOrdering.LastUpdatedDescending);

        var limit = query.Limit <= 0 ? int.MaxValue : query.Limit;
        if (results.Count > limit)
        {
            results.RemoveRange(limit, results.Count - limit);
        }

        return results;
    }
}

internal static class StrategyRunRepositoryOrdering
{
    internal static readonly IComparer<StrategyRunEntry> StartedAtAscending = Comparer<StrategyRunEntry>.Create(CompareStartedAtAscending);
    internal static readonly IComparer<StrategyRunEntry> LastUpdatedDescending = Comparer<StrategyRunEntry>.Create(CompareLastUpdatedDescending);

    internal static DateTimeOffset GetLastUpdatedAt(StrategyRunEntry run) => run.EndedAt ?? run.StartedAt;

    internal static StrategyRunStatus MapStatus(StrategyRunEntry run)
    {
        if (run.TerminalStatus.HasValue)
        {
            return run.TerminalStatus.Value;
        }

        return run.EndedAt.HasValue
            ? StrategyRunStatus.Completed
            : StrategyRunStatus.Running;
    }

    private static int CompareStartedAtAscending(StrategyRunEntry? left, StrategyRunEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = left.StartedAt.CompareTo(right.StartedAt);
        if (comparison != 0)
        {
            return comparison;
        }

        return StringComparer.Ordinal.Compare(left.RunId, right.RunId);
    }

    private static int CompareLastUpdatedDescending(StrategyRunEntry? left, StrategyRunEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var comparison = GetLastUpdatedAt(right).CompareTo(GetLastUpdatedAt(left));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.StartedAt.CompareTo(left.StartedAt);
        if (comparison != 0)
        {
            return comparison;
        }

        return StringComparer.Ordinal.Compare(left.RunId, right.RunId);
    }
}
