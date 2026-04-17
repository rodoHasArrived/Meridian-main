using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>
/// Status response from the core service.
/// </summary>
public sealed class StatusResponse
{
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; set; }

    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; set; }

    [JsonPropertyName("metrics")]
    public MetricsData? Metrics { get; set; }

    [JsonPropertyName("pipeline")]
    public PipelineData? Pipeline { get; set; }
}

/// <summary>
/// Metrics data snapshot with staleness and provenance tracking.
/// </summary>
public sealed class MetricsData
{
    [JsonPropertyName("published")]
    public long Published { get; set; }

    [JsonPropertyName("dropped")]
    public long Dropped { get; set; }

    [JsonPropertyName("integrity")]
    public long Integrity { get; set; }

    [JsonPropertyName("historicalBars")]
    public long HistoricalBars { get; set; }

    [JsonPropertyName("eventsPerSecond")]
    public float EventsPerSecond { get; set; }

    [JsonPropertyName("dropRate")]
    public float DropRate { get; set; }

    [JsonPropertyName("trades")]
    public long Trades { get; set; }

    [JsonPropertyName("depthUpdates")]
    public long DepthUpdates { get; set; }

    [JsonPropertyName("quotes")]
    public long Quotes { get; set; }

    /// <summary>
    /// UTC timestamp when these metrics were last updated from the provider.
    /// Null indicates no data has been received yet.
    /// </summary>
    [JsonPropertyName("lastUpdatedUtc")]
    public DateTimeOffset? LastUpdatedUtc { get; set; }

    /// <summary>
    /// Name of the provider that sourced these metrics.
    /// </summary>
    [JsonPropertyName("sourceProvider")]
    public string? SourceProvider { get; set; }

    /// <summary>
    /// Whether the metrics are considered stale (no update in >15 seconds).
    /// </summary>
    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }

    /// <summary>
    /// Age of these metrics in seconds since last update.
    /// </summary>
    [JsonPropertyName("ageSeconds")]
    public float AgeSeconds { get; set; }
}

/// <summary>
/// Pipeline statistics.
/// </summary>
public sealed class PipelineData
{
    [JsonPropertyName("publishedCount")]
    public long PublishedCount { get; set; }

    [JsonPropertyName("droppedCount")]
    public long DroppedCount { get; set; }

    [JsonPropertyName("consumedCount")]
    public long ConsumedCount { get; set; }

    [JsonPropertyName("currentQueueSize")]
    public int CurrentQueueSize { get; set; }

    [JsonPropertyName("peakQueueSize")]
    public long PeakQueueSize { get; set; }

    [JsonPropertyName("queueCapacity")]
    public int QueueCapacity { get; set; }

    [JsonPropertyName("queueUtilization")]
    public float QueueUtilization { get; set; }

    [JsonPropertyName("averageProcessingTimeUs")]
    public float AverageProcessingTimeUs { get; set; }
}

/// <summary>
/// Health check response.
/// </summary>
public sealed class HealthCheckResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; set; }

    [JsonPropertyName("checks")]
    public HealthCheckItem[]? Checks { get; set; }
}

/// <summary>
/// Individual health check item.
/// </summary>
public sealed class HealthCheckItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Health summary response for the /api/health/summary endpoint (D7).
/// </summary>
public sealed class HealthSummaryResponse
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("providers")]
    public HealthSummaryProviders? Providers { get; set; }

    [JsonPropertyName("storageHealthy")]
    public bool StorageHealthy { get; set; }

    [JsonPropertyName("pipelineActive")]
    public bool PipelineActive { get; set; }
}

/// <summary>
/// Provider counts for health summary response (D7).
/// </summary>
public sealed class HealthSummaryProviders
{
    [JsonPropertyName("streaming")]
    public int Streaming { get; set; }

    [JsonPropertyName("backfill")]
    public int Backfill { get; set; }

    [JsonPropertyName("symbolSearch")]
    public int SymbolSearch { get; set; }

    [JsonPropertyName("totalEnabled")]
    public int TotalEnabled { get; set; }
}

/// <summary>
/// Backfill provider information.
/// </summary>
public sealed class BackfillProviderInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isAvailable")]
    public bool IsAvailable { get; set; } = true;

    [JsonPropertyName("requiresApiKey")]
    public bool RequiresApiKey { get; set; }

    [JsonPropertyName("supportsIntraday")]
    public bool SupportsIntraday { get; set; }

    [JsonPropertyName("supportedGranularities")]
    public string[] SupportedGranularities { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Backfill operation result DTO for API responses.
/// </summary>
public sealed class BackfillResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("symbols")]
    public string[]? Symbols { get; set; }

    [JsonPropertyName("barsWritten")]
    public int BarsWritten { get; set; }

    [JsonPropertyName("startedUtc")]
    public DateTimeOffset? StartedUtc { get; set; }

    [JsonPropertyName("completedUtc")]
    public DateTimeOffset? CompletedUtc { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("symbolResults")]
    public SymbolBackfillResult[]? SymbolResults { get; set; }
}

/// <summary>
/// Per-symbol backfill result.
/// </summary>
public sealed class SymbolBackfillResult
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("barsDownloaded")]
    public int BarsDownloaded { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Backfill request.
/// </summary>
public sealed class BackfillRequest
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("granularity")]
    public string Granularity { get; set; } = "Daily";
}

/// <summary>
/// Tracks freshness and provenance of metrics for a specific symbol or subsystem.
/// Used to show staleness indicators and provider source badges in the UI.
/// </summary>
public sealed class MetricsFreshness
{
    /// <summary>
    /// The symbol or subsystem this freshness record applies to.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the last received data point.
    /// </summary>
    [JsonPropertyName("lastDataPointUtc")]
    public DateTimeOffset? LastDataPointUtc { get; set; }

    /// <summary>
    /// The provider that supplied the last data point.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Whether the data is considered stale (exceeded freshness threshold).
    /// </summary>
    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }

    /// <summary>
    /// Freshness state: Fresh, Warning, Stale, or NoData.
    /// </summary>
    [JsonPropertyName("freshnessState")]
    public string FreshnessState { get; set; } = "NoData";

    /// <summary>
    /// Age of the data in seconds since last update.
    /// </summary>
    [JsonPropertyName("ageSeconds")]
    public float AgeSeconds { get; set; }

    /// <summary>
    /// The configured freshness threshold in seconds.
    /// </summary>
    [JsonPropertyName("thresholdSeconds")]
    public int ThresholdSeconds { get; set; } = 60;
}

/// <summary>
/// Freshness state constants for metric staleness indicators.
/// </summary>
public static class FreshnessStates
{
    /// <summary>Data is fresh and within acceptable thresholds.</summary>
    public const string Fresh = "Fresh";

    /// <summary>Data is approaching staleness (>70% of threshold).</summary>
    public const string Warning = "Warning";

    /// <summary>Data has exceeded the staleness threshold.</summary>
    public const string Stale = "Stale";

    /// <summary>No data has been received yet.</summary>
    public const string NoData = "NoData";
}

/// <summary>
/// Storage analytics data.
/// </summary>
public sealed class StorageAnalytics
{
    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("tradeSizeBytes")]
    public long TradeSizeBytes { get; set; }

    [JsonPropertyName("depthSizeBytes")]
    public long DepthSizeBytes { get; set; }

    [JsonPropertyName("historicalSizeBytes")]
    public long HistoricalSizeBytes { get; set; }

    [JsonPropertyName("totalFileCount")]
    public int TotalFileCount { get; set; }

    [JsonPropertyName("tradeFileCount")]
    public int TradeFileCount { get; set; }

    [JsonPropertyName("depthFileCount")]
    public int DepthFileCount { get; set; }

    [JsonPropertyName("historicalFileCount")]
    public int HistoricalFileCount { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; }

    [JsonPropertyName("symbolBreakdown")]
    public StorageSymbolBreakdown[]? SymbolBreakdown { get; set; }

    [JsonPropertyName("dailyGrowthBytes")]
    public long DailyGrowthBytes { get; set; }

    [JsonPropertyName("projectedDaysUntilFull")]
    public int? ProjectedDaysUntilFull { get; set; }
}

/// <summary>
/// Per-symbol storage information for analytics breakdown.
/// </summary>
public sealed class StorageSymbolBreakdown
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("percentOfTotal")]
    public float PercentOfTotal { get; set; }

    [JsonPropertyName("oldestData")]
    public DateTimeOffset? OldestData { get; set; }

    [JsonPropertyName("newestData")]
    public DateTimeOffset? NewestData { get; set; }
}
