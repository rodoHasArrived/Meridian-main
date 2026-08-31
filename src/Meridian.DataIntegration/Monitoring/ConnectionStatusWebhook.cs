using System.Threading;
using System.Threading.Channels;
using Meridian.Core.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Contracts.Monitoring;

namespace Meridian.DataIntegration.Monitoring;

/// <summary>
/// Sends webhook notifications for connection status changes.
/// Implements MON-6: Connection Status Webhook.
/// </summary>
public sealed class ConnectionStatusWebhook : IAsyncDisposable
{
    private readonly ILogger<ConnectionStatusWebhook> _log;
    private readonly ConnectionStatusWebhookConfig _config;
    private readonly IMonitoringWebhookSink? _webhook;
    private readonly ConnectionHealthMonitor _connectionMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<WebhookNotification> _notifications;
    private readonly Task _dispatcherTask;
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _disposeStarted;

    // Rate limiting to prevent alert storms
    private readonly Dictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private readonly object _alertLock = new();

    public ConnectionStatusWebhook(
        ConnectionHealthMonitor connectionMonitor,
        IMonitoringWebhookSink? webhook = null,
        ConnectionStatusWebhookConfig? config = null,
        ILogger<ConnectionStatusWebhook>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));
        _webhook = webhook;
        _config = config ?? ConnectionStatusWebhookConfig.Default;
        _log = logger ?? NullLogger<ConnectionStatusWebhook>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _notifications = Channel.CreateUnbounded<WebhookNotification>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
        _dispatcherTask = DispatchNotificationsAsync();

        // Subscribe to connection events
        _connectionMonitor.OnConnectionLost += HandleConnectionLost;
        _connectionMonitor.OnConnectionRecovered += HandleConnectionRecovered;
        _connectionMonitor.OnHeartbeatMissed += HandleHeartbeatMissed;
        _connectionMonitor.OnHighLatency += HandleHighLatency;

        _log.LogInformation("ConnectionStatusWebhook initialized with min alert interval {Interval}s",
            _config.MinAlertIntervalSeconds);
    }

    private void HandleConnectionLost(ConnectionLostEvent evt)
    {
        if (IsDisposed || _webhook == null || !_config.NotifyOnConnectionLost)
            return;

        if (!ShouldSendAlert($"lost:{evt.ConnectionId}"))
            return;

        var message = FormatConnectionLostMessage(evt);
        _log.LogWarning("Connection lost: {ConnectionId} ({Provider}) - {Reason}",
            evt.ConnectionId, evt.ProviderName, evt.Reason);

        EnqueueEventNotification(message, "Connection Lost", "connection lost");
    }

    private void HandleConnectionRecovered(ConnectionRecoveredEvent evt)
    {
        if (IsDisposed || _webhook == null || !_config.NotifyOnConnectionRecovered)
            return;

        if (!ShouldSendAlert($"recovered:{evt.ConnectionId}"))
            return;

        var message = FormatConnectionRecoveredMessage(evt);
        _log.LogInformation("Connection recovered: {ConnectionId} ({Provider}) after {Downtime}",
            evt.ConnectionId, evt.ProviderName, evt.DowntimeDuration);

        EnqueueEventNotification(message, "Connection Recovered", "connection recovered");
    }

    private void HandleHeartbeatMissed(HeartbeatMissedEvent evt)
    {
        if (IsDisposed || _webhook == null || !_config.NotifyOnHeartbeatMissed)
            return;

        // Only notify if missed count exceeds threshold
        if (evt.MissedCount < _config.HeartbeatMissedThreshold)
            return;

        if (!ShouldSendAlert($"heartbeat:{evt.ConnectionId}"))
            return;

        var message = FormatHeartbeatMissedMessage(evt);
        _log.LogWarning("Heartbeat missed: {ConnectionId} ({Provider}) - {MissedCount} missed",
            evt.ConnectionId, evt.ProviderName, evt.MissedCount);

        EnqueueEventNotification(message, "Heartbeat Warning", "heartbeat missed");
    }

    private void HandleHighLatency(HighLatencyEvent evt)
    {
        if (IsDisposed || _webhook == null || !_config.NotifyOnHighLatency)
            return;

        // Only notify if latency exceeds our threshold
        if (evt.LatencyMs < _config.HighLatencyThresholdMs)
            return;

        if (!ShouldSendAlert($"latency:{evt.ConnectionId}"))
            return;

        var message = FormatHighLatencyMessage(evt);
        _log.LogWarning("High latency: {ConnectionId} ({Provider}) - {LatencyMs:F1}ms",
            evt.ConnectionId, evt.ProviderName, evt.LatencyMs);

        EnqueueEventNotification(message, "High Latency Alert", "high latency");
    }

    private bool IsDisposed => Volatile.Read(ref _disposeStarted) != 0;

    private void EnqueueEventNotification(string message, string title, string failureContext)
    {
        if (!_notifications.Writer.TryWrite(new WebhookNotification(
                message,
                title,
                failureContext,
                CancellationToken.None,
                Completion: null)))
        {
            _log.LogDebug("Skipped {NotificationType} webhook because the dispatcher is shutting down", failureContext);
        }
    }

    private async Task DispatchNotificationsAsync()
    {
        await foreach (var notification in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (notification.CancellationToken.IsCancellationRequested)
            {
                notification.Completion?.TrySetCanceled(notification.CancellationToken);
                continue;
            }

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _cts.Token,
                    notification.CancellationToken);
                await _webhook!.SendMonitoringMessageAsync(
                    notification.Message,
                    notification.Title,
                    linkedCts.Token).ConfigureAwait(false);
                notification.Completion?.TrySetResult();
            }
            catch (OperationCanceledException) when (notification.CancellationToken.IsCancellationRequested)
            {
                notification.Completion?.TrySetCanceled(notification.CancellationToken);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                notification.Completion?.TrySetCanceled(_cts.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send {NotificationType} webhook", notification.FailureContext);
                notification.Completion?.TrySetException(ex);
            }
        }
    }

    private bool ShouldSendAlert(string key)
    {
        lock (_alertLock)
        {
            var now = _timeProvider.GetUtcNow();

            if (_lastAlertTimes.TryGetValue(key, out var lastTime))
            {
                if ((now - lastTime).TotalSeconds < _config.MinAlertIntervalSeconds)
                {
                    return false;
                }
            }

            _lastAlertTimes[key] = now;

            // Clean up old entries
            if (_lastAlertTimes.Count > 100)
            {
                var cutoff = now.AddMinutes(-10);
                var keysToRemove = _lastAlertTimes
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var k in keysToRemove)
                {
                    _lastAlertTimes.Remove(k);
                }
            }

            return true;
        }
    }

    private static string FormatConnectionLostMessage(ConnectionLostEvent evt)
    {
        var uptimeStr = evt.UptimeDuration.TotalHours >= 1
            ? $"{evt.UptimeDuration.TotalHours:F1} hours"
            : $"{evt.UptimeDuration.TotalMinutes:F0} minutes";

        return $"Connection lost to {evt.ProviderName} ({evt.ConnectionId}).\n" +
               $"Reason: {evt.Reason ?? "Unknown"}\n" +
               $"Uptime before disconnect: {uptimeStr}";
    }

    private static string FormatConnectionRecoveredMessage(ConnectionRecoveredEvent evt)
    {
        var downtimeStr = evt.DowntimeDuration.TotalMinutes >= 1
            ? $"{evt.DowntimeDuration.TotalMinutes:F1} minutes"
            : $"{evt.DowntimeDuration.TotalSeconds:F0} seconds";

        return $"Connection to {evt.ProviderName} ({evt.ConnectionId}) recovered.\n" +
               $"Downtime: {downtimeStr}";
    }

    private static string FormatHeartbeatMissedMessage(HeartbeatMissedEvent evt)
    {
        return $"Heartbeat warning for {evt.ProviderName} ({evt.ConnectionId}).\n" +
               $"Missed heartbeats: {evt.MissedCount}\n" +
               $"Last activity: {evt.TimeSinceLastActivity.TotalSeconds:F0}s ago";
    }

    private static string FormatHighLatencyMessage(HighLatencyEvent evt)
    {
        return $"High latency detected for {evt.ProviderName} ({evt.ConnectionId}).\n" +
               $"Latency: {evt.LatencyMs:F1}ms";
    }

    /// <summary>
    /// Sends a manual connection status update.
    /// </summary>
    public async Task SendStatusUpdateAsync(string message, string? title = null, CancellationToken ct = default)
    {
        if (_webhook == null)
            return;

        try
        {
            await EnqueueAndWaitAsync(message, title ?? "Connection Status Update", "status update", ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send status update webhook");
        }
    }

    /// <summary>
    /// Sends a summary of all connection statuses.
    /// </summary>
    public async Task SendConnectionSummaryAsync(CancellationToken ct = default)
    {
        if (_webhook == null)
            return;

        var snapshot = _connectionMonitor.GetSnapshot();
        var message = FormatConnectionSummary(snapshot);

        try
        {
            await EnqueueAndWaitAsync(message, "Connection Summary", "connection summary", ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send connection summary webhook");
        }
    }

    private static string FormatConnectionSummary(ConnectionHealthSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"Total Connections: {snapshot.TotalConnections}",
            $"Healthy: {snapshot.HealthyConnections}",
            $"Unhealthy: {snapshot.UnhealthyConnections}",
            $"Avg Latency: {snapshot.GlobalAverageLatencyMs:F1}ms"
        };

        if (snapshot.UnhealthyConnections > 0)
        {
            lines.Add("");
            lines.Add("Unhealthy connections:");
            foreach (var conn in snapshot.Connections.Where(c => !c.IsHealthy))
            {
                lines.Add($"  - {conn.ProviderName}: {conn.MissedHeartbeats} missed heartbeats");
            }
        }

        return string.Join("\n", lines);
    }

    private Task EnqueueAndWaitAsync(
        string message,
        string title,
        string failureContext,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = new WebhookNotification(message, title, failureContext, ct, completion);

        if (!_notifications.Writer.TryWrite(notification))
        {
            completion.TrySetException(new ObjectDisposedException(nameof(ConnectionStatusWebhook)));
        }

        return completion.Task;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeStarted, 1);

        _connectionMonitor.OnConnectionLost -= HandleConnectionLost;
        _connectionMonitor.OnConnectionRecovered -= HandleConnectionRecovered;
        _connectionMonitor.OnHeartbeatMissed -= HandleHeartbeatMissed;
        _connectionMonitor.OnHighLatency -= HandleHighLatency;

        _notifications.Writer.TryComplete();
        await _dispatcherTask.ConfigureAwait(false);
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed record WebhookNotification(
        string Message,
        string Title,
        string FailureContext,
        CancellationToken CancellationToken,
        TaskCompletionSource? Completion);
}

/// <summary>
/// Configuration for connection status webhooks.
/// </summary>
public sealed record ConnectionStatusWebhookConfig
{
    /// <summary>
    /// Minimum interval between alerts for the same connection in seconds.
    /// </summary>
    public int MinAlertIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Whether to send notification when connection is lost.
    /// </summary>
    public bool NotifyOnConnectionLost { get; init; } = true;

    /// <summary>
    /// Whether to send notification when connection is recovered.
    /// </summary>
    public bool NotifyOnConnectionRecovered { get; init; } = true;

    /// <summary>
    /// Whether to send notification when heartbeat is missed.
    /// </summary>
    public bool NotifyOnHeartbeatMissed { get; init; } = true;

    /// <summary>
    /// Whether to send notification on high latency.
    /// </summary>
    public bool NotifyOnHighLatency { get; init; } = true;

    /// <summary>
    /// Number of missed heartbeats before sending notification.
    /// </summary>
    public int HeartbeatMissedThreshold { get; init; } = 2;

    /// <summary>
    /// Latency threshold in milliseconds for notifications.
    /// </summary>
    public double HighLatencyThresholdMs { get; init; } = 500;

    public static ConnectionStatusWebhookConfig Default => new();
}
