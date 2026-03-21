using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Monitors connection health for market data providers.
/// Tracks heartbeats, latency, and connection state with auto-reconnect support.
/// </summary>
public sealed class ConnectionHealthMonitor : IConnectionHealthMonitor, IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConnectionHealthMonitor>();
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();
    private readonly ConnectionHealthConfig _config;
    private readonly Timer _heartbeatTimer;
    private readonly Timer _statsTimer;
    private volatile bool _isDisposed;

    // Global latency tracking
    private long _totalLatencySamples;
    private long _totalLatencyTicks;
    private long _minLatencyTicks = long.MaxValue;
    private long _maxLatencyTicks;

    /// <summary>
    /// Event raised when a connection is lost.
    /// </summary>
    public event Action<ConnectionLostEvent>? OnConnectionLost;

    /// <summary>
    /// Event raised when a connection is recovered.
    /// </summary>
    public event Action<ConnectionRecoveredEvent>? OnConnectionRecovered;

    /// <summary>
    /// Event raised when heartbeat is missed (potential connection issue).
    /// </summary>
    public event Action<HeartbeatMissedEvent>? OnHeartbeatMissed;

    /// <summary>
    /// Event raised when high latency is detected.
    /// </summary>
    public event Action<HighLatencyEvent>? OnHighLatency;

    /// <summary>
    /// Delegate for sending ping messages to connections.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? PingSender { get; set; }

    public ConnectionHealthMonitor(ConnectionHealthConfig? config = null)
    {
        _config = config ?? ConnectionHealthConfig.Default;

        _heartbeatTimer = new Timer(CheckHeartbeats, null,
            TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds),
            TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds));

        _statsTimer = new Timer(UpdateStats, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        _log.Information("ConnectionHealthMonitor initialized with heartbeat interval {Interval}s, timeout {Timeout}s",
            _config.HeartbeatIntervalSeconds, _config.HeartbeatTimeoutSeconds);
    }

    /// <summary>
    /// Registers a new connection for monitoring.
    /// </summary>
    public void RegisterConnection(string connectionId, string providerName)
    {
        var state = _connections.GetOrAdd(connectionId, _ => new ConnectionState(connectionId, providerName));
        state.MarkConnected();
        _log.Information("Registered connection {ConnectionId} for provider {Provider}", connectionId, providerName);
    }

    /// <summary>
    /// Unregisters a connection from monitoring.
    /// </summary>
    public void UnregisterConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var state))
        {
            _log.Information("Unregistered connection {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// Records a heartbeat response (pong) for a connection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordHeartbeat(string connectionId)
    {
        RecordHeartbeat(connectionId, null);
    }

    /// <summary>
    /// Records a heartbeat response (pong) for a connection with optional round-trip time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordHeartbeat(string connectionId, long? roundTripTicks)
    {
        if (_connections.TryGetValue(connectionId, out var state))
        {
            state.RecordHeartbeat();

            if (roundTripTicks.HasValue && roundTripTicks.Value > 0)
            {
                state.RecordLatency(roundTripTicks.Value);
                RecordGlobalLatency(roundTripTicks.Value);
            }

            // Check if this recovers from a missed heartbeat state
            if (state.MissedHeartbeats > 0)
            {
                var missed = state.MissedHeartbeats;
                state.ResetMissedHeartbeats();
                _log.Information("Connection {ConnectionId} heartbeat recovered after {Missed} missed", connectionId, missed);
            }
        }
    }

    /// <summary>
    /// Records data received from a connection (implicit heartbeat).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordDataReceived(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var state))
        {
            state.RecordDataReceived();
        }
    }

    /// <summary>
    /// Records latency for a connection (interface implementation - latency in milliseconds).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string connectionId, double latencyMs)
    {
        var latencyTicks = (long)(latencyMs * Stopwatch.Frequency / 1000);
        RecordLatency(connectionId, latencyTicks);
    }

    /// <summary>
    /// Records latency for a connection (in ticks).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatency(string connectionId, long latencyTicks)
    {
        if (_connections.TryGetValue(connectionId, out var state))
        {
            state.RecordLatency(latencyTicks);
            RecordGlobalLatency(latencyTicks);

            // Check for high latency
            var latencyMs = (double)latencyTicks / Stopwatch.Frequency * 1000;
            if (latencyMs > _config.HighLatencyThresholdMs)
            {
                _log.Warning("High latency detected on {ConnectionId}: {LatencyMs:F2}ms", connectionId, latencyMs);
                try
                {
                    OnHighLatency?.Invoke(new HighLatencyEvent(connectionId, state.ProviderName, latencyMs, DateTimeOffset.UtcNow));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in high latency event handler");
                }
            }
        }
    }

    /// <summary>
    /// Marks a connection as disconnected.
    /// </summary>
    public void MarkDisconnected(string connectionId, string? reason = null)
    {
        if (_connections.TryGetValue(connectionId, out var state))
        {
            var wasConnected = state.IsConnected;
            state.MarkDisconnected();

            if (wasConnected)
            {
                _log.Warning("Connection {ConnectionId} disconnected: {Reason}", connectionId, reason ?? "Unknown");

                try
                {
                    OnConnectionLost?.Invoke(new ConnectionLostEvent(
                        connectionId, state.ProviderName, reason, DateTimeOffset.UtcNow, state.UptimeDuration));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in connection lost event handler");
                }
            }
        }
    }

    /// <summary>
    /// Marks a connection as connected (e.g., after reconnection).
    /// </summary>
    public void MarkConnected(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var state))
        {
            var wasDisconnected = !state.IsConnected;
            state.MarkConnected();

            if (wasDisconnected)
            {
                _log.Information("Connection {ConnectionId} restored", connectionId);

                try
                {
                    OnConnectionRecovered?.Invoke(new ConnectionRecoveredEvent(
                        connectionId, state.ProviderName, DateTimeOffset.UtcNow, state.DisconnectedDuration));
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error in connection recovered event handler");
                }
            }
        }
    }

    /// <summary>
    /// Gets the health snapshot for all connections.
    /// </summary>
    public ConnectionHealthSnapshot GetSnapshot()
    {
        var connections = new List<ConnectionStatus>();

        foreach (var kvp in _connections)
        {
            connections.Add(kvp.Value.GetStatus());
        }

        return new ConnectionHealthSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Connections: connections,
            TotalConnections: connections.Count,
            HealthyConnections: connections.Count(c => c.IsHealthy),
            UnhealthyConnections: connections.Count(c => !c.IsHealthy),
            GlobalAverageLatencyMs: GetAverageLatencyMs(),
            GlobalMinLatencyMs: GetMinLatencyMs(),
            GlobalMaxLatencyMs: GetMaxLatencyMs()
        );
    }

    /// <summary>
    /// Gets the status of a specific connection by connection ID.
    /// </summary>
    public ConnectionStatus? GetConnectionStatus(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var state) ? state.GetStatus() : null;
    }

    /// <summary>
    /// Gets the aggregate status for a provider by provider name.
    /// Returns the first connected connection's status, or the first disconnected one if none are connected.
    /// </summary>
    public ConnectionStatus? GetConnectionStatusByProvider(string providerName)
    {
        ConnectionStatus? firstDisconnected = null;
        foreach (var kvp in _connections)
        {
            if (string.Equals(kvp.Value.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
            {
                var status = kvp.Value.GetStatus();
                if (status.IsConnected)
                    return status;
                firstDisconnected ??= status;
            }
        }
        return firstDisconnected;
    }

    /// <summary>
    /// Gets the average latency in milliseconds across all connections.
    /// </summary>
    public double GetAverageLatencyMs()
    {
        var samples = Interlocked.Read(ref _totalLatencySamples);
        if (samples == 0)
            return 0;

        var ticks = Interlocked.Read(ref _totalLatencyTicks);
        return (double)ticks / samples / Stopwatch.Frequency * 1000;
    }

    /// <summary>
    /// Gets the minimum latency in milliseconds.
    /// </summary>
    public double GetMinLatencyMs()
    {
        var ticks = Interlocked.Read(ref _minLatencyTicks);
        if (ticks == long.MaxValue)
            return 0;
        return (double)ticks / Stopwatch.Frequency * 1000;
    }

    /// <summary>
    /// Gets the maximum latency in milliseconds.
    /// </summary>
    public double GetMaxLatencyMs()
    {
        var ticks = Interlocked.Read(ref _maxLatencyTicks);
        return (double)ticks / Stopwatch.Frequency * 1000;
    }

    private void RecordGlobalLatency(long ticks)
    {
        Interlocked.Add(ref _totalLatencyTicks, ticks);
        Interlocked.Increment(ref _totalLatencySamples);

        // Update min
        var currentMin = Interlocked.Read(ref _minLatencyTicks);
        while (ticks < currentMin)
        {
            var prev = Interlocked.CompareExchange(ref _minLatencyTicks, ticks, currentMin);
            if (prev == currentMin)
                break;
            currentMin = prev;
        }

        // Update max
        var currentMax = Interlocked.Read(ref _maxLatencyTicks);
        while (ticks > currentMax)
        {
            var prev = Interlocked.CompareExchange(ref _maxLatencyTicks, ticks, currentMax);
            if (prev == currentMax)
                break;
            currentMax = prev;
        }
    }

    private async void CheckHeartbeats(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromSeconds(_config.HeartbeatTimeoutSeconds);

            foreach (var kvp in _connections)
            {
                var conn = kvp.Value;
                if (!conn.IsConnected)
                    continue;

                var timeSinceLastActivity = now - conn.LastActivityTime;

                // Check if we've exceeded timeout
                if (timeSinceLastActivity > timeout)
                {
                    conn.IncrementMissedHeartbeats();

                    _log.Warning("Heartbeat missed for {ConnectionId}: {Elapsed:F1}s since last activity (missed: {Count})",
                        kvp.Key, timeSinceLastActivity.TotalSeconds, conn.MissedHeartbeats);

                    try
                    {
                        OnHeartbeatMissed?.Invoke(new HeartbeatMissedEvent(
                            kvp.Key, conn.ProviderName, conn.MissedHeartbeats, timeSinceLastActivity, now));
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in heartbeat missed event handler");
                    }

                    // If too many missed heartbeats, mark as potentially disconnected
                    if (conn.MissedHeartbeats >= _config.MaxMissedHeartbeats)
                    {
                        MarkDisconnected(kvp.Key, $"Too many missed heartbeats ({conn.MissedHeartbeats})");
                    }
                }

                // Send ping if configured
                if (PingSender != null && timeSinceLastActivity.TotalSeconds > _config.HeartbeatIntervalSeconds / 2)
                {
                    try
                    {
                        var pingStart = Stopwatch.GetTimestamp();
                        var success = await PingSender(kvp.Key, CancellationToken.None);
                        if (success)
                        {
                            var pingTicks = Stopwatch.GetTimestamp() - pingStart;
                            RecordHeartbeat(kvp.Key, pingTicks);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "Failed to send ping to {ConnectionId}", kvp.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during heartbeat check");
        }
    }

    private void UpdateStats(object? state)
    {
        if (_isDisposed)
            return;

        // Update per-connection statistics
        foreach (var kvp in _connections)
        {
            kvp.Value.UpdateStatistics();
        }

        // Evict connections that have been disconnected and had no activity for
        // longer than the heartbeat timeout. This prevents unbounded dictionary
        // growth when connections are not explicitly unregistered.
        var staleThreshold = TimeSpan.FromSeconds(_config.HeartbeatTimeoutSeconds * 2);
        var toRemove = new List<string>();
        foreach (var kvp in _connections)
        {
            var conn = kvp.Value;
            if (!conn.IsConnected &&
                (DateTimeOffset.UtcNow - conn.LastActivityTime) > staleThreshold)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            if (_connections.TryRemove(key, out _))
            {
                _log.Debug("Evicted stale disconnected connection {ConnectionId} from health monitor", key);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _heartbeatTimer.Dispose();
        _statsTimer.Dispose();
        _connections.Clear();
    }

    /// <summary>
    /// Internal state for tracking a single connection.
    /// </summary>
    private sealed class ConnectionState
    {
        public string ConnectionId { get; }
        public string ProviderName { get; }

        private volatile bool _isConnected;
        private DateTimeOffset _lastHeartbeatTime;
        private DateTimeOffset _lastDataReceivedTime;
        private DateTimeOffset _connectedSinceTime;
        private DateTimeOffset _disconnectedSinceTime;
        private int _missedHeartbeats;
        private long _reconnectCount;
        private long _totalDataReceived;

        // Latency tracking
        private long _latencySamples;
        private long _latencyTotalTicks;
        private long _minLatencyTicks = long.MaxValue;
        private long _maxLatencyTicks;
        private long _recentLatencyTicks;
        private long _recentLatencyCount;

        public bool IsConnected => _isConnected;
        public DateTimeOffset LastActivityTime => _lastDataReceivedTime > _lastHeartbeatTime ? _lastDataReceivedTime : _lastHeartbeatTime;
        public int MissedHeartbeats => Volatile.Read(ref _missedHeartbeats);
        public TimeSpan UptimeDuration => _isConnected ? DateTimeOffset.UtcNow - _connectedSinceTime : TimeSpan.Zero;
        public TimeSpan DisconnectedDuration => !_isConnected ? DateTimeOffset.UtcNow - _disconnectedSinceTime : TimeSpan.Zero;

        public ConnectionState(string connectionId, string providerName)
        {
            ConnectionId = connectionId;
            ProviderName = providerName;
            _lastHeartbeatTime = DateTimeOffset.UtcNow;
            _lastDataReceivedTime = DateTimeOffset.UtcNow;
        }

        public void MarkConnected()
        {
            if (!_isConnected)
            {
                Interlocked.Increment(ref _reconnectCount);
            }
            _isConnected = true;
            _connectedSinceTime = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _missedHeartbeats, 0);
        }

        public void MarkDisconnected()
        {
            _isConnected = false;
            _disconnectedSinceTime = DateTimeOffset.UtcNow;
        }

        public void RecordHeartbeat()
        {
            _lastHeartbeatTime = DateTimeOffset.UtcNow;
        }

        public void RecordDataReceived()
        {
            _lastDataReceivedTime = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref _totalDataReceived);
        }

        public void RecordLatency(long ticks)
        {
            Interlocked.Add(ref _latencyTotalTicks, ticks);
            Interlocked.Increment(ref _latencySamples);
            Interlocked.Add(ref _recentLatencyTicks, ticks);
            Interlocked.Increment(ref _recentLatencyCount);

            // Update min
            var currentMin = Interlocked.Read(ref _minLatencyTicks);
            while (ticks < currentMin)
            {
                var prev = Interlocked.CompareExchange(ref _minLatencyTicks, ticks, currentMin);
                if (prev == currentMin)
                    break;
                currentMin = prev;
            }

            // Update max
            var currentMax = Interlocked.Read(ref _maxLatencyTicks);
            while (ticks > currentMax)
            {
                var prev = Interlocked.CompareExchange(ref _maxLatencyTicks, ticks, currentMax);
                if (prev == currentMax)
                    break;
                currentMax = prev;
            }
        }

        public void IncrementMissedHeartbeats()
        {
            Interlocked.Increment(ref _missedHeartbeats);
        }

        public void ResetMissedHeartbeats()
        {
            Interlocked.Exchange(ref _missedHeartbeats, 0);
        }

        public void UpdateStatistics()
        {
            // Reset recent latency window
            Interlocked.Exchange(ref _recentLatencyTicks, 0);
            Interlocked.Exchange(ref _recentLatencyCount, 0);
        }

        public ConnectionStatus GetStatus()
        {
            var samples = Interlocked.Read(ref _latencySamples);
            var avgLatencyMs = samples > 0
                ? (double)Interlocked.Read(ref _latencyTotalTicks) / samples / Stopwatch.Frequency * 1000
                : 0;

            var minTicks = Interlocked.Read(ref _minLatencyTicks);
            var minLatencyMs = minTicks == long.MaxValue
                ? 0
                : (double)minTicks / Stopwatch.Frequency * 1000;

            var maxLatencyMs = (double)Interlocked.Read(ref _maxLatencyTicks) / Stopwatch.Frequency * 1000;

            var recentCount = Interlocked.Read(ref _recentLatencyCount);
            var recentAvgMs = recentCount > 0
                ? (double)Interlocked.Read(ref _recentLatencyTicks) / recentCount / Stopwatch.Frequency * 1000
                : avgLatencyMs;

            return new ConnectionStatus(
                ConnectionId: ConnectionId,
                ProviderName: ProviderName,
                IsConnected: _isConnected,
                IsHealthy: _isConnected && MissedHeartbeats == 0,
                LastHeartbeatTime: _lastHeartbeatTime,
                LastDataReceivedTime: _lastDataReceivedTime,
                MissedHeartbeats: MissedHeartbeats,
                ReconnectCount: Interlocked.Read(ref _reconnectCount),
                UptimeDuration: UptimeDuration,
                TotalDataReceived: Interlocked.Read(ref _totalDataReceived),
                AverageLatencyMs: avgLatencyMs,
                MinLatencyMs: minLatencyMs,
                MaxLatencyMs: maxLatencyMs,
                RecentAverageLatencyMs: recentAvgMs
            );
        }
    }
}

/// <summary>
/// Configuration for connection health monitoring.
/// </summary>
public sealed record ConnectionHealthConfig
{
    /// <summary>
    /// Interval between heartbeat checks in seconds.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Timeout before considering heartbeat missed in seconds.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Number of missed heartbeats before marking connection as lost.
    /// </summary>
    public int MaxMissedHeartbeats { get; init; } = 3;

    /// <summary>
    /// Latency threshold in milliseconds for high latency warnings.
    /// </summary>
    public double HighLatencyThresholdMs { get; init; } = 500;

    public static ConnectionHealthConfig Default => new();
}

/// <summary>
/// Snapshot of all connection health statuses.
/// </summary>
public readonly record struct ConnectionHealthSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<ConnectionStatus> Connections,
    int TotalConnections,
    int HealthyConnections,
    int UnhealthyConnections,
    double GlobalAverageLatencyMs,
    double GlobalMinLatencyMs,
    double GlobalMaxLatencyMs
);

/// <summary>
/// Status of a single connection.
/// </summary>
public readonly record struct ConnectionStatus(
    string ConnectionId,
    string ProviderName,
    bool IsConnected,
    bool IsHealthy,
    DateTimeOffset LastHeartbeatTime,
    DateTimeOffset LastDataReceivedTime,
    int MissedHeartbeats,
    long ReconnectCount,
    TimeSpan UptimeDuration,
    long TotalDataReceived,
    double AverageLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    double RecentAverageLatencyMs
);

// Event record structs (ConnectionLostEvent, ConnectionRecoveredEvent, HeartbeatMissedEvent, HighLatencyEvent)
// are defined in Meridian.Core/Monitoring/IConnectionHealthMonitor.cs
