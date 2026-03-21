using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Sends webhook notifications for connection status changes.
/// Implements MON-6: Connection Status Webhook.
/// </summary>
public sealed class ConnectionStatusWebhook : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConnectionStatusWebhook>();
    private readonly ConnectionStatusWebhookConfig _config;
    private readonly DailySummaryWebhook? _webhook;
    private readonly ConnectionHealthMonitor _connectionMonitor;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isDisposed;

    // Rate limiting to prevent alert storms
    private readonly Dictionary<string, DateTimeOffset> _lastAlertTimes = new();
    private readonly object _alertLock = new();

    public ConnectionStatusWebhook(
        ConnectionHealthMonitor connectionMonitor,
        DailySummaryWebhook? webhook = null,
        ConnectionStatusWebhookConfig? config = null)
    {
        _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));
        _webhook = webhook;
        _config = config ?? ConnectionStatusWebhookConfig.Default;

        // Subscribe to connection events
        _connectionMonitor.OnConnectionLost += HandleConnectionLost;
        _connectionMonitor.OnConnectionRecovered += HandleConnectionRecovered;
        _connectionMonitor.OnHeartbeatMissed += HandleHeartbeatMissed;
        _connectionMonitor.OnHighLatency += HandleHighLatency;

        _log.Information("ConnectionStatusWebhook initialized with min alert interval {Interval}s",
            _config.MinAlertIntervalSeconds);
    }

    private async void HandleConnectionLost(ConnectionLostEvent evt)
    {
        if (_isDisposed || _webhook == null || !_config.NotifyOnConnectionLost)
            return;

        if (!ShouldSendAlert($"lost:{evt.ConnectionId}"))
            return;

        var message = FormatConnectionLostMessage(evt);
        _log.Warning("Connection lost: {ConnectionId} ({Provider}) - {Reason}",
            evt.ConnectionId, evt.ProviderName, evt.Reason);

        try
        {
            await _webhook.SendMessageAsync(message, "Connection Lost");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send connection lost webhook");
        }
    }

    private async void HandleConnectionRecovered(ConnectionRecoveredEvent evt)
    {
        if (_isDisposed || _webhook == null || !_config.NotifyOnConnectionRecovered)
            return;

        if (!ShouldSendAlert($"recovered:{evt.ConnectionId}"))
            return;

        var message = FormatConnectionRecoveredMessage(evt);
        _log.Information("Connection recovered: {ConnectionId} ({Provider}) after {Downtime}",
            evt.ConnectionId, evt.ProviderName, evt.DowntimeDuration);

        try
        {
            await _webhook.SendMessageAsync(message, "Connection Recovered");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send connection recovered webhook");
        }
    }

    private async void HandleHeartbeatMissed(HeartbeatMissedEvent evt)
    {
        if (_isDisposed || _webhook == null || !_config.NotifyOnHeartbeatMissed)
            return;

        // Only notify if missed count exceeds threshold
        if (evt.MissedCount < _config.HeartbeatMissedThreshold)
            return;

        if (!ShouldSendAlert($"heartbeat:{evt.ConnectionId}"))
            return;

        var message = FormatHeartbeatMissedMessage(evt);
        _log.Warning("Heartbeat missed: {ConnectionId} ({Provider}) - {MissedCount} missed",
            evt.ConnectionId, evt.ProviderName, evt.MissedCount);

        try
        {
            await _webhook.SendMessageAsync(message, "Heartbeat Warning");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send heartbeat missed webhook");
        }
    }

    private async void HandleHighLatency(HighLatencyEvent evt)
    {
        if (_isDisposed || _webhook == null || !_config.NotifyOnHighLatency)
            return;

        // Only notify if latency exceeds our threshold
        if (evt.LatencyMs < _config.HighLatencyThresholdMs)
            return;

        if (!ShouldSendAlert($"latency:{evt.ConnectionId}"))
            return;

        var message = FormatHighLatencyMessage(evt);
        _log.Warning("High latency: {ConnectionId} ({Provider}) - {LatencyMs:F1}ms",
            evt.ConnectionId, evt.ProviderName, evt.LatencyMs);

        try
        {
            await _webhook.SendMessageAsync(message, "High Latency Alert");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send high latency webhook");
        }
    }

    private bool ShouldSendAlert(string key)
    {
        lock (_alertLock)
        {
            var now = DateTimeOffset.UtcNow;

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
            await _webhook.SendMessageAsync(message, title ?? "Connection Status Update");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send status update webhook");
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
            await _webhook.SendMessageAsync(message, "Connection Summary");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send connection summary webhook");
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

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return default;
        _isDisposed = true;

        _connectionMonitor.OnConnectionLost -= HandleConnectionLost;
        _connectionMonitor.OnConnectionRecovered -= HandleConnectionRecovered;
        _connectionMonitor.OnHeartbeatMissed -= HandleHeartbeatMissed;
        _connectionMonitor.OnHighLatency -= HandleHighLatency;

        _cts.Cancel();
        _cts.Dispose();
        return default;
    }
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
