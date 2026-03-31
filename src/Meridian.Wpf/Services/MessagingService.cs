using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Meridian.Wpf.Services;

/// <summary>
/// Simple pub/sub messaging service for inter-page communication.
/// Enables loose coupling between pages and components.
/// </summary>
public sealed class MessagingService
{
    private static readonly Lazy<MessagingService> _instance = new(() => new MessagingService());

    private readonly ConcurrentDictionary<string, List<WeakReference<Action<object?>>>> _typedSubscriptions = new();
    private readonly object _subscriptionLock = new();

    /// <summary>
    /// Gets the singleton instance of the MessagingService.
    /// </summary>
    public static MessagingService Instance => _instance.Value;

    private MessagingService()
    {
    }

    /// <summary>
    /// Event raised when a message is received.
    /// </summary>
    public event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Sends a simple string message to all subscribers.
    /// </summary>
    /// <param name="message">The message to send.</param>
    public void Send(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        MessageReceived?.Invoke(this, message);
    }

    /// <summary>
    /// Sends a typed message to subscribers of a specific message type.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message payload.</param>
    public void Send<T>(T message) where T : class
    {
        var messageType = typeof(T).FullName ?? typeof(T).Name;
        SendTyped(messageType, message);
    }

    /// <summary>
    /// Sends a named message with an optional payload.
    /// </summary>
    /// <param name="messageName">The name of the message.</param>
    /// <param name="payload">The optional payload.</param>
    public void SendNamed(string messageName, object? payload = null)
    {
        if (string.IsNullOrEmpty(messageName)) return;

        SendTyped(messageName, payload);

        // Also raise the generic MessageReceived event
        MessageReceived?.Invoke(this, messageName);
    }

    /// <summary>
    /// Subscribes to a typed message.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="handler">The message handler.</param>
    /// <returns>A subscription token for unsubscribing.</returns>
    public IDisposable Subscribe<T>(Action<T> handler) where T : class
    {
        var messageType = typeof(T).FullName ?? typeof(T).Name;
        return SubscribeTyped(messageType, obj => handler((T)obj!));
    }

    /// <summary>
    /// Subscribes to a named message.
    /// </summary>
    /// <param name="messageName">The name of the message to subscribe to.</param>
    /// <param name="handler">The message handler.</param>
    /// <returns>A subscription token for unsubscribing.</returns>
    public IDisposable Subscribe(string messageName, Action<object?> handler)
    {
        return SubscribeTyped(messageName, handler);
    }

    /// <summary>
    /// Subscribes to all simple string messages.
    /// </summary>
    /// <param name="handler">The message handler.</param>
    /// <returns>A subscription token for unsubscribing.</returns>
    public IDisposable SubscribeAll(Action<string> handler)
    {
        MessageReceived += OnMessage;
        return new SubscriptionToken(() => MessageReceived -= OnMessage);

        void OnMessage(object? sender, string message) => handler(message);
    }

    private IDisposable SubscribeTyped(string messageType, Action<object?> handler)
    {
        lock (_subscriptionLock)
        {
            if (!_typedSubscriptions.TryGetValue(messageType, out var handlers))
            {
                handlers = new List<WeakReference<Action<object?>>>();
                _typedSubscriptions[messageType] = handlers;
            }

            var weakRef = new WeakReference<Action<object?>>(handler);
            handlers.Add(weakRef);

            return new SubscriptionToken(() =>
            {
                lock (_subscriptionLock)
                {
                    if (_typedSubscriptions.TryGetValue(messageType, out var list))
                    {
                        list.Remove(weakRef);
                    }
                }
            });
        }
    }

    private void SendTyped(string messageType, object? payload)
    {
        if (!_typedSubscriptions.TryGetValue(messageType, out var handlers))
        {
            return;
        }

        List<WeakReference<Action<object?>>> toRemove = new();

        lock (_subscriptionLock)
        {
            foreach (var weakRef in handlers)
            {
                if (weakRef.TryGetTarget(out var handler))
                {
                    try
                    {
                        handler(payload);
                    }
                    catch (Exception ex)
                    {
                    }
                }
                else
                {
                    toRemove.Add(weakRef);
                }
            }

            // Clean up dead references
            foreach (var dead in toRemove)
            {
                handlers.Remove(dead);
            }
        }

    }

    /// <summary>
    /// Clears all subscriptions.
    /// </summary>
    public void ClearSubscriptions()
    {
        lock (_subscriptionLock)
        {
            _typedSubscriptions.Clear();
        }
    }

    /// <summary>
    /// Gets the number of active subscriptions for a message type.
    /// </summary>
    public int GetSubscriptionCount(string messageType)
    {
        if (_typedSubscriptions.TryGetValue(messageType, out var handlers))
        {
            lock (_subscriptionLock)
            {
                // Count only alive references
                var count = 0;
                foreach (var weakRef in handlers)
                {
                    if (weakRef.TryGetTarget(out _))
                    {
                        count++;
                    }
                }
                return count;
            }
        }
        return 0;
    }

    /// <summary>
    /// Token for managing subscriptions.
    /// </summary>
    private sealed class SubscriptionToken : IDisposable
    {
        private Action? _unsubscribe;
        private bool _disposed;

        public SubscriptionToken(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}

/// <summary>
/// Common message types for inter-page communication.
/// </summary>
public static class MessageTypes
{
    public const string SymbolsUpdated = "SymbolsUpdated";
    public const string ConfigurationChanged = "ConfigurationChanged";
    public const string ConnectionStatusChanged = "ConnectionStatusChanged";
    public const string BackfillStarted = "BackfillStarted";
    public const string BackfillCompleted = "BackfillCompleted";
    public const string BackfillProgress = "BackfillProgress";
    public const string DataQualityAlert = "DataQualityAlert";
    public const string StorageWarning = "StorageWarning";
    public const string ProviderHealthChanged = "ProviderHealthChanged";
    public const string ThemeChanged = "ThemeChanged";
    public const string RefreshRequested = "RefreshRequested";
    public const string NavigationRequested = "NavigationRequested";
    public const string WatchlistUpdated = "WatchlistUpdated";
    public const string ScheduleUpdated = "ScheduleUpdated";
    public const string TickerUpdate = "TickerUpdate";
}

/// <summary>
/// Message payload for navigation requests.
/// </summary>
public sealed class NavigationMessage
{
    public string PageTag { get; set; } = string.Empty;
    public object? Parameter { get; set; }
}

/// <summary>
/// Message payload for backfill progress updates.
/// </summary>
public sealed class BackfillProgressMessage
{
    public string Symbol { get; set; } = string.Empty;
    public int CurrentSymbol { get; set; }
    public int TotalSymbols { get; set; }
    public int BarsDownloaded { get; set; }
    public double PercentComplete { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Message payload for configuration changes.
/// </summary>
public sealed class ConfigurationChangedMessage
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}

/// <summary>
/// Message payload for a live price tick, published by any component that receives quote/trade data.
/// Subscribers (e.g. TickerStripViewModel) can consume this instead of polling the HTTP API.
/// </summary>
public sealed class TickerUpdateMessage
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Last { get; set; }
    public bool Uptick { get; set; }
}
