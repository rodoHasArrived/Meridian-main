using System.Text.Json;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Catalog;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SymbolMappingEndpoints
{
    private const string RolloutRoute = UiApiRoutes.SymbolMappings + "/rollout";

    private static void MapRolloutEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(RolloutRoute).WithTags("Symbol Mapping");
        group.RequireWorkstationTenantScope();
        group.RequireAnyPermission(UserPermission.ViewConfig, UserPermission.ModifyConfig);

        group.MapGet("", (ConfigStore store, IServiceProvider services) =>
        {
            var config = store.LoadRequired();
            var settings = config.DataSources?.SymbolMappings ?? new SymbolMappingsConfig(ResolutionMode: config.Backfill?.SymbolResolutionMode ?? SymbolResolutionMode.Compare);
            var resolver = services.GetService<ISymbolResolver>() as CanonicalRegistrySymbolResolver;
            var reader = services.GetService<ISymbolResolutionMismatchReader>();
            var mismatches = reader?.GetRecent() ?? [];
            var providers = LegacyProviders.Concat(settings.ProviderResolutionModes?.Keys ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
            return Results.Json(new
            {
                mismatchCount = reader?.TotalCount ?? 0,
                providers = providers.Select(provider => new
                {
                    provider,
                    savedMode = SavedMode(settings, provider),
                    activeMode = resolver?.GetProviderMode(provider),
                    pendingRestart = resolver is not null && resolver.GetProviderMode(provider) != SavedMode(settings, provider),
                    recentMismatches = mismatches.Where(m => NormalizeRolloutProvider(m.ToProvider).Equals(provider, StringComparison.OrdinalIgnoreCase))
                        .Select(ToMismatchResponse).ToArray()
                }).ToArray()
            }, jsonOptions);
        }).WithName("GetSymbolMappingRollout");

        group.MapPut("/{provider}", async (
            string provider, SymbolMappingRolloutRequest request, ConfigStore store,
            IServiceProvider services, HttpContext context, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            provider = provider.Trim().ToLowerInvariant();
            if (!LegacyProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
                return ApiProblemDetails.Validation(context, "provider", "Select ib, alpaca, polygon, or yahoo.");
            if (request.Mode is not (SymbolResolutionMode.Compare or SymbolResolutionMode.Canonical))
                return ApiProblemDetails.Validation(context, "mode", "Use Compare or Canonical for a guided rollout.");
            var resolver = services.GetService<ISymbolResolver>() as CanonicalRegistrySymbolResolver;
            if (resolver is null)
                return ApiProblemDetails.ServiceUnavailable(context, "active canonical symbol resolver");
            var reader = services.GetService<ISymbolResolutionMismatchReader>();
            if (request.Mode == SymbolResolutionMode.Canonical &&
                (!request.EvidenceReviewed || !request.ConfirmArchivesNotRewritten || !request.ConfirmSeriesRemainSeparate ||
                 string.IsNullOrWhiteSpace(request.Reason)))
                return ApiProblemDetails.Validation(context, "confirmation",
                    "Review comparison evidence and record a reason. Confirm existing archives are not rewritten and raw and adjusted series remain separate.");
            if (request.Mode == SymbolResolutionMode.Canonical &&
                (resolver.GetProviderMode(provider) != SymbolResolutionMode.Compare || reader is null))
                return ApiProblemDetails.Conflict(context, "Run this provider in Compare mode and inspect retained evidence before promoting it.");

            try
            {
                await store.UpdateAsync(config =>
                {
                    var dataSources = config.DataSources ?? new DataSourcesConfig();
                    var settings = dataSources.SymbolMappings ?? new SymbolMappingsConfig(ResolutionMode: config.Backfill?.SymbolResolutionMode ?? SymbolResolutionMode.Compare);
                    var previous = SavedMode(settings, provider);
                    if (previous != request.ExpectedMode)
                        throw new InvalidOperationException("The saved mode changed. Refresh before retrying.");
                    if (request.Mode == SymbolResolutionMode.Canonical &&
                        (previous != SymbolResolutionMode.Compare || reader!.TotalCount != request.ReviewedMismatchCount))
                        throw new InvalidOperationException("Comparison evidence or saved mode changed. Refresh and review it again.");
                    var modes = (settings.ProviderResolutionModes ?? new Dictionary<string, SymbolResolutionMode>())
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    modes[provider] = request.Mode;
                    return config with { DataSources = dataSources with { SymbolMappings = settings with { ProviderResolutionModes = modes } } };
                }, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return ApiProblemDetails.Conflict(context, ex.Message);
            }
            loggerFactory.CreateLogger(nameof(SymbolMappingEndpoints)).LogInformation(
                "Symbol resolution rollout saved for {Provider}: {PreviousMode} to {ResolutionMode}, reason={Reason}, reviewedMismatchCount={ReviewedMismatchCount}",
                provider, request.ExpectedMode, request.Mode, request.Reason, request.ReviewedMismatchCount);
            return Results.Json(new { provider, savedMode = request.Mode, activeMode = resolver.GetProviderMode(provider), impact = "RestartHost", pendingRestart = resolver.GetProviderMode(provider) != request.Mode }, jsonOptions);
        }).WithName("SaveSymbolMappingRollout")
            .RequirePermission(UserPermission.ModifyConfig)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static string NormalizeRolloutProvider(string provider)
        => provider.Trim().ToLowerInvariant() is "interactive-brokers" or "interactivebrokers" ? "ib" : provider.Trim().ToLowerInvariant();

    private static SymbolResolutionMode SavedMode(SymbolMappingsConfig settings, string provider)
    {
        if (settings.ProviderResolutionModes is not null)
            foreach (var pair in settings.ProviderResolutionModes)
                if (pair.Key.Equals(provider, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
        return settings.ResolutionMode;
    }
}

public sealed record SymbolMappingRolloutRequest(
    SymbolResolutionMode Mode,
    SymbolResolutionMode ExpectedMode,
    long ReviewedMismatchCount,
    bool EvidenceReviewed = false,
    bool ConfirmArchivesNotRewritten = false,
    bool ConfirmSeriesRemainSeparate = false,
    string? Reason = null);
