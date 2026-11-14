using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoint mapping for the relationship-aware provider routing layer.
/// </summary>
public static class ProviderRoutingEndpoints
{
    public static void MapProviderRoutingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapProviderCredentialOwnershipEndpoints(jsonOptions);
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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope)
                return EndpointHelpers.Forbidden();
            var result = await service.GetConnectionsForTenantAsync(tenant.TenantId!, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingConnections").RequirePermission(UserPermission.ManageCredentials)
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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope)
                return EndpointHelpers.Forbidden();
            var result = await service.GetBindingsForTenantAsync(tenant.TenantId!, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingBindings").RequirePermission(UserPermission.ManageCredentials)
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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope)
                return EndpointHelpers.Forbidden();
            var result = await service.GetTrustSnapshotsForTenantAsync(tenant.TenantId!, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetProviderRoutingTrustSnapshots").RequirePermission(UserPermission.ManageCredentials)
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

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!tenant.HasTenantScope)
                return EndpointHelpers.Forbidden();
            try
            {
                RoutePreviewResponse result;
                if (request.PreviewBackfill)
                {
                    if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewConfig | UserPermission.ManageProviders))
                        return EndpointHelpers.Forbidden();
                    var store = context.RequestServices.GetService<Meridian.Application.UI.ConfigStore>();
                    var resolver = context.RequestServices.GetService<IProviderCredentialResolver>();
                    if (store is null || resolver is null)
                        return ApiProblemDetails.ServiceUnavailable(context, "backfill routing preview");
                    var prepared = await resolver.PrepareAsync(ct: context.RequestAborted).ConfigureAwait(false);
                    using (prepared.Activate())
                    {
                        result = BackfillRoutingPreview.Preview(store.LoadRequired(), request,
                            config => new ProviderFactory(config, resolver, services: context.RequestServices).CreateBackfillProviders(),
                            context.RequestAborted);
                    }
                }
                else
                {
                    result = await service.PreviewForTenantAsync(request, tenant.TenantId!, context.RequestAborted).ConfigureAwait(false);
                }
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException exception)
            {
                return ApiProblemDetails.Validation(context, "proposedConfig", exception.Message);
            }
            catch (NotSupportedException)
            {
                return ApiProblemDetails.ServiceUnavailable(context, "proposed routing preview");
            }
        })
        .WithName("PreviewProviderRoute").RequirePermission(UserPermission.ManageCredentials)
        .WithDescription("Previews current or proposed provider routing without saving or fetching market data.")
        .Produces<RoutePreviewResponse>(StatusCodes.Status200OK);
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
