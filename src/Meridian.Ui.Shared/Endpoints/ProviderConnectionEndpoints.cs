using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class ProviderConnectionEndpoints
{
    public static void MapProviderConnectionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Connections");
        group.RequireWorkstationTenantScope();

        group.MapGet(UiApiRoutes.ProviderConnections, async (
            HttpContext context,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            var rows = await service.GetConnectionsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(rows, jsonOptions);
        })
        .WithName("GetProviderConnections").RequirePermission(UserPermission.ManageCredentials)
        .Produces<IReadOnlyList<ProviderConnectionRowDto>>(StatusCodes.Status200OK);

        group.MapPut(UiApiRoutes.ProviderCredentialMutation, async (
            string providerId,
            ProviderCredentialUpsertRequestDto request,
            HttpContext context,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var selectedService = ResolveConnectionService(context, providerId, service);
                var result = await selectedService.SaveCredentialsAsync(providerId, request with { RequestedBy = actor }, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ProviderCredentialValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message, unknownFields = ex.UnknownFields });
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "Credential request does not match retained connection ownership." });
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("PutProviderCredentials").RequirePermission(UserPermission.ManageCredentials)
        .Produces<ProviderCredentialMutationResultDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderCredentialVerify, async (
            string providerId,
            HttpContext context,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var selectedService = ResolveConnectionService(context, providerId, service);
                var result = await selectedService.VerifyAsync(providerId, context.RequestAborted, actor).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("VerifyProviderConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<ProviderCredentialVerificationResultDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapDelete(UiApiRoutes.ProviderCredentialMutation, async (
            string providerId,
            HttpContext context,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var selectedService = ResolveConnectionService(context, providerId, service);
                var result = await selectedService.DeleteCredentialsAsync(providerId, actor, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("DeleteProviderCredentials").RequirePermission(UserPermission.ManageCredentials)
        .Produces<ProviderCredentialMutationResultDto>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ProviderConnectionLifecycleService ResolveConnectionService(HttpContext context, string providerId, ProviderConnectionLifecycleService service)
    {
        if (!context.Request.Query.TryGetValue("connectionId", out var connectionIds))
            return service;
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (connectionIds.Count != 1 || string.IsNullOrWhiteSpace(connectionIds[0]) || !tenant.HasTenantScope)
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        return service.ForConnection(connectionIds[0]!, tenant.TenantId!, providerId);
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
