using System.Text.Json.Serialization;
using Meridian.Core.Scheduling;

namespace Meridian.Storage.Maintenance;

/// <summary>
/// Represents a scheduled archive maintenance configuration with cron-like scheduling.
/// Supports health checks, cleanup, compression, tier migration, and repair operations.
/// </summary>
public sealed class ArchiveMaintenanceSchedule
{
    /// <summary>
    /// Unique identifier for this schedule.
    /// </summary>
    public string ScheduleId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Human-readable name for the schedule.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this schedule does.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this schedule is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron expression for scheduling (standard 5-field cron format).
    /// Examples: "0 3 * * *" (daily at 3am), "0 4 * * 0" (weekly on Sunday at 4am)
    /// </summary>
    public string CronExpression { get; set; } = "0 3 * * *";

    /// <summary>
    /// Timezone for cron expression evaluation.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Type of maintenance operation to perform.
    /// </summary>
    public MaintenanceTaskType TaskType { get; set; } = MaintenanceTaskType.HealthCheck;

    /// <summary>
    /// Configuration options for this maintenance task.
    /// </summary>
    public MaintenanceTaskOptions Options { get; set; } = new();

    /// <summary>
    /// Paths to include in maintenance. Empty = use configured storage root.
    /// </summary>
    public List<string> TargetPaths { get; init; } = new();

    /// <summary>
    /// Priority level for this maintenance task.
    /// </summary>
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;

    /// <summary>
    /// Maximum duration for the maintenance operation before timeout.
    /// </summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Number of retries if maintenance fails.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Whether to send notifications on completion/failure.
    /// </summary>
    public bool NotifyOnCompletion { get; set; } = false;

    /// <summary>
    /// Whether to send notifications on failure only.
    /// </summary>
    public bool NotifyOnFailureOnly { get; set; } = true;

    /// <summary>
    /// When the schedule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the schedule was last modified.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the schedule last executed.
    /// </summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>
    /// When the schedule is next scheduled to execute.
    /// </summary>
    public DateTimeOffset? NextExecutionAt { get; set; }

    /// <summary>
    /// ID of the last execution for this schedule.
    /// </summary>
    public string? LastExecutionId { get; set; }

    /// <summary>
    /// Status of the last execution.
    /// </summary>
    public MaintenanceExecutionStatus? LastExecutionStatus { get; set; }

    /// <summary>
    /// Total number of times this schedule has executed.
    /// </summary>
    public int ExecutionCount { get; set; }

    /// <summary>
    /// Number of successful executions.
    /// </summary>
    public int SuccessfulExecutions { get; set; }

    /// <summary>
    /// Number of failed executions.
    /// </summary>
    public int FailedExecutions { get; set; }

    /// <summary>
    /// Tags for categorization and filtering.
    /// </summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// Gets the timezone info for cron evaluation.
    /// </summary>
    [JsonIgnore]
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>
    /// Calculate the next execution time based on the cron expression.
    /// </summary>
    public DateTimeOffset? CalculateNextExecution(DateTimeOffset? from = null)
    {
        var baseTime = from ?? DateTimeOffset.UtcNow;
        return CronExpressionParser.GetNextOccurrence(CronExpression, TimeZone, baseTime);
    }
}

/// <summary>
/// Type of maintenance operation.
/// </summary>
public enum MaintenanceTaskType : byte
{
    /// <summary>
    /// Run health checks on storage files.
    /// </summary>
    HealthCheck,

    /// <summary>
    /// Clean up orphaned and temporary files.
    /// </summary>
    Cleanup,

    /// <summary>
    /// Defragment small files into larger chunks.
    /// </summary>
    Defragmentation,

    /// <summary>
    /// Migrate files between storage tiers based on age.
    /// </summary>
    TierMigration,

    /// <summary>
    /// Recompress files with optimal compression settings.
    /// </summary>
    Compression,

    /// <summary>
    /// Repair corrupted or truncated files.
    /// </summary>
    Repair,

    /// <summary>
    /// Full maintenance: health check, cleanup, defrag, and tier migration.
    /// </summary>
    FullMaintenance,

    /// <summary>
    /// Verify and repair file integrity (checksums).
    /// </summary>
    IntegrityCheck,

    /// <summary>
    /// Archive old data to cold storage.
    /// </summary>
    Archival,

    /// <summary>
    /// Enforce retention policies and delete expired data.
    /// </summary>
    RetentionEnforcement
}

/// <summary>
/// Priority level for maintenance tasks.
/// </summary>
public enum MaintenancePriority : byte
{
    /// <summary>Critical - execute immediately, skip queue.</summary>
    Critical = 0,

    /// <summary>High - process before normal tasks.</summary>
    High = 10,

    /// <summary>Normal - default priority.</summary>
    Normal = 50,

    /// <summary>Low - process when system is less busy.</summary>
    Low = 100,

    /// <summary>Background - only run when completely idle.</summary>
    Background = 200
}

/// <summary>
/// Configuration options for maintenance tasks.
/// </summary>
public sealed class MaintenanceTaskOptions
{
    // Health check options
    public bool ValidateChecksums { get; set; } = true;
    public bool CheckSequenceContinuity { get; set; } = true;
    public bool IdentifyCorruption { get; set; } = true;
    public bool CheckFilePermissions { get; set; } = true;
    public int ParallelOperations { get; set; } = 4;

    // Cleanup options
    public bool DeleteOrphans { get; set; } = false;
    public bool DeleteTemporaryFiles { get; set; } = true;
    public bool DeleteEmptyDirectories { get; set; } = true;
    public int OrphanAgeDays { get; set; } = 7;

    // Defragmentation options
    public long MinFileSizeBytes { get; set; } = 1_048_576; // 1 MB
    public int MaxFilesPerMerge { get; set; } = 100;
    public int FileAgeDaysThreshold { get; set; } = 1;

    // Tier migration options
    public bool DryRun { get; set; } = false;
    public bool DeleteSourceAfterMigration { get; set; } = false;
    public bool VerifyAfterMigration { get; set; } = true;
    public int MaxMigrationsPerRun { get; set; } = 250;
    public long? MaxMigrationBytesPerRun { get; set; } = 2L * 1024 * 1024 * 1024;
    public bool RunOnlyDuringMarketClosedHours { get; set; } = true;
    public string MarketTimeZoneId { get; set; } = "America/New_York";
    public TimeSpan MarketOpenTime { get; set; } = new(9, 30, 0);
    public TimeSpan MarketCloseTime { get; set; } = new(16, 0, 0);

    // Compression options
    public string? TargetCompressionCodec { get; set; }
    public int? CompressionLevel { get; set; }
    public bool RecompressExisting { get; set; } = false;

    // Repair options
    public bool BackupBeforeRepair { get; set; } = true;
    public string? BackupPath { get; set; }
    public bool TruncateCorrupted { get; set; } = true;

    // Retention options
    public int? OverrideRetentionDays { get; set; }
    public bool SkipCriticalData { get; set; } = true;
}

/// <summary>
/// Record of a maintenance execution.
/// </summary>
public sealed class MaintenanceExecution
{
    /// <summary>
    /// Unique identifier for this execution.
    /// </summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// ID of the schedule that triggered this execution (null for manual runs).
    /// </summary>
    public string? ScheduleId { get; init; }

    /// <summary>
    /// Name of the schedule (for display purposes).
    /// </summary>
    public string? ScheduleName { get; init; }

    /// <summary>
    /// Type of maintenance performed.
    /// </summary>
    public MaintenanceTaskType TaskType { get; init; }

    /// <summary>
    /// Current status of the execution.
    /// </summary>
    public MaintenanceExecutionStatus Status { get; set; } = MaintenanceExecutionStatus.Pending;

    /// <summary>
    /// Whether this was a manual trigger (vs scheduled).
    /// </summary>
    public bool ManualTrigger { get; init; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Duration of the execution.
    /// </summary>
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;

    /// <summary>
    /// Number of files processed.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Number of files with issues found.
    /// </summary>
    public int IssuesFound { get; set; }

    /// <summary>
    /// Number of issues resolved/repaired.
    /// </summary>
    public int IssuesResolved { get; set; }

    /// <summary>
    /// Bytes processed during this execution.
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Bytes saved (through compression, cleanup, etc.).
    /// </summary>
    public long BytesSaved { get; set; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed log messages from the execution.
    /// </summary>
    public List<string> LogMessages { get; init; } = new();

    /// <summary>
    /// Summary result of the maintenance operation.
    /// </summary>
    public MaintenanceResult? Result { get; set; }
}

/// <summary>
/// Status of a maintenance execution.
/// </summary>
public enum MaintenanceExecutionStatus : byte
{
    /// <summary>Execution is pending (queued).</summary>
    Pending,

    /// <summary>Execution is currently running.</summary>
    Running,

    /// <summary>Execution completed successfully.</summary>
    Completed,

    /// <summary>Execution completed with warnings.</summary>
    CompletedWithWarnings,

    /// <summary>Execution failed.</summary>
    Failed,

    /// <summary>Execution was cancelled.</summary>
    Cancelled,

    /// <summary>Execution timed out.</summary>
    TimedOut
}

/// <summary>
/// Summary result of a maintenance operation.
/// </summary>
public sealed class MaintenanceResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int FilesProcessed { get; init; }
    public int FilesSkipped { get; init; }
    public int FilesFailed { get; init; }
    public long TotalBytesScanned { get; init; }
    public long BytesSaved { get; init; }
    public int IssuesFound { get; init; }
    public int IssuesResolved { get; init; }
    public List<MaintenanceIssue> Issues { get; init; } = new();
    public Dictionary<string, object> Metrics { get; init; } = new();
}

/// <summary>
/// An issue found during maintenance.
/// </summary>
public sealed record MaintenanceIssue(
    string Path,
    string IssueType,
    string Description,
    string Severity,
    bool WasResolved,
    string? ResolutionAction = null
);

/// <summary>
/// Preset schedule templates for common maintenance scenarios.
/// </summary>
public static class MaintenanceSchedulePresets
{
    /// <summary>
    /// Daily health check at 3 AM UTC.
    /// </summary>
    public static ArchiveMaintenanceSchedule DailyHealthCheck(string name) => new()
    {
        Name = name,
        Description = "Daily health check to identify storage issues",
        CronExpression = "0 3 * * *",
        TaskType = MaintenanceTaskType.HealthCheck,
        Priority = MaintenancePriority.Normal,
        Options = new MaintenanceTaskOptions
        {
            ValidateChecksums = true,
            IdentifyCorruption = true,
            ParallelOperations = 4
        }
    };

    /// <summary>
    /// Weekly full maintenance on Sunday at 2 AM UTC.
    /// </summary>
    public static ArchiveMaintenanceSchedule WeeklyFullMaintenance(string name) => new()
    {
        Name = name,
        Description = "Weekly comprehensive maintenance including cleanup, defrag, and tier migration",
        CronExpression = "0 2 * * 0",
        TaskType = MaintenanceTaskType.FullMaintenance,
        Priority = MaintenancePriority.Low,
        MaxDuration = TimeSpan.FromHours(4),
        Options = new MaintenanceTaskOptions
        {
            DeleteOrphans = true,
            DeleteTemporaryFiles = true,
            DeleteEmptyDirectories = true,
            DryRun = false
        }
    };

    /// <summary>
    /// Daily tier migration during US market-closed hours.
    /// </summary>
    public static ArchiveMaintenanceSchedule DailyTierMigration(string name) => new()
    {
        Name = name,
        Description = "Daily incremental tier migration to move aging data to colder storage",
        CronExpression = "0 1 * * 1-5",
        TimeZoneId = "America/New_York",
        TaskType = MaintenanceTaskType.TierMigration,
        Priority = MaintenancePriority.Normal,
        Options = new MaintenanceTaskOptions
        {
            DeleteSourceAfterMigration = true,
            VerifyAfterMigration = true,
            ParallelOperations = 4,
            MaxMigrationsPerRun = 250,
            MaxMigrationBytesPerRun = 2L * 1024 * 1024 * 1024,
            RunOnlyDuringMarketClosedHours = true,
            MarketTimeZoneId = "America/New_York"
        }
    };

    /// <summary>
    /// Monthly compression optimization on the first Sunday at 1 AM UTC.
    /// </summary>
    public static ArchiveMaintenanceSchedule MonthlyCompression(string name) => new()
    {
        Name = name,
        Description = "Monthly recompression with optimal settings for cold data",
        CronExpression = "0 1 1-7 * 0",
        TaskType = MaintenanceTaskType.Compression,
        Priority = MaintenancePriority.Background,
        MaxDuration = TimeSpan.FromHours(6),
        Options = new MaintenanceTaskOptions
        {
            TargetCompressionCodec = "zstd",
            CompressionLevel = 19,
            RecompressExisting = true
        }
    };

    /// <summary>
    /// Daily retention enforcement at 5 AM UTC.
    /// </summary>
    public static ArchiveMaintenanceSchedule DailyRetentionEnforcement(string name) => new()
    {
        Name = name,
        Description = "Daily enforcement of retention policies",
        CronExpression = "0 5 * * *",
        TaskType = MaintenanceTaskType.RetentionEnforcement,
        Priority = MaintenancePriority.Normal,
        Options = new MaintenanceTaskOptions
        {
            SkipCriticalData = true,
            DryRun = false
        }
    };
}

/// <summary>
/// Statistics summary for archive maintenance.
/// </summary>
public sealed record MaintenanceStatistics(
    DateTimeOffset GeneratedAt,
    int TotalSchedules,
    int EnabledSchedules,
    int DisabledSchedules,
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    int ExecutionsLast24Hours,
    int ExecutionsLast7Days,
    long TotalBytesProcessed,
    long TotalBytesSaved,
    int TotalIssuesFound,
    int TotalIssuesResolved,
    TimeSpan AverageExecutionDuration,
    DateTimeOffset? LastExecutionAt,
    DateTimeOffset? NextScheduledExecution
);

/// <summary>
/// Summary of a specific schedule's execution history.
/// </summary>
public sealed record ScheduleExecutionSummary(
    string ScheduleId,
    string ScheduleName,
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    double SuccessRate,
    TimeSpan AverageDuration,
    DateTimeOffset? LastExecutionAt,
    MaintenanceExecutionStatus? LastStatus,
    DateTimeOffset? NextScheduledAt,
    List<MaintenanceExecution> RecentExecutions
);
