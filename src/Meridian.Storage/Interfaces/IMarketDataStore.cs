using Meridian.Contracts.Store;
using Meridian.Domain.Events;

namespace Meridian.Storage.Interfaces;

/// <summary>
/// Unified read abstraction over all stored market data.
/// Replaces the fragmented query paths across <c>HistoricalDataQueryService</c>,
/// <c>MemoryMappedJsonlReader</c>, <c>JsonlReplayer</c>, <c>StorageSearchService</c>,
/// and <c>StorageCatalogService</c> with a single testable, optimised entry point.
/// </summary>
public interface IMarketDataStore
{
    /// <summary>
    /// Streams <see cref="MarketEvent"/> records that match <paramref name="query"/>,
    /// ordered by <see cref="MarketEvent.Timestamp"/> ascending.
    /// </summary>
    IAsyncEnumerable<MarketEvent> QueryAsync(
        MarketDataQuery query,
        CancellationToken ct = default);
}
