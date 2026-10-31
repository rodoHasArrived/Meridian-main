using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Administrative ownership commands shared by the browser and installed workstation.</summary>
public static class ProviderCredentialOwnershipEndpoints
{
    public static void MapProviderCredentialOwnershipEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(UiApiRoutes.ProviderRoutingConnections).WithTags("Provider Credential Ownership");
        group.RequireWorkstationTenantScope();
        group.RequirePermission(UserPermission.ManageCredentials | UserPermission.ManageProviders | UserPermission.ModifyConfig);

        group.MapPost("", (CreateOwnedProviderConnectionRequest request, HttpContext context,
            [FromServices] ProviderConnectionService service) => ExecuteAsync(context, async (tenant, actor) =>
                Results.Json(await service.UpsertForTenantAsync(request.Connection, tenant, request.Environment,
                    context.RequestAborted).ConfigureAwait(false), jsonOptions)))
            .WithName("CreateOwnedProviderConnection").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPut("/{connectionId}/runtime-credentials", (string connectionId, BindProviderRuntimeCredentialRequest request,
            HttpContext context, [FromServices] ProviderRuntimeCredentialService service) => ExecuteAsync(context, async (tenant, actor) =>
                Results.Json(await service.BindAsync(request.ProviderId, connectionId, tenant, context.RequestAborted, actor)
                    .ConfigureAwait(false), jsonOptions)))
            .WithName("BindProviderRuntimeCredentials").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/{connectionId}/runtime-credentials/status", (string connectionId, string providerId, HttpContext context,
            [FromServices] ProviderRuntimeCredentialService service) => ExecuteAsync(context, (tenant, actor) =>
                Task.FromResult(Results.Json(service.GetApplicationStatus(providerId, connectionId, tenant), jsonOptions))))
            .WithName("GetProviderRuntimeCredentialApplicationStatus");

        // Opt-in, never hot: the host's applier decides whether running instances can be recycled.
        group.MapPost("/{connectionId}/runtime-credentials/apply", (string connectionId, ApplyProviderRuntimeCredentialRequest request,
            HttpContext context, [FromServices] ProviderRuntimeCredentialService service) => ExecuteAsync(context, async (tenant, actor) =>
                Results.Json(await service.ApplyAsync(request.ProviderId, connectionId, tenant, actor, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions)))
            .WithName("ApplyProviderRuntimeCredentials").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet("/{connectionId}/credentials/migration-preview", (string connectionId, string providerId,
            HttpContext context, [FromServices] ProviderRuntimeCredentialService service) => ExecuteAsync(context, async (tenant, actor) =>
                Results.Json(await service.GetMigrationPreviewAsync(providerId, connectionId, tenant, context.RequestAborted)
                    .ConfigureAwait(false), jsonOptions)))
            .WithName("PreviewProviderCredentialOwnershipMigration");

        group.MapPost("/{connectionId}/credentials/migrate", (string connectionId, ProviderCredentialMigrationRequest request,
            HttpContext context, [FromServices] ProviderRuntimeCredentialService service) => TransferAsync(service, connectionId, request, context, false, jsonOptions))
            .WithName("MigrateProviderCredentialOwnership").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
        group.MapPost("/{connectionId}/credentials/rollback", (string connectionId, ProviderCredentialMigrationRequest request,
            HttpContext context, [FromServices] ProviderRuntimeCredentialService service) => TransferAsync(service, connectionId, request, context, true, jsonOptions))
            .WithName("RollbackProviderCredentialOwnership").RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static Task<IResult> TransferAsync(ProviderRuntimeCredentialService service, string connectionId,
        ProviderCredentialMigrationRequest request, HttpContext context, bool rollback, JsonSerializerOptions jsonOptions)
        => ExecuteAsync(context, async (tenant, actor) => Results.Json(await service.TransferAsync(request.ProviderId,
            connectionId, tenant, request.MigrationId, request.ExpectedGeneration, actor, rollback, context.RequestAborted)
            .ConfigureAwait(false), jsonOptions));

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<string, string, Task<IResult>> command)
    {
        var permissions = UserPermission.ManageCredentials | UserPermission.ManageProviders | UserPermission.ModifyConfig;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!EndpointAuthorization.HasPermission(context, permissions) || !tenant.HasTenantScope ||
            !EndpointAuthorization.TryResolveActor(context, out var actor))
            return EndpointHelpers.Forbidden();
        try
        {
            return await command(tenant.TenantId!, actor).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
        catch (ArgumentException) { return Results.BadRequest(new { code = "OwnershipRequestInvalid", error = "A valid provider, connection, environment, operation identity and expected credential generation are required." }); }
        catch (InvalidDataException) { return Results.Conflict(new { code = "OwnershipScopeMismatch", error = "Credential ownership does not match the retained account or environment." }); }
        catch (NotSupportedException) { return Results.Conflict(new { code = "VaultMigrationUnsupported", error = "The configured credential vault does not support explicit ownership migration; configure the file-backed vault before transferring ownership." }); }
        catch (ProviderCredentialConflictException) { return Results.Conflict(new { code = "CredentialGenerationConflict", error = "Credential ownership or generation changed since the preview. Reload the retained connection and preview again." }); }
        // Remaining operation refusals carry a specific, operator-facing reason (for example a
        // separate legacy OAuth token that must be migrated first); surface it rather than a generic text.
        catch (InvalidOperationException exception) { return Results.Conflict(new { code = "OwnershipTransferBlocked", error = exception.Message }); }
    }
}
