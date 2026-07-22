using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing scheduled backfill and maintenance tasks.
/// </summary>
public sealed class ScheduleManagerService
{
    private static readonly Lazy<ScheduleManagerService> _instance = new(() => new ScheduleManagerService());
    public static ScheduleManagerService Instance => _instance.Value;

    private ScheduleManagerService() { }


    /// <summary>
    /// Gets all backfill schedules.
    /// </summary>
    public async Task<List<BackfillSchedule>?> GetBackfillSchedulesAsync(CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<BackfillSchedule>>(UiApiRoutes.BackfillSchedules, ct).ConfigureAwait(false)).DataOrLoggedNull("Get backfill schedules");
    }

    /// <summary>
    /// Gets a specific backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> GetBackfillScheduleAsync(string id, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<BackfillSchedule>(BuildBackfillScheduleRoute(id), ct).ConfigureAwait(false)).DataOrLoggedNull("Get backfill schedule");
    }

    /// <summary>
    /// Creates a new backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> CreateBackfillScheduleAsync(CreateBackfillScheduleRequest request, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<BackfillSchedule>(UiApiRoutes.BackfillSchedules, request, ct).ConfigureAwait(false)).DataOrLoggedNull("Create backfill schedule");
    }

    /// <summary>
    /// Updates an existing backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> UpdateBackfillScheduleAsync(string id, UpdateBackfillScheduleRequest request, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<BackfillSchedule>(BuildBackfillScheduleRoute(id), request, ct).ConfigureAwait(false)).DataOrLoggedNull("Update backfill schedule");
    }

    /// <summary>
    /// Deletes a backfill schedule.
    /// </summary>
    public async Task<bool> DeleteBackfillScheduleAsync(string id, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<DeleteResponse>(BuildBackfillScheduleDeleteRoute(id), null, ct).ConfigureAwait(false);
        return response.Success;
    }

    /// <summary>
    /// Enables or disables a backfill schedule.
    /// </summary>
    public async Task<bool> SetBackfillScheduleEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<EnableResponse>(
            BuildBackfillScheduleEnabledRoute(id, enabled), null, ct).ConfigureAwait(false);
        return response.Success;
    }

    /// <summary>
    /// Runs a backfill schedule immediately.
    /// </summary>
    public async Task<ScheduleExecutionResult?> RunBackfillScheduleNowAsync(string id, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<ScheduleExecutionResult>(BuildBackfillScheduleRunRoute(id), null, ct).ConfigureAwait(false)).DataOrLoggedNull("Run backfill schedule");
    }

    /// <summary>
    /// Gets execution history for a backfill schedule.
    /// </summary>
    public async Task<List<ScheduleExecutionLog>?> GetBackfillExecutionHistoryAsync(string id, int limit = 50, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<ScheduleExecutionLog>>(BuildBackfillScheduleHistoryRoute(id, limit), ct).ConfigureAwait(false)).DataOrLoggedNull("Get backfill schedule history");
    }

    /// <summary>
    /// Gets available backfill schedule templates.
    /// </summary>
    public async Task<List<ScheduleTemplate>?> GetBackfillTemplatesAsync(CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<ScheduleTemplate>>(UiApiRoutes.BackfillSchedulesTemplates, ct).ConfigureAwait(false)).DataOrLoggedNull("Get schedule templates");
    }



    /// <summary>
    /// Gets all maintenance schedules.
    /// </summary>
    public async Task<List<MaintenanceSchedule>?> GetMaintenanceSchedulesAsync(CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<MaintenanceSchedule>>(UiApiRoutes.MaintenanceSchedules, ct).ConfigureAwait(false)).DataOrLoggedNull("Get maintenance schedules");
    }

    /// <summary>
    /// Creates a new maintenance schedule.
    /// </summary>
    public async Task<MaintenanceSchedule?> CreateMaintenanceScheduleAsync(CreateMaintenanceScheduleRequest request, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<MaintenanceSchedule>(UiApiRoutes.MaintenanceSchedules, request, ct).ConfigureAwait(false)).DataOrLoggedNull("Create maintenance schedule");
    }

    /// <summary>
    /// Updates an existing maintenance schedule.
    /// </summary>
    public async Task<MaintenanceSchedule?> UpdateMaintenanceScheduleAsync(string id, UpdateMaintenanceScheduleRequest request, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<MaintenanceSchedule>(BuildMaintenanceScheduleRoute(id), request, ct).ConfigureAwait(false)).DataOrLoggedNull("Update maintenance schedule");
    }

    /// <summary>
    /// Deletes a maintenance schedule.
    /// </summary>
    public async Task<bool> DeleteMaintenanceScheduleAsync(string id, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<DeleteResponse>(BuildMaintenanceScheduleDeleteRoute(id), null, ct).ConfigureAwait(false);
        return response.Success;
    }

    /// <summary>
    /// Enables or disables a maintenance schedule.
    /// </summary>
    public async Task<bool> SetMaintenanceScheduleEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<EnableResponse>(
            BuildMaintenanceScheduleEnabledRoute(id, enabled), null, ct).ConfigureAwait(false);
        return response.Success;
    }

    /// <summary>
    /// Runs a maintenance schedule immediately.
    /// </summary>
    public async Task<ScheduleExecutionResult?> RunMaintenanceScheduleNowAsync(string id, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<ScheduleExecutionResult>(BuildMaintenanceScheduleRunRoute(id), null, ct).ConfigureAwait(false)).DataOrLoggedNull("Run maintenance schedule");
    }

    /// <summary>
    /// Gets execution history for a maintenance schedule.
    /// </summary>
    public async Task<List<ScheduleExecutionLog>?> GetMaintenanceExecutionHistoryAsync(string id, int limit = 50, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.GetWithResponseAsync<List<ScheduleExecutionLog>>(BuildMaintenanceScheduleHistoryRoute(id, limit), ct).ConfigureAwait(false)).DataOrLoggedNull("Get maintenance schedule history");
    }



    /// <summary>
    /// Validates a cron expression.
    /// </summary>
    public async Task<CronValidationResult?> ValidateCronExpressionAsync(string cronExpression, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<CronValidationResult>(
            UiApiRoutes.SchedulesCronValidate,
            new { expression = cronExpression },
            ct).ConfigureAwait(false)).DataOrLoggedNull("Validate cron expression");
    }

    /// <summary>
    /// Gets next run times for a cron expression.
    /// </summary>
    public async Task<List<DateTime>?> GetNextRunTimesAsync(string cronExpression, int count = 5, CancellationToken ct = default)
    {
        return (await ApiClientService.Instance.PostWithResponseAsync<List<DateTime>>(
            UiApiRoutes.SchedulesCronNextRuns,
            new { expression = cronExpression, count },
            ct).ConfigureAwait(false)).DataOrLoggedNull("Get cron next run times");
    }

    internal static string BuildBackfillScheduleRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesById, "id", id);

    internal static string BuildBackfillScheduleDeleteRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesDelete, "id", id);

    internal static string BuildBackfillScheduleEnabledRoute(string id, bool enabled)
        => UiApiRoutes.WithParam(enabled ? UiApiRoutes.BackfillSchedulesEnable : UiApiRoutes.BackfillSchedulesDisable, "id", id);

    internal static string BuildBackfillScheduleRunRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesRun, "id", id);

    internal static string BuildBackfillScheduleHistoryRoute(string id, int limit)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.BackfillSchedulesHistory, "id", id),
            string.Create(CultureInfo.InvariantCulture, $"limit={limit}"));

    internal static string BuildMaintenanceScheduleRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.MaintenanceSchedulesById, "id", id);

    internal static string BuildMaintenanceScheduleDeleteRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.MaintenanceSchedulesDelete, "id", id);

    internal static string BuildMaintenanceScheduleEnabledRoute(string id, bool enabled)
        => UiApiRoutes.WithParam(enabled ? UiApiRoutes.MaintenanceSchedulesEnable : UiApiRoutes.MaintenanceSchedulesDisable, "id", id);

    internal static string BuildMaintenanceScheduleRunRoute(string id)
        => UiApiRoutes.WithParam(UiApiRoutes.MaintenanceSchedulesRun, "id", id);

    internal static string BuildMaintenanceScheduleHistoryRoute(string id, int limit)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WithParam(UiApiRoutes.MaintenanceSchedulesHistory, "id", id),
            string.Create(CultureInfo.InvariantCulture, $"limit={limit}"));

}

// DTO classes for schedule management

public sealed class BackfillSchedule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string CronDescription { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<string> Symbols { get; set; } = new();
    public string Provider { get; set; } = string.Empty;
    public string Granularity { get; set; } = "Daily";
    public int LookbackDays { get; set; }
    public string Priority { get; set; } = "Normal";
    public List<string> Tags { get; set; } = new();
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public string LastRunStatus { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateBackfillScheduleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = new();
    public string Provider { get; set; } = string.Empty;
    public string Granularity { get; set; } = "Daily";
    public int LookbackDays { get; set; } = 7;
    public string Priority { get; set; } = "Normal";
    public List<string> Tags { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
}

public sealed class UpdateBackfillScheduleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public List<string>? Symbols { get; set; }
    public string? Provider { get; set; }
    public string? Granularity { get; set; }
    public int? LookbackDays { get; set; }
    public string? Priority { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class MaintenanceSchedule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MaintenanceType { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string CronDescription { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? TargetPath { get; set; }
    public string Priority { get; set; } = "Normal";
    public int MaxDurationMinutes { get; set; }
    public int MaxRetries { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public string LastRunStatus { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateMaintenanceScheduleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MaintenanceType { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string Priority { get; set; } = "Normal";
    public int MaxDurationMinutes { get; set; } = 60;
    public int MaxRetries { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
}

public sealed class UpdateMaintenanceScheduleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CronExpression { get; set; }
    public string? TargetPath { get; set; }
    public string? Priority { get; set; }
    public int? MaxDurationMinutes { get; set; }
    public int? MaxRetries { get; set; }
}

public sealed class ScheduleExecutionLog
{
    public string Id { get; set; } = string.Empty;
    public string ScheduleId { get; set; } = string.Empty;
    public string ScheduleName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RecordsProcessed { get; set; }
    public int RecordsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}

public sealed class ScheduleExecutionResult
{
    public bool Success { get; set; }
    public string ExecutionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class ScheduleTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string CronDescription { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class CronValidationResult
{
    public bool IsValid { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public List<DateTime> NextRuns { get; set; } = new();
}

public sealed class DeleteResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class EnableResponse
{
    public bool Success { get; set; }
    public bool IsEnabled { get; set; }
}
