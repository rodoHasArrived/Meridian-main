using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Strategies.Models;
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
            if (TryAuthorizePromotionRead(context) is { } authorizationFailure)
                return authorizationFailure;

            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var result = await service.EvaluateAsync(
                    runId,
                    ResolveStrategyRunScope(context),
                    ct: context.RequestAborted)
                .ConfigureAwait(false);

            if (!result.Found)
                return Results.NotFound(result);

            return Results.Json(result, jsonOptions);
        })
        .WithName("EvaluatePromotion")
        .Produces<PromotionEvaluationResult>(200)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost("/approve", async (PromotionApprovalRequest request, HttpContext context) =>
        {
            if (TryAuthorizePromotionMutation(context, jsonOptions) is { } authorizationFailure)
                return authorizationFailure;

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var normalizedRequest = request with { ApprovedBy = actor };

            var result = await service.ApproveAsync(
                    normalizedRequest,
                    ResolveStrategyRunScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);

            return result.Success
                ? Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ApprovePromotion")
        .Produces<PromotionDecisionResult>(201)
        .Produces<PromotionDecisionResult>(400)
        .Produces(401)
        .Produces<PromotionDecisionResult>(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/reject", async (PromotionRejectionRequest request, HttpContext context) =>
        {
            if (TryAuthorizePromotionMutation(context, jsonOptions) is { } authorizationFailure)
                return authorizationFailure;

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var normalizedRequest = request with { RejectedBy = actor };

            var result = await service.RejectAsync(
                    normalizedRequest,
                    ResolveStrategyRunScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("RejectPromotion")
        .Produces<PromotionDecisionResult>(200)
        .Produces(401)
        .Produces<PromotionDecisionResult>(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/history", async (HttpContext context) =>
        {
            if (TryAuthorizePromotionRead(context) is { } authorizationFailure)
                return authorizationFailure;

            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            var history = await service.GetPromotionHistoryAsync(
                    ResolveStrategyRunScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(history, jsonOptions);
        })
        .WithName("GetPromotionHistory")
        .Produces<IReadOnlyList<StrategyPromotionRecord>>(200)
        .Produces(401)
        .Produces(403)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope();

        group.MapPost("/runs/{runId}/walk-forward-evidence", async (
            string runId,
            RecordWalkForwardEvidenceRequest request,
            HttpContext context) =>
        {
            if (TryAuthorizePromotionMutation(context, jsonOptions) is { } authorizationFailure)
                return authorizationFailure;

            var service = context.RequestServices.GetService<PromotionService>();
            if (service is null)
                return Results.Problem("Promotion service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

            if (request is null)
                return Results.BadRequest(new { error = "Walk-forward evidence payload is required." });

            var evidence = new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: request.OutOfSampleSharpeRatio,
                OutOfSampleMaxDrawdownPercent: request.OutOfSampleMaxDrawdownPercent,
                DegradationRatio: request.DegradationRatio,
                WindowCount: request.WindowCount,
                RecordedAt: DateTimeOffset.UtcNow,
                SourceReference: string.IsNullOrWhiteSpace(request.SourceReference) ? null : request.SourceReference.Trim());

            // Reject values outside their mathematical domain (non-finite numbers, negative
            // drawdown magnitudes) — they would pass the promotion policy's threshold
            // comparisons and let fabricated evidence clear the live-promotion gate.
            if (evidence.Validate() is { } validationError)
                return Results.BadRequest(new { error = validationError });

            var updated = await service.RecordWalkForwardEvidenceAsync(
                    runId,
                    evidence,
                    ResolveStrategyRunScope(context),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return updated is null
                ? Results.NotFound(new { error = $"Run '{runId}' was not found." })
                : Results.Json(updated.WalkForwardEvidence, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("RecordWalkForwardEvidence")
        .Produces<StrategyRunWalkForwardEvidence>(201)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501)
        .RequireWorkstationTenantCompanyScope()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static StrategyRunReadScope ResolveStrategyRunScope(HttpContext context)
    {
        var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return new StrategyRunReadScope(tenantContext.TenantId, tenantContext.CompanyId);
    }

    private static IResult? TryAuthorizePromotionRead(HttpContext context)
        => EndpointAuthorization.TryGetPermissions(context, out _)
            ? HasPromotionReadPermission(context)
                ? null
                : EndpointHelpers.Forbidden()
            : Results.Unauthorized();

    private static IResult? TryAuthorizePromotionMutation(HttpContext context, JsonSerializerOptions jsonOptions)
        => EndpointAuthorization.TryGetPermissions(context, out _)
            ? HasPromotionMutationPermission(context)
                ? null
                : Results.Json(new PromotionDecisionResult(false, null, null, "Forbidden."), jsonOptions, statusCode: StatusCodes.Status403Forbidden)
            : Results.Unauthorized();

    private static bool HasPromotionReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewStrategies,
            UserPermission.ManageStrategies);

    private static bool HasPromotionMutationPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageStrategies);
}
