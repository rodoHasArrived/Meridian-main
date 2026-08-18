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
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var rows = await service.GetConnectionsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(rows, jsonOptions);
        })
        .WithName("GetProviderConnections")
        .Produces<IReadOnlyList<ProviderConnectionRowDto>>(StatusCodes.Status200OK);

        group.MapPut(UiApiRoutes.ProviderCredentialMutation, async (
            string providerId,
            ProviderCredentialUpsertRequestDto request,
            HttpContext context,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service.SaveCredentialsAsync(providerId, request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ProviderCredentialValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message, unknownFields = ex.UnknownFields });
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
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service.VerifyAsync(providerId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
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
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                var result = await service.DeleteCredentialsAsync(providerId, "browser-workstation", context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
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

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
