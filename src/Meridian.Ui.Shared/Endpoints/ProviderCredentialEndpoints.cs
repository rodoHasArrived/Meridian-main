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
            if (!HasManageCredentialsPermission(context))
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
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var descriptor = ProviderCredentialCatalog.Find(provider);
            if (descriptor is null)
            {
                return Results.NotFound(new { error = $"Provider '{provider}' not found." });
            }

            var sw = Stopwatch.StartNew();
            var credentials = NormalizeCredentials(descriptor, request.Credentials);
            if (credentials.Count > 0)
            {
                await service.SaveCredentialsAsync(
                    descriptor.ProviderId,
                    new ProviderCredentialUpsertRequestDto(
                        credentials,
                        Environment: null,
                        RequestedBy: "provider-credential-compatibility-endpoint"),
                    context.RequestAborted).ConfigureAwait(false);
            }

            var verification = await service.VerifyAsync(descriptor.ProviderId, context.RequestAborted)
                .ConfigureAwait(false);
            sw.Stop();

            return Results.Json(new ProviderConnectionTest(
                Provider: descriptor.ProviderId,
                IsConnected: verification.Success,
                ResponseTimeMs: (int)sw.ElapsedMilliseconds,
                Status: verification.Success ? "Connected" : "Failed",
                ErrorMessage: verification.Success ? null : verification.LastError,
                TestedAt: DateTime.UtcNow), jsonOptions);
        })
        .WithName("TestProviderConnection").RequirePermission(UserPermission.ManageCredentials)
        .WithDescription("Compatibility wrapper around the canonical provider verification route.")
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
