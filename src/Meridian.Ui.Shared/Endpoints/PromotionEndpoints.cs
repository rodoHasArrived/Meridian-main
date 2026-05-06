using System.Text.Json;
using Meridian.Contracts.Auth;
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

            if (!HasPromotionPermission(context))
                return Results.Json(new PromotionDecisionResult(false, null, null, "Forbidden."), jsonOptions, statusCode: StatusCodes.Status403Forbidden);

            var normalizedRequest = request with { ApprovedBy = ResolveActor(context) };

            var result = await service.ApproveAsync(normalizedRequest, context.RequestAborted).ConfigureAwait(false);

            return result.Success
                ? Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ApprovePromotion")
        .Produces<PromotionDecisionResult>(201)
        .Produces<PromotionDecisionResult>(400)
        .Produces<PromotionDecisionResult>(403)
        .Produces(501)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/reject", async (PromotionRejectionRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            if (!HasPromotionPermission(context))
                return Results.Json(new PromotionDecisionResult(false, null, null, "Forbidden."), jsonOptions, statusCode: StatusCodes.Status403Forbidden);

            var normalizedRequest = request with { RejectedBy = ResolveActor(context) };

            var result = await service.RejectAsync(normalizedRequest, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RejectPromotion")
        .Produces<PromotionDecisionResult>(200)
        .Produces<PromotionDecisionResult>(403)
        .Produces(501)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/history", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var history = await service.GetPromotionHistoryAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(history, jsonOptions);
        })
        .WithName("GetPromotionHistory")
        .Produces<IReadOnlyList<StrategyPromotionRecord>>(200)
        .Produces(501);
    }

    private static string? ResolveActor(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Meridian-Actor", out var actorValues))
        {
            var actor = actorValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(actor))
            {
                return actor;
            }
        }

        return null;
    }

    private static bool HasPromotionPermission(HttpContext context)
    {
        var permissions = context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] as UserPermission?;
        return permissions is not null && (permissions.Value & UserPermission.ManageStrategies) == UserPermission.ManageStrategies;
    }
}
