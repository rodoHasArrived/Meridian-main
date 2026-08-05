using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Persists and retrieves the run history for all registered strategies.
/// Implemented by <see cref="Storage.StrategyRunStore"/>.
/// </summary>
public interface IStrategyRepository
{
    /// <summary>Records a new or updated run entry.</summary>
    Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Records an explicit lifecycle transition while preserving compatibility with repositories
    /// that only retain the latest run snapshot.
    /// </summary>
    Task RecordLifecycleEventAsync(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType,
        CancellationToken ct = default) =>
        RecordRunAsync(entry with { LastLifecycleEvent = eventType }, ct);

    /// <summary>
    /// Records a lifecycle transition and returns its validated terminal receipt when the event
    /// represents a completed command rather than an intent-only transition.
    /// </summary>
    async Task<VerifiedOperationOutcome?> RecordLifecycleEventWithOutcomeAsync(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType,
        CancellationToken ct = default)
    {
        await RecordLifecycleEventAsync(entry, eventType, ct).ConfigureAwait(false);
        return StrategyRunStore.CreateLifecycleOutcome(entry, eventType);
    }

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

    /// <summary>
    /// Queries runs using repository-owned workstation visibility filtering.
    /// </summary>
    /// <remarks>
    /// The default implementation preserves compatibility for existing repository adapters. The
    /// production <see cref="StrategyRunStore"/> overrides this method with visibility-specific
    /// indexes so foreign history is neither materialized nor allowed to consume the query limit.
    /// A <see langword="null"/> <paramref name="scope"/> represents an unscoped compatibility
    /// read; a non-null scope retains legacy unscoped runs in addition to exact tenant/company
    /// matches.
    /// </remarks>
    async Task<IReadOnlyList<StrategyRunEntry>> QueryVisibleRunsAsync(
        StrategyRunRepositoryQuery query,
        StrategyRunRepositoryScope? scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? int.MaxValue : query.Limit;
        var candidates = await QueryRunsAsync(
                query with { Limit = int.MaxValue },
                ct)
            .ConfigureAwait(false);

        return candidates
            .Where(run => StrategyRunRepositoryVisibility.IsVisible(run, scope))
            .Take(limit)
            .ToArray();
    }
}

/// <summary>
/// Trusted tenant and company boundary for repository-owned strategy-run visibility filtering.
/// </summary>
/// <remarks>
/// A non-null instance means the caller is operating in a scoped workstation context even when
/// one of the identifiers is missing. Missing identifiers fail closed for retained scoped runs
/// while legacy runs that declared neither identifier remain visible for compatibility.
/// </remarks>
public sealed record StrategyRunRepositoryScope(string? TenantId, string? CompanyId);

internal readonly record struct StrategyRunRepositoryScopeKey(string TenantId, string CompanyId);

internal static class StrategyRunRepositoryVisibility
{
    private const string WorkstationTenantIdParameter = "workstationTenantId";
    private const string WorkstationCompanyIdParameter = "workstationCompanyId";
    private const string CoveredCallStrategyId = "covered-call-overwrite";
    private const string CoveredCallScopedStrategyIdPrefix = "covered-call-overwrite:";

    internal static bool IsVisible(
        StrategyRunEntry run,
        StrategyRunRepositoryScope? scope)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (IsLegacyUnscopedVisible(run))
        {
            return true;
        }

        return scope is not null &&
            TryGetRetainedScope(run, out var retainedScope) &&
            TryCreateScopeKey(scope, out var requestedScope) &&
            retainedScope == requestedScope;
    }

    internal static bool IsLegacyUnscopedVisible(StrategyRunEntry run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var parameters = run.ParameterSet;
        var declaresTenant = parameters?.ContainsKey(WorkstationTenantIdParameter) == true;
        var declaresCompany = parameters?.ContainsKey(WorkstationCompanyIdParameter) == true;
        return !declaresTenant &&
            !declaresCompany &&
            !IsCoveredCall(run.StrategyId);
    }

    internal static bool TryGetRetainedScope(
        StrategyRunEntry run,
        out StrategyRunRepositoryScopeKey scope)
    {
        ArgumentNullException.ThrowIfNull(run);

        var parameters = run.ParameterSet;
        string? tenantId = null;
        string? companyId = null;
        var declaresTenant = parameters is not null &&
            parameters.TryGetValue(WorkstationTenantIdParameter, out tenantId);
        var declaresCompany = parameters is not null &&
            parameters.TryGetValue(WorkstationCompanyIdParameter, out companyId);

        if (declaresTenant &&
            declaresCompany &&
            !string.IsNullOrWhiteSpace(tenantId) &&
            !string.IsNullOrWhiteSpace(companyId))
        {
            scope = new StrategyRunRepositoryScopeKey(tenantId.Trim(), companyId.Trim());
            return true;
        }

        scope = default;
        return false;
    }

    internal static bool TryCreateScopeKey(
        StrategyRunRepositoryScope scope,
        out StrategyRunRepositoryScopeKey key)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (!string.IsNullOrWhiteSpace(scope.TenantId) &&
            !string.IsNullOrWhiteSpace(scope.CompanyId))
        {
            key = new StrategyRunRepositoryScopeKey(scope.TenantId.Trim(), scope.CompanyId.Trim());
            return true;
        }

        key = default;
        return false;
    }

    private static bool IsCoveredCall(string strategyId) =>
        string.Equals(strategyId, CoveredCallStrategyId, StringComparison.Ordinal) ||
        strategyId.StartsWith(CoveredCallScopedStrategyIdPrefix, StringComparison.Ordinal);
}

internal static class StrategyRunRepositoryOrdering
{
    internal static readonly IComparer<StrategyRunEntry> StartedAtAscending = Comparer<StrategyRunEntry>.Create(CompareStartedAtAscending);
    internal static readonly IComparer<StrategyRunEntry> LastUpdatedDescending = Comparer<StrategyRunEntry>.Create(CompareLastUpdatedDescending);

    internal static DateTimeOffset GetLastUpdatedAt(StrategyRunEntry run) =>
        run.LifecycleEventAtUtc ?? run.EndedAt ?? run.StartedAt;

    internal static StrategyRunStatus MapStatus(StrategyRunEntry run)
    {
        if (run.TerminalStatus.HasValue)
        {
            return run.TerminalStatus.Value;
        }

        return run.LastLifecycleEvent switch
        {
            StrategyRunLifecycleEventType.Paused => StrategyRunStatus.Paused,
            StrategyRunLifecycleEventType.Completed => StrategyRunStatus.Completed,
            StrategyRunLifecycleEventType.StartFailed or
            StrategyRunLifecycleEventType.Failed => StrategyRunStatus.Failed,
            StrategyRunLifecycleEventType.Cancelled => StrategyRunStatus.Cancelled,
            _ when run.EndedAt.HasValue => StrategyRunStatus.Completed,
            _ => StrategyRunStatus.Running
        };
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
