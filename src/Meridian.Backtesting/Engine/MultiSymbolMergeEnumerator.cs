using Meridian.Domain.Events;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Merges multiple per-symbol <see cref="IAsyncEnumerable{MarketEvent}"/> streams into a single
/// chronologically-ordered stream using a min-heap (priority queue) keyed on event timestamp.
/// O(log n) per event where n is the number of symbol streams.
/// </summary>
internal static class MultiSymbolMergeEnumerator
{
    /// <summary>Merge all streams into a single chronological sequence.</summary>
    public static async IAsyncEnumerable<MarketEvent> MergeAsync(
        IReadOnlyList<IAsyncEnumerable<MarketEvent>> streams,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Initialise enumerators and prime the heap
        var enumerators = new IAsyncEnumerator<MarketEvent>[streams.Count];
        var heap = new PriorityQueue<int, long>(streams.Count);

        for (var i = 0; i < streams.Count; i++)
        {
            enumerators[i] = streams[i].GetAsyncEnumerator(ct);
            if (await enumerators[i].MoveNextAsync())
                heap.Enqueue(i, enumerators[i].Current.Timestamp.ToUnixTimeMilliseconds());
        }

        try
        {
            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var idx = heap.Dequeue();
                yield return enumerators[idx].Current;

                if (await enumerators[idx].MoveNextAsync())
                    heap.Enqueue(idx, enumerators[idx].Current.Timestamp.ToUnixTimeMilliseconds());
            }
        }
        finally
        {
            foreach (var e in enumerators)
                await e.DisposeAsync();
        }
    }
}
