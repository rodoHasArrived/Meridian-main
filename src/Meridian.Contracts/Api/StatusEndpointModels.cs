using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Backpressure status response.
/// </summary>
public sealed record BackpressureStatusDto
{
    /// <summary>
    /// Gets a value indicating whether backpressure is currently active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the current backpressure level (none, low, medium, high, critical).
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; init; } = "none";

    /// <summary>
    /// Gets the queue utilization percentage (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("queueUtilization")]
    public float QueueUtilization { get; init; }

    /// <summary>
    /// Gets the total number of dropped events due to backpressure.
    /// </summary>
    [JsonPropertyName("droppedEvents")]
    public long DroppedEvents { get; init; }

    /// <summary>
    /// Gets the rate of dropped events per second.
    /// </summary>
    [JsonPropertyName("dropRate")]
    public float DropRate { get; init; }

    /// <summary>
    /// Gets the duration in seconds that backpressure has been active.
    /// </summary>
    [JsonPropertyName("durationSeconds")]
    public float DurationSeconds { get; init; }

    /// <summary>
    /// Gets an optional human-readable message about the backpressure condition.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Gets a value indicating whether the queue depth has reached a warning threshold.
    /// </summary>
    [JsonPropertyName("queueDepthWarning")]
    public bool QueueDepthWarning { get; init; }
}

/// <summary>
/// Provider latency statistics.
/// </summary>
public sealed record ProviderLatencyStatsDto
{
    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// Gets the average latency in milliseconds.
    /// </summary>
    [JsonPropertyName("averageMs")]
    public float AverageMs { get; init; }

    /// <summary>
    /// Gets the minimum latency in milliseconds.
    /// </summary>
    [JsonPropertyName("minMs")]
    public float MinMs { get; init; }

    /// <summary>
    /// Gets the maximum latency in milliseconds.
    /// </summary>
    [JsonPropertyName("maxMs")]
    public float MaxMs { get; init; }

    /// <summary>
    /// Gets the 50th percentile (median) latency in milliseconds.
    /// </summary>
    [JsonPropertyName("p50Ms")]
    public float P50Ms { get; init; }

    /// <summary>
    /// Gets the 95th percentile latency in milliseconds.
    /// </summary>
    [JsonPropertyName("p95Ms")]
    public float P95Ms { get; init; }

    /// <summary>
    /// Gets the 99th percentile latency in milliseconds.
    /// </summary>
    [JsonPropertyName("p99Ms")]
    public float P99Ms { get; init; }

    /// <summary>
    /// Gets the number of latency samples collected.
    /// </summary>
    [JsonPropertyName("sampleCount")]
    public long SampleCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the provider latency is within healthy thresholds.
    /// </summary>
    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; init; }
}

/// <summary>
/// Provider latency summary response.
/// </summary>
public sealed record ProviderLatencySummaryDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("providers")]
    public IReadOnlyList<ProviderLatencyStatsDto> Providers { get; init; } = Array.Empty<ProviderLatencyStatsDto>();

    [JsonPropertyName("globalAverageMs")]
    public float GlobalAverageMs { get; init; }

    [JsonPropertyName("globalP99Ms")]
    public float GlobalP99Ms { get; init; }
}

/// <summary>
/// Individual connection health information.
/// </summary>
public sealed record ConnectionHealthDto
{
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; init; } = string.Empty;

    [JsonPropertyName("providerName")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; init; }

    [JsonPropertyName("isHealthy")]
    public bool IsHealthy { get; init; }

    [JsonPropertyName("lastHeartbeatTime")]
    public DateTimeOffset? LastHeartbeatTime { get; init; }

    [JsonPropertyName("missedHeartbeats")]
    public int MissedHeartbeats { get; init; }

    [JsonPropertyName("uptimeSeconds")]
    public float UptimeSeconds { get; init; }

    [JsonPropertyName("averageLatencyMs")]
    public float AverageLatencyMs { get; init; }
}

/// <summary>
/// Connection health snapshot response.
/// </summary>
public sealed record ConnectionHealthSnapshotDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("totalConnections")]
    public int TotalConnections { get; init; }

    [JsonPropertyName("healthyConnections")]
    public int HealthyConnections { get; init; }

    [JsonPropertyName("unhealthyConnections")]
    public int UnhealthyConnections { get; init; }

    [JsonPropertyName("globalAverageLatencyMs")]
    public float GlobalAverageLatencyMs { get; init; }

    [JsonPropertyName("globalMinLatencyMs")]
    public float GlobalMinLatencyMs { get; init; }

    [JsonPropertyName("globalMaxLatencyMs")]
    public float GlobalMaxLatencyMs { get; init; }

    [JsonPropertyName("connections")]
    public IReadOnlyList<ConnectionHealthDto> Connections { get; init; } = Array.Empty<ConnectionHealthDto>();
}

/// <summary>
/// Error entry in the error log.
/// </summary>
public sealed record ErrorEntryDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = "error";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("exceptionType")]
    public string? ExceptionType { get; init; }

    [JsonPropertyName("context")]
    public string? Context { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }
}

/// <summary>
/// Error statistics.
/// </summary>
public sealed record ErrorStatsDto
{
    [JsonPropertyName("totalErrors")]
    public int TotalErrors { get; init; }

    [JsonPropertyName("errorsInLastMinute")]
    public int ErrorsInLastMinute { get; init; }

    [JsonPropertyName("errorsInLastHour")]
    public int ErrorsInLastHour { get; init; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; init; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; init; }

    [JsonPropertyName("criticalCount")]
    public int CriticalCount { get; init; }

    [JsonPropertyName("lastErrorTime")]
    public DateTimeOffset? LastErrorTime { get; init; }
}

/// <summary>
/// Errors response with entries and statistics.
/// </summary>
public sealed record ErrorsResponseDto
{
    [JsonPropertyName("errors")]
    public IReadOnlyList<ErrorEntryDto> Errors { get; init; } = Array.Empty<ErrorEntryDto>();

    [JsonPropertyName("stats")]
    public ErrorStatsDto? Stats { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Prometheus metrics response (plain text format).
/// </summary>
public sealed record PrometheusMetricsDto
{
    public string Content { get; init; } = string.Empty;
}
