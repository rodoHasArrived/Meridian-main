namespace Meridian.Storage.Maintenance;

/// <summary>
/// Interface for managing archive maintenance schedules.
/// </summary>
public interface IArchiveMaintenanceScheduleManager
{
    /// <summary>
    /// Get all maintenance schedules.
    /// </summary>
    IReadOnlyList<ArchiveMaintenanceSchedule> GetAllSchedules();

    /// <summary>
    /// Get a specific schedule by ID.
    /// </summary>
    ArchiveMaintenanceSchedule? GetSchedule(string scheduleId);

    /// <summary>
    /// Create a new maintenance schedule.
    /// </summary>
    Task<ArchiveMaintenanceSchedule> CreateScheduleAsync(ArchiveMaintenanceSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Create a schedule from a preset.
    /// </summary>
    Task<ArchiveMaintenanceSchedule> CreateFromPresetAsync(string presetName, string name, CancellationToken ct = default);

    /// <summary>
    /// Update an existing schedule.
    /// </summary>
    Task<ArchiveMaintenanceSchedule> UpdateScheduleAsync(ArchiveMaintenanceSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Delete a schedule.
    /// </summary>
    Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken ct = default);

    /// <summary>
    /// Enable or disable a schedule.
    /// </summary>
    Task<bool> SetScheduleEnabledAsync(string scheduleId, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Get schedules that are due for execution.
    /// </summary>
    IReadOnlyList<ArchiveMaintenanceSchedule> GetDueSchedules(DateTimeOffset asOf);

    /// <summary>
    /// Get an overview of all schedules.
    /// </summary>
    MaintenanceScheduleSummary GetStatusSummary();

    /// <summary>
    /// Event raised when a schedule is created.
    /// </summary>
    event EventHandler<ArchiveMaintenanceSchedule>? ScheduleCreated;

    /// <summary>
    /// Event raised when a schedule is updated.
    /// </summary>
    event EventHandler<ArchiveMaintenanceSchedule>? ScheduleUpdated;

    /// <summary>
    /// Event raised when a schedule is deleted.
    /// </summary>
    event EventHandler<string>? ScheduleDeleted;
}
