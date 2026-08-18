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
/// Determinism contract: if two events have the same UTC timestamp (at full tick precision), the
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
            await using var enumerator = streams[0].GetAsyncEnumerator(ct);
            long? singlePreviousUtcTicks = null;
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                var evt = ValidateCurrent(enumerator.Current, streamIndex: 0, singlePreviousUtcTicks);
                singlePreviousUtcTicks = evt.Timestamp.UtcTicks;
                yield return evt;
            }

            yield break;
        }

        // Initialise enumerators and prime the heap.
        // Heap priority is (UTC ticks, streamIndex), so equal timestamps are deterministically
        // ordered by stream index.
        var enumerators = new IAsyncEnumerator<MarketEvent>?[streams.Count];
        var previousUtcTicksByStream = new long?[streams.Count];
        var heap = new PriorityQueue<int, (long UtcTicks, int StreamIndex)>(
            streams.Count,
            Comparer<(long UtcTicks, int StreamIndex)>.Default);

        try
        {
            for (var i = 0; i < streams.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var enumerator = streams[i].GetAsyncEnumerator(ct);
                enumerators[i] = enumerator;
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    var evt = ValidateCurrent(enumerator.Current, i, previousUtcTicksByStream[i]);
                    previousUtcTicksByStream[i] = evt.Timestamp.UtcTicks;
                    heap.Enqueue(i, (evt.Timestamp.UtcTicks, i));
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
                    ct.ThrowIfCancellationRequested();
                    var evt = ValidateCurrent(enumerator.Current, idx, previousUtcTicksByStream[idx]);
                    previousUtcTicksByStream[idx] = evt.Timestamp.UtcTicks;
                    heap.Enqueue(idx, (evt.Timestamp.UtcTicks, idx));
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

    private static MarketEvent ValidateCurrent(
        MarketEvent? evt,
        int streamIndex,
        long? previousUtcTicks)
    {
        if (evt is null)
            throw new InvalidDataException($"Replay stream {streamIndex} yielded a null market event.");

        var utcTicks = evt.Timestamp.UtcTicks;
        if (previousUtcTicks.HasValue && utcTicks < previousUtcTicks.Value)
        {
            var previousTimestamp = new DateTimeOffset(previousUtcTicks.Value, TimeSpan.Zero);
            throw new InvalidDataException(
                $"Replay stream {streamIndex} is not chronological: event timestamp " +
                $"{evt.Timestamp:O} precedes {previousTimestamp:O}.");
        }

        return evt;
    }
}
