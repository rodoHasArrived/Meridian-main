using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapFeatureCapabilityEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationFeatureCapabilities), (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<FeatureCapabilitySettingsService>();
            if (service is null)
            {
                return Results.Problem("Feature capability settings service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Json(service.Get(), jsonOptions);
        })
        .WithName("GetWorkstationFeatureCapabilities")
        .Produces<FeatureCapabilitySettingsResponse>(200)
        .Produces(501);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationFeatureCapabilityByKey), async (
            string capabilityKey,
            FeatureCapabilityToggleRequest request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<FeatureCapabilitySettingsService>();
            if (service is null)
            {
                return Results.Problem("Feature capability settings service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var response = await service
                .SetAsync(capabilityKey, request.IsEnabled, context.RequestAborted)
                .ConfigureAwait(false);
            return response is null
                ? Results.NotFound(new { error = $"Feature capability '{capabilityKey}' was not found." })
                : Results.Json(response, jsonOptions);
        })
        .WithName("SetWorkstationFeatureCapability")
        .Produces<FeatureCapabilitySettingsResponse>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);
    }
}
