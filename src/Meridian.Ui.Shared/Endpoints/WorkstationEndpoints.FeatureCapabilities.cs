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
            var service = context.RequestServices.GetRequiredService<FeatureCapabilitySettingsService>();
            return Results.Json(service.Get(), jsonOptions);
        })
        .WithName("GetWorkstationFeatureCapabilities")
        .Produces<FeatureCapabilitySettingsResponse>(200);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationFeatureCapabilityByKey), async (
            string capabilityKey,
            FeatureCapabilityToggleRequest request,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetRequiredService<FeatureCapabilitySettingsService>();
            var response = await service
                .SetAsync(capabilityKey, request.IsEnabled, context.RequestAborted)
                .ConfigureAwait(false);
            return response is null
                ? Results.NotFound(new { error = $"Feature capability '{capabilityKey}' was not found." })
                : Results.Json(response, jsonOptions);
        })
        .WithName("SetWorkstationFeatureCapability")
        .Produces<FeatureCapabilitySettingsResponse>(200)
        .Produces(404);
    }
}
