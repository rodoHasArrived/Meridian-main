using System.Text.Json;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for the Backtest → Paper → Live promotion workflow.
/// Enables operators to evaluate, approve, or reject strategy promotions
/// through the web dashboard.
/// </summary>
public static class PromotionEndpoints
{
    public static void MapPromotionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/promotion").WithTags("Promotion");

        group.MapGet("/evaluate/{runId}", async (string runId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var result = await service.EvaluateAsync(runId, ct: context.RequestAborted).ConfigureAwait(false);

            if (!result.Found)
                return Results.NotFound(result);

            return Results.Json(result, jsonOptions);
        })
        .WithName("EvaluatePromotion")
        .Produces<PromotionEvaluationResult>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost("/approve", async (PromotionApprovalRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var result = await service.ApproveAsync(request, context.RequestAborted).ConfigureAwait(false);

            return result.Success
                ? Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ApprovePromotion")
        .Produces<PromotionDecisionResult>(201)
        .Produces<PromotionDecisionResult>(400)
        .Produces(501);

        group.MapPost("/reject", async (PromotionRejectionRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var result = await service.RejectAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RejectPromotion")
        .Produces<PromotionDecisionResult>(200)
        .Produces(501);

        group.MapGet("/history", (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var history = service.GetPromotionHistory();
            return Results.Json(history, jsonOptions);
        })
        .WithName("GetPromotionHistory")
        .Produces<IReadOnlyList<StrategyPromotionRecord>>(200)
        .Produces(501);
    }
}
