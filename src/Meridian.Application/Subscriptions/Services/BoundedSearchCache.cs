namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// A bounded, case-insensitive cache with absolute expiry and oldest-write eviction.
/// Expired entries are pruned on reads and writes; idle caches retain at most their capacity.
/// </summary>
internal sealed class BoundedSearchCache<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _order = new();
    private readonly TimeSpan _duration;
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;

    public BoundedSearchCache(TimeSpan duration, int capacity = 1024, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _duration = duration;
        _capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? value)
    {
        lock (_gate)
        {
            RemoveExpired(_timeProvider.GetTimestamp());
            value = _entries.TryGetValue(key, out var node) ? node.Value.Value : null;
            return value is not null;
        }
    }

    public void Set(string key, T value)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            RemoveExpired(now);
            if (_entries.Remove(key, out var existing))
                _order.Remove(existing);

            if (_duration <= TimeSpan.Zero)
                return;

            if (_entries.Count == _capacity)
                RemoveFirst();

            _entries[key] = _order.AddLast(new Entry(key, value, now));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    private void RemoveExpired(long now)
    {
        while (_order.First is { } first &&
               _timeProvider.GetElapsedTime(first.Value.CreatedAt, now) >= _duration)
            RemoveFirst();
    }

    private void RemoveFirst()
    {
        _entries.Remove(_order.First!.Value.Key);
        _order.RemoveFirst();
    }

    private sealed record Entry(string Key, T Value, long CreatedAt);
}
