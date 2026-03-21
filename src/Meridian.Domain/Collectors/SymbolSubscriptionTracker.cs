using System.Collections.Concurrent;
using Meridian.Contracts.Domain;

namespace Meridian.Domain.Collectors;

/// <summary>
/// Provides common subscription management functionality for market data collectors.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public abstract class SymbolSubscriptionTracker
{
    private readonly ConcurrentDictionary<SymbolId, bool> _subscriptions = new();
    private readonly bool _requireExplicitSubscription;

    protected SymbolSubscriptionTracker(bool requireExplicitSubscription = true)
    {
        _requireExplicitSubscription = requireExplicitSubscription;
    }

    /// <summary>
    /// Whether explicit subscription is required before processing updates.
    /// </summary>
    protected bool RequireExplicitSubscription => _requireExplicitSubscription;

    /// <summary>
    /// Registers a symbol for subscription.
    /// </summary>
    public void RegisterSubscription(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol required.", nameof(symbol));
        _subscriptions[new SymbolId(symbol.Trim())] = true;
    }

    /// <summary>
    /// Unregisters a symbol from subscription.
    /// </summary>
    public void UnregisterSubscription(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return;
        _subscriptions.TryRemove(new SymbolId(symbol.Trim()), out _);
    }

    /// <summary>
    /// Checks if a symbol is currently subscribed.
    /// </summary>
    public bool IsSubscribed(string symbol)
        => !string.IsNullOrWhiteSpace(symbol) && _subscriptions.TryGetValue(new SymbolId(symbol.Trim()), out var v) && v;

    /// <summary>
    /// Attempts to auto-subscribe a symbol if explicit subscription is not required.
    /// </summary>
    protected void TryAutoSubscribe(string symbol)
    {
        if (!_requireExplicitSubscription)
            _subscriptions.TryAdd(new SymbolId(symbol), true);
    }

    /// <summary>
    /// Checks if an update should be processed based on subscription state.
    /// </summary>
    protected bool ShouldProcessUpdate(string symbol)
    {
        if (_requireExplicitSubscription && !IsSubscribed(symbol))
            return false;

        TryAutoSubscribe(symbol);
        return true;
    }

    /// <summary>
    /// Gets all currently subscribed symbols.
    /// </summary>
    public IReadOnlyCollection<string> GetSubscribedSymbols()
        => _subscriptions.Where(kvp => kvp.Value).Select(kvp => kvp.Key.Value).ToList();
}
