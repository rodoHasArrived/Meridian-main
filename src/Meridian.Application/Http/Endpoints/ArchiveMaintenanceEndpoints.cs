using System.Text.Json;
using Meridian.Application.Scheduling;
using Meridian.Core.Scheduling;
using Meridian.Storage.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Application.UI;

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
    /// </summary>
    public static void MapArchiveMaintenanceEndpoints(this WebApplication app)
    {
        // ==================== SCHEDULE MANAGEMENT ====================

        app.MapGet("/api/maintenance/schedules", (ArchiveMaintenanceScheduleManager scheduleManager) =>
        {
            try
            {
                var schedules = scheduleManager.GetAllSchedules();
                return Results.Json(schedules, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get schedules: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/schedules/{scheduleId}", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId) =>
        {
            try
            {
                var schedule = scheduleManager.GetSchedule(scheduleId);
                return schedule is null
                    ? Results.NotFound($"Schedule '{scheduleId}' not found")
                    : Results.Json(schedule, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get schedule: {ex.Message}");
            }
        });

        app.MapPost("/api/maintenance/schedules", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            CreateMaintenanceScheduleRequest req) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Results.BadRequest("Schedule name is required");

                if (string.IsNullOrWhiteSpace(req.CronExpression) && string.IsNullOrWhiteSpace(req.Preset))
                    return Results.BadRequest("Either cronExpression or preset is required");

                ArchiveMaintenanceSchedule schedule;

                if (!string.IsNullOrWhiteSpace(req.Preset))
                {
                    schedule = await scheduleManager.CreateFromPresetAsync(req.Preset, req.Name);
                }
                else
                {
                    schedule = new ArchiveMaintenanceSchedule
                    {
                        Name = req.Name,
                        Description = req.Description ?? string.Empty,
                        CronExpression = req.CronExpression!,
                        TimeZoneId = req.TimeZoneId ?? "UTC",
                        TaskType = Enum.TryParse<MaintenanceTaskType>(req.TaskType, true, out var tt)
                            ? tt : MaintenanceTaskType.HealthCheck,
                        Priority = Enum.TryParse<MaintenancePriority>(req.Priority, true, out var p)
                            ? p : MaintenancePriority.Normal,
                        Enabled = req.Enabled ?? true,
                        MaxDuration = req.MaxDurationMinutes.HasValue
                            ? TimeSpan.FromMinutes(req.MaxDurationMinutes.Value)
                            : TimeSpan.FromHours(2),
                        MaxRetries = req.MaxRetries ?? 2,
                        TargetPaths = req.TargetPaths?.ToList() ?? new List<string>(),
                        Tags = req.Tags?.ToList() ?? new List<string>(),
                        Options = MapOptions(req.Options)
                    };

                    schedule = await scheduleManager.CreateScheduleAsync(schedule);
                }

                return Results.Json(schedule, JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to create schedule: {ex.Message}");
            }
        });

        app.MapPut("/api/maintenance/schedules/{scheduleId}", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            UpdateMaintenanceScheduleRequest req) =>
        {
            try
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

                schedule = await scheduleManager.UpdateScheduleAsync(schedule);
                return Results.Json(schedule, JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to update schedule: {ex.Message}");
            }
        });

        app.MapDelete("/api/maintenance/schedules/{scheduleId}", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId) =>
        {
            try
            {
                var deleted = await scheduleManager.DeleteScheduleAsync(scheduleId);
                return deleted
                    ? Results.Ok(new { message = $"Schedule '{scheduleId}' deleted" })
                    : Results.NotFound($"Schedule '{scheduleId}' not found");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to delete schedule: {ex.Message}");
            }
        });

        // ==================== SCHEDULE CONTROL ====================

        app.MapPost("/api/maintenance/schedules/{scheduleId}/enable", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId) =>
        {
            try
            {
                var success = await scheduleManager.SetScheduleEnabledAsync(scheduleId, true);
                return success
                    ? Results.Ok(new { message = $"Schedule '{scheduleId}' enabled" })
                    : Results.NotFound($"Schedule '{scheduleId}' not found");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to enable schedule: {ex.Message}");
            }
        });

        app.MapPost("/api/maintenance/schedules/{scheduleId}/disable", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId) =>
        {
            try
            {
                var success = await scheduleManager.SetScheduleEnabledAsync(scheduleId, false);
                return success
                    ? Results.Ok(new { message = $"Schedule '{scheduleId}' disabled" })
                    : Results.NotFound($"Schedule '{scheduleId}' not found");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to disable schedule: {ex.Message}");
            }
        });

        app.MapPost("/api/maintenance/schedules/{scheduleId}/trigger", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            string scheduleId) =>
        {
            try
            {
                var execution = await maintenanceService.TriggerScheduleAsync(scheduleId);
                return Results.Json(execution, JsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"Schedule '{scheduleId}' not found");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to trigger schedule: {ex.Message}");
            }
        });

        // ==================== IMMEDIATE EXECUTION ====================

        app.MapPost("/api/maintenance/execute", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            ExecuteMaintenanceRequest req) =>
        {
            try
            {
                if (!Enum.TryParse<MaintenanceTaskType>(req.TaskType, true, out var taskType))
                    return Results.BadRequest($"Invalid task type: {req.TaskType}");

                var options = req.Options != null ? MapOptions(req.Options) : new MaintenanceTaskOptions();

                var execution = await maintenanceService.ExecuteMaintenanceAsync(
                    taskType,
                    options,
                    req.TargetPaths);

                return Results.Json(execution, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Maintenance execution failed: {ex.Message}");
            }
        });

        app.MapPost("/api/maintenance/executions/{executionId}/cancel", async (
            ScheduledArchiveMaintenanceService maintenanceService,
            string executionId) =>
        {
            try
            {
                var cancelled = await maintenanceService.CancelExecutionAsync(executionId);
                return cancelled
                    ? Results.Ok(new { message = $"Execution '{executionId}' cancelled" })
                    : Results.NotFound($"Execution '{executionId}' not found or not running");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to cancel execution: {ex.Message}");
            }
        });

        // ==================== EXECUTION HISTORY ====================

        app.MapGet("/api/maintenance/executions", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? limit) =>
        {
            try
            {
                var executions = scheduleManager.ExecutionHistory.GetRecentExecutions(limit ?? 50);
                return Results.Json(executions, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get executions: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/executions/{executionId}", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string executionId) =>
        {
            try
            {
                var execution = scheduleManager.ExecutionHistory.GetExecution(executionId);
                return execution is null
                    ? Results.NotFound($"Execution '{executionId}' not found")
                    : Results.Json(execution, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get execution: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/schedules/{scheduleId}/executions", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            int? limit) =>
        {
            try
            {
                var executions = scheduleManager.ExecutionHistory.GetExecutionsForSchedule(scheduleId, limit ?? 50);
                return Results.Json(executions, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get schedule executions: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/executions/failed", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? limit) =>
        {
            try
            {
                var executions = scheduleManager.ExecutionHistory.GetFailedExecutions(limit ?? 50);
                return Results.Json(executions, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get failed executions: {ex.Message}");
            }
        });

        // ==================== STATISTICS & SUMMARIES ====================

        app.MapGet("/api/maintenance/schedules/summary", (ArchiveMaintenanceScheduleManager scheduleManager) =>
        {
            try
            {
                var summary = scheduleManager.GetStatusSummary();
                return Results.Json(summary, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get schedule summary: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/schedules/{scheduleId}/summary", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            string scheduleId,
            int? recentCount) =>
        {
            try
            {
                var summary = scheduleManager.ExecutionHistory.GetScheduleSummary(scheduleId, recentCount ?? 10);
                return Results.Json(summary, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get schedule summary: {ex.Message}");
            }
        });

        app.MapGet("/api/maintenance/statistics", (
            ArchiveMaintenanceScheduleManager scheduleManager,
            int? hours) =>
        {
            try
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
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get statistics: {ex.Message}");
            }
        });

        // ==================== SERVICE STATUS ====================

        app.MapGet("/api/maintenance/status", (ScheduledArchiveMaintenanceService maintenanceService) =>
        {
            try
            {
                var status = maintenanceService.GetStatus();
                return Results.Json(status, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get service status: {ex.Message}");
            }
        });

        // ==================== CRON VALIDATION ====================

        app.MapPost("/api/maintenance/validate-cron", (ValidateMaintenanceCronRequest req) =>
        {
            try
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
            }
            catch (TimeZoneNotFoundException)
            {
                return Results.BadRequest($"Invalid timezone: {req.TimeZoneId}");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Validation failed: {ex.Message}");
            }
        });

        // ==================== PRESETS ====================

        app.MapGet("/api/maintenance/presets", () =>
        {
            try
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
                        cronExpression = "0 1 1-7 * 0",
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
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get presets: {ex.Message}");
            }
        });

        // ==================== TASK TYPES ====================

        app.MapGet("/api/maintenance/task-types", () =>
        {
            try
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
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get task types: {ex.Message}");
            }
        });

        // ==================== CLEANUP ====================

        app.MapPost("/api/maintenance/executions/cleanup", async (
            ArchiveMaintenanceScheduleManager scheduleManager,
            CleanupHistoryRequest? req) =>
        {
            try
            {
                var maxAgeDays = req?.MaxAgeDays ?? 90;
                var deletedCount = await scheduleManager.ExecutionHistory.CleanupOldRecordsAsync(maxAgeDays);
                return Results.Ok(new
                {
                    message = $"Cleaned up {deletedCount} old execution records",
                    deletedCount
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to cleanup history: {ex.Message}");
            }
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
