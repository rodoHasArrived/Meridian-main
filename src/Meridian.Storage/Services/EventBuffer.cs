using Meridian.Domain.Events;

namespace Meridian.Storage.Services;

/// <summary>
/// Thread-safe generic event buffer using a swap-buffer drain strategy to eliminate
/// per-drain allocations. Maintains two <see cref="List{T}"/> instances: <c>_active</c>
/// (receives new events) and <c>_standby</c> (returned to callers from
/// <see cref="DrainAll"/> so they can process events without holding the lock).
///
/// <para>
/// Eliminates the duplicate lock-based buffer implementations that existed in
/// JsonlStorageSink and ParquetStorageSink.
/// </para>
/// </summary>
/// <typeparam name="T">Type of events to buffer.</typeparam>
public class EventBuffer<T> : IDisposable where T : class
{
    /// <summary>Lock protecting <see cref="_active"/> and <see cref="_standby"/>.</summary>
    protected readonly object _lock = new();

    /// <summary>
    /// Active list that receives new events. Swapped with <see cref="_standby"/> on drain.
    /// Accessible to subclasses for single-lock operations such as per-symbol partitioning.
    /// </summary>
    protected List<T> _active;

    /// <summary>
    /// Approximate count of buffered events. Updated inside <see cref="_lock"/> but readable
    /// without the lock (volatile) so that <see cref="ShouldFlush"/> and <see cref="IsEmpty"/>
    /// avoid lock acquisition on every incoming event.
    /// </summary>
    protected volatile int _count;

    private List<T> _standby;
    private readonly int _maxCapacity;
    private bool _disposed;

    /// <summary>
    /// Creates a new event buffer.
    /// </summary>
    /// <param name="initialCapacity">Initial backing-list capacity hint (default: 1000).</param>
    /// <param name="maxCapacity">
    /// Maximum number of events to retain. When the limit is reached the oldest event is
    /// silently dropped to make room for the new one (drop-oldest policy). Defaults to
    /// <see cref="int.MaxValue"/> (unbounded).
    /// </param>
    public EventBuffer(int initialCapacity = 1000, int maxCapacity = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity, nameof(maxCapacity));
        var cap = Math.Min(initialCapacity, maxCapacity);
        _active = new List<T>(cap);
        _standby = new List<T>(cap);
        _maxCapacity = maxCapacity;
    }

    /// <summary>
    /// Gets the approximate current count of buffered events.
    /// This is a lock-free volatile read; callers should tolerate minor staleness.
    /// </summary>
    public int Count => _count;

    /// <summary>Gets whether the buffer is empty (lock-free).</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Returns <see langword="true"/> when the buffered event count meets or exceeds
    /// <paramref name="threshold"/>. Lock-free — safe to call on every incoming event.
    /// </summary>
    public bool ShouldFlush(int threshold) => _count >= threshold;

    /// <summary>
    /// Add a single event to the buffer. When the buffer is at <c>maxCapacity</c> the
    /// oldest buffered event is dropped to make room (drop-oldest policy).
    /// </summary>
    public void Add(T evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(evt, nameof(evt));

        lock (_lock)
        {
            if (_active.Count >= _maxCapacity)
                _active.RemoveAt(0); // drop oldest; net count change is zero
            else
                _count++;
            _active.Add(evt);
        }
    }

    /// <summary>
    /// Add multiple events to the buffer, applying the drop-oldest policy as needed.
    /// </summary>
    public void AddRange(IEnumerable<T> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(events, nameof(events));

        lock (_lock)
        {
            foreach (var evt in events)
            {
                ArgumentNullException.ThrowIfNull(evt, nameof(evt));
                if (_active.Count >= _maxCapacity)
                    _active.RemoveAt(0);
                else
                    _count++;
                _active.Add(evt);
            }
        }
    }

    /// <summary>
    /// Atomically drains all buffered events via a buffer swap — no copy allocation.
    ///
    /// <para>
    /// The returned <see cref="IReadOnlyList{T}"/> is the buffer's internal backing store
    /// and will be cleared and reused on the <em>next</em> call to <see cref="DrainAll"/>.
    /// Callers must complete all processing of the returned list before the next drain
    /// cycle begins on the same buffer instance.
    /// </para>
    /// </summary>
    public IReadOnlyList<T> DrainAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (_active.Count == 0)
                return Array.Empty<T>();

            // Swap: hand the active list to the caller; adopt the cleared standby as active.
            var drained = _active;
            _active = _standby;
            _active.Clear();  // prepare ex-standby for incoming events
            _standby = drained;
            _count = 0;
            return drained;
        }
    }

    /// <summary>
    /// Restores a drained batch to the front of the active buffer so a failed flush can be retried
    /// without reordering older events behind newer arrivals.
    /// </summary>
    internal void RestoreToFront(IReadOnlyList<T> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
            return;

        lock (_lock)
        {
            _active.InsertRange(0, events);
            _count = _active.Count;
        }
    }

    /// <summary>
    /// Drain up to <paramref name="maxCount"/> events from the front of the buffer.
    /// </summary>
    /// <remarks>
    /// This operation is O(n) in the number of <em>remaining</em> elements because
    /// <see cref="List{T}.RemoveRange"/> must shift the tail. For high-frequency partial
    /// drains consider using <see cref="DrainAll"/> with an external cap instead.
    /// </remarks>
    public IReadOnlyList<T> Drain(int maxCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount, nameof(maxCount));

        lock (_lock)
        {
            if (_active.Count == 0)
                return Array.Empty<T>();

            var count = Math.Min(maxCount, _active.Count);
            var result = _active.GetRange(0, count); // allocates only the result list
            _active.RemoveRange(0, count);
            _count = _active.Count;
            return result;
        }
    }

    /// <summary>
    /// Returns a snapshot of all buffered events without removing them.
    /// Allocates a new list on every call; intended for diagnostics/monitoring only.
    /// </summary>
    public IReadOnlyList<T> PeekAll()
    {
        lock (_lock)
        {
            return _active.ToList();
        }
    }

    /// <summary>
    /// Clear all events from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _active.Clear();
            _count = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_lock)
        {
            _active.Clear();
            _count = 0;
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Specialized event buffer for <see cref="MarketEvent"/> types.
/// </summary>
public sealed class MarketEventBuffer : EventBuffer<MarketEvent>
{
    public MarketEventBuffer(int initialCapacity = 1000) : base(initialCapacity)
    {
    }

    /// <summary>
    /// Drain events for a specific symbol in a single lock acquisition.
    /// Non-matching events remain in the buffer and preserve their original ordering.
    /// </summary>
    /// <remarks>
    /// The previous implementation called <see cref="EventBuffer{T}.DrainAll"/> followed by
    /// <see cref="EventBuffer{T}.AddRange"/> in two separate lock acquisitions, creating a
    /// race window where concurrently produced events could be silently reordered. This
    /// implementation partitions events in a single pass under one lock.
    /// </remarks>
    public IReadOnlyList<MarketEvent> DrainBySymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrEmpty(symbol, nameof(symbol));

        lock (_lock)
        {
            if (_active.Count == 0)
                return Array.Empty<MarketEvent>();

            var matching = new List<MarketEvent>();
            var remaining = new List<MarketEvent>();

            foreach (var evt in _active)
            {
                if (evt.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    matching.Add(evt);
                else
                    remaining.Add(evt);
            }

            if (matching.Count == 0)
                return Array.Empty<MarketEvent>();

            _active.Clear();
            _active.AddRange(remaining);
            _count = _active.Count;

            return matching;
        }
    }
}
