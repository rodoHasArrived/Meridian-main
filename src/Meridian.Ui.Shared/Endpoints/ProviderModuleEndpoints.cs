using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// API endpoints for provider module setup: discovery, configuration, enable/disable, remove, test, and restart.
/// All endpoints live under /api/providers/modules and require the workstation tenant scope.
/// </summary>
public static class ProviderModuleEndpoints
{
    public static void MapProviderModuleEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Setup");
        group.RequireWorkstationTenantScope();

        // GET /api/providers/modules — all configured modules with credential status
        group.MapGet(UiApiRoutes.ProviderModules, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var modules = await setupService.GetConfiguredModulesAsync(ct).ConfigureAwait(false);
            return Results.Json(modules, jsonOptions);
        })
        .WithName("GetProviderModules")
        .WithDescription("Returns all configured provider modules with credential status (no credential values).")
        .Produces<IReadOnlyList<ProviderModuleStatusDto>>(200)
        .Produces(StatusCodes.Status403Forbidden);

        // GET /api/providers/modules/catalogue — discoverable module types
        group.MapGet(UiApiRoutes.ProviderModulesCatalogue, (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var catalogue = setupService.GetDiscoveredModuleCatalogue();
            return Results.Json(catalogue, jsonOptions);
        })
        .WithName("GetProviderModuleCatalogue")
        .WithDescription("Returns all discoverable provider module types with capability and credential metadata.")
        .Produces<IReadOnlyList<ProviderModuleCatalogueEntry>>(200)
        .Produces(StatusCodes.Status403Forbidden);

        // POST /api/providers/modules — create or update a module configuration
        group.MapPost(UiApiRoutes.ProviderModules, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            [FromBody] UpsertProviderModuleRequest request,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var result = await setupService.UpsertModuleAsync(request, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result, jsonOptions)
                : Results.BadRequest(result);
        })
        .WithName("UpsertProviderModule")
        .WithDescription("Creates or updates a provider module configuration. Credentials are stored server-side and never returned.")
        .Produces(200)
        .Produces(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // PUT /api/providers/modules/{moduleId} — update existing module
        group.MapPut(UiApiRoutes.ProviderModuleById, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            string moduleId,
            [FromBody] UpsertProviderModuleRequest request,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var effectiveRequest = request with { ModuleId = moduleId };
            var result = await setupService.UpsertModuleAsync(effectiveRequest, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result, jsonOptions)
                : Results.BadRequest(result);
        })
        .WithName("UpdateProviderModule")
        .WithDescription("Updates an existing provider module configuration by ID.")
        .Produces(200)
        .Produces(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // DELETE /api/providers/modules/{moduleId} — remove module and credentials
        group.MapDelete(UiApiRoutes.ProviderModuleById, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            string moduleId,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var result = await setupService.RemoveModuleAsync(moduleId, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Ok()
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("DeleteProviderModule")
        .WithDescription("Removes a provider module configuration and all stored credentials for that module.")
        .Produces(200)
        .Produces(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // PUT /api/providers/modules/{moduleId}/enabled — live enable/disable toggle
        group.MapPut(UiApiRoutes.ProviderModuleEnabled, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            string moduleId,
            [FromBody] SetEnabledRequest request,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var result = await setupService.SetEnabledAsync(moduleId, request.Enabled, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result, jsonOptions)
                : Results.BadRequest(result);
        })
        .WithName("SetProviderModuleEnabled")
        .WithDescription("Enables or disables a provider module. Applied live without restart; also persisted to config.")
        .Produces(200)
        .Produces(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/providers/modules/{moduleId}/test — credential validation + optional probe
        group.MapPost(UiApiRoutes.ProviderModuleTest, async (
            HttpContext context,
            [FromServices] IProviderModuleSetupService setupService,
            string moduleId,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            var result = await setupService.TestModuleAsync(moduleId, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("TestProviderModule")
        .WithDescription("Validates credentials and optionally probes live connectivity for a provider module.")
        .Produces<ProviderModuleTestResult>(200)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // POST /api/providers/restart — graceful restart to apply config changes
        group.MapPost(UiApiRoutes.ProviderRestart, (
            HttpContext context,
            [FromServices] IHostApplicationLifetime lifetime) =>
        {
            if (!EndpointAuthorization.HasPermission(context, Identity.Auth.UserPermission.ManageProviders))
                return EndpointHelpers.Forbidden();

            lifetime.StopApplication();
            return Results.Ok(new { restarting = true, message = "Application restart initiated. Reconnect in a few seconds." });
        })
        .WithName("RestartProviderHost")
        .WithDescription("Triggers a graceful application restart to apply provider configuration changes that require a reload.")
        .Produces(200)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record SetEnabledRequest(bool Enabled);
}
