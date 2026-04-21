using System.Collections.Concurrent;
using Meridian.Execution.Models;

namespace Meridian.Execution.Services;

/// <summary>
/// Thread-safe registry that tracks all live <see cref="IMultiAccountPortfolioState"/> instances
/// by their strategy run ID.  Strategies call <see cref="Register"/> on start and
/// <see cref="Deregister"/> on stop so that cross-strategy aggregation and the REST API can
/// discover all currently active portfolios.
/// </summary>
public sealed class PortfolioRegistry
{
    private readonly ConcurrentDictionary<string, IMultiAccountPortfolioState> _portfolios =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers <paramref name="portfolio"/> under <paramref name="runId"/>.
    /// If an entry already exists for the same run ID it is silently replaced.
    /// </summary>
    public void Register(string runId, IMultiAccountPortfolioState portfolio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(portfolio);
        _portfolios[runId] = portfolio;
    }

    /// <summary>
    /// Removes the portfolio registered under <paramref name="runId"/>.
    /// No-op when no entry exists for that run ID.
    /// </summary>
    public void Deregister(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        _portfolios.TryRemove(runId, out _);
    }

    /// <summary>
    /// Returns a snapshot of all currently registered portfolios keyed by run ID.
    /// </summary>
    public IReadOnlyDictionary<string, IMultiAccountPortfolioState> GetAll()
        => new Dictionary<string, IMultiAccountPortfolioState>(_portfolios, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the portfolio registered under <paramref name="runId"/>, or
    /// <see langword="null"/> when no entry exists.
    /// </summary>
    public IMultiAccountPortfolioState? TryGet(string runId)
        => _portfolios.TryGetValue(runId, out var p) ? p : null;

    /// <summary>Count of currently registered portfolios.</summary>
    public int Count => _portfolios.Count;
}
