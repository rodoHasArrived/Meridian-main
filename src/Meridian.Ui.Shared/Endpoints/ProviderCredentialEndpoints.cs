using System.Diagnostics;
using System.Text.Json;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Compatibility provider-credential endpoints. Canonical mutation and verification routes live
/// under /api/providers/{providerId}/credentials and /api/providers/{providerId}/verify.
/// </summary>
public static class ProviderCredentialEndpoints
{
    public static void MapProviderCredentialEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Provider Credentials");
        group.RequireWorkstationTenantScope();

        group.MapPost(UiApiRoutes.ProviderCredentialsValidate, (
            HttpContext context,
            string provider,
            ProviderCredentialRequest request) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            var descriptor = ProviderCredentialCatalog.Find(provider);
            if (descriptor is null)
            {
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });
            }

            var credentials = NormalizeCredentials(descriptor, request.Credentials);
            var missing = descriptor.RequiredFields
                .Where(field => field.Required && string.IsNullOrWhiteSpace(credentials.GetValueOrDefault(field.Name)))
                .Select(field => field.Name)
                .ToArray();
            var isValid = missing.Length == 0;

            return Results.Json(new ProviderCredentialValidationResult(
                IsValid: isValid,
                Status: isValid ? "Valid" : "Invalid",
                ErrorMessage: isValid
                    ? null
                    : $"Credentials for {descriptor.DisplayName} are incomplete or invalid.",
                LastVerified: DateTime.UtcNow,
                Details: new Dictionary<string, string>
                {
                    ["provider"] = descriptor.ProviderId,
                    ["credentialCount"] = credentials.Count.ToString(),
                    ["missingFields"] = string.Join(",", missing),
                    ["canonicalRoute"] = $"/api/providers/{descriptor.ProviderId}/credentials"
                }), jsonOptions);
        })
        .WithName("ValidateProviderCredentials").RequirePermission(UserPermission.ManageCredentials)
        .WithDescription("Validates provider credential completeness without returning secrets.")
        .Produces<ProviderCredentialValidationResult>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.ProviderConnectionTest, async (
            HttpContext context,
            string provider,
            ProviderCredentialRequest request,
            ProviderConnectionLifecycleService service) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return EndpointHelpers.Forbidden();
            }

            var descriptor = ProviderCredentialCatalog.Find(provider);
            if (descriptor is null)
            {
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var selected = ProviderConnectionEndpoints.ResolveConnectionService(context, descriptor.ProviderId, service);
                if (request.Credentials is { Count: > 0 })
                {
                    var draft = new ProviderCredentialDraftRequestDto(NormalizeCredentials(descriptor, request.Credentials));
                    var result = await selected.VerifyDraftAsync(descriptor.ProviderId, draft, context.RequestAborted)
                        .ConfigureAwait(false);
                    var verified = result.Status == ProviderCredentialDraftStatusDto.Verified;
                    return Results.Json(new ProviderConnectionTest(descriptor.ProviderId, verified, (int)sw.ElapsedMilliseconds,
                        verified ? "Connected" : result.Status.ToString(), verified ? null : result.Message, DateTime.UtcNow), jsonOptions);
                }

                var verification = await selected.VerifyAsync(descriptor.ProviderId, context.RequestAborted, actor).ConfigureAwait(false);
                return Results.Json(new ProviderConnectionTest(descriptor.ProviderId, verification.Success, (int)sw.ElapsedMilliseconds,
                    verification.Success ? "Connected" : "Failed", verification.LastError, DateTime.UtcNow), jsonOptions);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "Credential request does not match retained connection ownership." });
            }
        })
        .WithName("TestProviderConnection").RequirePermission(UserPermission.ManageCredentials)
        .WithDescription("Verifies draft fields without persisting them, or re-verifies a saved connection when no fields are supplied.")
        .Produces<ProviderConnectionTest>(StatusCodes.Status200OK)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static Dictionary<string, string?> NormalizeCredentials(
        ProviderCredentialCatalogEntry descriptor,
        Dictionary<string, string>? credentials)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (credentials is null)
        {
            return normalized;
        }

        foreach (var field in descriptor.RequiredFields)
        {
            if (credentials.TryGetValue(field.Name, out var directValue))
            {
                normalized[field.Name] = directValue;
                continue;
            }

            foreach (var envName in field.EnvironmentNames)
            {
                if (credentials.TryGetValue(envName, out var envValue))
                {
                    normalized[field.Name] = envValue;
                    break;
                }
            }
        }

        return normalized;
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
