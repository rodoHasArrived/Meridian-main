using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Storage.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering maintenance schedule CRUD API endpoints.
/// </summary>
public static class MaintenanceScheduleEndpoints
{
    public static void MapMaintenanceScheduleEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Maintenance");

        // List maintenance schedules
        group.MapGet(UiApiRoutes.MaintenanceSchedules, ([FromServices] ArchiveMaintenanceScheduleManager? schedMgr) =>
        {
            var schedules = schedMgr?.GetAllSchedules() ?? [];
            return Results.Json(new
            {
                schedules,
                total = schedules.Count,
                summary = schedMgr?.GetStatusSummary(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetMaintenanceSchedules")
        .Produces(200);

        // Create maintenance schedule
        group.MapPost(UiApiRoutes.MaintenanceSchedules, async ([FromServices] ArchiveMaintenanceScheduleManager? schedMgr, ArchiveMaintenanceSchedule schedule, CancellationToken ct) =>
        {
            if (schedMgr is null)
                return Results.Json(new { error = "Schedule manager not available" }, jsonOptions, statusCode: 503);

            var created = await schedMgr.CreateScheduleAsync(schedule, ct);
            return Results.Json(created, jsonOptions);
        })
        .WithName("CreateMaintenanceSchedule")
        .Produces(200)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get maintenance schedule by ID
        group.MapGet(UiApiRoutes.MaintenanceSchedulesById, (string id, [FromServices] ArchiveMaintenanceScheduleManager? schedMgr) =>
        {
            var schedule = schedMgr?.GetSchedule(id);
            return schedule is null ? Results.NotFound() : Results.Json(schedule, jsonOptions);
        })
        .WithName("GetMaintenanceScheduleById")
        .Produces(200)
        .Produces(404);

        // Delete maintenance schedule
        group.MapDelete(UiApiRoutes.MaintenanceSchedulesDelete, async (string id, [FromServices] ArchiveMaintenanceScheduleManager? schedMgr, CancellationToken ct) =>
        {
            if (schedMgr is null)
                return Results.Json(new { error = "Schedule manager not available" }, jsonOptions, statusCode: 503);

            var deleted = await schedMgr.DeleteScheduleAsync(id, ct);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithName("DeleteMaintenanceSchedule")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Enable maintenance schedule
        group.MapPost(UiApiRoutes.MaintenanceSchedulesEnable, async (string id, [FromServices] ArchiveMaintenanceScheduleManager? schedMgr, CancellationToken ct) =>
        {
            if (schedMgr is null)
                return Results.Json(new { error = "Schedule manager not available" }, jsonOptions, statusCode: 503);

            var ok = await schedMgr.SetScheduleEnabledAsync(id, true, ct);
            return ok ? Results.Ok() : Results.NotFound();
        })
        .WithName("EnableMaintenanceSchedule")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Disable maintenance schedule
        group.MapPost(UiApiRoutes.MaintenanceSchedulesDisable, async (string id, [FromServices] ArchiveMaintenanceScheduleManager? schedMgr, CancellationToken ct) =>
        {
            if (schedMgr is null)
                return Results.Json(new { error = "Schedule manager not available" }, jsonOptions, statusCode: 503);

            var ok = await schedMgr.SetScheduleEnabledAsync(id, false, ct);
            return ok ? Results.Ok() : Results.NotFound();
        })
        .WithName("DisableMaintenanceSchedule")
        .Produces(200)
        .Produces(404)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Run maintenance schedule now
        group.MapPost(UiApiRoutes.MaintenanceSchedulesRun, async (string id, [FromServices] ScheduledArchiveMaintenanceService? maintService, CancellationToken ct) =>
        {
            if (maintService is null)
                return Results.Json(new { error = "Maintenance service not available" }, jsonOptions, statusCode: 503);

            try
            {
                var execution = await maintService.TriggerScheduleAsync(id, ct);
                return Results.Json(execution, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Schedule '{id}' not found" });
            }
        })
        .WithName("RunMaintenanceScheduleNow")
        .Produces(200)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Maintenance schedule history
        group.MapGet(UiApiRoutes.MaintenanceSchedulesHistory, (string id, int? limit, [FromServices] MaintenanceExecutionHistory? history, [FromServices] ArchiveMaintenanceScheduleManager? schedMgrForHistory) =>
        {
            var executions = history?.GetExecutionsForSchedule(id, limit ?? 50) ?? [];
            return Results.Json(new
            {
                scheduleId = id,
                executions,
                total = executions.Count,
                summary = schedMgrForHistory?.ExecutionHistory?.GetScheduleSummary(id)
            }, jsonOptions);
        })
        .WithName("GetMaintenanceScheduleHistory")
        .Produces(200);
    }
}
