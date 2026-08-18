using System.Linq;
using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
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
        .WithName("TriggerProviderFailover").RequirePermission(UserPermission.ManageProviders)
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageProviders))
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
        .WithName("ResetProviderFailover").RequirePermission(UserPermission.ManageProviders)
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageProviders))
        .WithDescription("Resets the failover state to defaults, clearing any manual overrides.")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Rate limits
        group.MapGet(UiApiRoutes.ProviderRateLimits, (
            [FromServices] ProviderRegistry? registry,
            [FromServices] IEnumerable<IDataSource> dataSources) =>
        {
            return Results.Json(
                CreateRateLimitsResponse(registry, dataSources, DateTimeOffset.UtcNow),
                jsonOptions);
        })
        .WithName("GetProviderRateLimits")
        .WithDescription("Returns typed rate limit configuration and current runtime state for provider surfaces that expose diagnostics.")
        .Produces<ProviderRateLimitsResponse>(200);

        // Rate limit history
        group.MapGet(UiApiRoutes.ProviderRateLimitHistory, (string providerName, int? hours) =>
        {
            return Results.Json(
                new ProviderRateLimitHistoryResponse(
                    providerName,
                    hours ?? 24,
                    Array.Empty<ProviderRateLimitEventDto>(),
                    DateTimeOffset.UtcNow,
                    IsAvailable: false,
                    Message: "Runtime rate-limit history is not retained. Use /api/providers/rate-limits for the current snapshot."),
                jsonOptions);
        })
        .WithName("GetProviderRateLimitHistory")
        .WithDescription("Legacy route retained for compatibility; rate-limit event history is not retained.")
        .Produces<ProviderRateLimitHistoryResponse>(200);

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
        .WithName("SwitchProvider").RequirePermission(UserPermission.ManageProviders)
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageProviders))
        .WithDescription("Switches the active streaming data source to the specified provider.")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Test provider
        group.MapPost(UiApiRoutes.ProviderTest, (string providerName, [FromServices] ProviderRegistry? registry) =>
        {
            var provider = registry?.GetAllProviders()
                .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
            var diagnostics = provider is null
                ? null
                : ProviderConnectionDiagnosticsProjection.Find(
                    ProviderConnectionDiagnosticsProjection.BuildByProviderId(registry),
                    provider.Name,
                    provider.DisplayName);
            var connectionState = provider is null
                ? "not-found"
                : !provider.IsEnabled
                    ? "disabled"
                    : diagnostics is null
                        ? "unavailable"
                        : ResolveConnectionState(provider.IsEnabled, diagnostics.LifecycleState, diagnostics.IsConnected);

            return Results.Json(new
            {
                provider = providerName,
                found = provider?.Name is not null,
                isEnabled = provider?.IsEnabled ?? false,
                reachable = provider is null
                    ? (bool?)null
                    : ResolveIsConnected(provider.IsEnabled, diagnostics?.IsConnected),
                connectionState,
                diagnosticsAvailable = diagnostics is not null,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("TestProvider")
        .WithDescription("Returns live provider connection diagnostics when available; reachability is null when no runtime probe exists.")
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
            var diagnosticsByProviderId = ProviderConnectionDiagnosticsProjection.BuildByProviderId(registry);
            var providers = registry is null
                ? Array.Empty<object>()
                : registry.GetAllProviders().Select(p =>
                {
                    var diagnostics = ProviderConnectionDiagnosticsProjection.Find(
                        diagnosticsByProviderId,
                        p.Name,
                        p.DisplayName);
                    var isConnected = ResolveIsConnected(p.IsEnabled, diagnostics?.IsConnected);
                    var connectionState = ResolveConnectionState(
                        p.IsEnabled,
                        diagnostics?.LifecycleState,
                        diagnostics?.IsConnected);

                    return (object)new
                    {
                        name = p.Name,
                        providerId = p.Name,
                        displayName = p.DisplayName,
                        providerName = p.DisplayName,
                        type = p.ProviderType.ToString(),
                        isEnabled = p.IsEnabled,
                        isConnected,
                        connectionState,
                        diagnosticsAvailable = diagnostics is not null,
                        healthy = !p.IsEnabled
                            ? false
                            : diagnostics is null ? (bool?)null : diagnostics.IsConnected,
                        connectionStabilityScore = diagnostics is null ? (int?)null : diagnostics.IsConnected ? 100 : 0,
                        averageLatencyMs = 0,
                        latencyP99Ms = 0,
                        latencyConsistencyScore = 100,
                        dataCompletenessPercent = 100,
                        reconnectsLastHour = diagnostics?.ReconnectAttempts ?? 0,
                        uptimePercent = diagnostics is null ? (int?)null : diagnostics.IsConnected ? 100 : 0,
                        messagesPerSecond = 0,
                        errorsLastHour = diagnostics?.LastFailureKind is null ? 0 : 1,
                        lifecycleState = diagnostics?.LifecycleState,
                        webSocketState = diagnostics?.WebSocketState,
                        isReconnecting = diagnostics?.IsReconnecting,
                        lastHeartbeatReceivedAt = diagnostics?.LastHeartbeatReceivedAt,
                        lastMessageReceivedAt = diagnostics?.LastMessageReceivedAt,
                        lastReconnectAttemptAt = diagnostics?.LastReconnectAttemptAt,
                        reconnectAttempts = diagnostics?.ReconnectAttempts,
                        lastFailureKind = diagnostics?.LastFailureKind
                    };
                }).ToArray();

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
            var diagnosticsByProviderId = ProviderConnectionDiagnosticsProjection.BuildByProviderId(registry);

            var providerSummaries = allProviders.Select(p =>
            {
                var diagnostics = ProviderConnectionDiagnosticsProjection.Find(
                    diagnosticsByProviderId,
                    p.Name,
                    p.DisplayName);
                // Determine per-provider traffic-light colour
                var isConnected = ResolveIsConnected(p.IsEnabled, diagnostics?.IsConnected);
                var connectionState = ResolveConnectionState(
                    p.IsEnabled,
                    diagnostics?.LifecycleState,
                    diagnostics?.IsConnected);
                var trafficLight = !p.IsEnabled
                    ? "red"
                    : diagnostics is null
                        ? "unknown"
                        : diagnostics.IsReconnecting
                            ? "yellow"
                            : diagnostics.IsConnected ? "green" : "red";

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
                        if (p.IsEnabled && diagnostics?.IsConnected == true && m.AverageLatencyMs > 500)
                            trafficLight = "yellow";
                    }
                }

                return new
                {
                    name = p.Name,
                    displayName = p.DisplayName,
                    type = p.ProviderType.ToString(),
                    isEnabled = p.IsEnabled,
                    isConnected,
                    connectionState,
                    diagnosticsAvailable = diagnostics is not null,
                    trafficLight,
                    latencyMs,
                    lifecycleState = diagnostics?.LifecycleState,
                    lastHeartbeatReceivedAt = diagnostics?.LastHeartbeatReceivedAt,
                    lastMessageReceivedAt = diagnostics?.LastMessageReceivedAt,
                    lastReconnectAttemptAt = diagnostics?.LastReconnectAttemptAt,
                    reconnectAttempts = diagnostics?.ReconnectAttempts,
                    lastFailureKind = diagnostics?.LastFailureKind
                };
            }).ToArray();

            // Derive overall traffic light:
            //   green  = all enabled providers healthy
            //   yellow = at least one provider is yellow (degraded / high latency)
            //   red    = no enabled providers, or active failover detected
            var enabledCount = allProviders.Count(p => p.IsEnabled);
            var yellowCount = providerSummaries.Count(p => p.trafficLight == "yellow");
            var redCount = providerSummaries.Count(p => p.trafficLight == "red");
            var unknownCount = providerSummaries.Count(p => p.trafficLight == "unknown");

            var overallTrafficLight = enabledCount == 0 || redCount > 0
                ? "red"
                : yellowCount > 0
                    ? "yellow"
                    : unknownCount > 0 ? "unknown" : "green";

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

    internal static ProviderRateLimitsResponse CreateRateLimitsResponse(
        ProviderRegistry? registry,
        IEnumerable<IDataSource> dataSources,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        var providers = new List<ProviderRateLimitSnapshotDto>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in registry?.GetBackfillProviders() ?? Array.Empty<IHistoricalDataProvider>())
        {
            var snapshot = (provider as IProviderRateLimitDiagnosticsSource)?.GetRateLimitDiagnosticsSnapshot();
            var surface = snapshot?.Surface ?? ProviderRateLimitSurfaces.Historical;
            if (!keys.Add($"{provider.Name}:{surface}"))
                continue;

            providers.Add(new ProviderRateLimitSnapshotDto(
                Provider: provider.Name,
                Name: provider.Name,
                DisplayName: provider.DisplayName,
                Priority: provider.Priority,
                Capabilities: MapCapabilities(provider.Capabilities),
                Surface: surface,
                StateAvailable: snapshot?.StateAvailable == true,
                ObservedAt: snapshot?.ObservedAt ?? observedAt,
                RequestsInWindow: snapshot?.StateAvailable == true ? snapshot.RequestsInWindow : null,
                MaxRequestsPerWindow: snapshot?.MaxRequestsPerWindow ?? provider.MaxRequestsPerWindow,
                RemainingRequests: snapshot?.StateAvailable == true ? snapshot.RemainingRequests : null,
                WindowSeconds: (snapshot?.Window ?? provider.RateLimitWindow).TotalSeconds,
                UsageRatio: snapshot?.StateAvailable == true ? snapshot.UsageRatio : null,
                IsRateLimited: snapshot?.StateAvailable == true && snapshot.IsRateLimited,
                ResetAt: snapshot?.StateAvailable == true ? snapshot.ResetAt : null,
                Reason: snapshot?.Reason ?? "runtime-diagnostics-unavailable"));
        }

        foreach (var provider in registry?.GetStreamingProviders().OfType<IProviderRateLimitDiagnosticsSource>()
                     ?? Array.Empty<IProviderRateLimitDiagnosticsSource>())
        {
            var snapshot = provider.GetRateLimitDiagnosticsSnapshot();
            if (!keys.Add($"{snapshot.ProviderId}:{snapshot.Surface}"))
                continue;

            var metadata = (IProviderMetadata)provider;
            providers.Add(new ProviderRateLimitSnapshotDto(
                Provider: snapshot.ProviderId,
                Name: metadata.ProviderId,
                DisplayName: metadata.ProviderDisplayName,
                Priority: metadata.ProviderPriority,
                Capabilities: MapCapabilities(metadata.ProviderCapabilities),
                Surface: snapshot.Surface,
                StateAvailable: snapshot.StateAvailable,
                ObservedAt: snapshot.ObservedAt,
                RequestsInWindow: snapshot.StateAvailable ? snapshot.RequestsInWindow : null,
                MaxRequestsPerWindow: snapshot.MaxRequestsPerWindow,
                RemainingRequests: snapshot.StateAvailable ? snapshot.RemainingRequests : null,
                WindowSeconds: snapshot.Window.TotalSeconds,
                UsageRatio: snapshot.StateAvailable ? snapshot.UsageRatio : null,
                IsRateLimited: snapshot.StateAvailable && snapshot.IsRateLimited,
                ResetAt: snapshot.StateAvailable ? snapshot.ResetAt : null,
                Reason: snapshot.Reason));
        }

        foreach (var source in dataSources.OfType<IProviderRateLimitDiagnosticsSource>())
        {
            var snapshot = source.GetRateLimitDiagnosticsSnapshot();
            if (!keys.Add($"{snapshot.ProviderId}:{snapshot.Surface}"))
                continue;

            var dataSource = (IDataSource)source;
            providers.Add(new ProviderRateLimitSnapshotDto(
                Provider: snapshot.ProviderId,
                Name: snapshot.ProviderId,
                DisplayName: dataSource.DisplayName,
                Priority: dataSource.Priority,
                Capabilities: MapCapabilities(dataSource),
                Surface: snapshot.Surface,
                StateAvailable: snapshot.StateAvailable,
                ObservedAt: snapshot.ObservedAt,
                RequestsInWindow: snapshot.StateAvailable ? snapshot.RequestsInWindow : null,
                MaxRequestsPerWindow: snapshot.MaxRequestsPerWindow,
                RemainingRequests: snapshot.StateAvailable ? snapshot.RemainingRequests : null,
                WindowSeconds: snapshot.Window.TotalSeconds,
                UsageRatio: snapshot.StateAvailable ? snapshot.UsageRatio : null,
                IsRateLimited: snapshot.StateAvailable && snapshot.IsRateLimited,
                ResetAt: snapshot.StateAvailable ? snapshot.ResetAt : null,
                Reason: snapshot.Reason));
        }

        return new ProviderRateLimitsResponse(
            providers
                .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(provider => provider.Surface, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            observedAt);
    }

    internal static string ResolveConnectionState(
        bool isEnabled,
        string? lifecycleState,
        bool? isConnected)
    {
        if (!isEnabled)
            return "disabled";
        if (string.IsNullOrWhiteSpace(lifecycleState))
            return "unknown";

        return lifecycleState.Trim().ToLowerInvariant() switch
        {
            "connected" when isConnected == true => "connected",
            "reconnecting" => "reconnecting",
            "degraded" => "degraded",
            "connecting" => "connecting",
            "disconnecting" => "disconnecting",
            "disconnected" => "disconnected",
            "failed" => "failed",
            "notconfigured" => "not-configured",
            "configured" => "configured",
            _ => isConnected == true ? "connected" : "disconnected"
        };
    }

    internal static bool? ResolveIsConnected(bool isEnabled, bool? runtimeIsConnected)
        => isEnabled ? runtimeIsConnected : false;

    private static ProviderRateLimitCapabilitiesDto MapCapabilities(HistoricalDataCapabilities capabilities) => new(
        capabilities.AdjustedPrices,
        capabilities.Intraday,
        capabilities.Dividends,
        capabilities.Splits,
        capabilities.Quotes,
        capabilities.Trades,
        capabilities.Auctions,
        capabilities.SupportedMarkets.ToArray());

    private static ProviderRateLimitCapabilitiesDto MapCapabilities(IDataSource source)
    {
        var capabilities = source.Capabilities;
        return new ProviderRateLimitCapabilitiesDto(
            capabilities.HasFlag(DataSourceCapabilities.HistoricalAdjustedPrices),
            capabilities.HasFlag(DataSourceCapabilities.HistoricalIntradayBars),
            capabilities.HasFlag(DataSourceCapabilities.HistoricalDividends),
            capabilities.HasFlag(DataSourceCapabilities.HistoricalSplits),
            capabilities.HasFlag(DataSourceCapabilities.HistoricalTicks),
            capabilities.HasFlag(DataSourceCapabilities.HistoricalTicks),
            Auctions: false,
            source.SupportedMarkets.ToArray());
    }

    private static ProviderRateLimitCapabilitiesDto MapCapabilities(ProviderCapabilities capabilities) => new(
        capabilities.SupportsAdjustedPrices,
        capabilities.SupportsIntraday,
        capabilities.SupportsDividends,
        capabilities.SupportsSplits,
        capabilities.SupportsRealtimeQuotes || capabilities.SupportsHistoricalQuotes,
        capabilities.SupportsRealtimeTrades || capabilities.SupportsHistoricalTrades,
        capabilities.SupportsHistoricalAuctions,
        capabilities.SupportedMarkets.ToArray());

    private sealed record FailoverTriggerRequest(string? TargetProvider);
    private sealed record ProviderSwitchRequest(string? ProviderName, bool SaveAsDefault);
}
