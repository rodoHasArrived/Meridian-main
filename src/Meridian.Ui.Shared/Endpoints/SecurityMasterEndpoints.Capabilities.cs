using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    private static void MapInstrumentCapabilities(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterCapabilities, async (
            [FromQuery] Guid[] securityIds,
            [FromServices] InstrumentCapabilityReadService service,
            CancellationToken ct) =>
        {
            if (securityIds.Length is 0 or > InstrumentCapabilityReadService.MaximumBatchSize || securityIds.Contains(Guid.Empty))
                return Results.BadRequest(new { error = "Select between 1 and 200 nonempty security identifiers." });
            var result = await service.GetBatchAsync(securityIds, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        }).WithName("GetSecurityMasterCapabilities")
            .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
            .Produces<InstrumentCapabilityBatchDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
    }
}
