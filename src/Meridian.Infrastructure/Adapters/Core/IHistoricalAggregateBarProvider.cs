using Meridian.Domain.Models;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional historical-provider seam for intraday aggregate bars.
/// Providers that only support daily bars do not need to implement this contract.
/// </summary>
public interface IHistoricalAggregateBarProvider
{
    /// <summary>
    /// Granularities the provider can serve for aggregate-bar backfill.
    /// </summary>
    IReadOnlyList<DataGranularity> SupportedGranularities { get; }

    /// <summary>
    /// Fetch aggregate bars for the requested symbol and granularity.
    /// </summary>
    Task<IReadOnlyList<AggregateBar>> GetAggregateBarsAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);
}
