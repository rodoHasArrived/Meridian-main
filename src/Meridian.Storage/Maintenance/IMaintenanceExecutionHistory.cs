namespace Meridian.Storage.Maintenance;

/// <summary>
/// Interface for tracking maintenance execution history.
/// </summary>
public interface IMaintenanceExecutionHistory
{
    /// <summary>
    /// Record a new execution.
    /// </summary>
    void RecordExecution(MaintenanceExecution execution);

    /// <summary>
    /// Update an existing execution record.
    /// </summary>
    void UpdateExecution(MaintenanceExecution execution);

    /// <summary>
    /// Get a specific execution by ID.
    /// </summary>
    MaintenanceExecution? GetExecution(string executionId);

    /// <summary>
    /// Get recent executions.
    /// </summary>
    IReadOnlyList<MaintenanceExecution> GetRecentExecutions(int limit = 50);

    /// <summary>
    /// Get executions for a specific schedule.
    /// </summary>
    IReadOnlyList<MaintenanceExecution> GetExecutionsForSchedule(string scheduleId, int limit = 50);

    /// <summary>
    /// Get failed executions.
    /// </summary>
    IReadOnlyList<MaintenanceExecution> GetFailedExecutions(int limit = 50);

    /// <summary>
    /// Get executions within a time range.
    /// </summary>
    IReadOnlyList<MaintenanceExecution> GetExecutionsByTimeRange(DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// Get summary for a specific schedule.
    /// </summary>
    ScheduleExecutionSummary GetScheduleSummary(string scheduleId, int recentCount = 10);

    /// <summary>
    /// Get overall maintenance statistics.
    /// </summary>
    MaintenanceStatistics GetStatistics(TimeSpan? period = null);

    /// <summary>
    /// Clean up old execution records.
    /// </summary>
    Task<int> CleanupOldRecordsAsync(int maxAgeDays = 90, CancellationToken ct = default);
}
