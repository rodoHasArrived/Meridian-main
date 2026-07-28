using System.Text.Json;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Flat projection of a parked risk escalation for API and workstation consumption.</summary>
public sealed record RiskEscalationDto(
    string EscalationId,
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal? LimitPrice,
    string Reason,
    string? RuleName,
    string Status,
    DateTimeOffset ParkedAt,
    string? ResolvedBy,
    string? ResolutionReason,
    DateTimeOffset? ResolvedAt);

/// <summary>Operator resolution request for a parked escalation.</summary>
/// <param name="Reason">Operator-supplied rationale, audited with the decision.</param>
/// <param name="Release">Approve only: when true (default) the approved order is immediately resubmitted through the full pre-trade gate.</param>
public sealed record RiskEscalationResolutionRequest(string? Reason = null, bool Release = true);

/// <summary>Approve response: the resolved escalation plus the release order result when resubmitted.</summary>
public sealed record RiskEscalationApprovalResponse(RiskEscalationDto Escalation, OrderResult? ReleaseResult);

/// <summary>
/// Exposes runtime risk rule status, operator-managed rule configuration, and the
/// governed-approval queue for escalated orders.
/// </summary>
public static class RiskEndpoints
{
    public static void MapRiskEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        MapRiskRoutes(app.MapGroup("/api/risk").WithTags("Risk"), jsonOptions);
        // Versioning scaffold for future contract evolution.
        MapRiskRoutes(app.MapGroup("/api/v1/risk").WithTags("Risk"), jsonOptions);
    }

    private static void MapRiskRoutes(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/rules", async (HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var statuses = await runtime.GetAllStatusesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(statuses, jsonOptions);
        })
        .Produces<IReadOnlyList<RiskRuleStatusDto>>(200)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/status", async (string ruleName, HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var status = await runtime.GetStatusAsync(ruleName, context.RequestAborted).ConfigureAwait(false);
            return status is null ? Results.NotFound() : Results.Json(status, jsonOptions);
        })
        .Produces<RiskRuleStatusDto>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/config", (string ruleName, HttpContext context) =>
        {
            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var config = runtime.GetConfig(ruleName);
            return config is null ? Results.NotFound() : Results.Json(config, jsonOptions);
        })
        .Produces<RiskRuleConfigDto>(200)
        .Produces(404)
        .Produces(503);

        group.MapPut("/rules/{ruleName}/config", async (
            string ruleName,
            RiskRuleConfigUpdateRequest request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var runtime = context.RequestServices.GetService<RiskRuleRuntimeService>();
            if (runtime is null)
            {
                return Results.Problem("Risk runtime service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var updated = await runtime
                    .UpdateConfigAsync(ruleName, request, ResolveActor(context), context.RequestAborted)
                    .ConfigureAwait(false);
                return updated is null ? Results.NotFound() : Results.Json(updated, jsonOptions);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .Produces<RiskRuleConfigDto>(200)
        .Produces(400)
        .Produces(404)
        .Produces(503);

        group.MapGet("/escalations", (HttpContext context) =>
        {
            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var entries = queue.GetRecent().Select(ToDto).ToArray();
            return Results.Json(entries, jsonOptions);
        })
        .Produces<IReadOnlyList<RiskEscalationDto>>(200)
        .Produces(503);

        group.MapPost("/escalations/{escalationId}/approve", async (
            string escalationId,
            RiskEscalationResolutionRequest? request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var actor = ResolveActor(context);
            var approved = queue.Approve(escalationId, actor, request?.Reason);
            if (approved is null)
            {
                return Results.NotFound();
            }

            // Release the parked order back through the full pre-trade gate. The approval
            // token satisfies exactly this escalation once; every other rule still enforces.
            OrderResult? releaseResult = null;
            if (request?.Release != false &&
                context.RequestServices.GetService<IOrderManager>() is { } oms)
            {
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [RiskEscalationQueueService.ApprovalMetadataKey] = approved.EscalationId,
                    ["actor"] = actor,
                    ["correlationId"] = $"risk-escalation-{approved.EscalationId}"
                };
                if (approved.Request.Metadata is not null)
                {
                    foreach (var (key, value) in approved.Request.Metadata)
                    {
                        metadata.TryAdd(key, value);
                    }
                }

                var releaseRequest = approved.Request with { Metadata = metadata };
                releaseResult = await oms.PlaceOrderAsync(releaseRequest, context.RequestAborted).ConfigureAwait(false);
            }

            var latest = queue.TryGet(escalationId) ?? approved;
            return Results.Json(
                new RiskEscalationApprovalResponse(ToDto(latest), releaseResult),
                jsonOptions);
        })
        .Produces<RiskEscalationApprovalResponse>(200)
        .Produces(404)
        .Produces(503);

        group.MapPost("/escalations/{escalationId}/deny", (
            string escalationId,
            RiskEscalationResolutionRequest? request,
            HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var queue = context.RequestServices.GetService<RiskEscalationQueueService>();
            if (queue is null)
            {
                return Results.Problem("Risk escalation queue is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var denied = queue.Deny(escalationId, ResolveActor(context), request?.Reason);
            return denied is null ? Results.NotFound() : Results.Json(ToDto(denied), jsonOptions);
        })
        .Produces<RiskEscalationDto>(200)
        .Produces(404)
        .Produces(503);
    }

    private static RiskEscalationDto ToDto(RiskEscalationEntry entry) => new(
        EscalationId: entry.EscalationId,
        Symbol: entry.Request.Symbol,
        Side: entry.Request.Side.ToString(),
        Type: entry.Request.Type.ToString(),
        Quantity: entry.Request.Quantity,
        LimitPrice: entry.Request.LimitPrice,
        Reason: entry.Reason,
        RuleName: entry.RuleName,
        Status: entry.Status.ToString(),
        ParkedAt: entry.ParkedAt,
        ResolvedBy: entry.ResolvedBy,
        ResolutionReason: entry.ResolutionReason,
        ResolvedAt: entry.ResolvedAt);

    private static string ResolveActor(HttpContext context)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is string userName &&
            !string.IsNullOrWhiteSpace(userName))
        {
            return userName.Trim();
        }

        if (context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return context.User.Identity.Name!;
        }

        return "operator";
    }

    private static bool HasRiskConfigPermission(HttpContext context)
    {
        if (!context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var value))
        {
            return false;
        }

        var permissions = value is UserPermission userPermission
            ? userPermission
            : UserPermission.None;

        return permissions.HasFlag(UserPermission.ManageOrders);
    }
}
