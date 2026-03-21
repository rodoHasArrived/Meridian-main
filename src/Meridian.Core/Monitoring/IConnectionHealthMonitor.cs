namespace Meridian.Application.Monitoring;

/// <summary>
/// Interface for monitoring connection health of market data providers.
/// Provides events for connection state changes used by failover logic.
/// </summary>
public interface IConnectionHealthMonitor
{
    /// <summary>
    /// Event raised when a connection is lost.
    /// </summary>
    event Action<ConnectionLostEvent>? OnConnectionLost;

    /// <summary>
    /// Event raised when a connection is recovered.
    /// </summary>
    event Action<ConnectionRecoveredEvent>? OnConnectionRecovered;

    /// <summary>
    /// Event raised when heartbeat is missed (potential connection issue).
    /// </summary>
    event Action<HeartbeatMissedEvent>? OnHeartbeatMissed;

    /// <summary>
    /// Registers a new connection for monitoring.
    /// </summary>
    void RegisterConnection(string connectionId, string providerName);

    /// <summary>
    /// Unregisters a connection from monitoring.
    /// </summary>
    void UnregisterConnection(string connectionId);

    /// <summary>
    /// Records a heartbeat for a connection.
    /// </summary>
    void RecordHeartbeat(string connectionId);

    /// <summary>
    /// Records a latency sample for a connection.
    /// </summary>
    void RecordLatency(string connectionId, double latencyMs);

    /// <summary>
    /// Records that data was received on a connection (resets heartbeat timer).
    /// </summary>
    void RecordDataReceived(string connectionId);
}

/// <summary>
/// Event raised when a connection is lost.
/// </summary>
public readonly record struct ConnectionLostEvent(
    string ConnectionId,
    string ProviderName,
    string? Reason,
    DateTimeOffset Timestamp,
    TimeSpan UptimeDuration
);

/// <summary>
/// Event raised when a connection is recovered.
/// </summary>
public readonly record struct ConnectionRecoveredEvent(
    string ConnectionId,
    string ProviderName,
    DateTimeOffset Timestamp,
    TimeSpan DowntimeDuration
);

/// <summary>
/// Event raised when a heartbeat is missed.
/// </summary>
public readonly record struct HeartbeatMissedEvent(
    string ConnectionId,
    string ProviderName,
    int MissedCount,
    TimeSpan TimeSinceLastActivity,
    DateTimeOffset Timestamp
);

/// <summary>
/// Event raised when high latency is detected.
/// </summary>
public readonly record struct HighLatencyEvent(
    string ConnectionId,
    string ProviderName,
    double LatencyMs,
    DateTimeOffset Timestamp
);
