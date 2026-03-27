using Meridian.Domain.Events;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Merges multiple per-symbol <see cref="IAsyncEnumerable{MarketEvent}"/> streams into a single
/// chronologically-ordered stream using a min-heap (priority queue) keyed on event timestamp.
/// O(log n) per event where n is the number of symbol streams.
/// </summary>
/// <remarks>
/// Tie-breaking: when two streams have events at the same millisecond timestamp, the stream with
/// the lower index (i.e. earlier position in the <paramref name="streams"/> list) is always
/// dequeued first. This gives deterministic replay order provided the caller passes streams in a
/// consistent order (e.g. sorted by symbol name).
/// </remarks>
internal static class MultiSymbolMergeEnumerator
{
    /// <summary>Merge all streams into a single chronological sequence.</summary>
    public static async IAsyncEnumerable<MarketEvent> MergeAsync(
        IReadOnlyList<IAsyncEnumerable<MarketEvent>> streams,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Initialise enumerators and prime the heap.
        // The heap key is a composite long: (timestampMs << 20) | streamIndex, so that events
        // at the same millisecond are always dequeued in stream-index order (deterministic).
        var enumerators = new IAsyncEnumerator<MarketEvent>[streams.Count];
        var heap = new PriorityQueue<int, long>(streams.Count);

        for (var i = 0; i < streams.Count; i++)
        {
            enumerators[i] = streams[i].GetAsyncEnumerator(ct);
            if (await enumerators[i].MoveNextAsync().ConfigureAwait(false))
                heap.Enqueue(i, MakeHeapKey(enumerators[i].Current.Timestamp.ToUnixTimeMilliseconds(), i));
        }

        try
        {
            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var idx = heap.Dequeue();
                yield return enumerators[idx].Current;

                if (await enumerators[idx].MoveNextAsync().ConfigureAwait(false))
                    heap.Enqueue(idx, MakeHeapKey(enumerators[idx].Current.Timestamp.ToUnixTimeMilliseconds(), idx));
            }
        }
        finally
        {
            foreach (var e in enumerators)
                await e.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Packs a millisecond timestamp and stream index into a single <see cref="long"/> heap key.
    /// Lower stream index wins on equal timestamps, giving deterministic tie-breaking.
    /// Supports up to 2^20 (≈1 million) concurrent streams.
    /// </summary>
    private static long MakeHeapKey(long timestampMs, int streamIndex)
        => (timestampMs << 20) | (uint)(streamIndex & 0xFFFFF);
}
