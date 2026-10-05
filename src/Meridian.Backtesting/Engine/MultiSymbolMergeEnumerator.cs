using Meridian.Domain.Events;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Merges multiple per-symbol <see cref="IAsyncEnumerable{MarketEvent}"/> streams into a single
/// chronologically-ordered stream using a min-heap (priority queue) keyed on timestamp plus
/// a deterministic secondary stream key.
/// O(log n) per event where n is the number of symbol streams.
/// </summary>
/// <remarks>
/// <para>
/// Determinism contract: compare full UTC ticks, then the optional symbol rank, then the stream
/// index. Without symbol ranks, equal instants retain the caller's stream order.
/// </para>
/// <para>
/// Callers that require repeatable equal-timestamp ordering must pass streams in a stable order
/// (for example, symbol-sorted order) and keep that ordering consistent between runs.
/// </para>
/// </remarks>
internal static class MultiSymbolMergeEnumerator
{
    /// <summary>Merge all streams into a single chronological sequence.</summary>
    public static async IAsyncEnumerable<MarketEvent> MergeAsync(
        IReadOnlyList<IAsyncEnumerable<MarketEvent>> streams,
        [EnumeratorCancellation] CancellationToken ct = default,
        IReadOnlyDictionary<string, int>? symbolOrder = null)
    {
        if (streams.Count == 0)
            yield break;

        if (streams.Count == 1)
        {
            await foreach (var evt in streams[0].WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                yield return evt;
            }

            yield break;
        }

        // Initialise enumerators and prime the heap.
        // Shared-root streams can contain several symbols. Their symbol ranks preserve the same
        // equal-instant order as distinct per-symbol streams, including mixed storage layouts.
        var enumerators = new IAsyncEnumerator<MarketEvent>?[streams.Count];
        var heap = new PriorityQueue<int, (long TimestampTicks, int SymbolRank, int StreamIndex)>(
            streams.Count,
            Comparer<(long TimestampTicks, int SymbolRank, int StreamIndex)>.Default);
        Exception? initializationError = null;

        (long TimestampTicks, int SymbolRank, int StreamIndex) Priority(MarketEvent evt, int streamIndex) =>
            (evt.Timestamp.UtcTicks,
                symbolOrder is not null && symbolOrder.TryGetValue(evt.EffectiveSymbol, out var rank) ? rank : streamIndex,
                streamIndex);

        try
        {
            try
            {
                for (var i = 0; i < streams.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var enumerator = streams[i].GetAsyncEnumerator(ct);
                    enumerators[i] = enumerator;
                    if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        heap.Enqueue(
                            i,
                            Priority(enumerator.Current, i));
                    }
                }
            }
            catch (Exception ex)
            {
                initializationError = ex;
                throw;
            }

            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var idx = heap.Dequeue();
                var enumerator = enumerators[idx]!;
                yield return enumerator.Current;

                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    heap.Enqueue(
                        idx,
                        Priority(enumerator.Current, idx));
                }
            }
        }
        finally
        {
            List<Exception>? disposalErrors = null;
            foreach (var e in enumerators)
            {
                if (e is null)
                    continue;
                try
                {
                    await e.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (disposalErrors ??= []).Add(ex);
                }
            }
            if (disposalErrors is not null)
            {
                if (initializationError is not null)
                    disposalErrors.Insert(0, initializationError);
                throw new AggregateException("Failed to dispose merged streams.", disposalErrors);
            }
        }
    }
}
