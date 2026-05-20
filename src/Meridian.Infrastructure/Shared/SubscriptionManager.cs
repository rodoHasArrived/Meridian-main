using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Shared;

/// <summary>
/// Represents a subscription to market data.
/// </summary>
/// <param name="Id">Unique subscription identifier.</param>
/// <param name="Symbol">Symbol being subscribed to.</param>
/// <param name="Kind">Type of subscription (e.g., "trades", "quotes", "depth").</param>
/// <param name="CreatedAt">When the subscription was created.</param>
/// <param name="Status">Current lifecycle state of the subscription.</param>
/// <param name="LastMessageAt">Most recent message accepted for this subscription.</param>
/// <param name="LastError">Most recent subscription-specific failure, if any.</param>
/// <param name="RecoveryAttempts">Number of recovery attempts after reconnect/failover.</param>
/// <param name="SourceOfRequest">Optional caller/workflow that requested the subscription.</param>
public sealed record Subscription(
    int Id,
    string Symbol,
    string Kind,
    DateTimeOffset CreatedAt,
    SubscriptionStatus Status = SubscriptionStatus.Active,
    DateTimeOffset? LastMessageAt = null,
    string? LastError = null,
    int RecoveryAttempts = 0,
    string? SourceOfRequest = null);

public enum SubscriptionStatus
{
    Pending = 0,
    Active = 1,
    Recovering = 2,
    Failed = 3
}

public sealed record SubscriptionRequestValidation(
    bool IsValid,
    bool IsDuplicate,
    string? NormalizedSymbol,
    string? NormalizedKind,
    int? ExistingSubscriptionId,
    string? FailureReason)
{
    public static SubscriptionRequestValidation Invalid(string failureReason)
        => new(false, false, null, null, null, failureReason);

    public static SubscriptionRequestValidation Valid(
        string normalizedSymbol,
        string normalizedKind,
        bool isDuplicate,
        int? existingSubscriptionId)
        => new(true, isDuplicate, normalizedSymbol, normalizedKind, existingSubscriptionId, null);
}

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
    public int Subscribe(string symbol, string kind, string? sourceOfRequest = null)
    {
        var validation = ValidateSubscriptionRequest(symbol, kind);
        if (!validation.IsValid)
            return -1;

        lock (_gate)
        {
            return AddSubscriptionCore(validation.NormalizedSymbol!, validation.NormalizedKind!, sourceOfRequest);
        }
    }

    /// <summary>
    /// Creates a subscription only when the normalized symbol/kind is not already active.
    /// Useful during reconnect and failover recovery where duplicate upstream
    /// provider subscriptions are unsafe.
    /// </summary>
    public int SubscribeOrGetExisting(string symbol, string kind, string? sourceOfRequest = null)
    {
        var validation = ValidateSubscriptionRequest(symbol, kind);
        if (!validation.IsValid)
            return -1;

        lock (_gate)
        {
            var existing = FindExistingSubscription(validation.NormalizedSymbol!, validation.NormalizedKind!);
            if (existing is not null)
                return existing.Id;

            return AddSubscriptionCore(validation.NormalizedSymbol!, validation.NormalizedKind!, sourceOfRequest);
        }
    }

    public SubscriptionRequestValidation ValidateSubscriptionRequest(string symbol, string kind)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return SubscriptionRequestValidation.Invalid("Symbol is required.");

        if (string.IsNullOrWhiteSpace(kind))
            return SubscriptionRequestValidation.Invalid("Subscription kind is required.");

        var normalizedSymbol = symbol.Trim();
        var normalizedKind = kind.Trim();

        lock (_gate)
        {
            var existing = FindExistingSubscription(normalizedSymbol, normalizedKind);

            return SubscriptionRequestValidation.Valid(
                normalizedSymbol,
                normalizedKind,
                existing is not null,
                existing?.Id);
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

    public Subscription? GetSubscription(int subscriptionId)
    {
        lock (_gate)
        {
            return _subscriptions.TryGetValue(subscriptionId, out var subscription)
                ? subscription
                : null;
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

    public bool RecordMessageReceived(int subscriptionId, DateTimeOffset? receivedAt = null)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out var subscription))
                return false;

            _subscriptions[subscriptionId] = subscription with
            {
                Status = SubscriptionStatus.Active,
                LastMessageAt = receivedAt ?? DateTimeOffset.UtcNow,
                LastError = null
            };
            return true;
        }
    }

    public bool RecordSubscriptionError(int subscriptionId, string error)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out var subscription))
                return false;

            _subscriptions[subscriptionId] = subscription with
            {
                Status = SubscriptionStatus.Failed,
                LastError = string.IsNullOrWhiteSpace(error) ? "Subscription failed." : error.Trim()
            };
            return true;
        }
    }

    public bool RecordRecoveryAttempt(int subscriptionId, string? lastError = null)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out var subscription))
                return false;

            _subscriptions[subscriptionId] = subscription with
            {
                Status = SubscriptionStatus.Recovering,
                RecoveryAttempts = subscription.RecoveryAttempts + 1,
                LastError = NormalizeOptional(lastError) ?? subscription.LastError
            };
            return true;
        }
    }

    public IReadOnlyList<Subscription> GetStaleSubscriptions(
        TimeSpan staleThreshold,
        DateTimeOffset? now = null)
    {
        var referenceTime = now ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            return _subscriptions.Values
                .Where(subscription =>
                    subscription.Status == SubscriptionStatus.Active &&
                    subscription.LastMessageAt is DateTimeOffset lastMessageAt &&
                    referenceTime - lastMessageAt > staleThreshold)
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
                FailedSubscriptions: _subscriptions.Values.Count(s => s.Status == SubscriptionStatus.Failed),
                RecoveringSubscriptions: _subscriptions.Values.Count(s => s.Status == SubscriptionStatus.Recovering),
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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private int AddSubscriptionCore(string normalizedSymbol, string normalizedKind, string? sourceOfRequest)
    {
        var id = Interlocked.Increment(ref _nextId);
        var subscription = new Subscription(
            id,
            normalizedSymbol,
            normalizedKind,
            DateTimeOffset.UtcNow,
            SourceOfRequest: NormalizeOptional(sourceOfRequest));

        _subscriptions[id] = subscription;

        if (!_symbolsByKind.TryGetValue(normalizedKind, out var symbols))
        {
            symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _symbolsByKind[normalizedKind] = symbols;
        }
        symbols.Add(normalizedSymbol);

        return id;
    }

    private Subscription? FindExistingSubscription(string normalizedSymbol, string normalizedKind)
        => _subscriptions.Values.FirstOrDefault(s =>
            s.Symbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase) &&
            s.Kind.Equals(normalizedKind, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Snapshot of subscription state for serialization or logging.
/// </summary>
public sealed record SubscriptionSnapshot(
    int TotalSubscriptions,
    int FailedSubscriptions,
    int RecoveringSubscriptions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SymbolsByKind,
    IReadOnlyList<Subscription> Subscriptions);
