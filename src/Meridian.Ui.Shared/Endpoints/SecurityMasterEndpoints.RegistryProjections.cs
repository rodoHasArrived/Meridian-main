using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// The generic registry projection read: one security's relational terms projection for any
    /// class the projection registry writes, as column-to-value data driven by the descriptor. It
    /// is the read path for the registry classes that have no typed reader; the typed per-class
    /// reference endpoints stay as they are. The security's current asset class gates the read, so
    /// an unknown class, a class without a registry projection and a class that is not the
    /// security's own all answer 404.
    /// </summary>
    private static void MapRegistryProjectionEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        var options = new JsonSerializerOptions(jsonOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                SecurityTermsProjectionReadJsonContext.Default,
                jsonOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
        };

        group.MapGet(UiApiRoutes.SecurityMasterRegistryProjection, async (
            string assetClass,
            Guid securityId,
            [FromServices] IRegistryProjectionReadService readService,
            CancellationToken ct) =>
        {
            var projection = await readService.GetAsync(assetClass, securityId, ct).ConfigureAwait(false);
            return projection is null
                ? Results.NotFound()
                : Results.Json(projection, options);
        })
        .WithName("GetSecurityMasterRegistryProjection").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityTermsProjectionReadDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
