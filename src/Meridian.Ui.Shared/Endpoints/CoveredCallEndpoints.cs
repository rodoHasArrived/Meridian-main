using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Core.Exceptions;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Services.CoveredCall;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for the covered-call backtest UI (slice 1).
/// Endpoints return 503 when <see cref="ICoveredCallBacktestService"/> is not registered.
/// </summary>
public static class CoveredCallEndpoints
{
    public static void MapCoveredCallEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(string.Empty).WithTags("Strategies.CoveredCall");

        // POST /api/strategies/covered-call/runs — start a new backtest
        group.MapPost(UiApiRoutes.CoveredCallRuns, async (
            [FromBody] CoveredCallBacktestRequest body,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            try
            {
                var handle = await service.StartAsync(body, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(handle, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Problem(400, ex.Message);
            }
        })
        .WithName("StartCoveredCallBacktest")
        .Accepts<CoveredCallBacktestRequest>("application/json")
        .Produces<CoveredCallRunHandle>(200)
        .Produces<CoveredCallProblemDetails>(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // GET /api/strategies/covered-call/runs — list prior runs
        group.MapGet(UiApiRoutes.CoveredCallRuns, async (
            [FromQuery] int? limit,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            var runs = await service.ListRunsAsync(limit ?? 50, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(runs, jsonOptions);
        })
        .WithName("ListCoveredCallRuns")
        .Produces<IReadOnlyList<CoveredCallRunSummary>>(200)
        .Produces(503);

        // GET /api/strategies/covered-call/runs/{runId}/status
        group.MapGet(UiApiRoutes.CoveredCallRunStatus, async (
            string runId,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            var status = await service.GetStatusAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (status is null)
            {
                return Problem(404, $"Run '{runId}' is not known to this host.");
            }
            return Results.Json(status, jsonOptions);
        })
        .WithName("GetCoveredCallRunStatus")
        .Produces<CoveredCallRunStatusDto>(200)
        .Produces<CoveredCallProblemDetails>(404)
        .Produces(503);

        // GET /api/strategies/covered-call/runs/{runId}/result
        group.MapGet(UiApiRoutes.CoveredCallRunResult, async (
            string runId,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            // Try the persisted result first: GetResultAsync rehydrates from IStrategyRepository
            // when the in-memory cache has expired, so a completed run that has fallen out of
            // _runs (e.g. after a host restart) can still be opened from history.
            var result = await service.GetResultAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (result is not null)
            {
                return Results.Json(result, jsonOptions);
            }

            // No result yet — consult the in-memory status to distinguish "still running" from
            // "no such run" from "expired".
            var status = await service.GetStatusAsync(runId, context.RequestAborted).ConfigureAwait(false);
            if (status is null)
            {
                return Problem(404, $"Run '{runId}' is not known to this host.");
            }

            if (!string.Equals(status.Phase, "Completed", StringComparison.Ordinal))
            {
                return Problem(409, $"Run '{runId}' is in phase '{status.Phase}'. Keep polling status until it completes.");
            }

            return Problem(410, $"Run '{runId}' completed but the cached result has expired and no persisted result is available.");
        })
        .WithName("GetCoveredCallRunResult")
        .Produces<CoveredCallRunResult>(200)
        .Produces<CoveredCallProblemDetails>(404)
        .Produces<CoveredCallProblemDetails>(409)
        .Produces<CoveredCallProblemDetails>(410)
        .Produces(503);

        // POST /api/strategies/covered-call/runs/{runId}/cancel
        group.MapPost(UiApiRoutes.CoveredCallRunCancel, async (
            string runId,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            await service.CancelAsync(runId, context.RequestAborted).ConfigureAwait(false);
            var status = await service.GetStatusAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return status is null
                ? Problem(404, $"Run '{runId}' is not known to this host.")
                : Results.Json(status, jsonOptions);
        })
        .WithName("CancelCoveredCallRun")
        .Produces<CoveredCallRunStatusDto>(200)
        .Produces<CoveredCallProblemDetails>(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/strategies/covered-call/chain-preview
        group.MapPost(UiApiRoutes.CoveredCallChainPreview, async (
            [FromBody] CoveredCallChainPreviewRequest body,
            [FromServices] ICoveredCallBacktestService? service,
            HttpContext context) =>
        {
            if (service is null)
            {
                return Problem(503, "Covered-call backtest service is not registered.");
            }

            try
            {
                var preview = await service.PreviewChainAsync(body, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(preview, jsonOptions);
            }
            catch (ConfigurationException ex)
            {
                return Problem(503, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Problem(400, ex.Message);
            }
        })
        .WithName("PreviewCoveredCallChain")
        .Accepts<CoveredCallChainPreviewRequest>("application/json")
        .Produces<CoveredCallChainPreview>(200)
        .Produces<CoveredCallProblemDetails>(400)
        .Produces<CoveredCallProblemDetails>(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static IResult Problem(int statusCode, string detail) =>
        Results.Json(
            new CoveredCallProblemDetails(
                Title: detail,
                Status: statusCode,
                Detail: detail),
            statusCode: statusCode);
}
