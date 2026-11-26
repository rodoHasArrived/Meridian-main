using System.Text.Json;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Non-secret provider-boundary validation settings and before/after change evidence.</summary>
public static class ProviderValidationEndpoints
{
    private const UserPermission EditPermissions = UserPermission.ManageProviders | UserPermission.ModifyConfig;

    public static void MapProviderValidationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/providers/{providerName}/validation-profile").WithTags("Providers");
        group.RequireWorkstationTenantScope();
        group.MapGet("", (string providerName, HttpContext context, [FromServices] ConfigStore store) =>
        {
            if (!IsProviderId(providerName))
                return ApiProblemDetails.Validation(context, "provider", "Select a valid provider ID.");
            try
            {
                var provider = ProviderIdentity.NormalizeId(providerName);
                var (supportsStreamingRules, startup) = ReadStartupProfile(context, provider);
                var canEdit = EndpointAuthorization.HasPermission(context, EditPermissions) &&
                    EndpointAuthorization.TryResolveActor(context, out _);
                return Results.Json(CreateSnapshot(provider, LoadProfileConfig(store).Backfill,
                    canEdit, supportsStreamingRules, startup), jsonOptions);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                return ProfileUnavailable(context, error, "The saved provider profile could not be read.");
            }
        }).WithName("GetProviderValidationProfile")
            .RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ManageProviders, UserPermission.ViewHistoricalData,
                UserPermission.ViewDiagnostics, UserPermission.ManageStorage);

        group.MapPut("", async (string providerName, ProviderValidationProfileRequest request,
            HttpContext context, [FromServices] ConfigStore store, [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (!IsProviderId(providerName))
                return ApiProblemDetails.Validation(context, "provider", "Select a valid provider ID.");
            providerName = ProviderIdentity.NormalizeId(providerName);
            var actor = HttpContextWorkstationTenantContextAccessor.Resolve(context).Actor;
            if (string.IsNullOrWhiteSpace(actor))
                return Results.Unauthorized();
            try
            {
                var (supportsStreamingRules, startup) = ReadStartupProfile(context, providerName);
                var (audit, snapshot) = await store.UpdateAsync(config =>
                {
                    var (next, entry) = ApplyProfile(config, providerName, request, actor);
                    return (next, (entry, CreateSnapshot(providerName, next.Backfill,
                        canEdit: true, supportsStreamingRules, startup)));
                }, ct).ConfigureAwait(false);
                loggerFactory.CreateLogger(nameof(ProviderValidationEndpoints)).LogInformation(
                    "Provider validation profile changed {Provider} {AuditId} {Actor} {Loosened} {Reason} {StaleToleranceDays} {FutureSkewSeconds} {StaleThresholdSeconds} {MaxLatencyMs}",
                    providerName, audit.Id, audit.Actor, audit.Loosened, audit.Reason, audit.After.StaleToleranceDays,
                    audit.After.FutureSkewSeconds, audit.After.StaleThresholdSeconds, audit.After.MaxLatencyMs);
                return Results.Json(new
                {
                    saved = true,
                    snapshot.Impact,
                    snapshot.PendingRestart,
                    profile = audit.After,
                    audit,
                    snapshot
                }, jsonOptions);
            }
            catch (StaleProfileException)
            {
                return ApiProblemDetails.Conflict(context, "The saved profile changed. Reload it before reviewing another edit.");
            }
            catch (ArgumentException exception)
            {
                return ApiProblemDetails.Validation(context, "profile", exception.Message);
            }
            catch (UiConfigurationValidationException exception)
            {
                return ApiProblemDetails.Validation(context, "profile", exception.Message);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or InvalidOperationException or TimeoutException)
            {
                return ProfileUnavailable(context, error, "The profile and its audit entry could not be saved.");
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
        var backfill = config.Backfill ?? new BackfillConfig();
        provider = ProviderIdentity.NormalizeId(provider);
        var previous = ProviderValidationProfile.Resolve(backfill, provider);
        var isOverride = backfill.ValidationProfiles?.Keys.Any(id => ProviderIdentity.EqualsId(id, provider)) == true;

        if (request.ExpectedRevision is not null &&
            request.ExpectedRevision != Revision(previous, isOverride, ProviderAudit(backfill, provider).FirstOrDefault()))
            throw new StaleProfileException();
        if (request.ResetToDefault == (request.Profile is not null))
            throw new ArgumentException("Supply a proposed profile or reset to the deployment default.");
        var proposed = request.Profile ?? ProviderValidationProfile.Default;
        proposed.Validate();
        var reason = request.Reason?.Trim();
        if (reason?.Length > 2000)
            throw new ArgumentException("The change reason cannot exceed 2000 characters.");
        var loosened = proposed.Loosens(previous);
        if (loosened && string.IsNullOrEmpty(reason))
            throw new ArgumentException("Loosening validation requires a recorded reason.");
        if (loosened && !request.ConfirmLoosening)
            throw new ArgumentException("Confirm the impact on future jobs before loosening validation.");
        var audit = new ProviderValidationProfileAudit(Guid.NewGuid(), provider, DateTimeOffset.UtcNow,
            actor, string.IsNullOrEmpty(reason) ? null : reason, loosened, previous, proposed, request.ResetToDefault);
        var profiles = new Dictionary<string, ProviderValidationProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in backfill.ValidationProfiles ?? new Dictionary<string, ProviderValidationProfile>())
        {
            if (!ProviderIdentity.EqualsId(pair.Key, provider))
                profiles[pair.Key] = pair.Value;
        }
        if (!request.ResetToDefault)
            profiles[provider] = proposed;
        return (config with
        {
            Backfill = backfill with
            {
                ValidationProfiles = profiles,
                ValidationProfileAudit = (backfill.ValidationProfileAudit ?? []).Append(audit).ToArray()
            }
        }, audit);
    }

    private static (bool SupportsStreamingRules, ProviderValidationProfile? Startup) ReadStartupProfile(HttpContext context, string provider)
    {
        var supported = provider is "alpaca" or "polygon" or "robinhood";
        return (supported, supported
            ? context.RequestServices.GetService<ProviderFactory>()?.GetStartupValidationProfile(provider)
            : null);
    }

    private static ProfileSnapshot CreateSnapshot(
        string provider, BackfillConfig? backfill, bool canEdit, bool supportsStreamingRules, ProviderValidationProfile? startup)
    {
        var profile = ProviderValidationProfile.Resolve(backfill, provider);
        var isOverride = backfill?.ValidationProfiles?.Keys.Any(id => ProviderIdentity.EqualsId(id, provider)) == true;
        var audit = ProviderAudit(backfill, provider).Take(100).ToArray();
        var pendingRestart = startup is not null && (startup with { StaleToleranceDays = profile.StaleToleranceDays }) != profile;
        return new ProfileSnapshot(provider, profile, ProviderValidationProfile.Default, isOverride, audit,
            canEdit,
            Revision(profile, isOverride, audit.FirstOrDefault()), supportsStreamingRules, startup,
            pendingRestart, pendingRestart ? "RestartHost" : "Live");
    }

    private static IEnumerable<ProviderValidationProfileAudit> ProviderAudit(BackfillConfig? backfill, string provider)
        => (backfill?.ValidationProfileAudit ?? []).Reverse().Where(entry => ProviderIdentity.EqualsId(entry.Provider, provider));

    private static bool IsProviderId(string id)
        => id.Length is > 0 and <= 80 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Revision(ProviderValidationProfile profile, bool isOverride, ProviderValidationProfileAudit? lastChange)
        => FormattableString.Invariant($"{lastChange?.Id:N}/{isOverride}/{profile.StaleToleranceDays}/{profile.FutureSkewSeconds}/{profile.StaleThresholdSeconds}/{profile.MaxLatencyMs:R}");

    private static AppConfig LoadProfileConfig(ConfigStore store)
    {
        try
        {
            return store.CoreStore.LoadRequired();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return store.Load();
        }
    }

    private static IResult ProfileUnavailable(HttpContext context, Exception error, string detail)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ProviderValidationEndpoints))
            .LogWarning(error, "Could not access provider validation configuration.");
        return ApiProblemDetails.ServiceUnavailable(context, "provider validation configuration", detail);
    }

    private sealed class StaleProfileException : Exception;

    private sealed record ProfileSnapshot(
        string Provider, ProviderValidationProfile Profile, ProviderValidationProfile DefaultProfile, bool IsOverride,
        ProviderValidationProfileAudit[] Audit, bool CanEdit, string Revision, bool SupportsStreamingRules,
        ProviderValidationProfile? StartupStreamingProfile, bool PendingRestart, string Impact);
}

public sealed record ProviderValidationProfileRequest(
    ProviderValidationProfile? Profile = null,
    string? Reason = null,
    bool ConfirmLoosening = false,
    bool ResetToDefault = false,
    string? ExpectedRevision = null);
