using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// The signed-in operator's default operating scope (C1). Browser and WPF read it when a session
/// opens with no scope in its link or local storage; only the operator themselves can read or set it.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static void MapOperatorScopePreferenceEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationOperatorScopePreference), async (HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IOperatorScopePreferenceStore>();
            if (store is null)
            {
                return Results.Problem("Operator scope preferences are not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var operatorKey = ResolveOperatorPreferenceKey(context);
            if (operatorKey is null)
            {
                return Results.Unauthorized();
            }

            var preference = await store.GetAsync(operatorKey, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(preference, jsonOptions);
        })
        .WithName("GetWorkstationOperatorScopePreference").RequireAuthenticatedSession()
        .Produces<OperatorScopePreferenceDto>(200)
        .Produces(401)
        .Produces(501);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationOperatorScopePreference), async (OperatorScopePreferenceSaveRequest request, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IOperatorScopePreferenceStore>();
            if (store is null)
            {
                return Results.Problem("Operator scope preferences are not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var operatorKey = ResolveOperatorPreferenceKey(context);
            if (operatorKey is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var saved = await store.SetAsync(operatorKey, request.DefaultScope, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveWorkstationOperatorScopePreference").RequireAuthenticatedSession()
        .Produces<OperatorScopePreferenceDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(501);
    }

    /// <summary>Tenant, company and user, so one operator's default never reaches another.</summary>
    private static string? ResolveOperatorPreferenceKey(HttpContext context)
    {
        var user = context.Items[LoginSessionMiddleware.CurrentUserKey] as string;
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        var tenant = context.Items[LoginSessionMiddleware.CurrentTenantIdKey] as string ?? string.Empty;
        var company = context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] as string ?? string.Empty;
        return $"{tenant}|{company}|{user.Trim()}";
    }
}
