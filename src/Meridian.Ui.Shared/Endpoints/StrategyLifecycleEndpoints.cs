using System.Text.Json;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for querying and controlling the lifecycle of registered live strategies.
/// Exposes pause, stop, and status operations. Strategy start is handled separately via
/// the execution cockpit once an execution context is established.
/// </summary>
public static class StrategyLifecycleEndpoints
{
    public static void MapStrategyLifecycleEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/strategies").WithTags("Strategies");

        // --- Status queries ---

        group.MapGet("/status", (HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var statuses = manager.GetStatuses();
            var result = statuses.Select(kvp => new StrategyStatusDto(kvp.Key, kvp.Value.ToString())).ToArray();
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetAllStrategyStatuses")
        .Produces<StrategyStatusDto[]>(200)
        .Produces(503);

        group.MapGet("/{strategyId}/status", (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var statuses = manager.GetStatuses();
            if (!statuses.TryGetValue(strategyId, out var status))
                return Results.NotFound(new { strategyId, error = "Strategy not registered." });

            return Results.Json(new StrategyStatusDto(strategyId, status.ToString()), jsonOptions);
        })
        .WithName("GetStrategyStatus")
        .Produces<StrategyStatusDto>(200)
        .Produces(404)
        .Produces(503);

        // --- Lifecycle control ---

        group.MapPost("/{strategyId}/pause", async (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!manager.GetStatuses().ContainsKey(strategyId))
                return Results.NotFound(new { strategyId, error = "Strategy not registered." });

            try
            {
                await manager.PauseAsync(strategyId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new StrategyActionResult(strategyId, "paused", Success: true, Reason: null), jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new StrategyActionResult(strategyId, "pause", Success: false, Reason: ex.Message),
                    jsonOptions,
                    statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("PauseStrategy")
        .Produces<StrategyActionResult>(200)
        .Produces<StrategyActionResult>(409)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/{strategyId}/stop", async (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!manager.GetStatuses().ContainsKey(strategyId))
                return Results.NotFound(new { strategyId, error = "Strategy not registered." });

            try
            {
                await manager.StopAsync(strategyId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(new StrategyActionResult(strategyId, "stopped", Success: true, Reason: null), jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new StrategyActionResult(strategyId, "stop", Success: false, Reason: ex.Message),
                    jsonOptions,
                    statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("StopStrategy")
        .Produces<StrategyActionResult>(200)
        .Produces<StrategyActionResult>(409)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
}

// --- DTOs ---

/// <summary>Snapshot of a strategy's current lifecycle state.</summary>
public sealed record StrategyStatusDto(string StrategyId, string Status);

/// <summary>Result of a lifecycle control action (pause/stop).</summary>
public sealed record StrategyActionResult(
    string StrategyId,
    string Action,
    bool Success,
    string? Reason);
