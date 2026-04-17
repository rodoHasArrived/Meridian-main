using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Represents a backfill job that tracks the progress of historical data retrieval.
/// Jobs are persistent and can be paused/resumed across application restarts.
/// </summary>
public sealed class BackfillJob
{
    /// <summary>
    /// Unique identifier for this job.
    /// </summary>
    public string JobId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Human-readable name for the job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Symbols to backfill.
    /// </summary>
    public List<string> Symbols { get; init; } = new();

    /// <summary>
    /// Start date for backfill (inclusive).
    /// </summary>
    public DateOnly FromDate { get; init; }

    /// <summary>
    /// End date for backfill (inclusive).
    /// </summary>
    public DateOnly ToDate { get; init; }

    /// <summary>
    /// Data granularity (timeframe).
    /// </summary>
    public DataGranularity Granularity { get; init; } = DataGranularity.Daily;

    /// <summary>
    /// Preferred providers in priority order. Empty = use all available.
    /// </summary>
    public List<string> PreferredProviders { get; init; } = new();

    /// <summary>
    /// Current job status.
    /// </summary>
    public BackfillJobStatus Status { get; set; } = BackfillJobStatus.Pending;

    /// <summary>
    /// When the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the job was last started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When the job completed (or was cancelled).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When the job was last paused.
    /// </summary>
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>
    /// Total symbols to process.
    /// </summary>
    public int TotalSymbols => Symbols.Count;

    /// <summary>
    /// Total date range days.
    /// </summary>
    public int TotalDays => ToDate.DayNumber - FromDate.DayNumber + 1;

    /// <summary>
    /// Progress tracking per symbol.
    /// </summary>
    public ConcurrentDictionary<string, SymbolBackfillProgress> SymbolProgress { get; init; } = new();

    /// <summary>
    /// Errors encountered during processing.
    /// </summary>
    public ConcurrentQueue<BackfillError> Errors { get; init; } = new();

    /// <summary>
    /// Job configuration options.
    /// </summary>
    public BackfillJobOptions Options { get; set; } = new();

    /// <summary>
    /// Statistics for the job.
    /// </summary>
    public BackfillJobStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Reason for the current status (e.g., pause reason, error message).
    /// </summary>
    public string? StatusReason { get; set; }

    /// <summary>
    /// Calculate overall progress percentage.
    /// </summary>
    [JsonIgnore]
    public double ProgressPercent
    {
        get
        {
            if (SymbolProgress.IsEmpty)
                return 0;
            var totalRequests = SymbolProgress.Values.Sum(p => p.TotalRequests);
            if (totalRequests == 0)
                return 0;
            var completedRequests = SymbolProgress.Values.Sum(p => p.CompletedRequests);
            return (completedRequests * 100.0) / totalRequests;
        }
    }

    /// <summary>
    /// Check if job is in a terminal state.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => Status is BackfillJobStatus.Completed or BackfillJobStatus.Failed or BackfillJobStatus.Cancelled;

    /// <summary>
    /// Check if job can be started.
    /// </summary>
    [JsonIgnore]
    public bool CanStart => Status is BackfillJobStatus.Pending or BackfillJobStatus.Paused;

    /// <summary>
    /// Check if job can be paused.
    /// </summary>
    [JsonIgnore]
    public bool CanPause => Status is BackfillJobStatus.Running or BackfillJobStatus.RateLimited;

    /// <summary>
    /// Elapsed time since job started.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Elapsed
    {
        get
        {
            if (!StartedAt.HasValue)
                return TimeSpan.Zero;
            var endTime = CompletedAt ?? PausedAt ?? DateTimeOffset.UtcNow;
            return endTime - StartedAt.Value;
        }
    }
}

/// <summary>
/// Progress tracking for a single symbol within a backfill job.
/// </summary>
public sealed class SymbolBackfillProgress
{
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Dates that need to be backfilled (gaps detected).
    /// </summary>
    public List<DateOnly> DatesToFill { get; set; } = new();

    /// <summary>
    /// Dates that have been successfully filled.
    /// </summary>
    public HashSet<DateOnly> FilledDates { get; init; } = new();

    /// <summary>
    /// Dates that failed to fill after all retries.
    /// </summary>
    public HashSet<DateOnly> FailedDates { get; init; } = new();

    /// <summary>
    /// Total requests needed for this symbol.
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Completed requests.
    /// </summary>
    public int CompletedRequests { get; set; }

    /// <summary>
    /// Failed requests.
    /// </summary>
    public int FailedRequests { get; set; }

    /// <summary>
    /// Total bars retrieved.
    /// </summary>
    public long BarsRetrieved { get; set; }

    /// <summary>
    /// Provider that successfully provided data.
    /// </summary>
    public string? SuccessfulProvider { get; set; }

    /// <summary>
    /// Last error message if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When this symbol started processing.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When this symbol completed processing.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Current processing status.
    /// </summary>
    public SymbolBackfillStatus Status { get; set; } = SymbolBackfillStatus.Pending;

    public double ProgressPercent => TotalRequests > 0 ? (CompletedRequests * 100.0) / TotalRequests : 0;
}

/// <summary>
/// Configuration options for a backfill job.
/// </summary>
public sealed record BackfillJobOptions
{
    /// <summary>
    /// Maximum concurrent requests across all providers.
    /// </summary>
    public int MaxConcurrentRequests { get; init; } = 3;

    /// <summary>
    /// Maximum retries per failed request.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Delay between retries.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Skip existing data (check archive before requesting).
    /// </summary>
    public bool SkipExistingData { get; init; } = true;

    /// <summary>
    /// Fill gaps only (vs. overwrite existing data).
    /// </summary>
    public bool FillGapsOnly { get; init; } = true;

    /// <summary>
    /// Prefer adjusted prices when available.
    /// </summary>
    public bool PreferAdjustedPrices { get; init; } = true;

    /// <summary>
    /// Auto-pause when all providers are rate-limited.
    /// </summary>
    public bool AutoPauseOnRateLimit { get; init; } = true;

    /// <summary>
    /// Auto-resume after rate limit window expires.
    /// </summary>
    public bool AutoResumeAfterRateLimit { get; init; } = true;

    /// <summary>
    /// Maximum time to wait for rate limit reset before pausing.
    /// </summary>
    public TimeSpan MaxRateLimitWait { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Priority for this job (lower = higher priority).
    /// </summary>
    public int Priority { get; init; } = 10;

    /// <summary>
    /// Batch size for date ranges (days per request).
    /// </summary>
    public int BatchSizeDays { get; init; } = 365;
}

/// <summary>
/// Statistics for a backfill job.
/// </summary>
public sealed class BackfillJobStatistics
{
    public long TotalBarsRetrieved { get; set; }
    public long TotalBytesDownloaded { get; set; }
    public int TotalRequestsMade { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public int SkippedRequests { get; set; }
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public TimeSpan TotalWaitTime { get; set; }
    public Dictionary<string, int> RequestsByProvider { get; init; } = new();
    public Dictionary<string, int> BarsByProvider { get; init; } = new();
}

/// <summary>
/// Error information for a backfill request.
/// </summary>
public sealed record BackfillError(
    string Symbol,
    DateOnly Date,
    string Provider,
    string Message,
    DateTimeOffset Timestamp,
    int RetryCount = 0,
    bool IsRetryable = true
);

/// <summary>
/// Status of a backfill job.
/// </summary>
public enum BackfillJobStatus : byte
{
    /// <summary>Job is created but not started.</summary>
    Pending,

    /// <summary>Job is actively running.</summary>
    Running,

    /// <summary>Job is paused by user or system.</summary>
    Paused,

    /// <summary>Job is waiting for rate limit to reset.</summary>
    RateLimited,

    /// <summary>Job completed successfully.</summary>
    Completed,

    /// <summary>Job failed with unrecoverable errors.</summary>
    Failed,

    /// <summary>Job was cancelled by user.</summary>
    Cancelled
}

/// <summary>
/// Status of a symbol within a backfill job.
/// </summary>
public enum SymbolBackfillStatus : byte
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Data granularity for backfill requests.
/// </summary>
public enum DataGranularity : byte
{
    /// <summary>1-minute bars.</summary>
    Minute1,

    /// <summary>5-minute bars.</summary>
    Minute5,

    /// <summary>15-minute bars.</summary>
    Minute15,

    /// <summary>30-minute bars.</summary>
    Minute30,

    /// <summary>1-hour bars.</summary>
    Hour1,

    /// <summary>4-hour bars.</summary>
    Hour4,

    /// <summary>Daily bars (default).</summary>
    Daily,

    /// <summary>Weekly bars.</summary>
    Weekly,

    /// <summary>Monthly bars.</summary>
    Monthly
}

/// <summary>
/// Extension methods for DataGranularity.
/// </summary>
public static class DataGranularityExtensions
{
    public static bool TryParseValue(string? value, out DataGranularity granularity)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            granularity = DataGranularity.Daily;
            return false;
        }

        var normalized = value.Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        granularity = normalized.ToLowerInvariant() switch
        {
            "1m" or "1min" or "minute1" => DataGranularity.Minute1,
            "5m" or "5min" or "minute5" => DataGranularity.Minute5,
            "15m" or "15min" or "minute15" => DataGranularity.Minute15,
            "30m" or "30min" or "minute30" => DataGranularity.Minute30,
            "1h" or "1hour" or "hour1" or "hourly" => DataGranularity.Hour1,
            "4h" or "4hour" or "hour4" => DataGranularity.Hour4,
            "1d" or "1day" or "daily" => DataGranularity.Daily,
            "1w" or "1week" or "weekly" => DataGranularity.Weekly,
            "1month" or "monthly" => DataGranularity.Monthly,
            _ => default
        };

        return normalized.ToLowerInvariant() switch
        {
            "1m" or "1min" or "minute1" or
            "5m" or "5min" or "minute5" or
            "15m" or "15min" or "minute15" or
            "30m" or "30min" or "minute30" or
            "1h" or "1hour" or "hour1" or "hourly" or
            "4h" or "4hour" or "hour4" or
            "1d" or "1day" or "daily" or
            "1w" or "1week" or "weekly" or
            "1month" or "monthly" => true,
            _ => Enum.TryParse<DataGranularity>(value, ignoreCase: true, out granularity)
        };
    }

    public static DataGranularity ParseValueOrDefault(string? value, DataGranularity defaultValue = DataGranularity.Daily)
        => TryParseValue(value, out var granularity) ? granularity : defaultValue;

    public static bool IsIntraday(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 or
        DataGranularity.Minute5 or
        DataGranularity.Minute15 or
        DataGranularity.Minute30 or
        DataGranularity.Hour1 or
        DataGranularity.Hour4 => true,
        _ => false
    };

    public static string ToAlpacaTimeframe(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => "1Min",
        DataGranularity.Minute5 => "5Min",
        DataGranularity.Minute15 => "15Min",
        DataGranularity.Minute30 => "30Min",
        DataGranularity.Hour1 => "1Hour",
        DataGranularity.Hour4 => "4Hour",
        DataGranularity.Daily => "1Day",
        DataGranularity.Weekly => "1Week",
        DataGranularity.Monthly => "1Month",
        _ => "1Day"
    };

    public static TimeSpan ToTimeSpan(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => TimeSpan.FromMinutes(1),
        DataGranularity.Minute5 => TimeSpan.FromMinutes(5),
        DataGranularity.Minute15 => TimeSpan.FromMinutes(15),
        DataGranularity.Minute30 => TimeSpan.FromMinutes(30),
        DataGranularity.Hour1 => TimeSpan.FromHours(1),
        DataGranularity.Hour4 => TimeSpan.FromHours(4),
        DataGranularity.Daily => TimeSpan.FromDays(1),
        DataGranularity.Weekly => TimeSpan.FromDays(7),
        DataGranularity.Monthly => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(1)
    };

    public static string ToUiValue(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => "1Min",
        DataGranularity.Minute5 => "5Min",
        DataGranularity.Minute15 => "15Min",
        DataGranularity.Minute30 => "30Min",
        DataGranularity.Hour1 => "Hourly",
        DataGranularity.Hour4 => "4Hour",
        DataGranularity.Daily => "Daily",
        DataGranularity.Weekly => "Weekly",
        DataGranularity.Monthly => "Monthly",
        _ => "Daily"
    };

    public static string ToConfigValue(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => "minute1",
        DataGranularity.Minute5 => "minute5",
        DataGranularity.Minute15 => "minute15",
        DataGranularity.Minute30 => "minute30",
        DataGranularity.Hour1 => "hourly",
        DataGranularity.Hour4 => "hour4",
        DataGranularity.Daily => "daily",
        DataGranularity.Weekly => "weekly",
        DataGranularity.Monthly => "monthly",
        _ => "daily"
    };

    public static string ToStorageFilePrefix(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => "bar_1min",
        DataGranularity.Minute5 => "bar_5min",
        DataGranularity.Minute15 => "bar_15min",
        DataGranularity.Minute30 => "bar_30min",
        DataGranularity.Hour1 => "bar_hourly",
        DataGranularity.Hour4 => "bar_4hour",
        _ => "bar_daily"
    };

    public static string ToDisplayName(this DataGranularity granularity) => granularity switch
    {
        DataGranularity.Minute1 => "1 Minute",
        DataGranularity.Minute5 => "5 Minutes",
        DataGranularity.Minute15 => "15 Minutes",
        DataGranularity.Minute30 => "30 Minutes",
        DataGranularity.Hour1 => "1 Hour",
        DataGranularity.Hour4 => "4 Hours",
        DataGranularity.Daily => "Daily",
        DataGranularity.Weekly => "Weekly",
        DataGranularity.Monthly => "Monthly",
        _ => "Daily"
    };
}
