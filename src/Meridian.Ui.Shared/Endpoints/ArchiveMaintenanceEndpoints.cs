using System.Text.Json;
using Meridian.Application.Scheduling;
using Meridian.Core.Scheduling;
using Meridian.Storage.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// HTTP API endpoints for archive maintenance management.
/// Provides remote management capabilities for scheduled maintenance operations.
/// </summary>
public static class ArchiveMaintenanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Configure all archive maintenance routes.
    /// The schedule CRUD routes (list, get by ID, create, enable, disable) are handled by
    /// MapMaintenanceScheduleEndpoints to avoid duplicate route registrations.
    /// This method registers the remaining maintenance routes: update, delete, trigger,
    /// execution management, statistics, validation, and presets.
    /// </summary>
    public static void MapArchiveMaintenanceEndpoints(this WebApplication app)
    {
        // ==================== SCHEDULE MANAGEMENT ====================
        // NOTE: GET /schedules, GET /schedules/{id}, POST /schedules, POST /schedules/{id}/enable,
        // and POST /schedules/{id}/disable are registered by MapMaintenanceScheduleEndpoints.
        // Only update and delete are registered here to avoid conflicting route definitions.

        app.MapPut("/api/maintenance/schedules/{scheduleId}", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            UpdateMaintenanceScheduleRequest req,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var schedule = scheduleManager.GetSchedule(scheduleId);
                if (schedule is null)
                    return Results.NotFound($"Schedule '{scheduleId}' not found");

                if (!string.IsNullOrWhiteSpace(req.Name))
                    schedule.Name = req.Name;
                if (!string.IsNullOrWhiteSpace(req.Description))
                    schedule.Description = req.Description;
                if (!string.IsNullOrWhiteSpace(req.CronExpression))
                    schedule.CronExpression = req.CronExpression;
                if (!string.IsNullOrWhiteSpace(req.TimeZoneId))
                    schedule.TimeZoneId = req.TimeZoneId;
                if (req.TaskType != null && Enum.TryParse<MaintenanceTaskType>(req.TaskType, true, out var tt))
                    schedule.TaskType = tt;
                if (req.Priority != null && Enum.TryParse<MaintenancePriority>(req.Priority, true, out var p))
                    schedule.Priority = p;
                if (req.Enabled.HasValue)
                    schedule.Enabled = req.Enabled.Value;
                if (req.MaxDurationMinutes.HasValue)
                    schedule.MaxDuration = TimeSpan.FromMinutes(req.MaxDurationMinutes.Value);
                if (req.MaxRetries.HasValue)
                    schedule.MaxRetries = req.MaxRetries.Value;
                if (req.TargetPaths != null)
                {
                    schedule.TargetPaths.Clear();
                    schedule.TargetPaths.AddRange(req.TargetPaths);
                }
                if (req.Tags != null)
                {
                    schedule.Tags.Clear();
                    schedule.Tags.AddRange(req.Tags);
                }
                if (req.Options != null)
                    schedule.Options = MapOptions(req.Options);

                schedule = await scheduleManager.UpdateScheduleAsync(schedule, ct);
                return Results.Json(schedule, JsonOptions);
            },
            "Failed to update schedule",
            mapException: ex => ex switch
            {
                ArgumentException aex => Results.BadRequest(aex.Message),
                ArchiveMaintenanceScheduleConcurrencyException conflict => Results.Conflict(new
                {
                    error = conflict.Message,
                    conflict.ScheduleId,
                    conflict.ExpectedRevision,
                    conflict.ActualRevision
                }),
                _ => null
            },
            includeExceptionMessage: true);
        });

        app.MapDelete("/api/maintenance/schedules/{scheduleId}", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var deleted = await scheduleManager.DeleteScheduleAsync(scheduleId, ct);
                return deleted
                    ? Results.Ok(new { message = $"Schedule '{scheduleId}' deleted" })
                    : Results.NotFound($"Schedule '{scheduleId}' not found");
            },
            "Failed to delete schedule",
            includeExceptionMessage: true);
        });

        // ==================== SCHEDULE CONTROL ====================
        // NOTE: POST /schedules/{id}/enable and POST /schedules/{id}/disable are registered
        // by MapMaintenanceScheduleEndpoints. Only /trigger is registered here.

        app.MapPost("/api/maintenance/schedules/{scheduleId}/trigger", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            string scheduleId,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var execution = await maintenanceService.TriggerScheduleAsync(scheduleId, ct);
                return Results.Json(execution, JsonOptions);
            },
            "Failed to trigger schedule",
            mapException: ex => ex switch
            {
                KeyNotFoundException => Results.NotFound($"Schedule '{scheduleId}' not found"),
                InvalidOperationException => Results.Conflict(
                    $"Schedule '{scheduleId}' already has an execution queued or running"),
                _ => null
            },
            includeExceptionMessage: true);
        });

        // ==================== IMMEDIATE EXECUTION ====================

        app.MapPost("/api/maintenance/execute", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            ExecuteMaintenanceRequest req,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                if (!Enum.TryParse<MaintenanceTaskType>(req.TaskType, true, out var taskType))
                    return Results.BadRequest($"Invalid task type: {req.TaskType}");

                var options = req.Options != null ? MapOptions(req.Options) : new MaintenanceTaskOptions();

                var execution = await maintenanceService.ExecuteMaintenanceAsync(
                    taskType,
                    options,
                    req.TargetPaths,
                    ct);

                return Results.Json(execution, JsonOptions);
            },
            "Maintenance execution failed",
            includeExceptionMessage: true);
        });

        app.MapPost("/api/maintenance/executions/{executionId}/cancel", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            string executionId) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var cancelled = await maintenanceService.CancelExecutionAsync(executionId);
                return cancelled
                    ? Results.Ok(new { message = $"Execution '{executionId}' cancelled" })
                    : Results.NotFound($"Execution '{executionId}' not found or not running");
            },
            "Failed to cancel execution",
            includeExceptionMessage: true);
        });

        // ==================== EXECUTION HISTORY ====================

        app.MapGet("/api/maintenance/executions", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? limit) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var executions = scheduleManager.ExecutionHistory.GetRecentExecutions(limit ?? 50);
                return Results.Json(executions, JsonOptions);
            },
            "Failed to get executions",
            includeExceptionMessage: true);
        });

        app.MapGet("/api/maintenance/executions/{executionId}", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string executionId) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var execution = scheduleManager.ExecutionHistory.GetExecution(executionId);
                return execution is null
                    ? Results.NotFound($"Execution '{executionId}' not found")
                    : Results.Json(execution, JsonOptions);
            },
            "Failed to get execution",
            includeExceptionMessage: true);
        });

        app.MapGet("/api/maintenance/schedules/{scheduleId}/executions", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            int? limit) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var executions = scheduleManager.ExecutionHistory.GetExecutionsForSchedule(scheduleId, limit ?? 50);
                return Results.Json(executions, JsonOptions);
            },
            "Failed to get schedule executions",
            includeExceptionMessage: true);
        });

        app.MapGet("/api/maintenance/executions/failed", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? limit) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var executions = scheduleManager.ExecutionHistory.GetFailedExecutions(limit ?? 50);
                return Results.Json(executions, JsonOptions);
            },
            "Failed to get failed executions",
            includeExceptionMessage: true);
        });

        // ==================== STATISTICS & SUMMARIES ====================

        app.MapGet("/api/maintenance/schedules/summary", async (ArchiveMaintenanceScheduleManager scheduleManager) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var summary = scheduleManager.GetStatusSummary();
                return Results.Json(summary, JsonOptions);
            },
            "Failed to get schedule summary",
            includeExceptionMessage: true);
        });

        app.MapGet("/api/maintenance/schedules/{scheduleId}/summary", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            int? recentCount) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var summary = scheduleManager.ExecutionHistory.GetScheduleSummary(scheduleId, recentCount ?? 10);
                return Results.Json(summary, JsonOptions);
            },
            "Failed to get schedule summary",
            includeExceptionMessage: true);
        });

        app.MapGet("/api/maintenance/statistics", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? hours) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var period = hours.HasValue ? TimeSpan.FromHours(hours.Value) : (TimeSpan?)null;
                var stats = scheduleManager.ExecutionHistory.GetStatistics(period);

                // Enrich with schedule counts
                var scheduleSummary = scheduleManager.GetStatusSummary();
                var enrichedStats = stats with
                {
                    TotalSchedules = scheduleSummary.TotalSchedules,
                    EnabledSchedules = scheduleSummary.EnabledSchedules,
                    DisabledSchedules = scheduleSummary.DisabledSchedules,
                    NextScheduledExecution = scheduleSummary.NextDueSchedule
                };

                return Results.Json(enrichedStats, JsonOptions);
            },
            "Failed to get statistics",
            includeExceptionMessage: true);
        });

        // ==================== SERVICE STATUS ====================

        app.MapGet("/api/maintenance/status", async (ScheduledArchiveMaintenanceService maintenanceService) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var status = maintenanceService.GetStatus();
                return Results.Json(status, JsonOptions);
            },
            "Failed to get service status",
            includeExceptionMessage: true);
        });

        // ==================== CRON VALIDATION ====================

        app.MapPost("/api/maintenance/validate-cron", async (ValidateMaintenanceCronRequest req) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(req.CronExpression))
                    return Results.BadRequest("Cron expression is required");

                var isValid = CronExpressionParser.IsValid(req.CronExpression);
                var description = isValid
                    ? CronExpressionParser.GetDescription(req.CronExpression)
                    : "Invalid cron expression";

                DateTimeOffset? nextExecution = null;
                if (isValid)
                {
                    var tz = string.IsNullOrWhiteSpace(req.TimeZoneId)
                        ? TimeZoneInfo.Utc
                        : TimeZoneInfo.FindSystemTimeZoneById(req.TimeZoneId);
                    nextExecution = CronExpressionParser.GetNextOccurrence(
                        req.CronExpression, tz, DateTimeOffset.UtcNow);
                }

                return Results.Json(new
                {
                    isValid,
                    description,
                    nextExecution
                }, JsonOptions);
            },
            "Validation failed",
            mapException: ex => ex switch
            {
                TimeZoneNotFoundException => Results.BadRequest($"Invalid timezone: {req.TimeZoneId}"),
                _ => null
            },
            includeExceptionMessage: true);
        });

        // ==================== PRESETS ====================

        app.MapGet("/api/maintenance/presets", async () =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var presets = new[]
                {
                    new
                    {
                        name = "daily-health",
                        displayName = "Daily Health Check",
                        description = "Run daily at 3 AM UTC to check storage health",
                        cronExpression = "0 3 * * *",
                        taskType = "HealthCheck"
                    },
                    new
                    {
                        name = "weekly-full",
                        displayName = "Weekly Full Maintenance",
                        description = "Run every Sunday at 2 AM UTC for comprehensive maintenance",
                        cronExpression = "0 2 * * 0",
                        taskType = "FullMaintenance"
                    },
                    new
                    {
                        name = "daily-tier",
                        displayName = "Daily Tier Migration",
                        description = "Run daily at 4 AM UTC to migrate aging data",
                        cronExpression = "0 4 * * *",
                        taskType = "TierMigration"
                    },
                    new
                    {
                        name = "monthly-compression",
                        displayName = "Monthly Compression",
                        description = "Run on first Sunday of month at 1 AM UTC for optimal compression",
                        cronExpression = "0 1 * * 0#1",
                        taskType = "Compression"
                    },
                    new
                    {
                        name = "daily-retention",
                        displayName = "Daily Retention Enforcement",
                        description = "Run daily at 5 AM UTC to enforce retention policies",
                        cronExpression = "0 5 * * *",
                        taskType = "RetentionEnforcement"
                    }
                };

                return Results.Json(presets, JsonOptions);
            },
            "Failed to get presets",
            includeExceptionMessage: true);
        });

        // ==================== TASK TYPES ====================

        app.MapGet("/api/maintenance/task-types", async () =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var taskTypes = Enum.GetValues<MaintenanceTaskType>()
                    .Select(t => new
                    {
                        value = t.ToString(),
                        name = t.ToString(),
                        description = GetTaskTypeDescription(t)
                    })
                    .ToArray();

                return Results.Json(taskTypes, JsonOptions);
            },
            "Failed to get task types",
            includeExceptionMessage: true);
        });

        // ==================== CLEANUP ====================

        app.MapPost("/api/maintenance/executions/cleanup", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            CleanupHistoryRequest? req) =>
        {
            return await EndpointHelpers.GuardAsync(async () =>
            {
                var maxAgeDays = req?.MaxAgeDays ?? 90;
                var deletedCount = await scheduleManager.ExecutionHistory.CleanupOldRecordsAsync(maxAgeDays);
                return Results.Ok(new
                {
                    message = $"Cleaned up {deletedCount} old execution records",
                    deletedCount
                });
            },
            "Failed to cleanup history",
            includeExceptionMessage: true);
        });
    }

    private static MaintenanceTaskOptions MapOptions(MaintenanceOptionsDto? dto)
    {
        if (dto is null)
            return new MaintenanceTaskOptions();

        return new MaintenanceTaskOptions
        {
            ValidateChecksums = dto.ValidateChecksums ?? true,
            CheckSequenceContinuity = dto.CheckSequenceContinuity ?? true,
            IdentifyCorruption = dto.IdentifyCorruption ?? true,
            CheckFilePermissions = dto.CheckFilePermissions ?? true,
            ParallelOperations = dto.ParallelOperations ?? 4,
            DeleteOrphans = dto.DeleteOrphans ?? false,
            DeleteTemporaryFiles = dto.DeleteTemporaryFiles ?? true,
            DeleteEmptyDirectories = dto.DeleteEmptyDirectories ?? true,
            OrphanAgeDays = dto.OrphanAgeDays ?? 7,
            MinFileSizeBytes = dto.MinFileSizeBytes ?? 1_048_576,
            MaxFilesPerMerge = dto.MaxFilesPerMerge ?? 100,
            FileAgeDaysThreshold = dto.FileAgeDaysThreshold ?? 1,
            DryRun = dto.DryRun ?? false,
            DeleteSourceAfterMigration = dto.DeleteSourceAfterMigration ?? false,
            VerifyAfterMigration = dto.VerifyAfterMigration ?? true,
            TargetCompressionCodec = dto.TargetCompressionCodec,
            CompressionLevel = dto.CompressionLevel,
            RecompressExisting = dto.RecompressExisting ?? false,
            BackupBeforeRepair = dto.BackupBeforeRepair ?? true,
            BackupPath = dto.BackupPath,
            TruncateCorrupted = dto.TruncateCorrupted ?? true,
            OverrideRetentionDays = dto.OverrideRetentionDays,
            SkipCriticalData = dto.SkipCriticalData ?? true
        };
    }

    private static string GetTaskTypeDescription(MaintenanceTaskType taskType) => taskType switch
    {
        MaintenanceTaskType.HealthCheck => "Run health checks on storage files to identify issues",
        MaintenanceTaskType.Cleanup => "Clean up orphaned and temporary files",
        MaintenanceTaskType.Defragmentation => "Merge small files into larger chunks for better performance",
        MaintenanceTaskType.TierMigration => "Migrate files between storage tiers based on age",
        MaintenanceTaskType.Compression => "Recompress files with optimal compression settings",
        MaintenanceTaskType.Repair => "Repair corrupted or truncated files",
        MaintenanceTaskType.FullMaintenance => "Full maintenance: health check, cleanup, defrag, and tier migration",
        MaintenanceTaskType.IntegrityCheck => "Verify file integrity using checksums",
        MaintenanceTaskType.Archival => "Archive old data to cold storage",
        MaintenanceTaskType.RetentionEnforcement => "Enforce retention policies and delete expired data",
        _ => "Unknown task type"
    };
}

// ==================== REQUEST DTOs ====================

public record CreateMaintenanceScheduleRequest(
    string Name,
    string? Description = null,
    string? Preset = null,
    string? CronExpression = null,
    string? TimeZoneId = null,
    string? TaskType = null,
    string? Priority = null,
    bool? Enabled = null,
    int? MaxDurationMinutes = null,
    int? MaxRetries = null,
    string[]? TargetPaths = null,
    string[]? Tags = null,
    MaintenanceOptionsDto? Options = null
);

public record UpdateMaintenanceScheduleRequest(
    string? Name = null,
    string? Description = null,
    string? CronExpression = null,
    string? TimeZoneId = null,
    string? TaskType = null,
    string? Priority = null,
    bool? Enabled = null,
    int? MaxDurationMinutes = null,
    int? MaxRetries = null,
    string[]? TargetPaths = null,
    string[]? Tags = null,
    MaintenanceOptionsDto? Options = null
);

public record ExecuteMaintenanceRequest(
    string TaskType,
    string[]? TargetPaths = null,
    MaintenanceOptionsDto? Options = null
);

public record ValidateMaintenanceCronRequest(
    string CronExpression,
    string? TimeZoneId = null
);

public record CleanupHistoryRequest(
    int? MaxAgeDays = null
);

public record MaintenanceOptionsDto(
    bool? ValidateChecksums = null,
    bool? CheckSequenceContinuity = null,
    bool? IdentifyCorruption = null,
    bool? CheckFilePermissions = null,
    int? ParallelOperations = null,
    bool? DeleteOrphans = null,
    bool? DeleteTemporaryFiles = null,
    bool? DeleteEmptyDirectories = null,
    int? OrphanAgeDays = null,
    long? MinFileSizeBytes = null,
    int? MaxFilesPerMerge = null,
    int? FileAgeDaysThreshold = null,
    bool? DryRun = null,
    bool? DeleteSourceAfterMigration = null,
    bool? VerifyAfterMigration = null,
    string? TargetCompressionCodec = null,
    int? CompressionLevel = null,
    bool? RecompressExisting = null,
    bool? BackupBeforeRepair = null,
    string? BackupPath = null,
    bool? TruncateCorrupted = null,
    int? OverrideRetentionDays = null,
    bool? SkipCriticalData = null
);
