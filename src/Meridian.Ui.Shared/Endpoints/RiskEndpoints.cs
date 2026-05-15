using System.Text.Json;
using Meridian.Contracts.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Exposes runtime risk rule status and operator-managed rule configuration.
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
    }

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
            return true;
        }

        var permissions = value is UserPermission userPermission
            ? userPermission
            : UserPermission.None;

        return permissions.HasFlag(UserPermission.ManageOrders);
    }
}
