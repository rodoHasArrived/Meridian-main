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
/// The signed-in operator's last visit to a workstation surface (C3), the baseline for "since your
/// last visit" markers in the browser and WPF. Only the operator themselves can read or record it.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static void MapOperatorSurfaceVisitEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationOperatorSurfaceVisit), (string surface, HttpContext context)
            => HandleOperatorSurfaceVisitAsync(surface, context, jsonOptions, record: false))
        .WithName("GetWorkstationOperatorSurfaceVisit").RequireAuthenticatedSession()
        .Produces<OperatorSurfaceVisitDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationOperatorSurfaceVisit), (string surface, HttpContext context)
            => HandleOperatorSurfaceVisitAsync(surface, context, jsonOptions, record: true))
        .WithName("RecordWorkstationOperatorSurfaceVisit").RequireAuthenticatedSession()
        .Produces<OperatorSurfaceVisitDto>(200)
        .Produces(400)
        .Produces(401)
        .Produces(501);
    }

    private static async Task<IResult> HandleOperatorSurfaceVisitAsync(
        string surface,
        HttpContext context,
        JsonSerializerOptions jsonOptions,
        bool record)
    {
        var store = context.RequestServices.GetService<IOperatorSurfaceVisitStore>();
        if (store is null)
        {
            return Results.Problem("Operator surface visits are not registered.", statusCode: StatusCodes.Status501NotImplemented);
        }

        var operatorKey = ResolveOperatorPreferenceKey(context);
        if (operatorKey is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var visit = record
                ? await store.RecordVisitAsync(operatorKey, surface, context.RequestAborted).ConfigureAwait(false)
                : await store.GetAsync(operatorKey, surface, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(visit, jsonOptions);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
