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

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationOpenApiImport), async (
            ProviderIntegrationOpenApiImportRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationOpenApiImportService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration OpenAPI import service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .ImportAsync(tenantId, request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
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
        .WithName("ImportWorkstationProviderIntegrationOpenApi")
        .Produces<ProviderIntegrationOpenApiImportResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationSetupSave), async (
            ProviderIntegrationSetupSaveRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationSetupService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration setup service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .SaveDraftAsync(tenantId, request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
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
        .WithName("SaveWorkstationProviderIntegrationSetup")
        .Produces<ProviderIntegrationSetupSaveResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
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
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var readiness = await service
                    .EvaluateAsync(tenantId, manifestId, connectionId, context.RequestAborted)
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
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .RunManualCsvDryRunAsync(tenantId, request, context.RequestAborted)
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
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .RunRestDryRunAsync(tenantId, request, context.RequestAborted)
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

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationActivate), async (
            ProviderIntegrationActivationRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationActivationService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration activation service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .ActivateAsync(tenantId, request, context.RequestAborted)
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
        .WithName("ActivateWorkstationProviderIntegration")
        .Produces<ProviderIntegrationActivationResultDto>(StatusCodes.Status200OK)
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
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var monitor = await service
                    .GetConnectionMonitorAsync(
                        tenantId,
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationConnectionSyncPlan), async (
            string connectionId,
            DateTimeOffset? evaluatedAt,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationSyncPlanningService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration sync-planning service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var plan = await service
                    .PlanAsync(
                        tenantId,
                        new ProviderIntegrationSyncPlanRequestDto(
                            connectionId,
                            evaluatedAt ?? DateTimeOffset.UtcNow),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(plan, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Provider integration connection '{connectionId}' was not found." });
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
        .WithName("GetWorkstationProviderIntegrationConnectionSyncPlan")
        .Produces<ProviderIntegrationSyncPlanDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationConnectionRunDueSync), async (
            string connectionId,
            ProviderIntegrationRunDueSyncRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!StringComparer.Ordinal.Equals(connectionId, request.ConnectionId))
            {
                return Results.BadRequest(new { error = "The route connection id must match the request connection id." });
            }

            var service = context.RequestServices.GetService<ProviderIntegrationSyncOrchestrationService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration sync-orchestration service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .RunDueAsync(tenantId, request, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Provider integration connection '{connectionId}' was not found." });
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
        .WithName("RunDueWorkstationProviderIntegrationSync")
        .Produces<ProviderIntegrationRunDueSyncResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationSchemaDriftCheck), async (
            ProviderIntegrationSchemaDriftCheckRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationSchemaDriftService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration schema-drift service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .CheckAsync(tenantId, request, context.RequestAborted)
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
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CheckWorkstationProviderIntegrationSchemaDrift")
        .Produces<ProviderIntegrationSchemaDriftCheckResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationQuarantineReview), async (
            string connectionId,
            int? recentRunLimit,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationQuarantineReviewService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration quarantine review service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var review = await service
                    .GetReviewAsync(
                        tenantId,
                        connectionId,
                        recentRunLimit ?? 10,
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(review, jsonOptions);
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
        .WithName("GetWorkstationProviderIntegrationQuarantineReview")
        .Produces<ProviderIntegrationQuarantineReviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ViewConfig,
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationQuarantineResolve), async (
            ProviderIntegrationQuarantineResolutionRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationQuarantineReviewService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration quarantine review service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .ResolveAsync(tenantId, request, context.RequestAborted)
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
        .WithName("ResolveWorkstationProviderIntegrationQuarantineRecord")
        .Produces<ProviderIntegrationQuarantineResolutionResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
            UserPermission.ManageProviders,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationProviderIntegrationQuarantineReplay), async (
            ProviderIntegrationQuarantineReplayRequestDto request,
            HttpContext context) =>
        {
            if (!HasProviderIntegrationConfigurePermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ProviderIntegrationQuarantineReplayService>();
            if (service is null)
            {
                return Results.Problem(
                    "Provider integration quarantine replay service is not registered.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                if (!TryResolveRequiredTenantId(context, out var tenantId))
                {
                    return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await service
                    .ReplayAsync(tenantId, request, context.RequestAborted)
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
        .WithName("ReplayWorkstationProviderIntegrationQuarantineRecords")
        .Produces<ProviderIntegrationQuarantineReplayResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(
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
