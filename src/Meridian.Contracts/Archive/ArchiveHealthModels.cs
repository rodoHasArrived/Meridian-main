using System.Text.Json.Serialization;

namespace Meridian.Contracts.Archive;

/// <summary>
/// Archive health status severity constants.
/// </summary>
public static class ArchiveHealthSeverity
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
}

/// <summary>
/// Archive health status constants.
/// </summary>
public static class ArchiveHealthStatusValues
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
    public const string Unknown = "Unknown";
}

/// <summary>
/// Archive issue category constants.
/// </summary>
public static class ArchiveIssueCategory
{
    public const string Integrity = "Integrity";
    public const string Completeness = "Completeness";
    public const string Storage = "Storage";
    public const string Performance = "Performance";
}

/// <summary>
/// Verification job type constants.
/// </summary>
public static class VerificationJobType
{
    public const string Full = "Full";
    public const string Incremental = "Incremental";
    public const string Selective = "Selective";
}

/// <summary>
/// Verification job status constants.
/// </summary>
public static class VerificationJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// Archive health status and metrics.
/// </summary>
public sealed class ArchiveHealthStatus
{
    [JsonPropertyName("overallHealthScore")]
    public float OverallHealthScore { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = ArchiveHealthStatusValues.Unknown;

    [JsonPropertyName("lastFullVerificationAt")]
    public DateTime? LastFullVerificationAt { get; set; }

    [JsonPropertyName("lastVerificationDurationMinutes")]
    public int? LastVerificationDurationMinutes { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("verifiedFiles")]
    public int VerifiedFiles { get; set; }

    [JsonPropertyName("pendingFiles")]
    public int PendingFiles { get; set; }

    [JsonPropertyName("failedFiles")]
    public int FailedFiles { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("verifiedSizeBytes")]
    public long VerifiedSizeBytes { get; set; }

    [JsonPropertyName("issues")]
    public ArchiveIssue[]? Issues { get; set; }

    [JsonPropertyName("recommendations")]
    public string[]? Recommendations { get; set; }

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("storageHealthInfo")]
    public StorageHealthInfo? StorageHealthInfo { get; set; }
}

/// <summary>
/// An issue detected in the archive.
/// </summary>
public sealed class ArchiveIssue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = ArchiveHealthSeverity.Warning;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("affectedFiles")]
    public string[]? AffectedFiles { get; set; }

    [JsonPropertyName("affectedSymbols")]
    public string[]? AffectedSymbols { get; set; }

    [JsonPropertyName("suggestedAction")]
    public string? SuggestedAction { get; set; }

    [JsonPropertyName("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonPropertyName("isAutoFixable")]
    public bool IsAutoFixable { get; set; }
}

/// <summary>
/// Storage media health information.
/// </summary>
public sealed class StorageHealthInfo
{
    [JsonPropertyName("driveType")]
    public string DriveType { get; set; } = "Unknown";

    [JsonPropertyName("healthStatus")]
    public string HealthStatus { get; set; } = "Unknown";

    [JsonPropertyName("totalCapacity")]
    public long TotalCapacity { get; set; }

    [JsonPropertyName("freeSpace")]
    public long FreeSpace { get; set; }

    [JsonPropertyName("usedPercent")]
    public float UsedPercent { get; set; }

    [JsonPropertyName("averageWriteLatencyMs")]
    public float? AverageWriteLatencyMs { get; set; }

    [JsonPropertyName("readSpeedMbps")]
    public float? ReadSpeedMbps { get; set; }

    [JsonPropertyName("writeSpeedMbps")]
    public float? WriteSpeedMbps { get; set; }

    [JsonPropertyName("daysUntilFull")]
    public int? DaysUntilFull { get; set; }
}

/// <summary>
/// Verification job status.
/// </summary>
public sealed class VerificationJob
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = VerificationJobType.Full;

    [JsonPropertyName("status")]
    public string Status { get; set; } = VerificationJobStatus.Pending;

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("processedFiles")]
    public int ProcessedFiles { get; set; }

    [JsonPropertyName("verifiedFiles")]
    public int VerifiedFiles { get; set; }

    [JsonPropertyName("failedFiles")]
    public int FailedFiles { get; set; }

    [JsonPropertyName("progressPercent")]
    public float ProgressPercent { get; set; }

    [JsonPropertyName("estimatedTimeRemainingSeconds")]
    public int? EstimatedTimeRemainingSeconds { get; set; }

    [JsonPropertyName("filesPerSecond")]
    public float FilesPerSecond { get; set; }

    [JsonPropertyName("errors")]
    public string[]? Errors { get; set; }
}

/// <summary>
/// Scheduled verification configuration.
/// </summary>
public sealed class VerificationScheduleConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("dailyVerificationTime")]
    public string DailyVerificationTime { get; set; } = "03:00";

    [JsonPropertyName("dailyVerificationScope")]
    public string DailyVerificationScope { get; set; } = "Last7Days";

    [JsonPropertyName("weeklyFullVerificationDay")]
    public int WeeklyFullVerificationDay { get; set; } = 0;

    [JsonPropertyName("monthlyFullVerificationDay")]
    public int MonthlyFullVerificationDay { get; set; } = 1;

    [JsonPropertyName("pauseDuringMarketHours")]
    public bool PauseDuringMarketHours { get; set; } = true;

    [JsonPropertyName("maxConcurrentVerifications")]
    public int MaxConcurrentVerifications { get; set; } = 4;
}
