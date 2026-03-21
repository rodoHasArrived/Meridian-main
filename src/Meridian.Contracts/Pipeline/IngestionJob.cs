using System.Text.Json.Serialization;

namespace Meridian.Contracts.Pipeline;

/// <summary>
/// Unified ingestion job state machine that applies to both realtime and historical flows.
/// Implements a single contract for all ingestion workload types, ensuring consistent
/// status tracking, checkpoint semantics, and retry behavior.
/// </summary>
/// <remarks>
/// State transitions:
///   Draft → Queued → Running → Paused → Completed | Failed | Cancelled
///
/// Resume behavior by workload type:
///   Realtime: restart from latest stream + gap-fill window
///   Historical: resume from last committed bar cursor (checkpoint token)
/// </remarks>
public sealed class IngestionJob
{
    /// <summary>
    /// Unique identifier for this ingestion job.
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The type of ingestion workload.
    /// </summary>
    [JsonPropertyName("workloadType")]
    public IngestionWorkloadType WorkloadType { get; set; }

    /// <summary>
    /// Current state of the job in the state machine.
    /// </summary>
    [JsonPropertyName("state")]
    public IngestionJobState State { get; set; } = IngestionJobState.Draft;

    /// <summary>
    /// Symbols targeted by this job.
    /// </summary>
    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The data provider executing this job.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Optional time range start (historical workloads).
    /// </summary>
    [JsonPropertyName("fromDate")]
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Optional time range end (historical workloads).
    /// </summary>
    [JsonPropertyName("toDate")]
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// Checkpoint token enabling resume from last durable offset.
    /// For historical: last committed bar cursor (symbol/date).
    /// For realtime: latest stream offset + gap-fill window.
    /// </summary>
    [JsonPropertyName("checkpointToken")]
    public IngestionCheckpointToken? CheckpointToken { get; set; }

    /// <summary>
    /// Retry envelope tracking attempt count, schedule, and policy.
    /// </summary>
    [JsonPropertyName("retryEnvelope")]
    public RetryEnvelope RetryEnvelope { get; set; } = new();

    /// <summary>
    /// SLA expectations for this job.
    /// </summary>
    [JsonPropertyName("sla")]
    public IngestionSla? Sla { get; set; }

    /// <summary>
    /// UTC timestamp when the job was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the job started running.
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the job reached a terminal state.
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if the job failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Per-symbol progress details.
    /// </summary>
    [JsonPropertyName("symbolProgress")]
    public List<IngestionSymbolProgress> SymbolProgress { get; set; } = new();

    /// <summary>
    /// Gets whether the job is in a terminal state.
    /// </summary>
    [JsonIgnore]
    public bool IsTerminal => State is IngestionJobState.Completed
        or IngestionJobState.Failed
        or IngestionJobState.Cancelled;

    /// <summary>
    /// Gets whether the job can be resumed.
    /// </summary>
    [JsonIgnore]
    public bool IsResumable => State is IngestionJobState.Failed
        or IngestionJobState.Paused
        && CheckpointToken != null;

    /// <summary>
    /// Attempts to transition the job to a new state.
    /// Returns false if the transition is invalid.
    /// </summary>
    public bool TryTransition(IngestionJobState newState)
    {
        if (!IsValidTransition(State, newState))
            return false;

        var previousState = State;
        State = newState;

        switch (newState)
        {
            case IngestionJobState.Running:
                StartedAt ??= DateTime.UtcNow;
                break;
            case IngestionJobState.Completed:
            case IngestionJobState.Failed:
            case IngestionJobState.Cancelled:
                CompletedAt = DateTime.UtcNow;
                break;
        }

        return true;
    }

    /// <summary>
    /// Validates whether a state transition is allowed.
    /// </summary>
    public static bool IsValidTransition(IngestionJobState from, IngestionJobState to)
    {
        return (from, to) switch
        {
            (IngestionJobState.Draft, IngestionJobState.Queued) => true,
            (IngestionJobState.Queued, IngestionJobState.Running) => true,
            (IngestionJobState.Queued, IngestionJobState.Cancelled) => true,
            (IngestionJobState.Running, IngestionJobState.Paused) => true,
            (IngestionJobState.Running, IngestionJobState.Completed) => true,
            (IngestionJobState.Running, IngestionJobState.Failed) => true,
            (IngestionJobState.Running, IngestionJobState.Cancelled) => true,
            (IngestionJobState.Paused, IngestionJobState.Running) => true,
            (IngestionJobState.Paused, IngestionJobState.Cancelled) => true,
            (IngestionJobState.Failed, IngestionJobState.Queued) => true,   // retry
            _ => false
        };
    }
}

/// <summary>
/// States for the unified ingestion job state machine.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IngestionJobState : byte
{
    /// <summary>Job is being configured but not yet submitted.</summary>
    Draft,

    /// <summary>Job is submitted and waiting to run.</summary>
    Queued,

    /// <summary>Job is actively processing data.</summary>
    Running,

    /// <summary>Job has been temporarily paused by user or system.</summary>
    Paused,

    /// <summary>Job has successfully completed all work.</summary>
    Completed,

    /// <summary>Job encountered a fatal error and stopped.</summary>
    Failed,

    /// <summary>Job was cancelled by user request.</summary>
    Cancelled
}

/// <summary>
/// Type of ingestion workload.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IngestionWorkloadType : byte
{
    /// <summary>Real-time streaming data collection.</summary>
    Realtime,

    /// <summary>Historical backfill from a provider.</summary>
    Historical,

    /// <summary>Gap-fill for missing data periods.</summary>
    GapFill,

    /// <summary>Scheduled recurring backfill.</summary>
    ScheduledBackfill
}

/// <summary>
/// Checkpoint token enabling safe resume of an interrupted job.
/// </summary>
public sealed class IngestionCheckpointToken
{
    /// <summary>
    /// The symbol that was last successfully processed.
    /// </summary>
    [JsonPropertyName("lastSymbol")]
    public string? LastSymbol { get; set; }

    /// <summary>
    /// The date cursor for the last committed data point.
    /// </summary>
    [JsonPropertyName("lastDate")]
    public DateTime? LastDate { get; set; }

    /// <summary>
    /// The last durable offset (e.g., sequence number or stream position).
    /// </summary>
    [JsonPropertyName("lastOffset")]
    public long? LastOffset { get; set; }

    /// <summary>
    /// For realtime: the gap-fill window start (how far back to re-process on resume).
    /// </summary>
    [JsonPropertyName("gapFillWindowStart")]
    public DateTime? GapFillWindowStart { get; set; }

    /// <summary>
    /// UTC timestamp when this checkpoint was captured.
    /// </summary>
    [JsonPropertyName("capturedAt")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Retry envelope tracking attempt count, backoff schedule, and policy.
/// </summary>
public sealed class RetryEnvelope
{
    /// <summary>
    /// Number of retry attempts made so far.
    /// </summary>
    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; set; }

    /// <summary>
    /// Maximum number of retries allowed.
    /// </summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Scheduled time for the next retry attempt.
    /// </summary>
    [JsonPropertyName("nextRetryAt")]
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// The retry policy class name (e.g., ExponentialBackoff, Linear).
    /// </summary>
    [JsonPropertyName("policyClass")]
    public string PolicyClass { get; set; } = "ExponentialBackoff";

    /// <summary>
    /// Base delay in seconds for retry backoff calculation.
    /// </summary>
    [JsonPropertyName("baseDelaySeconds")]
    public int BaseDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Whether retries have been exhausted.
    /// </summary>
    [JsonIgnore]
    public bool IsExhausted => AttemptCount >= MaxRetries;

    /// <summary>
    /// Calculates the next retry delay using exponential backoff.
    /// </summary>
    [JsonIgnore]
    public TimeSpan NextDelay => TimeSpan.FromSeconds(
        BaseDelaySeconds * Math.Pow(2, Math.Min(AttemptCount, 10)));
}

/// <summary>
/// SLA expectations for an ingestion job.
/// </summary>
public sealed class IngestionSla
{
    /// <summary>
    /// Target data freshness in seconds (how stale data can be before violation).
    /// </summary>
    [JsonPropertyName("freshnessTargetSeconds")]
    public int FreshnessTargetSeconds { get; set; } = 60;

    /// <summary>
    /// Deadline by which the job must complete (for historical workloads).
    /// </summary>
    [JsonPropertyName("completionDeadline")]
    public DateTime? CompletionDeadline { get; set; }

    /// <summary>
    /// Minimum completeness ratio (0.0 to 1.0) required for the job to be considered successful.
    /// </summary>
    [JsonPropertyName("minimumCompleteness")]
    public float MinimumCompleteness { get; set; } = 0.95f;
}

/// <summary>
/// Per-symbol progress within an ingestion job.
/// </summary>
public sealed class IngestionSymbolProgress
{
    /// <summary>
    /// The symbol being processed.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Current state of this symbol's processing.
    /// </summary>
    [JsonPropertyName("state")]
    public IngestionJobState State { get; set; } = IngestionJobState.Queued;

    /// <summary>
    /// Number of data points (bars, ticks, etc.) processed.
    /// </summary>
    [JsonPropertyName("dataPointsProcessed")]
    public long DataPointsProcessed { get; set; }

    /// <summary>
    /// Expected total data points (if known).
    /// </summary>
    [JsonPropertyName("expectedDataPoints")]
    public long ExpectedDataPoints { get; set; }

    /// <summary>
    /// Last date/offset successfully committed.
    /// </summary>
    [JsonPropertyName("lastCommittedDate")]
    public DateTime? LastCommittedDate { get; set; }

    /// <summary>
    /// Error message if this symbol failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of retries for this specific symbol.
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    [JsonIgnore]
    public double ProgressPercent => ExpectedDataPoints > 0
        ? DataPointsProcessed * 100.0 / ExpectedDataPoints
        : 0;
}
