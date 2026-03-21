namespace Meridian.Storage.Maintenance;

/// <summary>
/// Interface for archive maintenance service that orchestrates scheduled and on-demand maintenance operations.
/// </summary>
public interface IArchiveMaintenanceService
{
    /// <summary>
    /// Whether the maintenance service is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Number of maintenance executions currently queued.
    /// </summary>
    int QueuedExecutions { get; }

    /// <summary>
    /// Currently running execution (if any).
    /// </summary>
    MaintenanceExecution? CurrentExecution { get; }

    /// <summary>
    /// Execute a maintenance task immediately.
    /// </summary>
    /// <param name="taskType">Type of maintenance to perform.</param>
    /// <param name="options">Options for the maintenance task.</param>
    /// <param name="targetPaths">Specific paths to target (null = use defaults).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The execution record with results.</returns>
    Task<MaintenanceExecution> ExecuteMaintenanceAsync(
        MaintenanceTaskType taskType,
        MaintenanceTaskOptions? options = null,
        string[]? targetPaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Trigger a scheduled maintenance to run immediately.
    /// </summary>
    /// <param name="scheduleId">ID of the schedule to trigger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The execution record.</returns>
    Task<MaintenanceExecution> TriggerScheduleAsync(string scheduleId, CancellationToken ct = default);

    /// <summary>
    /// Cancel a running or queued maintenance execution.
    /// </summary>
    /// <param name="executionId">ID of the execution to cancel.</param>
    /// <returns>True if cancellation was successful.</returns>
    Task<bool> CancelExecutionAsync(string executionId);

    /// <summary>
    /// Get the current status of the maintenance service.
    /// </summary>
    MaintenanceServiceStatus GetStatus();

    /// <summary>
    /// Event raised when a maintenance execution starts.
    /// </summary>
    event EventHandler<MaintenanceExecution>? ExecutionStarted;

    /// <summary>
    /// Event raised when a maintenance execution completes.
    /// </summary>
    event EventHandler<MaintenanceExecution>? ExecutionCompleted;

    /// <summary>
    /// Event raised when a maintenance execution fails.
    /// </summary>
    event EventHandler<MaintenanceExecution>? ExecutionFailed;
}

/// <summary>
/// Status of the maintenance service.
/// </summary>
public sealed record MaintenanceServiceStatus(
    bool IsRunning,
    int QueuedExecutions,
    MaintenanceExecution? CurrentExecution,
    DateTimeOffset? NextScheduledExecution,
    int ActiveSchedules,
    long TotalExecutionsToday,
    TimeSpan Uptime
);

// IArchiveMaintenanceScheduleManager has been extracted to its own file: IArchiveMaintenanceScheduleManager.cs
// IMaintenanceExecutionHistory has been extracted to its own file: IMaintenanceExecutionHistory.cs

/// <summary>
/// Summary of maintenance schedules.
/// </summary>
public sealed record MaintenanceScheduleSummary(
    int TotalSchedules,
    int EnabledSchedules,
    int DisabledSchedules,
    Dictionary<MaintenanceTaskType, int> ByTaskType,
    DateTimeOffset? NextDueSchedule,
    string? NextDueScheduleName
);
