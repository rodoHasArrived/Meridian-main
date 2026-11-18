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
/// The signed-in operator's presentation preferences (theme, density, display time zone, page
/// descriptions, browser shortcut overrides), so they follow the operator to another browser or
/// machine. Only the operator themselves can read or replace them; the values are display-only.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static void MapOperatorPreferencesEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationOperatorPreferences), async (HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IOperatorPreferenceStore>();
            if (store is null)
            {
                return Results.Problem("Operator preferences are not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var operatorKey = ResolveOperatorPreferenceKey(context);
            if (operatorKey is null)
            {
                return Results.Unauthorized();
            }

            var preferences = await store.GetAsync(operatorKey, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(preferences, jsonOptions);
        })
        .WithName("GetWorkstationOperatorPreferences").RequireAuthenticatedSession()
        .Produces<OperatorPreferencesDto>(200)
        .Produces(401)
        .Produces(501);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationOperatorPreferences), async (OperatorPreferencesSaveRequest request, HttpContext context) =>
        {
            var store = context.RequestServices.GetService<IOperatorPreferenceStore>();
            if (store is null)
            {
                return Results.Problem("Operator preferences are not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var operatorKey = ResolveOperatorPreferenceKey(context);
            if (operatorKey is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var saved = await store.SetAsync(operatorKey, request.Preferences, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveWorkstationOperatorPreferences").RequireAuthenticatedSession()
        .Produces<OperatorPreferencesDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(501);
    }
}
