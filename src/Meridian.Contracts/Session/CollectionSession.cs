using System.Text.Json.Serialization;

namespace Meridian.Contracts.Session;

/// <summary>
/// Represents a discrete data collection session with comprehensive tracking.
/// </summary>
public sealed class CollectionSession
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the human-readable session name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the session description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the current session status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = SessionStatus.Pending;

    /// <summary>
    /// Gets or sets the timestamp when data collection started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when data collection ended.
    /// </summary>
    [JsonPropertyName("endedAt")]
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the array of symbols being collected.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the array of event types being collected.
    /// </summary>
    [JsonPropertyName("eventTypes")]
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the data provider name.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the session tags for categorization.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets additional notes about the session.
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the session statistics.
    /// </summary>
    [JsonPropertyName("statistics")]
    public CollectionSessionStatistics? Statistics { get; set; }

    /// <summary>
    /// Gets or sets the data quality score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("qualityScore")]
    public float QualityScore { get; set; }

    /// <summary>
    /// Gets or sets the path to the session manifest file.
    /// </summary>
    [JsonPropertyName("manifestPath")]
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the session was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when the session was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Session status constants.
/// </summary>
public static class SessionStatus
{
    /// <summary>
    /// Session is waiting to start.
    /// </summary>
    public const string Pending = "Pending";

    /// <summary>
    /// Session is actively collecting data.
    /// </summary>
    public const string Active = "Active";

    /// <summary>
    /// Session is temporarily paused.
    /// </summary>
    public const string Paused = "Paused";

    /// <summary>
    /// Session has completed successfully.
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>
    /// Session has failed due to an error.
    /// </summary>
    public const string Failed = "Failed";
}

/// <summary>
/// Statistics for a collection session.
/// </summary>
public sealed class CollectionSessionStatistics
{
    /// <summary>
    /// Gets or sets the total number of events collected.
    /// </summary>
    [JsonPropertyName("totalEvents")]
    public long TotalEvents { get; set; }

    /// <summary>
    /// Gets or sets the number of trade events collected.
    /// </summary>
    [JsonPropertyName("tradeEvents")]
    public long TradeEvents { get; set; }

    /// <summary>
    /// Gets or sets the number of quote events collected.
    /// </summary>
    [JsonPropertyName("quoteEvents")]
    public long QuoteEvents { get; set; }

    /// <summary>
    /// Gets or sets the number of market depth events collected.
    /// </summary>
    [JsonPropertyName("depthEvents")]
    public long DepthEvents { get; set; }

    /// <summary>
    /// Gets or sets the number of bar events collected.
    /// </summary>
    [JsonPropertyName("barEvents")]
    public long BarEvents { get; set; }

    /// <summary>
    /// Gets or sets the total uncompressed data size in bytes.
    /// </summary>
    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the total compressed data size in bytes.
    /// </summary>
    [JsonPropertyName("compressedBytes")]
    public long CompressedBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of data files created.
    /// </summary>
    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    /// <summary>
    /// Gets or sets the number of data gaps detected.
    /// </summary>
    [JsonPropertyName("gapsDetected")]
    public int GapsDetected { get; set; }

    /// <summary>
    /// Gets or sets the number of gaps that were filled.
    /// </summary>
    [JsonPropertyName("gapsFilled")]
    public int GapsFilled { get; set; }

    /// <summary>
    /// Gets or sets the number of sequence errors detected.
    /// </summary>
    [JsonPropertyName("sequenceErrors")]
    public int SequenceErrors { get; set; }

    /// <summary>
    /// Gets or sets the average events processed per second.
    /// </summary>
    [JsonPropertyName("eventsPerSecond")]
    public float EventsPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the compression ratio achieved.
    /// </summary>
    [JsonPropertyName("compressionRatio")]
    public float CompressionRatio { get; set; }
}

/// <summary>
/// Configuration for collection sessions.
/// </summary>
public sealed class CollectionSessionsConfig
{
    /// <summary>
    /// Gets or sets the array of collection sessions.
    /// </summary>
    [JsonPropertyName("sessions")]
    public CollectionSession[]? Sessions { get; set; }

    /// <summary>
    /// Gets or sets the currently active session identifier.
    /// </summary>
    [JsonPropertyName("activeSessionId")]
    public string? ActiveSessionId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to auto-create daily sessions.
    /// </summary>
    [JsonPropertyName("autoCreateDailySessions")]
    public bool AutoCreateDailySessions { get; set; } = true;

    /// <summary>
    /// Gets or sets the pattern for naming new sessions.
    /// </summary>
    [JsonPropertyName("sessionNamingPattern")]
    public string SessionNamingPattern { get; set; } = "{date}-{mode}";

    /// <summary>
    /// Gets or sets a value indicating whether to generate a manifest on session completion.
    /// </summary>
    [JsonPropertyName("generateManifestOnComplete")]
    public bool GenerateManifestOnComplete { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of days to retain session history.
    /// </summary>
    [JsonPropertyName("retainSessionHistory")]
    public int RetainSessionHistory { get; set; } = 365;
}
