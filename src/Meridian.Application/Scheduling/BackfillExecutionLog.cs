using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Backfill;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Records the execution history of scheduled backfill jobs.
/// Provides visibility into past runs, successes, failures, and performance metrics.
/// </summary>
public sealed class BackfillExecutionLog
{
    /// <summary>
    /// Unique identifier for this execution.
    /// </summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// ID of the schedule that triggered this execution.
    /// </summary>
    public string ScheduleId { get; init; } = string.Empty;

    /// <summary>
    /// Name of the schedule for display purposes.
    /// </summary>
    public string ScheduleName { get; init; } = string.Empty;

    /// <summary>
    /// ID of the backfill job created for this execution.
    /// </summary>
    public string? JobId { get; set; }

    /// <summary>
    /// How this execution was triggered.
    /// </summary>
    public ExecutionTrigger Trigger { get; init; } = ExecutionTrigger.Scheduled;

    /// <summary>
    /// Current status of the execution.
    /// </summary>
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;

    /// <summary>
    /// When the execution was scheduled to run.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the execution actually started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the execution.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    /// <summary>
    /// Date range that was backfilled.
    /// </summary>
    public DateOnly FromDate { get; set; }

    /// <summary>
    /// End date of the backfill range.
    /// </summary>
    public DateOnly ToDate { get; set; }

    /// <summary>
    /// Symbols that were processed.
    /// </summary>
    public List<string> Symbols { get; init; } = new();

    /// <summary>
    /// Detailed statistics from the execution.
    /// </summary>
    public ExecutionStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Error message if the execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace for debugging (only for failures).
    /// </summary>
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// Warnings generated during execution.
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    /// Provider usage breakdown.
    /// </summary>
    public Dictionary<string, ProviderUsageStats> ProviderStats { get; init; } = new();

    /// <summary>
    /// Per-symbol results.
    /// </summary>
    public ConcurrentDictionary<string, SymbolExecutionResult> SymbolResults { get; init; } = new();

    /// <summary>
    /// Auto-remediation trigger reason when execution was system-triggered.
    /// </summary>
    public string? AutoRemediationTriggerReason { get; set; }

    /// <summary>
    /// Attempt count for the same auto-remediation idempotency key.
    /// </summary>
    public int AutoRemediationAttemptCount { get; set; }

    /// <summary>
    /// Last known remediation outcome used by observability surfaces.
    /// </summary>
    public string? AutoRemediationLastOutcome { get; set; }

    /// <summary>
    /// Idempotency key (symbol+range+provider) used to suppress duplicates.
    /// </summary>
    public string? AutoRemediationIdempotencyKey { get; set; }

    /// <summary>
    /// Typed SLA metadata when this execution was created by auto gap remediation.
    /// Null for non-remediation executions; legacy remediation entries carry the same
    /// data as key=value strings in <see cref="Warnings"/> instead.
    /// </summary>
    public BackfillRemediationSlaMetadata? AutoRemediationSla { get; set; }
}

/// <summary>
/// How a scheduled execution was triggered.
/// </summary>
public enum ExecutionTrigger : byte
{
    /// <summary>Triggered by cron schedule.</summary>
    Scheduled,

    /// <summary>Manually triggered by user.</summary>
    Manual,

    /// <summary>Triggered by API call.</summary>
    Api,

    /// <summary>Retry of a failed execution.</summary>
    Retry,

    /// <summary>System startup catch-up.</summary>
    CatchUp,

    /// <summary>Triggered automatically by quality/gap remediation policy.</summary>
    AutoRemediation
}

/// <summary>
/// Status of a scheduled execution.
/// </summary>
public enum ExecutionStatus : byte
{
    /// <summary>Execution is queued.</summary>
    Pending,

    /// <summary>Execution is running.</summary>
    Running,

    /// <summary>Execution completed successfully.</summary>
    Completed,

    /// <summary>Execution completed with some failures.</summary>
    PartialSuccess,

    /// <summary>Execution failed.</summary>
    Failed,

    /// <summary>Execution was cancelled.</summary>
    Cancelled,

    /// <summary>Execution was skipped (e.g., already running).</summary>
    Skipped
}

/// <summary>
/// Statistics from a scheduled execution.
/// </summary>
public sealed class ExecutionStatistics
{
    public int TotalSymbols { get; set; }
    public int SuccessfulSymbols { get; set; }
    public int FailedSymbols { get; set; }
    public int SkippedSymbols { get; set; }

    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public int GapsRemaining => GapsDetected - GapsFilled;

    public long TotalBarsRetrieved { get; set; }
    public long TotalBytesDownloaded { get; set; }

    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public int RateLimitedRequests { get; set; }

    public double AverageRequestDurationMs { get; set; }
    public double MaxRequestDurationMs { get; set; }

    /// <summary>
    /// Success rate (0.0 - 1.0).
    /// </summary>
    public double SuccessRate => TotalRequests > 0
        ? (double)SuccessfulRequests / TotalRequests
        : 0;
}

/// <summary>
/// Provider usage statistics for an execution.
/// </summary>
public sealed class ProviderUsageStats
{
    public string ProviderName { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int RateLimitHits { get; set; }
    public long BarsRetrieved { get; set; }
    public long BytesDownloaded { get; set; }
    public double AverageLatencyMs { get; set; }
    public bool WasPrimary { get; set; }
}

/// <summary>
/// Execution result for a single symbol.
/// </summary>
public sealed class SymbolExecutionResult
{
    public string Symbol { get; set; } = string.Empty;
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public long BarsRetrieved { get; set; }
    public string? Provider { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Manager for querying and analyzing execution history.
/// </summary>
public sealed class BackfillExecutionHistory
{
    private static readonly JsonSerializerOptions PersistenceJsonOptions = CreatePersistenceJsonOptions();
    private readonly ConcurrentDictionary<string, BackfillExecutionLog> _executions = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly string? _persistencePath;
    private readonly ILogger _log = LoggingSetup.ForContext<BackfillExecutionHistory>();
    private const int MaxHistoryEntries = 1000;

    /// <summary>
    /// Creates an in-memory history. Retained for focused tests and callers that do not opt into
    /// restart durability.
    /// </summary>
    public BackfillExecutionHistory()
    {
    }

    /// <summary>
    /// Creates a bounded history backed by an atomically replaced JSON snapshot.
    /// </summary>
    public BackfillExecutionHistory(string persistencePath)
    {
        if (string.IsNullOrWhiteSpace(persistencePath))
            throw new ArgumentException("A persistence path is required.", nameof(persistencePath));

        _persistencePath = Path.GetFullPath(persistencePath);
        LoadPersistedSnapshot();
    }

    /// <summary>
    /// Add an execution to the history.
    /// </summary>
    public void AddExecution(BackfillExecutionLog execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        lock (_lock)
        {
            _executions[execution.ExecutionId] = execution;
            TrimToBoundLocked();
            PersistSnapshotLocked();
        }
    }

    /// <summary>
    /// Replaces and persists a previously added execution after mutable terminal fields change.
    /// </summary>
    public void UpdateExecution(BackfillExecutionLog execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        lock (_lock)
        {
            _executions[execution.ExecutionId] = execution;
            TrimToBoundLocked();
            PersistSnapshotLocked();
        }
    }

    /// <summary>
    /// Get an execution by ID.
    /// </summary>
    public BackfillExecutionLog? GetExecution(string executionId)
    {
        return _executions.TryGetValue(executionId, out var log) ? log : null;
    }

    /// <summary>
    /// Get all executions for a schedule.
    /// </summary>
    public IReadOnlyList<BackfillExecutionLog> GetExecutionsForSchedule(string scheduleId, int limit = 50)
    {
        return _executions.Values
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.ScheduledAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get recent executions across all schedules.
    /// </summary>
    public IReadOnlyList<BackfillExecutionLog> GetRecentExecutions(int limit = 50)
    {
        return _executions.Values
            .OrderByDescending(e => e.ScheduledAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get failed executions.
    /// </summary>
    public IReadOnlyList<BackfillExecutionLog> GetFailedExecutions(int limit = 50)
    {
        return _executions.Values
            .Where(e => e.Status == ExecutionStatus.Failed)
            .OrderByDescending(e => e.ScheduledAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get executions within a time range.
    /// </summary>
    public IReadOnlyList<BackfillExecutionLog> GetExecutionsInRange(
        DateTimeOffset from,
        DateTimeOffset to,
        int limit = 100)
    {
        return _executions.Values
            .Where(e => e.ScheduledAt >= from && e.ScheduledAt <= to)
            .OrderByDescending(e => e.ScheduledAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get aggregate statistics for a schedule.
    /// </summary>
    public ScheduleExecutionSummary GetScheduleSummary(string scheduleId, int recentCount = 30)
    {
        var executions = GetExecutionsForSchedule(scheduleId, recentCount);

        return new ScheduleExecutionSummary
        {
            ScheduleId = scheduleId,
            TotalExecutions = executions.Count,
            SuccessfulExecutions = executions.Count(e => e.Status == ExecutionStatus.Completed),
            FailedExecutions = executions.Count(e => e.Status == ExecutionStatus.Failed),
            PartialSuccessExecutions = executions.Count(e => e.Status == ExecutionStatus.PartialSuccess),
            AverageDurationMinutes = executions
                .Where(e => e.Duration.HasValue)
                .Select(e => e.Duration!.Value.TotalMinutes)
                .DefaultIfEmpty(0)
                .Average(),
            TotalBarsRetrieved = executions.Sum(e => e.Statistics.TotalBarsRetrieved),
            TotalGapsFilled = executions.Sum(e => e.Statistics.GapsFilled),
            LastExecution = executions.FirstOrDefault()?.ScheduledAt,
            LastSuccessfulExecution = executions
                .FirstOrDefault(e => e.Status == ExecutionStatus.Completed)?.CompletedAt
        };
    }

    /// <summary>
    /// Get overall system statistics.
    /// </summary>
    public SystemExecutionSummary GetSystemSummary(TimeSpan? period = null)
    {
        var cutoff = period.HasValue
            ? DateTimeOffset.UtcNow - period.Value
            : DateTimeOffset.MinValue;

        var executions = _executions.Values
            .Where(e => e.ScheduledAt >= cutoff)
            .ToList();

        return new SystemExecutionSummary
        {
            Period = period ?? TimeSpan.MaxValue,
            TotalExecutions = executions.Count,
            CompletedExecutions = executions.Count(e => e.Status == ExecutionStatus.Completed),
            FailedExecutions = executions.Count(e => e.Status == ExecutionStatus.Failed),
            SuccessRate = executions.Count > 0
                ? (double)executions.Count(e => e.Status == ExecutionStatus.Completed) / executions.Count
                : 0,
            TotalBarsRetrieved = executions.Sum(e => e.Statistics.TotalBarsRetrieved),
            TotalGapsFilled = executions.Sum(e => e.Statistics.GapsFilled),
            AverageDurationMinutes = executions
                .Where(e => e.Duration.HasValue)
                .Select(e => e.Duration!.Value.TotalMinutes)
                .DefaultIfEmpty(0)
                .Average(),
            ProviderUsage = executions
                .SelectMany(e => e.ProviderStats.Values)
                .GroupBy(p => p.ProviderName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(p => p.RequestCount))
        };
    }

    private void LoadPersistedSnapshot()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
            return;

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var snapshot = JsonSerializer.Deserialize<BackfillExecutionHistorySnapshot>(
                json,
                PersistenceJsonOptions);
            foreach (var execution in (snapshot?.Executions ?? [])
                         .OfType<BackfillExecutionLog>()
                         .OrderByDescending(static execution => execution.ScheduledAt)
                         .Take(MaxHistoryEntries))
            {
                _executions[execution.ExecutionId] = execution;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warning(ex, "Could not load persisted backfill execution history from {Path}", _persistencePath);
        }
    }

    private void TrimToBoundLocked()
    {
        var excess = _executions.Count - MaxHistoryEntries;
        if (excess <= 0)
            return;

        var toRemove = _executions.Values
            .OrderBy(static execution => execution.ScheduledAt)
            .Take(excess)
            .Select(static execution => execution.ExecutionId)
            .ToArray();
        foreach (var id in toRemove)
            _executions.TryRemove(id, out _);
    }

    private void PersistSnapshotLocked()
    {
        if (_persistencePath is null)
            return;

        try
        {
            var snapshot = new BackfillExecutionHistorySnapshot(
                Version: 1,
                Executions: _executions.Values
                    .OrderByDescending(static execution => execution.ScheduledAt)
                    .Take(MaxHistoryEntries)
                    .ToArray());
            var json = JsonSerializer.Serialize(snapshot, PersistenceJsonOptions);
            AtomicFileWriter.Write(_persistencePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not persist backfill execution history to {Path}", _persistencePath);
        }
    }

    private static JsonSerializerOptions CreatePersistenceJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record BackfillExecutionHistorySnapshot(
        int Version,
        IReadOnlyList<BackfillExecutionLog> Executions);
}

/// <summary>
/// Summary statistics for a single schedule.
/// </summary>
public sealed record ScheduleExecutionSummary
{
    public string ScheduleId { get; init; } = string.Empty;
    public int TotalExecutions { get; init; }
    public int SuccessfulExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public int PartialSuccessExecutions { get; init; }
    public double AverageDurationMinutes { get; init; }
    public long TotalBarsRetrieved { get; init; }
    public int TotalGapsFilled { get; init; }
    public DateTimeOffset? LastExecution { get; init; }
    public DateTimeOffset? LastSuccessfulExecution { get; init; }
    public double SuccessRate => TotalExecutions > 0
        ? (double)SuccessfulExecutions / TotalExecutions
        : 0;
}

/// <summary>
/// System-wide execution summary.
/// </summary>
public sealed record SystemExecutionSummary
{
    public TimeSpan Period { get; init; }
    public int TotalExecutions { get; init; }
    public int CompletedExecutions { get; init; }
    public int FailedExecutions { get; init; }
    public double SuccessRate { get; init; }
    public long TotalBarsRetrieved { get; init; }
    public int TotalGapsFilled { get; init; }
    public double AverageDurationMinutes { get; init; }
    public Dictionary<string, int> ProviderUsage { get; init; } = new();
}
