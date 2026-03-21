using System.Text.Json.Serialization;

namespace Meridian.Contracts.Backfill;

/// <summary>
/// Backfill job status constants.
/// Use these constants to avoid magic strings when setting or comparing job status.
/// </summary>
public static class BackfillJobStatus
{
    /// <summary>Job is queued but not yet started.</summary>
    public const string Pending = "Pending";

    /// <summary>Job is actively downloading data.</summary>
    public const string Running = "Running";

    /// <summary>Job has been temporarily paused.</summary>
    public const string Paused = "Paused";

    /// <summary>Job has successfully completed all symbols.</summary>
    public const string Completed = "Completed";

    /// <summary>Job encountered a fatal error and stopped.</summary>
    public const string Failed = "Failed";

    /// <summary>Job was cancelled by user request.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// All valid status values.
    /// </summary>
    public static readonly IReadOnlyList<string> AllStatuses = new[]
    {
        Pending, Running, Paused, Completed, Failed, Cancelled
    };

    /// <summary>
    /// Checks if the given status represents a terminal state (job is finished).
    /// </summary>
    public static bool IsTerminal(string status) =>
        status is Completed or Failed or Cancelled;

    /// <summary>
    /// Checks if the given status is valid.
    /// </summary>
    public static bool IsValid(string status) =>
        AllStatuses.Contains(status);
}

/// <summary>
/// Symbol backfill status constants.
/// Use these constants to avoid magic strings when setting or comparing symbol status.
/// </summary>
public static class SymbolBackfillStatus
{
    /// <summary>Symbol is queued for download.</summary>
    public const string Pending = "Pending";

    /// <summary>Symbol data is currently being downloaded.</summary>
    public const string Downloading = "Downloading";

    /// <summary>Symbol data has been successfully downloaded.</summary>
    public const string Completed = "Completed";

    /// <summary>Symbol download failed after all retries.</summary>
    public const string Failed = "Failed";

    /// <summary>Symbol was skipped (e.g., data already exists).</summary>
    public const string Skipped = "Skipped";

    /// <summary>
    /// All valid status values.
    /// </summary>
    public static readonly IReadOnlyList<string> AllStatuses = new[]
    {
        Pending, Downloading, Completed, Failed, Skipped
    };

    /// <summary>
    /// Checks if the given status represents a terminal state.
    /// </summary>
    public static bool IsTerminal(string status) =>
        status is Completed or Failed or Skipped;

    /// <summary>
    /// Checks if the given status is valid.
    /// </summary>
    public static bool IsValid(string status) =>
        AllStatuses.Contains(status);
}

/// <summary>
/// Tracks overall progress of a backfill job.
/// This is the primary contract for communicating backfill status to clients.
/// </summary>
/// <remarks>
/// This class is designed for JSON serialization and API responses.
/// For provider-specific progress tracking, see ProviderBackfillProgress in Infrastructure.
/// </remarks>
public sealed class BackfillProgress
{
    private string _status = BackfillJobStatus.Pending;
    private int _totalSymbols;
    private int _completedSymbols;
    private int _failedSymbols;
    private long _totalBars;
    private long _downloadedBars;

    /// <summary>
    /// Unique identifier for the backfill job.
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Current status of the job. Use <see cref="BackfillJobStatus"/> constants.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status
    {
        get => _status;
        set => _status = BackfillJobStatus.IsValid(value) ? value : throw new ArgumentException($"Invalid status: {value}", nameof(value));
    }

    /// <summary>
    /// Total number of symbols to process in this job.
    /// </summary>
    [JsonPropertyName("totalSymbols")]
    public int TotalSymbols
    {
        get => _totalSymbols;
        set => _totalSymbols = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TotalSymbols cannot be negative");
    }

    /// <summary>
    /// Number of symbols successfully completed.
    /// </summary>
    [JsonPropertyName("completedSymbols")]
    public int CompletedSymbols
    {
        get => _completedSymbols;
        set => _completedSymbols = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "CompletedSymbols cannot be negative");
    }

    /// <summary>
    /// Number of symbols that failed after all retries.
    /// </summary>
    [JsonPropertyName("failedSymbols")]
    public int FailedSymbols
    {
        get => _failedSymbols;
        set => _failedSymbols = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "FailedSymbols cannot be negative");
    }

    /// <summary>
    /// Expected total number of bars across all symbols.
    /// May be zero if not known in advance.
    /// </summary>
    [JsonPropertyName("totalBars")]
    public long TotalBars
    {
        get => _totalBars;
        set => _totalBars = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TotalBars cannot be negative");
    }

    /// <summary>
    /// Number of bars successfully downloaded so far.
    /// </summary>
    [JsonPropertyName("downloadedBars")]
    public long DownloadedBars
    {
        get => _downloadedBars;
        set => _downloadedBars = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "DownloadedBars cannot be negative");
    }

    /// <summary>
    /// Current throughput in bars per second.
    /// </summary>
    [JsonPropertyName("barsPerSecond")]
    public float BarsPerSecond { get; set; }

    /// <summary>
    /// Estimated seconds remaining based on current throughput.
    /// Null if not enough data to estimate.
    /// </summary>
    [JsonPropertyName("estimatedSecondsRemaining")]
    public int? EstimatedSecondsRemaining { get; set; }

    /// <summary>
    /// UTC timestamp when the job started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the job completed (or failed/cancelled).
    /// Null if still in progress.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Per-symbol progress details.
    /// May be null for summary-only responses.
    /// </summary>
    [JsonPropertyName("symbolProgress")]
    public SymbolBackfillProgress[]? SymbolProgress { get; set; }

    /// <summary>
    /// Name of the provider currently being used.
    /// </summary>
    [JsonPropertyName("currentProvider")]
    public string? CurrentProvider { get; set; }

    /// <summary>
    /// Error message if the job failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the overall progress percentage based on completed symbols.
    /// </summary>
    [JsonIgnore]
    public double ProgressPercent => TotalSymbols > 0 ? (double)CompletedSymbols / TotalSymbols * 100 : 0;

    /// <summary>
    /// Gets the progress percentage based on downloaded bars.
    /// </summary>
    [JsonIgnore]
    public double BarsProgressPercent => TotalBars > 0 ? (double)DownloadedBars / TotalBars * 100 : 0;

    /// <summary>
    /// Gets the elapsed time since the job started.
    /// </summary>
    [JsonIgnore]
    public TimeSpan Elapsed => DateTime.UtcNow - StartedAt;

    /// <summary>
    /// Gets whether the job is in a terminal state.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => BackfillJobStatus.IsTerminal(Status);

    /// <summary>
    /// Gets the number of remaining symbols to process.
    /// </summary>
    [JsonIgnore]
    public int RemainingSymbols => Math.Max(0, TotalSymbols - CompletedSymbols - FailedSymbols);
}

/// <summary>
/// Tracks progress of backfill operation for a single symbol.
/// </summary>
public sealed class SymbolBackfillProgress
{
    private string _status = SymbolBackfillStatus.Pending;
    private int _barsDownloaded;
    private int _expectedBars;
    private int _retryCount;

    /// <summary>
    /// The symbol being backfilled (e.g., "AAPL", "SPY").
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the symbol backfill. Use <see cref="SymbolBackfillStatus"/> constants.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status
    {
        get => _status;
        set => _status = SymbolBackfillStatus.IsValid(value) ? value : throw new ArgumentException($"Invalid status: {value}", nameof(value));
    }

    /// <summary>
    /// Progress percentage (0-100) based on bars downloaded vs expected.
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    /// <summary>
    /// Number of bars successfully downloaded for this symbol.
    /// </summary>
    [JsonPropertyName("barsDownloaded")]
    public int BarsDownloaded
    {
        get => _barsDownloaded;
        set => _barsDownloaded = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "BarsDownloaded cannot be negative");
    }

    /// <summary>
    /// Expected number of bars to download for this symbol.
    /// May be zero if not known in advance.
    /// </summary>
    [JsonPropertyName("expectedBars")]
    public int ExpectedBars
    {
        get => _expectedBars;
        set => _expectedBars = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "ExpectedBars cannot be negative");
    }

    /// <summary>
    /// UTC timestamp when the symbol backfill started.
    /// Null if not yet started.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the symbol backfill completed.
    /// Null if still in progress.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if the symbol backfill failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Name of the provider used for this symbol.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Number of retry attempts made for this symbol.
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount
    {
        get => _retryCount;
        set => _retryCount = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "RetryCount cannot be negative");
    }

    /// <summary>
    /// Gets whether the symbol backfill is in a terminal state.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => SymbolBackfillStatus.IsTerminal(Status);

    /// <summary>
    /// Gets the calculated progress percentage based on downloaded vs expected bars.
    /// </summary>
    [JsonIgnore]
    public double CalculatedProgress => ExpectedBars > 0 ? (double)BarsDownloaded / ExpectedBars * 100 : 0;

    /// <summary>
    /// Gets the duration of the symbol backfill.
    /// Null if not started or still in progress.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}
