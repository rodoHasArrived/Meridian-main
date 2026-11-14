using System.Text.Json;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Non-secret provider-boundary validation settings and before/after change evidence.</summary>
public static class ProviderValidationEndpoints
{
    public static void MapProviderValidationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/providers/{providerName}/validation-profile").WithTags("Providers");
        group.RequireWorkstationTenantScope();
        group.MapGet("", (string providerName, [FromServices] ConfigStore store) =>
        {
            var backfill = store.Load().Backfill;
            return Results.Json(new
            {
                provider = providerName,
                profile = ProviderValidationProfile.Resolve(backfill, providerName),
                audit = backfill?.ValidationProfileAudit?.Where(entry =>
                    string.Equals(entry.Provider, providerName, StringComparison.OrdinalIgnoreCase)).Reverse().Take(100).ToArray() ?? [],
                impact = "RestartHost"
            }, jsonOptions);
        }).WithName("GetProviderValidationProfile")
            .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ManageProviders);

        group.MapPut("", async (string providerName, ProviderValidationProfileRequest request,
            HttpContext context, [FromServices] ConfigStore store, [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(providerName) || providerName.Length > 80 ||
                providerName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                return ApiProblemDetails.Validation(context, "provider", "A provider ID is required.");
            if (request.Profile is null)
                return ApiProblemDetails.Validation(context, "profile", "A validation profile is required.");
            var actor = HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor;
            if (string.IsNullOrWhiteSpace(actor))
                return Results.Unauthorized();
            try
            {
                request.Profile.Validate();
                if (request.Reason?.Length > 2000)
                    throw new ArgumentException("The change reason cannot exceed 2000 characters.");
                var audit = await store.UpdateAsync(config => ApplyProfile(config, providerName, request,
                    actor), ct).ConfigureAwait(false);
                loggerFactory.CreateLogger(nameof(ProviderValidationEndpoints)).LogInformation(
                    "Provider validation profile changed {Provider} {AuditId} {Actor} {Loosened} {Reason} {StaleToleranceDays} {FutureSkewSeconds} {StaleThresholdSeconds} {MaxLatencyMs}",
                    providerName, audit.Id, audit.Actor, audit.Loosened, audit.Reason, audit.After.StaleToleranceDays,
                    audit.After.FutureSkewSeconds, audit.After.StaleThresholdSeconds, audit.After.MaxLatencyMs);
                return Results.Json(new { saved = true, impact = "RestartHost", pendingRestart = true, profile = audit.After, audit }, jsonOptions);
            }
            catch (ArgumentException exception)
            {
                return ApiProblemDetails.Validation(context, "profile", exception.Message);
            }
        }).WithName("UpdateProviderValidationProfile")
            .RequirePermission(UserPermission.ManageProviders)
            .RequirePermission(UserPermission.ModifyConfig)
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ModifyConfig))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageProviders))
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    public static (AppConfig Config, ProviderValidationProfileAudit Result) ApplyProfile(AppConfig config,
        string provider, ProviderValidationProfileRequest request, string actor)
    {
        ArgumentNullException.ThrowIfNull(request.Profile);
        request.Profile.Validate();
        var backfill = config.Backfill ?? new BackfillConfig();
        var previous = ProviderValidationProfile.Resolve(backfill, provider);
        var loosened = request.Profile.Loosens(previous);
        if (loosened && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Loosening validation requires a recorded reason.");
        var audit = new ProviderValidationProfileAudit(Guid.NewGuid(), provider, DateTimeOffset.UtcNow,
            actor, request.Reason?.Trim(), loosened, previous, request.Profile);
        var profiles = new Dictionary<string, ProviderValidationProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in backfill.ValidationProfiles ?? new Dictionary<string, ProviderValidationProfile>())
            profiles[pair.Key] = pair.Value;
        profiles[provider] = request.Profile;
        return (config with
        {
            Backfill = backfill with
            {
                ValidationProfiles = profiles,
                ValidationProfileAudit = (backfill.ValidationProfileAudit ?? []).Append(audit).ToArray()
            }
        }, audit);
    }
}

public sealed record ProviderValidationProfileRequest(ProviderValidationProfile Profile, string? Reason = null);
