using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        return await ApiClientService.Instance.GetAsync<List<BackfillSchedule>>("/api/backfill/schedules", ct);
    }

    /// <summary>
    /// Gets a specific backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> GetBackfillScheduleAsync(string id, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<BackfillSchedule>($"/api/backfill/schedules/{id}", ct);
    }

    /// <summary>
    /// Creates a new backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> CreateBackfillScheduleAsync(CreateBackfillScheduleRequest request, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<BackfillSchedule>("/api/backfill/schedules", request, ct);
    }

    /// <summary>
    /// Updates an existing backfill schedule.
    /// </summary>
    public async Task<BackfillSchedule?> UpdateBackfillScheduleAsync(string id, UpdateBackfillScheduleRequest request, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<BackfillSchedule>($"/api/backfill/schedules/{id}", request, ct);
    }

    /// <summary>
    /// Deletes a backfill schedule.
    /// </summary>
    public async Task<bool> DeleteBackfillScheduleAsync(string id, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<DeleteResponse>($"/api/backfill/schedules/{id}/delete", null, ct);
        return response.Success;
    }

    /// <summary>
    /// Enables or disables a backfill schedule.
    /// </summary>
    public async Task<bool> SetBackfillScheduleEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<EnableResponse>(
            $"/api/backfill/schedules/{id}/{(enabled ? "enable" : "disable")}", null, ct);
        return response.Success;
    }

    /// <summary>
    /// Runs a backfill schedule immediately.
    /// </summary>
    public async Task<ScheduleExecutionResult?> RunBackfillScheduleNowAsync(string id, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<ScheduleExecutionResult>($"/api/backfill/schedules/{id}/run", null, ct);
    }

    /// <summary>
    /// Gets execution history for a backfill schedule.
    /// </summary>
    public async Task<List<ScheduleExecutionLog>?> GetBackfillExecutionHistoryAsync(string id, int limit = 50, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<ScheduleExecutionLog>>($"/api/backfill/schedules/{id}/history?limit={limit}", ct);
    }

    /// <summary>
    /// Gets available backfill schedule templates.
    /// </summary>
    public async Task<List<ScheduleTemplate>?> GetBackfillTemplatesAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<ScheduleTemplate>>("/api/backfill/schedules/templates", ct);
    }



    /// <summary>
    /// Gets all maintenance schedules.
    /// </summary>
    public async Task<List<MaintenanceSchedule>?> GetMaintenanceSchedulesAsync(CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<MaintenanceSchedule>>("/api/maintenance/schedules", ct);
    }

    /// <summary>
    /// Creates a new maintenance schedule.
    /// </summary>
    public async Task<MaintenanceSchedule?> CreateMaintenanceScheduleAsync(CreateMaintenanceScheduleRequest request, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<MaintenanceSchedule>("/api/maintenance/schedules", request, ct);
    }

    /// <summary>
    /// Updates an existing maintenance schedule.
    /// </summary>
    public async Task<MaintenanceSchedule?> UpdateMaintenanceScheduleAsync(string id, UpdateMaintenanceScheduleRequest request, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<MaintenanceSchedule>($"/api/maintenance/schedules/{id}", request, ct);
    }

    /// <summary>
    /// Deletes a maintenance schedule.
    /// </summary>
    public async Task<bool> DeleteMaintenanceScheduleAsync(string id, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<DeleteResponse>($"/api/maintenance/schedules/{id}/delete", null, ct);
        return response.Success;
    }

    /// <summary>
    /// Enables or disables a maintenance schedule.
    /// </summary>
    public async Task<bool> SetMaintenanceScheduleEnabledAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<EnableResponse>(
            $"/api/maintenance/schedules/{id}/{(enabled ? "enable" : "disable")}", null, ct);
        return response.Success;
    }

    /// <summary>
    /// Runs a maintenance schedule immediately.
    /// </summary>
    public async Task<ScheduleExecutionResult?> RunMaintenanceScheduleNowAsync(string id, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<ScheduleExecutionResult>($"/api/maintenance/schedules/{id}/run", null, ct);
    }

    /// <summary>
    /// Gets execution history for a maintenance schedule.
    /// </summary>
    public async Task<List<ScheduleExecutionLog>?> GetMaintenanceExecutionHistoryAsync(string id, int limit = 50, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.GetAsync<List<ScheduleExecutionLog>>($"/api/maintenance/schedules/{id}/history?limit={limit}", ct);
    }



    /// <summary>
    /// Validates a cron expression.
    /// </summary>
    public async Task<CronValidationResult?> ValidateCronExpressionAsync(string cronExpression, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<CronValidationResult>(
            "/api/schedules/cron/validate",
            new { expression = cronExpression },
            ct);
    }

    /// <summary>
    /// Gets next run times for a cron expression.
    /// </summary>
    public async Task<List<DateTime>?> GetNextRunTimesAsync(string cronExpression, int count = 5, CancellationToken ct = default)
    {
        return await ApiClientService.Instance.PostAsync<List<DateTime>>(
            "/api/schedules/cron/next-runs",
            new { expression = cronExpression, count },
            ct);
    }

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
