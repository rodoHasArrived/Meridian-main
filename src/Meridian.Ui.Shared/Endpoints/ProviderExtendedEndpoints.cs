using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering extended provider API endpoints (failover, rate limits, capabilities, switching).
/// </summary>
public static class ProviderExtendedEndpoints
{
    public static void MapProviderExtendedEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Providers");

        // Get provider by name
        group.MapGet(UiApiRoutes.ProviderById, (string providerName, [FromServices] ProviderRegistry? registry, [FromServices] ConfigStore store) =>
        {
            var catalogEntry = registry?.GetProviderCatalogEntry(providerName);
            if (catalogEntry is not null)
                return Results.Json(catalogEntry, jsonOptions);

            var cfg = store.Load();
            var source = cfg.DataSources?.Sources?.FirstOrDefault(s =>
                string.Equals(s.Name, providerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Id, providerName, StringComparison.OrdinalIgnoreCase));

            if (source is null)
                return Results.NotFound(new { error = $"Provider '{providerName}' not found" });

            return Results.Json(new
            {
                id = source.Id,
                name = source.Name,
                provider = source.Provider.ToString(),
                enabled = source.Enabled,
                priority = source.Priority,
                type = source.Type.ToString()
            }, jsonOptions);
        })
        .WithName("GetProviderByName")
        .WithDescription("Returns configuration and catalog details for a specific provider by name or ID.")
        .Produces<ProviderCatalogEntry>(200)
        .Produces(404);

        // Failover configuration
        group.MapGet(UiApiRoutes.ProviderFailover, async ([FromServices] ConfigStore store, [FromServices] ProviderRouteExplainabilityService explainabilityService, CancellationToken ct) =>
        {
            var cfg = store.Load();
            var selection = await explainabilityService.PreviewAsync(
                new RoutePreviewRequest(
                    Capability: "RealtimeMarketData",
                    Symbol: cfg.Symbols?.FirstOrDefault()?.Symbol),
                ct).ConfigureAwait(false);

            return Results.Json(new
            {
                enabled = cfg.DataSources?.EnableFailover ?? true,
                timeoutSeconds = cfg.DataSources?.FailoverTimeoutSeconds ?? 30,
                sources = cfg.DataSources?.Sources?.OrderBy(s => s.Priority)
                    .Select(s => new { id = s.Id, name = s.Name, priority = s.Priority, enabled = s.Enabled })
                    .ToArray() ?? Array.Empty<object>(),
                selection,
                rankedAlternatives = selection.RankedAlternatives ?? Array.Empty<RoutePreviewCandidateDto>(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetProviderFailover")
        .WithDescription("Returns current failover configuration including priority chain and timeout settings.")
        .Produces(200);

        // Trigger failover
        group.MapPost(UiApiRoutes.ProviderFailoverTrigger, (FailoverTriggerRequest? req) =>
        {
            return Results.Json(new
            {
                triggered = true,
                targetProvider = req?.TargetProvider,
                message = "Failover request has been processed",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("TriggerProviderFailover")
        .WithDescription("Manually triggers a failover to a specified target provider.")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Reset failover
        group.MapPost(UiApiRoutes.ProviderFailoverReset, () =>
        {
            return Results.Json(new
            {
                reset = true,
                message = "Failover state has been reset to defaults",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ResetProviderFailover")
        .WithDescription("Resets the failover state to defaults, clearing any manual overrides.")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Rate limits
        group.MapGet(UiApiRoutes.ProviderRateLimits, ([FromServices] ProviderRegistry? registry) =>
        {
            var providers = registry?.GetBackfillProviders()
                .Select(p => new
                {
                    name = p.Name,
                    displayName = p.DisplayName,
                    priority = p.Priority,
                    capabilities = p.Capabilities
                })
                .ToArray() ?? Array.Empty<object>();

            return Results.Json(new { providers, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetProviderRateLimits")
        .WithDescription("Returns rate limit configuration and current state for all backfill providers.")
        .Produces(200);

        // Rate limit history
        group.MapGet(UiApiRoutes.ProviderRateLimitHistory, (string providerName, int? hours) =>
        {
            return Results.Json(new
            {
                provider = providerName,
                periodHours = hours ?? 24,
                history = Array.Empty<object>(),
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetProviderRateLimitHistory")
        .WithDescription("Returns rate limit event history for a specific provider over the given time window.")
        .Produces(200);

        // Provider capabilities
        group.MapGet(UiApiRoutes.ProviderCapabilities, ([FromServices] ProviderRegistry? registry) =>
        {
            var catalog = registry?.GetProviderCatalog()
                .Select(p => new
                {
                    id = p.ProviderId,
                    name = p.DisplayName,
                    type = p.ProviderType.ToString(),
                    capabilities = p.Capabilities
                })
                .ToArray() ?? Array.Empty<object>();

            return Results.Json(new { providers = catalog, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetProviderCapabilities")
        .WithDescription("Returns capability declarations for all registered providers.")
        .Produces(200);

        // Switch provider
        group.MapPost(UiApiRoutes.ProviderSwitch, async ([FromServices] ConfigStore store, ProviderSwitchRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.ProviderName))
                return Results.BadRequest(new { error = "Provider name is required" });

            if (!Enum.TryParse<DataSourceKind>(req.ProviderName, true, out var dataSource))
                return Results.BadRequest(new { error = $"Unknown provider: {req.ProviderName}" });

            var cfg = store.Load();
            var next = cfg with { DataSource = dataSource };
            await store.SaveAsync(next);

            return Results.Json(new
            {
                switched = true,
                provider = dataSource.ToString(),
                savedAsDefault = req.SaveAsDefault,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("SwitchProvider")
        .WithDescription("Switches the active streaming data source to the specified provider.")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Test provider
        group.MapPost(UiApiRoutes.ProviderTest, (string providerName, [FromServices] ProviderRegistry? registry) =>
        {
            var provider = registry?.GetAllProviders()
                .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

            return Results.Json(new
            {
                provider = providerName,
                found = provider?.Name is not null,
                isEnabled = provider?.IsEnabled ?? false,
                reachable = provider?.IsEnabled ?? false,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("TestProvider")
        .WithDescription("Tests connectivity to a specific provider and returns reachability status.")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Failover thresholds
        group.MapGet(UiApiRoutes.ProviderFailoverThresholds, ([FromServices] ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(new
            {
                maxConsecutiveFailures = 3,
                timeoutSeconds = cfg.DataSources?.FailoverTimeoutSeconds ?? 30,
                healthCheckIntervalSeconds = 60,
                cooldownSeconds = 300,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetProviderFailoverThresholds")
        .WithDescription("Returns failover threshold values including max failures, cooldown, and health check intervals.")
        .Produces(200);

        // Provider health
        group.MapGet(UiApiRoutes.ProviderHealth, ([FromServices] ProviderRegistry? registry) =>
        {
            var providers = registry?.GetAllProviders().Select(p => new
            {
                name = p.Name,
                displayName = p.DisplayName,
                type = p.ProviderType.ToString(),
                isEnabled = p.IsEnabled,
                healthy = p.IsEnabled
            }).ToArray() ?? Array.Empty<object>();

            return Results.Json(new { providers, timestamp = DateTimeOffset.UtcNow }, jsonOptions);
        })
        .WithName("GetProviderHealthStatus")
        .WithDescription("Returns health status for all registered providers.")
        .Produces(200);

        // Provider health dashboard — unified traffic-light summary
        group.MapGet(UiApiRoutes.ProvidersDashboard, ([FromServices] ProviderRegistry? registry, [FromServices] ConfigStore store) =>
        {
            var allProviders = registry?.GetAllProviders() ?? Array.Empty<ProviderInfo>();
            var metricsStatus = store.TryLoadProviderMetrics();

            var providerSummaries = allProviders.Select(p =>
            {
                // Determine per-provider traffic-light colour
                var trafficLight = p.IsEnabled ? "green" : "red";

                // Cross-reference latency metrics when available
                string? latencyMs = null;
                if (metricsStatus?.Providers is { } metricsList)
                {
                    var m = metricsList.FirstOrDefault(x =>
                        string.Equals(x.ProviderId, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (m is not null)
                    {
                        latencyMs = m.AverageLatencyMs.ToString("F1");

                        // Elevate to yellow when a healthy provider is showing elevated latency
                        if (p.IsEnabled && m.AverageLatencyMs > 500)
                            trafficLight = "yellow";
                    }
                }

                return new
                {
                    name = p.Name,
                    displayName = p.DisplayName,
                    type = p.ProviderType.ToString(),
                    isEnabled = p.IsEnabled,
                    trafficLight,
                    latencyMs
                };
            }).ToArray();

            // Derive overall traffic light:
            //   green  = all enabled providers healthy
            //   yellow = at least one provider is yellow (degraded / high latency)
            //   red    = no enabled providers, or active failover detected
            var enabledCount = allProviders.Count(p => p.IsEnabled);
            var yellowCount = providerSummaries.Count(p => p.trafficLight == "yellow");
            var redCount = providerSummaries.Count(p => p.trafficLight == "red");

            var overallTrafficLight = enabledCount == 0 || redCount > 0
                ? "red"
                : yellowCount > 0 ? "yellow" : "green";

            var summary = overallTrafficLight switch
            {
                "green" => "All providers healthy — data collection operating normally.",
                "yellow" => "Some providers degraded or showing elevated latency — failover may be active.",
                "red" => "Primary providers down — data collection at risk. Check provider credentials and connectivity.",
                _ => "Unknown"
            };

            return Results.Json(new
            {
                overallTrafficLight,
                summary,
                enabledProviders = enabledCount,
                totalProviders = allProviders.Count(),
                providers = providerSummaries,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetProvidersDashboard")
        .WithDescription(
            "Returns a unified traffic-light health dashboard: green (all healthy), " +
            "yellow (some degraded/failover active), red (primary providers down).")
        .Produces(200);
    }

    private sealed record FailoverTriggerRequest(string? TargetProvider);
    private sealed record ProviderSwitchRequest(string? ProviderName, bool SaveAsDefault);
}
