using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoint mapping for the relationship-aware provider routing layer.
/// </summary>
public static class ProviderRoutingEndpoints
{
    public static void MapProviderRoutingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Routing");
        group.RequireWorkstationTenantScope();

        group.MapGet(UiApiRoutes.ProviderRoutingConnections, async (
            ProviderConnectionService service,
            HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.GetConnectionsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingConnections")
        .WithDescription("Returns configured provider-routing connections.")
        .Produces<ProviderConnectionDto[]>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ProviderRoutingBindings, async (
            ProviderBindingService service,
            HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.GetBindingsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingBindings")
        .WithDescription("Returns configured provider-routing capability bindings.")
        .Produces<ProviderBindingDto[]>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.ProviderRoutingTrustSnapshots, async (
            ProviderTrustScoringService service,
            HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.GetTrustSnapshotsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingTrustSnapshots")
        .WithDescription("Returns provider trust snapshots for configured routing connections.")
        .Produces<ProviderTrustSnapshotDto[]>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.ProviderRoutingPreview, async (
            RoutePreviewRequest request,
            ProviderRouteExplainabilityService service,
            HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await service.PreviewAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("PreviewProviderRoute")
        .WithDescription("Previews the selected provider route for a capability request.")
        .Produces<RoutePreviewResponse>(StatusCodes.Status200OK);
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
