using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Extensibility;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Extensibility;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapExtensibilityEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityCatalog), (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<ExtensibilityCatalogService>() ?? BuildFallbackService();
            return Results.Json(service.GetCatalog(DateTimeOffset.UtcNow), jsonOptions);
        })
        .WithName("GetWorkstationExtensibilityCatalog")
        .Produces<ExtensibilityCatalogDto>(StatusCodes.Status200OK)
        .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityTenantTemplates), async (HttpContext context) =>
        {
            if (!HasExtensibilityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<ExtensibilityConfigurationService>();
            if (service is null)
            {
                return Results.Problem("Extensibility configuration service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
            }

            var templates = await service.ListTenantTemplatesAsync(tenantId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(templates, jsonOptions);
        })
        .WithName("ListWorkstationExtensibilityTenantTemplates")
        .Produces<TenantTemplateConfigurationBundleDto[]>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig, UserPermission.AdminMaintenance);

        group.MapPut(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityTenantTemplateById), async (
            string tenantTemplateId,
            TenantTemplateConfigurationBundleDto? request,
            HttpContext context) =>
        {
            if (!HasExtensibilityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return Results.BadRequest(new { error = "Tenant template configuration bundle is required." });
            }

            if (string.IsNullOrWhiteSpace(request.TenantTemplateId))
            {
                return Results.BadRequest(new { error = "Tenant template id is required." });
            }

            if (!string.Equals(tenantTemplateId.Trim(), request.TenantTemplateId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Tenant template route id must match the request body id." });
            }

            var service = context.RequestServices.GetService<ExtensibilityConfigurationService>();
            if (service is null)
            {
                return Results.Problem("Extensibility configuration service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var saved = await service.UpsertTenantTemplateAsync(tenantId, request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("SaveWorkstationExtensibilityTenantTemplate")
        .Produces<TenantTemplateConfigurationBundleDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(UserPermission.ModifyConfig, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityTenantTemplateActivations), async (
            string tenantTemplateId,
            HttpContext context) =>
        {
            if (!HasExtensibilityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(tenantTemplateId))
            {
                return Results.BadRequest(new { error = "Tenant template id is required." });
            }

            var service = context.RequestServices.GetService<ExtensibilityConfigurationService>();
            if (service is null)
            {
                return Results.Problem("Extensibility configuration service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
            }

            var history = await service.ListActivationHistoryAsync(tenantId, tenantTemplateId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(history, jsonOptions);
        })
        .Produces<TenantTemplateActivationResultDto[]>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig, UserPermission.AdminMaintenance);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityTenantTemplateReadiness), async (
            string tenantTemplateId,
            HttpContext context) =>
        {
            if (!HasExtensibilityReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(tenantTemplateId))
            {
                return Results.BadRequest(new { error = "Tenant template id is required." });
            }

            var service = context.RequestServices.GetService<ExtensibilityConfigurationService>();
            if (service is null)
            {
                return Results.Problem("Extensibility configuration service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var readiness = await service.EvaluateTenantTemplateActivationAsync(tenantId, tenantTemplateId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(readiness, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Tenant template '{tenantTemplateId}' was not found." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetWorkstationExtensibilityTenantTemplateReadiness")
        .Produces<ExtensibilityActivationReadinessDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig, UserPermission.AdminMaintenance);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationExtensibilityTenantTemplateActivate), async (
            string tenantTemplateId,
            TenantTemplateActivationRequestDto? request,
            HttpContext context) =>
        {
            if (!HasExtensibilityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var service = context.RequestServices.GetService<ExtensibilityConfigurationService>();
            if (service is null)
            {
                return Results.Problem("Extensibility configuration service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!TryResolveRequiredTenantId(context, out var tenantId))
            {
                return Results.Problem("A tenant-scoped workstation request context is required.", statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var result = await service
                    .ActivateTenantTemplateAsync(tenantId, tenantTemplateId, currentUser, request, DateTimeOffset.UtcNow, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(
                    result,
                    jsonOptions,
                    statusCode: result.IsActivated ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = $"Tenant template '{tenantTemplateId}' was not found." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("ActivateWorkstationExtensibilityTenantTemplate")
        .Produces<TenantTemplateActivationResultDto>(StatusCodes.Status200OK)
        .Produces<TenantTemplateActivationResultDto>(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireAnyPermission(UserPermission.ModifyConfig, UserPermission.AdminMaintenance);
    }

    private static ExtensibilityCatalogService BuildFallbackService()
        => new(
        [
            new WorkflowExtensibilityCatalogProvider([new BuiltInWorkflowDefinitionProvider()]),
            new ReportingTemplateExtensibilityCatalogProvider(new DefaultReportingTemplateCatalog()),
            new PermissionExtensibilityCatalogProvider(),
            new OperationalConfigurationExtensibilityCatalogProvider()
        ]);

    private static bool HasExtensibilityReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ViewConfig,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

    private static bool HasExtensibilityMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.ModifyConfig,
            UserPermission.AdminMaintenance);

    private static bool TryResolveRequiredTenantId(HttpContext context, out string tenantId)
    {
        tenantId = HttpContextWorkstationTenantContextAccessor.Resolve(context).TenantId ?? string.Empty;
        tenantId = tenantId.Trim();
        return !string.IsNullOrWhiteSpace(tenantId);
    }
}
