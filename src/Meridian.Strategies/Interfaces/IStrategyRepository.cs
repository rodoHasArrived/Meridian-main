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
}
