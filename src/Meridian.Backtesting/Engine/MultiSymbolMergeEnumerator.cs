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
/// Determinism contract: if two events have the same timestamp (at millisecond precision), the
/// event from the lower stream index (earlier position in <paramref name="streams"/>) is always
/// dequeued first.
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
        [EnumeratorCancellation] CancellationToken ct = default)
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
        // Heap priority is (timestampMs, streamIndex), so equal timestamps are deterministically
        // ordered by stream index.
        var enumerators = new IAsyncEnumerator<MarketEvent>?[streams.Count];
        var heap = new PriorityQueue<int, (long TimestampMs, int StreamIndex)>(
            streams.Count,
            Comparer<(long TimestampMs, int StreamIndex)>.Default);

        try
        {
            // Priming is part of resource ownership. A later stream can fail while it captures or
            // prepares a snapshot, so already-created enumerators must still be disposed.
            for (var i = 0; i < streams.Count; i++)
            {
                var enumerator = streams[i].GetAsyncEnumerator(ct);
                enumerators[i] = enumerator;
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    heap.Enqueue(
                        i,
                        (enumerator.Current.Timestamp.ToUnixTimeMilliseconds(), i));
                }
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
                        (enumerator.Current.Timestamp.ToUnixTimeMilliseconds(), idx));
                }
            }
        }
        finally
        {
            foreach (var e in enumerators)
            {
                if (e is not null)
                    await e.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
