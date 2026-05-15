using System.Text.Json;
using Meridian.Contracts.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class RiskEndpoints
{
    public static void MapRiskEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/risk").WithTags("Risk");

        group.MapGet("/rules", (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<OperatorRiskRuleService>();
            if (service is null)
            {
                return Results.Problem("Risk rule service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var rules = service.GetRuleStatuses();
            return Results.Json(rules, jsonOptions);
        })
        .WithName("GetRiskRules")
        .Produces<IReadOnlyList<RiskRuleStatusSnapshot>>(200)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/status", (string ruleName, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<OperatorRiskRuleService>();
            if (service is null)
            {
                return Results.Problem("Risk rule service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var status = service.GetRuleStatus(ruleName);
            return status is null
                ? Results.NotFound()
                : Results.Json(status, jsonOptions);
        })
        .WithName("GetRiskRuleStatus")
        .Produces<RiskRuleStatusSnapshot>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/rules/{ruleName}/config", (string ruleName, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<OperatorRiskRuleService>();
            if (service is null)
            {
                return Results.Problem("Risk rule service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                return Results.Json(service.GetRuleConfig(ruleName), jsonOptions);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.NotFound();
            }
        })
        .WithName("GetRiskRuleConfig")
        .Produces<RiskRuleConfigSnapshot>(200)
        .Produces(404)
        .Produces(503);

        group.MapPut("/rules/{ruleName}/config", async (string ruleName, UpdateRiskRuleConfigRequest request, HttpContext context) =>
        {
            if (!HasRiskConfigPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<OperatorRiskRuleService>();
            if (service is null)
            {
                return Results.Problem("Risk rule service is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var actor = ResolveActor(context);
                var updated = await service.UpdateRuleConfigAsync(
                    ruleName,
                    request.Config,
                    actor,
                    request.Reason,
                    context.RequestAborted).ConfigureAwait(false);

                return Results.Json(updated, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.NotFound();
            }
        })
        .WithName("UpdateRiskRuleConfig")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<RiskRuleConfigSnapshot>(200)
        .Produces(400)
        .Produces(403)
        .Produces(404)
        .Produces(429)
        .Produces(503);
    }

    private static bool HasRiskConfigPermission(HttpContext context)
    {
        if (!context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserPermissionsKey, out var value))
        {
            return true;
        }

        var permissions = value is UserPermission userPermission
            ? userPermission
            : UserPermission.None;

        return permissions.HasFlag(UserPermission.ManageOrders);
    }

    private static string ResolveActor(HttpContext context)
    {
        if (context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var actor) && actor is string actorName && !string.IsNullOrWhiteSpace(actorName))
        {
            return actorName;
        }

        if (context.Request.Headers.TryGetValue("X-Meridian-Actor", out var actorHeader))
        {
            var headerValue = actorHeader.ToString();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        return "operator";
    }
}

public sealed record UpdateRiskRuleConfigRequest(
    IReadOnlyDictionary<string, string> Config,
    string? Reason = null);
