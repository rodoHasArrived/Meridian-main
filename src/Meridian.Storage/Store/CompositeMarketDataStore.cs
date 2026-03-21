using System.Runtime.CompilerServices;
using Meridian.Contracts.Store;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Store;

/// <summary>
/// <see cref="IMarketDataStore"/> that aggregates results from multiple underlying stores
/// and merges them in ascending <see cref="MarketEvent.Timestamp"/> order.
/// </summary>
/// <remarks>
/// <para>
/// Use this as the top-level store when data lives across different tiers or formats
/// (e.g. JSONL hot tier + Parquet cold tier).  Each delegate store is queried
/// independently; their results are merged via an N-way sorted merge so callers always
/// receive a coherent time-ordered stream.
/// </para>
/// <para>
/// The <see cref="MarketDataQuery.Limit"/> is applied <em>after</em> merging, so all
/// delegates are queried until the combined limit is reached.
/// </para>
/// </remarks>
public sealed class CompositeMarketDataStore : IMarketDataStore
{
    private readonly IReadOnlyList<IMarketDataStore> _stores;

    /// <summary>
    /// Initialises a composite store that fans out queries to <paramref name="stores"/>
    /// and merges results by timestamp.
    /// </summary>
    /// <param name="stores">One or more backing stores to query.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stores"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stores"/> is empty.</exception>
    public CompositeMarketDataStore(IEnumerable<IMarketDataStore> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        _stores = stores.ToList();
        if (_stores.Count == 0)
            throw new ArgumentException("At least one backing store is required.", nameof(stores));
    }

    /// <summary>Convenience constructor for two stores (e.g. JSONL + Parquet).</summary>
    public CompositeMarketDataStore(IMarketDataStore first, IMarketDataStore second)
        : this(new[] { first, second }) { }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MarketEvent> QueryAsync(
        MarketDataQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Query without the limit so each store returns its full result set;
        // we apply the limit ourselves after merging.
        var unlimitedQuery = query with { Limit = null };

        // Open one enumerator per backing store.
        var enumerators = _stores
            .Select(s => s.QueryAsync(unlimitedQuery, ct).GetAsyncEnumerator(ct))
            .ToArray();

        // Priority queue: (timestamp, storeIndex) — min-heap on timestamp.
        var heap = new PriorityQueue<(MarketEvent Event, int StoreIndex), DateTimeOffset>(
            _stores.Count);

        try
        {
            // Seed the heap with the first event from each store.
            for (int i = 0; i < enumerators.Length; i++)
            {
                if (await enumerators[i].MoveNextAsync())
                    heap.Enqueue((enumerators[i].Current, i), enumerators[i].Current.Timestamp);
            }

            int yielded = 0;

            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var (evt, storeIdx) = heap.Dequeue();

                yield return evt;
                yielded++;

                if (query.Limit.HasValue && yielded >= query.Limit.Value)
                    yield break;

                // Advance the store that produced this event.
                if (await enumerators[storeIdx].MoveNextAsync())
                {
                    heap.Enqueue(
                        (enumerators[storeIdx].Current, storeIdx),
                        enumerators[storeIdx].Current.Timestamp);
                }
            }
        }
        finally
        {
            // Dispose all enumerators regardless of how the iteration ends.
            foreach (var e in enumerators)
                await e.DisposeAsync();
        }
    }
}
