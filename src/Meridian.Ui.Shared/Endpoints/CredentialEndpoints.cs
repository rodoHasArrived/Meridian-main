using System.Text.Json;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Compatibility endpoints for legacy provider credential clients.
/// New callers should use /api/providers/{providerId}/credentials and /api/providers/{providerId}/verify.
/// </summary>
public static class CredentialEndpoints
{
    public static void MapCredentialEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Credentials");

        group.MapGet(global::Meridian.Contracts.Api.UiApiRoutes.Credentials, async (
            HttpContext context,
            IProviderCredentialStore store) =>
        {
            if (!HasManageCredentialsPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var rows = new List<object>();
            foreach (var descriptor in ProviderCredentialCatalog.All)
            {
                var status = await store.GetStatusAsync(descriptor.ProviderId, context.RequestAborted)
                    .ConfigureAwait(false);
                rows.Add(ToLegacyStatus(status));
            }

            return Results.Json(rows, jsonOptions);
        })
        .WithName("GetCredentialCompatibilityStatuses")
        .Produces(StatusCodes.Status200OK);

        group.MapGet(global::Meridian.Contracts.Api.UiApiRoutes.CredentialByProvider, async (
            string provider,
            HttpContext context,
            IProviderCredentialStore store) =>
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

            var status = await store.GetStatusAsync(descriptor.ProviderId, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(new
            {
                providerId = status.ProviderId,
                displayName = status.DisplayName,
                state = status.CredentialState.ToString(),
                credentialSource = status.CredentialSource,
                verificationState = status.VerificationState,
                maskedValue = status.MaskedKeyPreview,
                environment = status.Environment,
                envVars = descriptor.RequiredFields.Select(field => new
                {
                    name = field.EnvironmentNames.FirstOrDefault() ?? field.Name,
                    field = field.Name,
                    isSet = status.PresentFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase),
                    maskedValue = status.PresentFields.Contains(field.Name, StringComparer.OrdinalIgnoreCase)
                        ? status.MaskedKeyPreview ?? "********"
                        : string.Empty
                }).ToArray()
            }, jsonOptions);
        })
        .WithName("GetCredentialCompatibilityStatus")
        .Produces(StatusCodes.Status200OK);

        group.MapPost(global::Meridian.Contracts.Api.UiApiRoutes.CredentialByProvider, async (
            string provider,
            HttpContext context,
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

            var request = await ReadLegacyRequestAsync(descriptor, context, context.RequestAborted)
                .ConfigureAwait(false);
            var result = await service.SaveCredentialsAsync(descriptor.ProviderId, request, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(new
            {
                providerId = result.ProviderId,
                state = result.CredentialState.ToString(),
                credentialSource = result.CredentialSource,
                verificationState = result.VerificationState,
                maskedValue = result.MaskedKeyPreview,
                environment = result.Environment,
                warnings = result.Warnings
            }, jsonOptions);
        })
        .WithName("SaveCredentialCompatibilityStatus")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapDelete(global::Meridian.Contracts.Api.UiApiRoutes.CredentialByProvider, async (
            string provider,
            HttpContext context,
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

            var result = await service.DeleteCredentialsAsync(
                descriptor.ProviderId,
                actor: "legacy-credentials-endpoint",
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(new
            {
                providerId = result.ProviderId,
                state = result.CredentialState.ToString(),
                credentialSource = result.CredentialSource,
                warnings = result.Warnings
            }, jsonOptions);
        })
        .WithName("DeleteCredentialCompatibilityStatus")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(global::Meridian.Contracts.Api.UiApiRoutes.CredentialTest, async (
            string provider,
            HttpContext context,
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

            if (context.Request.ContentLength is > 0)
            {
                var request = await ReadLegacyRequestAsync(descriptor, context, context.RequestAborted)
                    .ConfigureAwait(false);
                if (request.Credentials?.Count > 0)
                {
                    await service.SaveCredentialsAsync(descriptor.ProviderId, request, context.RequestAborted)
                        .ConfigureAwait(false);
                }
            }

            var result = await service.VerifyAsync(descriptor.ProviderId, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(new
            {
                providerId = result.ProviderId,
                success = result.Success,
                state = result.Success ? "Configured" : "Missing",
                verificationState = result.VerificationState,
                lastVerifiedAt = result.LastVerifiedAt,
                message = result.Success
                    ? $"Credentials for '{descriptor.ProviderId}' are present and verified."
                    : result.LastError ?? $"Credentials for '{descriptor.ProviderId}' are missing or incomplete.",
                warnings = result.Warnings
            }, jsonOptions);
        })
        .WithName("TestCredentialCompatibilityStatus")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static object ToLegacyStatus(ProviderCredentialStoreStatus status) => new
    {
        providerId = status.ProviderId,
        displayName = status.DisplayName,
        state = status.CredentialState.ToString(),
        credentialSource = status.CredentialSource,
        verificationState = status.VerificationState,
        maskedValue = status.MaskedKeyPreview,
        environment = status.Environment
    };

    private static async Task<ProviderCredentialUpsertRequestDto> ReadLegacyRequestAsync(
        ProviderCredentialCatalogEntry descriptor,
        HttpContext context,
        CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException("Invalid JSON body.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var credentialElement = root.TryGetProperty("credentials", out var nestedCredentials) &&
                                    nestedCredentials.ValueKind == JsonValueKind.Object
                ? nestedCredentials
                : root;
            var credentials = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in descriptor.RequiredFields)
            {
                if (TryReadCredentialValue(credentialElement, field, out var value))
                {
                    credentials[field.Name] = value;
                }
            }

            var environment = TryReadString(root, "environment")
                ?? descriptor.EnvironmentNames?.Select(name => TryReadString(root, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return new ProviderCredentialUpsertRequestDto(
                credentials,
                environment,
                RequestedBy: "legacy-credentials-endpoint");
        }
    }

    private static bool TryReadCredentialValue(
        JsonElement source,
        ProviderCredentialFieldDefinition field,
        out string? value)
    {
        value = TryReadString(source, field.Name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var envName in field.EnvironmentNames)
        {
            value = TryReadString(source, envName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? TryReadString(JsonElement source, string propertyName)
    {
        foreach (var property in source.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
        }

        return null;
    }

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
