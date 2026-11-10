using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// The OMS order table. Reads are lock-free, as with the <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// it wraps. Every write that changes an order's state also publishes the new state to
/// <see cref="OrderStateBroadcast"/>, inside the same lock, so subscribers see each order's
/// transitions in the order they were written, including non-fill transitions (acceptance,
/// cancellation, rejection) and states of orders the table later evicts.
/// </summary>
internal sealed class OrderStateTable : IReadOnlyDictionary<string, OrderState>
{
    private readonly ConcurrentDictionary<string, OrderState> _inner = new();
    private readonly Lock _writeLock = new();

    // Incarnation tokens: a client order id may be reused once its order is terminal, and the
    // replacement can share the previous order's CreatedAt on a coarse clock. A fresh token is
    // issued whenever an id is registered or a terminal order is replaced by a live one, so
    // subscribers never coalesce two orders' states. Guarded by _writeLock.
    private readonly Dictionary<string, long> _incarnations = new(StringComparer.Ordinal);
    private long _nextIncarnation;

    public OrderStateBroadcast Broadcast { get; } = new();

    public OrderState this[string key]
    {
        get => _inner[key];
        set
        {
            lock (_writeLock)
            {
                _inner.TryGetValue(key, out var previous);
                _inner[key] = value;
                Publish(key, previous, value);
            }
        }
    }

    public bool TryAdd(string key, OrderState value)
    {
        lock (_writeLock)
        {
            if (!_inner.TryAdd(key, value))
                return false;
            Publish(key, previous: null, value);
            return true;
        }
    }

    public bool TryUpdate(string key, OrderState newValue, OrderState comparisonValue)
    {
        lock (_writeLock)
        {
            if (!_inner.TryUpdate(key, newValue, comparisonValue))
                return false;
            Publish(key, comparisonValue, newValue);
            return true;
        }
    }

    public OrderState AddOrUpdate(
        string key,
        Func<string, OrderState> addValueFactory,
        Func<string, OrderState, OrderState> updateValueFactory)
    {
        lock (_writeLock)
        {
            _inner.TryGetValue(key, out var previous);
            var value = _inner.AddOrUpdate(key, addValueFactory, updateValueFactory);
            Publish(key, previous, value);
            return value;
        }
    }

    /// <summary>Retention eviction. Not a state transition, so nothing is published.</summary>
    public bool TryRemove(KeyValuePair<string, OrderState> item)
    {
        lock (_writeLock)
        {
            if (!_inner.TryRemove(item))
                return false;
            _incarnations.Remove(item.Key);
            return true;
        }
    }

    // Called under _writeLock.
    private void Publish(string key, OrderState? previous, OrderState current)
    {
        var newIncarnation = previous is null
            || !_incarnations.ContainsKey(key)
            || (OrderStatusFacts.IsTerminal(previous.Status) && !OrderStatusFacts.IsTerminal(current.Status));
        if (newIncarnation)
            _incarnations[key] = ++_nextIncarnation;
        Broadcast.Publish(current, _incarnations[key]);
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out OrderState value) => _inner.TryGetValue(key, out value);

    public bool ContainsKey(string key) => _inner.ContainsKey(key);

    public OrderState? GetValueOrDefault(string key) => _inner.GetValueOrDefault(key);

    public int Count => _inner.Count;

    public IEnumerable<string> Keys => _inner.Keys;

    public IEnumerable<OrderState> Values => _inner.Values;

    public IEnumerator<KeyValuePair<string, OrderState>> GetEnumerator() => _inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Fans OMS order-state transitions out to <see cref="OrderStateSubscription"/>s. Publishing never
/// blocks or allocates per subscriber beyond a dictionary write, so the OMS write path is unaffected
/// by how fast (or whether) subscribers read.
/// </summary>
internal sealed class OrderStateBroadcast
{
    private readonly Lock _gate = new();
    private OrderStateSubscription[] _subscribers = [];

    public OrderStateSubscription Subscribe()
    {
        var subscription = new OrderStateSubscription(this);
        lock (_gate)
        {
            _subscribers = [.. _subscribers, subscription];
        }

        return subscription;
    }

    internal void Unsubscribe(OrderStateSubscription subscription)
    {
        lock (_gate)
        {
            _subscribers = _subscribers.Where(item => !ReferenceEquals(item, subscription)).ToArray();
        }
    }

    public void Publish(OrderState state, long incarnation)
    {
        foreach (var subscriber in Volatile.Read(ref _subscribers))
            subscriber.Offer(state, incarnation);
    }
}

/// <summary>
/// One subscriber's view of OMS order-state transitions. Each order incarnation (a reused client
/// order id is a separate incarnation, identified by a token the OMS issues) keeps only its latest unread
/// state: a slow reader skips intermediate states but always receives the newest one, so a
/// final state is never lost. Memory is bounded by the number of orders with unread changes.
/// </summary>
public sealed class OrderStateSubscription : IAsyncDisposable
{
    private readonly OrderStateBroadcast? _broadcast;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OrderState> _pending = new(StringComparer.Ordinal);
    private readonly System.Threading.Channels.Channel<string> _ready =
        System.Threading.Channels.Channel.CreateUnbounded<string>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
    private int _disposed;

    internal OrderStateSubscription(OrderStateBroadcast broadcast) => _broadcast = broadcast;

    internal void Offer(OrderState state, long incarnation)
    {
        var key = $"{state.OrderId}\u001f{incarnation}";
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            var queued = _pending.ContainsKey(key);
            _pending[key] = state;
            if (!queued)
                _ready.Writer.TryWrite(key);
        }
    }

    /// <summary>
    /// Order states in transition order, each the latest state of its order when read. Ends when
    /// the subscription is disposed or <paramref name="ct"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<OrderState> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var key in _ready.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            OrderState? state;
            lock (_gate)
            {
                _pending.Remove(key, out state);
            }

            if (state is not null)
                yield return state;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _broadcast?.Unsubscribe(this);
            lock (_gate)
            {
                _pending.Clear();
            }

            _ready.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
