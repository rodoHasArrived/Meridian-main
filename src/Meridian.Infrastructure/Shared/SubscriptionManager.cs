using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Shared;

/// <summary>
/// Represents a subscription to market data.
/// </summary>
/// <param name="Id">Unique subscription identifier.</param>
/// <param name="Symbol">Symbol being subscribed to.</param>
/// <param name="Kind">Type of subscription (e.g., "trades", "quotes", "depth").</param>
/// <param name="CreatedAt">When the subscription was created.</param>
public sealed record Subscription(int Id, string Symbol, string Kind, DateTimeOffset CreatedAt);

/// <summary>
/// Thread-safe subscription manager that provides centralized subscription tracking
/// for market data providers. Eliminates duplicate subscription management code
/// across streaming provider implementations.
/// </summary>
/// <remarks>
/// Features:
/// - Thread-safe subscription add/remove operations
/// - Automatic ID generation with configurable starting range
/// - Symbol deduplication (multiple subscriptions to same symbol share the reference)
/// - Query capabilities for active subscriptions
/// - Batch operations for reconnection scenarios
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized subscription management for providers")]
public sealed class SubscriptionManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Subscription> _subscriptions = new();
    private readonly Dictionary<string, HashSet<string>> _symbolsByKind = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId;
    private bool _disposed;

    /// <summary>
    /// Creates a new subscription manager with the specified starting ID.
    /// </summary>
    /// <param name="startingId">Starting ID for subscription allocation (default: 100,000).</param>
    public SubscriptionManager(int startingId = 100_000)
    {
        _nextId = startingId;
    }

    /// <summary>
    /// Gets the total number of active subscriptions.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _subscriptions.Count;
            }
        }
    }

    /// <summary>
    /// Gets the total number of active subscriptions.
    /// Alias for <see cref="Count"/> for monitoring clarity.
    /// </summary>
    public int ActiveSubscriptionCount => Count;

    /// <summary>
    /// Creates a new subscription for a symbol with the specified kind.
    /// </summary>
    /// <param name="symbol">Symbol to subscribe to.</param>
    /// <param name="kind">Type of subscription (e.g., "trades", "quotes").</param>
    /// <returns>Subscription ID or -1 if symbol is invalid.</returns>
    public int Subscribe(string symbol, string kind)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return -1;

        var trimmedSymbol = symbol.Trim();
        if (trimmedSymbol.Length == 0)
            return -1;

        lock (_gate)
        {
            var id = Interlocked.Increment(ref _nextId);
            var subscription = new Subscription(id, trimmedSymbol, kind, DateTimeOffset.UtcNow);

            _subscriptions[id] = subscription;

            if (!_symbolsByKind.TryGetValue(kind, out var symbols))
            {
                symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _symbolsByKind[kind] = symbols;
            }
            symbols.Add(trimmedSymbol);

            return id;
        }
    }

    /// <summary>
    /// Removes a subscription by ID.
    /// </summary>
    /// <param name="subscriptionId">Subscription ID to remove.</param>
    /// <returns>The removed subscription, or null if not found.</returns>
    public Subscription? Unsubscribe(int subscriptionId)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out var subscription))
                return null;

            _subscriptions.Remove(subscriptionId);

            // Remove symbol from kind set only if no other subscriptions exist for this symbol+kind
            if (_symbolsByKind.TryGetValue(subscription.Kind, out var symbols))
            {
                var hasOtherSubscriptions = _subscriptions.Values
                    .Any(s => s.Kind.Equals(subscription.Kind, StringComparison.OrdinalIgnoreCase) &&
                             s.Symbol.Equals(subscription.Symbol, StringComparison.OrdinalIgnoreCase));

                if (!hasOtherSubscriptions)
                {
                    symbols.Remove(subscription.Symbol);

                    // Remove empty kind entries to prevent unbounded memory growth
                    // when cycling through many subscription kinds over time
                    if (symbols.Count == 0)
                    {
                        _symbolsByKind.Remove(subscription.Kind);
                    }
                }
            }

            return subscription;
        }
    }

    /// <summary>
    /// Gets all unique symbols for a specific subscription kind.
    /// </summary>
    /// <param name="kind">Type of subscription (e.g., "trades", "quotes").</param>
    /// <returns>Array of subscribed symbols for the given kind.</returns>
    public string[] GetSymbolsByKind(string kind)
    {
        lock (_gate)
        {
            if (_symbolsByKind.TryGetValue(kind, out var symbols))
            {
                return symbols.ToArray();
            }
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets all active subscriptions.
    /// </summary>
    /// <returns>List of all active subscriptions.</returns>
    public IReadOnlyList<Subscription> GetAllSubscriptions()
    {
        lock (_gate)
        {
            return _subscriptions.Values.ToList();
        }
    }

    /// <summary>
    /// Checks if a symbol has any active subscriptions of the given kind.
    /// </summary>
    /// <param name="symbol">Symbol to check.</param>
    /// <param name="kind">Type of subscription to check.</param>
    /// <returns>True if the symbol has active subscriptions of the given kind.</returns>
    public bool HasSubscription(string symbol, string kind)
    {
        lock (_gate)
        {
            if (!_symbolsByKind.TryGetValue(kind, out var symbols))
                return false;

            return symbols.Contains(symbol);
        }
    }

    /// <summary>
    /// Gets all subscription kinds that have active subscriptions.
    /// </summary>
    /// <returns>List of active subscription kinds.</returns>
    public IReadOnlyList<string> GetActiveKinds()
    {
        lock (_gate)
        {
            return _symbolsByKind
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Clears all subscriptions. Useful for reconnection scenarios.
    /// </summary>
    /// <returns>List of subscriptions that were cleared.</returns>
    public IReadOnlyList<Subscription> Clear()
    {
        lock (_gate)
        {
            var cleared = _subscriptions.Values.ToList();
            _subscriptions.Clear();
            _symbolsByKind.Clear();
            return cleared;
        }
    }

    /// <summary>
    /// Gets a snapshot of the current subscription state for serialization or logging.
    /// </summary>
    /// <returns>Snapshot of subscription state.</returns>
    public SubscriptionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new SubscriptionSnapshot(
                TotalSubscriptions: _subscriptions.Count,
                SymbolsByKind: _symbolsByKind.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToArray() as IReadOnlyList<string>),
                Subscriptions: _subscriptions.Values.ToList()
            );
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_gate)
        {
            _subscriptions.Clear();
            _symbolsByKind.Clear();
        }
    }
}

/// <summary>
/// Snapshot of subscription state for serialization or logging.
/// </summary>
public sealed record SubscriptionSnapshot(
    int TotalSubscriptions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SymbolsByKind,
    IReadOnlyList<Subscription> Subscriptions);
