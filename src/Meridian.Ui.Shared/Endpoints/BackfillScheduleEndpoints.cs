using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using BackfillResult = Meridian.Contracts.Backfill.BackfillResult;
using UiBackfillCoordinator = Meridian.Ui.Shared.Services.BackfillCoordinator;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering backfill schedule, execution, and utility API endpoints.
/// </summary>
public static class BackfillScheduleEndpoints
{
    public static void MapBackfillScheduleEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Backfill");
        group.RequireWorkstationTenantScope();

        // Backfill health
        group.MapGet(UiApiRoutes.BackfillHealth, (
            HttpContext context,
            [FromServices] BackfillScheduleManager? schedMgr,
            [FromServices] UiBackfillCoordinator? backfill) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            var summary = schedMgr.GetStatusSummary();
            return Results.Json(new
            {
                healthy = true,
                schedules = new
                {
                    total = summary.TotalSchedules,
                    enabled = summary.EnabledSchedules,
                    dueNow = summary.SchedulesDueNow,
                    nextExecution = summary.NextScheduledExecution
                },
                successRate = summary.OverallSuccessRate,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetBackfillHealth")
        .WithDescription("Returns backfill system health including schedule status and success rates.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Resolve symbol for backfill
        group.MapGet(UiApiRoutes.BackfillResolve, (
            HttpContext context,
            string symbol,
            [FromServices] ProviderRegistry? registry) =>
        {
            if (registry is null)
                return ApiProblemDetails.ServiceUnavailable(context, "provider registry");

            var backfillProviders = registry.GetBackfillProviders()
                .Select(p => new { name = p.Name, displayName = p.DisplayName, priority = p.Priority })
                .ToArray();

            return Results.Json(new
            {
                symbol,
                availableProviders = backfillProviders,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ResolveBackfillSymbol")
        .WithDescription("Resolves a symbol against available backfill providers and returns supported providers.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Gap fill
        group.MapPost(UiApiRoutes.BackfillGapFill, async (
            HttpContext context,
            [FromServices] UiBackfillCoordinator? backfill,
            [FromServices] ILoggerFactory loggerFactory,
            GapFillRequest req,
            CancellationToken ct) =>
        {
            if (backfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill coordinator");

            if (req.Symbols is null || req.Symbols.Length == 0)
                return ApiProblemDetails.Validation(context, "symbols", "At least one symbol is required.");

            BackfillRequest request;
            try
            {
                request = new BackfillRequest(
                    req.Provider ?? "stooq",
                    req.Symbols,
                    req.From,
                    req.To);
                backfill.ValidateRequest(request);
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblemDetails.Validation(context, "request", ex.Message);
            }

            try
            {
                var result = await backfill.RunAsync(request, ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (InvalidOperationException)
            {
                return ApiProblemDetails.Conflict(
                    context,
                    "Another backfill operation is already active.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ApiProblemDetails.Timeout(context);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(BackfillScheduleEndpoints))
                    .LogError(ex, "Backfill gap-fill execution failed.");
                return ApiProblemDetails.Internal(
                    context,
                    "The backfill gap-fill operation could not be completed.");
            }
        })
        .WithName("RunBackfillGapFill")
        .WithDescription("Runs an immediate gap-fill operation to repair missing data for specified symbols.")
        .Produces<BackfillResult>(200)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Backfill presets
        group.MapGet(UiApiRoutes.BackfillPresets, () =>
        {
            var presets = new[]
            {
                new { name = "daily-eod", description = "End-of-day bars for US equities", symbols = new[] { "SPY", "QQQ", "IWM" }, provider = "stooq", cronExpression = "0 18 * * 1-5" },
                new { name = "weekly-full", description = "Weekly full backfill for watchlist", symbols = Array.Empty<string>(), provider = "alpaca", cronExpression = "0 6 * * 6" },
                new { name = "gap-fill", description = "Automatic gap detection and repair", symbols = Array.Empty<string>(), provider = "auto", cronExpression = "0 2 * * *" }
            };
            return Results.Json(new { presets, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetBackfillPresets")
        .WithDescription("Returns built-in backfill preset configurations for common use cases.")
        .Produces(200)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Backfill executions
        group.MapGet(UiApiRoutes.BackfillExecutions, (HttpContext context, int? limit, IServiceProvider services) =>
        {
            var history = services.GetService<BackfillExecutionHistory>();
            if (history is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill execution history");

            var executions = history.GetRecentExecutions(limit ?? 50);
            var defaultProvider = services
                .GetService<IOptions<AutoGapRemediationPolicy>>()?
                .Value.DefaultProvider;
            var response = BackfillExecutionContractProjection.Build(
                executions,
                defaultProvider,
                DateTimeOffset.UtcNow);
            return Results.Json(response, jsonOptions);
        })
        .WithName("GetBackfillExecutions")
        .WithDescription("Returns typed recent execution history with remediation SLA and compatibility evidence.")
        .Produces<BackfillExecutionHistoryResponse>(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Backfill statistics
        group.MapGet(UiApiRoutes.BackfillStatistics, (
            HttpContext context,
            [FromServices] BackfillExecutionHistory? history,
            [FromServices] BackfillScheduleManager? schedMgr) =>
        {
            if (history is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill execution history");
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var systemSummary = history.GetSystemSummary(TimeSpan.FromDays(30));
            var statusSummary = schedMgr.GetStatusSummary();
            var autoExecutions = history.GetRecentExecutions(250)
                .Where(e => e.Trigger == ExecutionTrigger.AutoRemediation)
                .ToList();

            return Results.Json(new
            {
                schedules = statusSummary,
                executions = systemSummary,
                autoRemediation = new
                {
                    totalTriggers = autoExecutions.Count,
                    completed = autoExecutions.Count(e => string.Equals(e.AutoRemediationLastOutcome, "Completed", StringComparison.OrdinalIgnoreCase)),
                    failed = autoExecutions.Count(e => string.Equals(e.AutoRemediationLastOutcome, "FailedTransient", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.AutoRemediationLastOutcome, "FailedPermanent", StringComparison.OrdinalIgnoreCase)),
                    latestTriggerReason = autoExecutions.FirstOrDefault()?.AutoRemediationTriggerReason,
                    latestAttemptCount = autoExecutions.FirstOrDefault()?.AutoRemediationAttemptCount ?? 0,
                    latestOutcome = autoExecutions.FirstOrDefault()?.AutoRemediationLastOutcome
                },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetBackfillStatistics")
        .WithDescription("Returns aggregate backfill statistics including execution counts and success rates.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // List backfill schedules
        group.MapGet(UiApiRoutes.BackfillSchedules, (
            HttpContext context,
            [FromServices] BackfillScheduleManager? schedMgr) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var schedules = schedMgr.GetAllSchedules();
            return Results.Json(new
            {
                schedules,
                total = schedules.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetBackfillSchedules")
        .WithDescription("Lists all configured backfill schedules with their current state.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Create backfill schedule
        group.MapPost(UiApiRoutes.BackfillSchedules, async (
            HttpContext context,
            [FromServices] BackfillScheduleManager? schedMgr,
            [FromServices] ILoggerFactory loggerFactory,
            BackfillSchedule schedule,
            CancellationToken ct) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            try
            {
                var created = await schedMgr.CreateScheduleAsync(schedule, ct).ConfigureAwait(false);
                return Results.Json(created, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return ApiProblemDetails.Validation(context, "schedule", ex.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(BackfillScheduleEndpoints))
                    .LogError(ex, "Backfill schedule creation failed.");
                return ApiProblemDetails.Internal(
                    context,
                    "The backfill schedule could not be created.");
            }
        })
        .WithName("CreateBackfillSchedule")
        .WithDescription("Creates a new backfill schedule with cron expression and symbol configuration.")
        .Produces(200)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Get backfill schedule by ID
        group.MapGet(UiApiRoutes.BackfillSchedulesById, (
            HttpContext context,
            string id,
            [FromServices] BackfillScheduleManager? schedMgr) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var schedule = schedMgr.GetSchedule(id);
            return schedule is null
                ? ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.")
                : Results.Json(schedule, jsonOptions);
        })
        .WithName("GetBackfillScheduleById")
        .WithDescription("Returns a specific backfill schedule by ID.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Delete backfill schedule (REST-style: DELETE /api/backfill/schedules/{id})
        group.MapDelete(UiApiRoutes.BackfillSchedulesById, async (
            HttpContext context,
            string id,
            [FromServices] BackfillScheduleManager? schedMgr,
            CancellationToken ct) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var deleted = await schedMgr.DeleteScheduleAsync(id, ct).ConfigureAwait(false);
            return deleted
                ? Results.Ok()
                : ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.");
        })
        .WithName("DeleteBackfillScheduleById")
        .WithDescription("Deletes a backfill schedule by ID (REST-style endpoint).")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Enable backfill schedule
        group.MapPost(UiApiRoutes.BackfillSchedulesEnable, async (
            HttpContext context,
            string id,
            [FromServices] BackfillScheduleManager? schedMgr,
            CancellationToken ct) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var ok = await schedMgr.SetScheduleEnabledAsync(id, true, ct).ConfigureAwait(false);
            return ok
                ? Results.Ok()
                : ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.");
        })
        .WithName("EnableBackfillSchedule")
        .WithDescription("Enables a previously disabled backfill schedule.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Disable backfill schedule
        group.MapPost(UiApiRoutes.BackfillSchedulesDisable, async (
            HttpContext context,
            string id,
            [FromServices] BackfillScheduleManager? schedMgr,
            CancellationToken ct) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");

            var ok = await schedMgr.SetScheduleEnabledAsync(id, false, ct).ConfigureAwait(false);
            return ok
                ? Results.Ok()
                : ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.");
        })
        .WithName("DisableBackfillSchedule")
        .WithDescription("Disables a backfill schedule, preventing future automatic executions.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Run backfill schedule now
        group.MapPost(UiApiRoutes.BackfillSchedulesRun, async (
            HttpContext context,
            string id,
            [FromServices] BackfillScheduleManager? schedMgr,
            [FromServices] ScheduledBackfillService? scheduledBackfill,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (schedMgr is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill schedule manager");
            if (scheduledBackfill is null)
                return ApiProblemDetails.ServiceUnavailable(context, "scheduled backfill runtime");

            var schedule = schedMgr.GetSchedule(id);
            if (schedule is null)
                return ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.");

            try
            {
                var execution = await scheduledBackfill
                    .TriggerManualExecutionAsync(id, ct)
                    .ConfigureAwait(false);
                return Results.Json(new
                {
                    executionId = execution.ExecutionId,
                    scheduleId = id,
                    status = execution.Status.ToString(),
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return ApiProblemDetails.NotFound(context, "The requested backfill schedule was not found.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                return ApiProblemDetails.Timeout(context);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(nameof(BackfillScheduleEndpoints))
                    .LogError(ex, "Manual backfill schedule execution failed for {ScheduleId}.", id);
                return ApiProblemDetails.Internal(
                    context,
                    "The backfill schedule could not be started.");
            }
        })
        .WithName("RunBackfillScheduleNow")
        .WithDescription("Triggers immediate execution of a backfill schedule, ignoring its cron timing.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .RequirePermission(UserPermission.TriggerBackfill);

        // Backfill schedule history
        group.MapGet(UiApiRoutes.BackfillSchedulesHistory, (
            HttpContext context,
            string id,
            int? limit,
            [FromServices] BackfillExecutionHistory? history) =>
        {
            if (history is null)
                return ApiProblemDetails.ServiceUnavailable(context, "backfill execution history");

            var executions = history.GetExecutionsForSchedule(id, limit ?? 50);
            return Results.Json(new { executions, total = executions.Count }, jsonOptions);
        })
        .WithName("GetBackfillScheduleHistory")
        .WithDescription("Returns execution history for a specific backfill schedule.")
        .Produces(200)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);

        // Backfill schedule templates
        group.MapGet(UiApiRoutes.BackfillSchedulesTemplates, () =>
        {
            var templates = new[]
            {
                new { id = "eod-equities", name = "End-of-Day Equities", cronExpression = "0 18 * * 1-5", backfillType = "EndOfDay", description = "Daily EOD bar backfill after US market close" },
                new { id = "gap-fill-daily", name = "Daily Gap Fill", cronExpression = "0 2 * * *", backfillType = "GapFill", description = "Nightly gap detection and repair" },
                new { id = "weekly-full", name = "Weekly Full Backfill", cronExpression = "0 6 * * 6", backfillType = "FullBackfill", description = "Full backfill on Saturday mornings" },
                new { id = "rolling-30d", name = "Rolling 30-Day Window", cronExpression = "0 3 * * 1-5", backfillType = "RollingWindow", description = "Maintain rolling 30-day data window" }
            };
            return Results.Json(new { templates }, jsonOptions);
        })
        .WithName("GetBackfillScheduleTemplates")
        .WithDescription("Returns predefined schedule templates for common backfill patterns.")
        .Produces(200)
        .RequireAnyPermission(UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill);
    }

    private sealed record GapFillRequest(string[]? Symbols, string? Provider, DateOnly? From, DateOnly? To);
}
