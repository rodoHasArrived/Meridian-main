using System.Text.Json;
using Meridian.Application.Integrations;
using Meridian.Contracts.Api;
using Meridian.Contracts.Integrations;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapProviderIntegrationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationTemplates), (
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var catalog = context.RequestServices.GetService<ProviderIntegrationTemplateCatalog>();
            if (catalog is null)
            {
                return Results.Problem(
                    "Provider integration template catalog is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            return Results.Json(catalog.ListEntries(), jsonOptions);
        })
        .WithName("ListWorkstationProviderIntegrationTemplates")
        .Produces<IReadOnlyList<ProviderIntegrationTemplateCatalogEntryDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationTemplateById), (
            string manifestId,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var catalog = context.RequestServices.GetService<ProviderIntegrationTemplateCatalog>();
            if (catalog is null)
            {
                return Results.Problem(
                    "Provider integration template catalog is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var manifest = catalog.GetManifest(manifestId);
                return manifest is null
                    ? Results.NotFound(new { error = $"Provider integration template '{manifestId}' was not found." })
                    : Results.Json(manifest, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetWorkstationProviderIntegrationTemplate")
        .Produces<ProviderIntegrationManifestDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationManifestReadiness), async (
            string manifestId,
            string? connectionId,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationActivationReadinessService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration activation-readiness service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var readiness = await service
                    .EvaluateAsync(manifestId, connectionId, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(readiness, jsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetWorkstationProviderIntegrationReadiness")
        .Produces<ProviderIntegrationActivationReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationManualCsvDryRun), async (
            ManualCsvProviderIntegrationDryRunRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationDryRunService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration manual CSV dry-run service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var result = await service
                    .RunManualCsvDryRunAsync(request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RunWorkstationProviderIntegrationManualCsvDryRun")
        .Produces<ProviderIntegrationDryRunResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationRestDryRun), async (
            ProviderIntegrationRestDryRunRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationRestDryRunService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration REST dry-run service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var result = await service
                    .RunRestDryRunAsync(request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RunWorkstationProviderIntegrationRestDryRun")
        .Produces<ProviderIntegrationDryRunResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationConnectionMonitor), async (
            string connectionId,
            int? recentRunLimit,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationMonitoringService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration monitoring service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var monitor = await service
                    .GetConnectionMonitorAsync(
                        connectionId,
                        recentRunLimit ?? 10,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(monitor, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Provider integration connection '{connectionId}' was not found." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetWorkstationProviderIntegrationConnectionMonitor")
        .Produces<ProviderIntegrationConnectionMonitorDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);
    }

    private static bool HasProviderIntegrationReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

    private static bool HasProviderIntegrationConfigurePermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);
}
