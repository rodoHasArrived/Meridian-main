using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Meridian.Infrastructure.Contracts;
using Serilog;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Default implementation of IAlertDispatcher providing centralized
/// alert publishing and subscription management.
/// </summary>
[ImplementsAdr("ADR-001", "Alert dispatcher implementation")]
public sealed class AlertDispatcher : IAlertDispatcher, IDisposable
{
    private readonly ConcurrentDictionary<Guid, AlertSubscription> _subscriptions = new();
    private readonly ConcurrentQueue<MonitoringAlert> _recentAlerts = new();
    private readonly ILogger _log;
    private readonly int _maxRecentAlerts;

    // Statistics tracking
    private long _totalAlerts;
    private readonly ConcurrentDictionary<AlertSeverity, long> _alertsBySeverity = new();
    private readonly ConcurrentDictionary<AlertCategory, long> _alertsByCategory = new();
    private readonly ConcurrentDictionary<string, long> _alertsBySource = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private bool _disposed;

    /// <summary>
    /// Creates a new alert dispatcher.
    /// </summary>
    /// <param name="maxRecentAlerts">Maximum number of recent alerts to retain (default: 1000).</param>
    /// <param name="log">Optional logger.</param>
    public AlertDispatcher(int maxRecentAlerts = 1000, ILogger? log = null)
    {
        _maxRecentAlerts = maxRecentAlerts;
        _log = log ?? LoggingSetup.ForContext<AlertDispatcher>();
    }

    /// <inheritdoc/>
    public void Publish(MonitoringAlert alert)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(alert);

        // Update statistics
        Interlocked.Increment(ref _totalAlerts);
        _alertsBySeverity.AddOrUpdate(alert.Severity, 1, (_, count) => count + 1);
        _alertsByCategory.AddOrUpdate(alert.Category, 1, (_, count) => count + 1);
        _alertsBySource.AddOrUpdate(alert.Source, 1, (_, count) => count + 1);

        // Store in recent alerts
        _recentAlerts.Enqueue(alert);
        TrimRecentAlerts();

        // Log based on severity
        LogAlert(alert);

        // Dispatch to subscribers
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.Filter.Matches(alert))
            {
                try
                {
                    subscription.SyncHandler?.Invoke(alert);
                    subscription.AsyncHandler?.Invoke(alert);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in alert subscriber for alert {AlertId}", alert.Id);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task PublishAsync(MonitoringAlert alert, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(alert);

        // Update statistics
        Interlocked.Increment(ref _totalAlerts);
        _alertsBySeverity.AddOrUpdate(alert.Severity, 1, (_, count) => count + 1);
        _alertsByCategory.AddOrUpdate(alert.Category, 1, (_, count) => count + 1);
        _alertsBySource.AddOrUpdate(alert.Source, 1, (_, count) => count + 1);

        // Store in recent alerts
        _recentAlerts.Enqueue(alert);
        TrimRecentAlerts();

        // Log based on severity
        LogAlert(alert);

        // Dispatch to subscribers
        var tasks = new List<Task>();
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.Filter.Matches(alert))
            {
                try
                {
                    subscription.SyncHandler?.Invoke(alert);

                    if (subscription.AsyncHandler != null)
                    {
                        tasks.Add(subscription.AsyncHandler(alert));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in alert subscriber for alert {AlertId}", alert.Id);
                }
            }
        }

        if (tasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in async alert subscribers for alert {AlertId}", alert.Id);
            }
        }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(Action<MonitoringAlert> handler, AlertFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new AlertSubscription(
            Guid.NewGuid(),
            filter ?? AlertFilter.All,
            handler,
            null);

        _subscriptions[subscription.Id] = subscription;
        _log.Debug("Added alert subscription {SubscriptionId}", subscription.Id);

        return new SubscriptionHandle(this, subscription.Id);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(Func<MonitoringAlert, Task> handler, AlertFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new AlertSubscription(
            Guid.NewGuid(),
            filter ?? AlertFilter.All,
            null,
            handler);

        _subscriptions[subscription.Id] = subscription;
        _log.Debug("Added async alert subscription {SubscriptionId}", subscription.Id);

        return new SubscriptionHandle(this, subscription.Id);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MonitoringAlert> GetRecentAlerts(int count = 100, AlertFilter? filter = null)
    {
        var alerts = _recentAlerts.ToArray();
        var filtered = filter != null
            ? alerts.Where(a => filter.Matches(a))
            : alerts;

        return filtered
            .OrderByDescending(a => a.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc/>
    public AlertStatistics GetStatistics()
    {
        return new AlertStatistics(
            _totalAlerts,
            new Dictionary<AlertSeverity, long>(_alertsBySeverity),
            new Dictionary<AlertCategory, long>(_alertsByCategory),
            new Dictionary<string, long>(_alertsBySource),
            _startedAt);
    }

    private void LogAlert(MonitoringAlert alert)
    {
        var logMessage = "[{Source}] {Title}: {Message}";
        var args = new object[] { alert.Source, alert.Title, alert.Message };

        switch (alert.Severity)
        {
            case AlertSeverity.Info:
                _log.Information(logMessage, args);
                break;
            case AlertSeverity.Warning:
                _log.Warning(logMessage, args);
                break;
            case AlertSeverity.Error:
                if (alert.Exception != null)
                    _log.Error(alert.Exception, logMessage, args);
                else
                    _log.Error(logMessage, args);
                break;
            case AlertSeverity.Critical:
                if (alert.Exception != null)
                    _log.Fatal(alert.Exception, logMessage, args);
                else
                    _log.Fatal(logMessage, args);
                break;
        }
    }

    private void TrimRecentAlerts()
    {
        while (_recentAlerts.Count > _maxRecentAlerts && _recentAlerts.TryDequeue(out _))
        {
            // Remove oldest alerts
        }
    }

    private void Unsubscribe(Guid subscriptionId)
    {
        if (_subscriptions.TryRemove(subscriptionId, out _))
        {
            _log.Debug("Removed alert subscription {SubscriptionId}", subscriptionId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _subscriptions.Clear();
    }

    private sealed record AlertSubscription(
        Guid Id,
        AlertFilter Filter,
        Action<MonitoringAlert>? SyncHandler,
        Func<MonitoringAlert, Task>? AsyncHandler);

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly AlertDispatcher _dispatcher;
        private readonly Guid _subscriptionId;
        private bool _disposed;

        public SubscriptionHandle(AlertDispatcher dispatcher, Guid subscriptionId)
        {
            _dispatcher = dispatcher;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _dispatcher.Unsubscribe(_subscriptionId);
        }
    }
}
