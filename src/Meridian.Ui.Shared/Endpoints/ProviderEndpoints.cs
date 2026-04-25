using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering provider-related API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
public static class ProviderEndpoints
{
    /// <summary>
    /// Maps all provider and data source API endpoints.
    /// </summary>
    public static void MapProviderEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Providers");

        // Get all data sources
        group.MapGet(UiApiRoutes.ConfigDataSources, (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(new
            {
                sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>(),
                defaultRealTimeSourceId = cfg.DataSources?.DefaultRealTimeSourceId,
                defaultHistoricalSourceId = cfg.DataSources?.DefaultHistoricalSourceId,
                enableFailover = cfg.DataSources?.EnableFailover ?? true,
                failoverTimeoutSeconds = cfg.DataSources?.FailoverTimeoutSeconds ?? 30
            }, jsonOptions);
        })
        .WithName("GetDataSources")
        .WithDescription("Returns all configured data sources with failover and default source settings.")
        .Produces(200);

        // Create or update data source
        group.MapPost(UiApiRoutes.ConfigDataSources, async (ConfigStore store, DataSourceConfigRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest("Name is required.");

            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var sources = (dataSources.Sources ?? Array.Empty<DataSourceConfig>()).ToList();

            var id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("N") : req.Id;
            var source = new DataSourceConfig(
                Id: id,
                Name: req.Name,
                Provider: Enum.TryParse<DataSourceKind>(req.Provider, ignoreCase: true, out var p) ? p : DataSourceKind.IB,
                Enabled: req.Enabled,
                Type: Enum.TryParse<DataSourceType>(req.Type, ignoreCase: true, out var t) ? t : DataSourceType.RealTime,
                Priority: req.Priority,
                Alpaca: req.Alpaca?.ToDomain(),
                Polygon: req.Polygon?.ToDomain(),
                IB: req.IB?.ToDomain(),
                Symbols: req.Symbols,
                Description: req.Description,
                Tags: req.Tags
            );

            var idx = sources.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                sources[idx] = source;
            else
                sources.Add(source);

            var next = cfg with { DataSources = dataSources with { Sources = sources.ToArray() } };
            await store.SaveAsync(next);

            return Results.Ok(new { id });
        })
        .WithName("UpsertDataSource")
        .WithDescription("Creates or updates a data source configuration entry.")
        .Produces(200)
        .Produces(400);

        // Delete data source
        group.MapDelete(UiApiRoutes.ConfigDataSources + "/{id}", async (ConfigStore store, string id) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var sources = (dataSources.Sources ?? Array.Empty<DataSourceConfig>()).ToList();

            sources.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

            var next = cfg with { DataSources = dataSources with { Sources = sources.ToArray() } };
            await store.SaveAsync(next);

            return Results.Ok();
        })
        .WithName("DeleteDataSource")
        .WithDescription("Removes a data source configuration by ID.")
        .Produces(200);

        // Toggle data source enabled status
        group.MapPost(UiApiRoutes.ConfigDataSources + "/{id}/toggle", async (ConfigStore store, string id, ToggleRequest req) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var sources = (dataSources.Sources ?? Array.Empty<DataSourceConfig>()).ToList();

            var source = sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (source == null)
                return Results.NotFound();

            var idx = sources.IndexOf(source);
            sources[idx] = source with { Enabled = req.Enabled };

            var next = cfg with { DataSources = dataSources with { Sources = sources.ToArray() } };
            await store.SaveAsync(next);

            return Results.Ok();
        })
        .WithName("ToggleDataSource")
        .WithDescription("Toggles the enabled/disabled state of a data source.")
        .Produces(200)
        .Produces(404);

        // Set default data sources
        group.MapPost(UiApiRoutes.ConfigDataSourcesDefaults, async (ConfigStore store, DefaultSourcesRequest req) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();

            var next = cfg with
            {
                DataSources = dataSources with
                {
                    DefaultRealTimeSourceId = req.DefaultRealTimeSourceId,
                    DefaultHistoricalSourceId = req.DefaultHistoricalSourceId
                }
            };
            await store.SaveAsync(next);

            return Results.Ok();
        })
        .WithName("SetDefaultSources")
        .WithDescription("Sets the default real-time and historical data source IDs.")
        .Produces(200);

        // Update failover settings
        group.MapPost(UiApiRoutes.ConfigDataSourcesFailover, async (ConfigStore store, FailoverSettingsRequest req) =>
        {
            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();

            var next = cfg with
            {
                DataSources = dataSources with
                {
                    EnableFailover = req.EnableFailover,
                    FailoverTimeoutSeconds = req.FailoverTimeoutSeconds
                }
            };
            await store.SaveAsync(next);

            return Results.Ok();
        })
        .WithName("UpdateFailoverSettings")
        .WithDescription("Updates automatic failover settings including timeout and enable/disable.")
        .Produces(200);

        // Provider comparison view
        group.MapGet(UiApiRoutes.ProviderComparison, async ([FromServices] ConfigStore store, [FromServices] ProviderRouteExplainabilityService explainabilityService, CancellationToken ct) =>
        {
            var metricsStatus = store.TryLoadProviderMetrics();
            var cfg = store.Load();
            var selection = await explainabilityService.PreviewAsync(
                new RoutePreviewRequest(
                    Capability: "RealtimeMarketData",
                    Symbol: cfg.Symbols?.FirstOrDefault()?.Symbol),
                ct).ConfigureAwait(false);

            if (metricsStatus is not null)
            {
                var providers = metricsStatus.Providers.Select(p => new ProviderMetricsResponse(
                    ProviderId: p.ProviderId,
                    ProviderType: p.ProviderType,
                    TradesReceived: p.TradesReceived,
                    DepthUpdatesReceived: p.DepthUpdatesReceived,
                    QuotesReceived: p.QuotesReceived,
                    ConnectionAttempts: p.ConnectionAttempts,
                    ConnectionFailures: p.ConnectionFailures,
                    MessagesDropped: p.MessagesDropped,
                    ActiveSubscriptions: p.ActiveSubscriptions,
                    AverageLatencyMs: p.AverageLatencyMs,
                    MinLatencyMs: p.MinLatencyMs,
                    MaxLatencyMs: p.MaxLatencyMs,
                    DataQualityScore: p.DataQualityScore,
                    ConnectionSuccessRate: p.ConnectionSuccessRate,
                    Timestamp: p.Timestamp
                )).ToArray();

                var comparison = new ProviderComparisonResponse(
                    Timestamp: metricsStatus.Timestamp,
                    Providers: providers,
                    TotalProviders: metricsStatus.TotalProviders,
                    HealthyProviders: metricsStatus.HealthyProviders
                );
                return Results.Json(new
                {
                    comparison.Timestamp,
                    comparison.Providers,
                    comparison.TotalProviders,
                    comparison.HealthyProviders,
                    selection,
                    rankedAlternatives = selection.RankedAlternatives ?? Array.Empty<RoutePreviewCandidateDto>()
                }, jsonOptions);
            }

            // Fallback to configuration-based data
            var sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
            var fallbackProviders = sources.Select(s => CreateFallbackMetrics(s)).ToArray();

            var fallbackComparison = new ProviderComparisonResponse(
                Timestamp: DateTimeOffset.UtcNow,
                Providers: fallbackProviders,
                TotalProviders: sources.Length,
                HealthyProviders: sources.Count(s => s.Enabled)
            );
            return Results.Json(new
            {
                fallbackComparison.Timestamp,
                fallbackComparison.Providers,
                fallbackComparison.TotalProviders,
                fallbackComparison.HealthyProviders,
                selection,
                rankedAlternatives = selection.RankedAlternatives ?? Array.Empty<RoutePreviewCandidateDto>()
            }, jsonOptions);
        })
        .WithName("GetProviderComparison")
        .WithDescription("Returns a side-by-side comparison of all provider metrics including latency, quality, and throughput.")
        .Produces<ProviderComparisonResponse>(200);

        // Provider status
        group.MapGet(UiApiRoutes.ProviderStatus, (ConfigStore store) =>
        {
            var cfg = store.Load();
            var sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
            var metricsStatus = store.TryLoadProviderMetrics();

            var status = sources.Select(s =>
            {
                var realMetrics = metricsStatus?.Providers.FirstOrDefault(p =>
                    string.Equals(p.ProviderId, s.Id, StringComparison.OrdinalIgnoreCase));

                return new ProviderStatusResponse(
                    ProviderId: s.Id,
                    Name: s.Name,
                    ProviderType: s.Provider.ToString(),
                    IsConnected: realMetrics?.IsConnected ?? s.Enabled,
                    IsEnabled: s.Enabled,
                    Priority: s.Priority,
                    ActiveSubscriptions: (int)(realMetrics?.ActiveSubscriptions ?? 0),
                    LastHeartbeat: realMetrics?.Timestamp ?? DateTimeOffset.UtcNow
                );
            }).ToArray();

            return Results.Json(status, jsonOptions);
        })
        .WithName("GetProviderStatus")
        .WithDescription("Returns connection status for all configured providers.")
        .Produces<ProviderStatusResponse[]>(200);

        // Provider metrics
        group.MapGet(UiApiRoutes.ProviderMetrics, (ConfigStore store) =>
        {
            var metricsStatus = store.TryLoadProviderMetrics();

            if (metricsStatus is not null)
            {
                var metrics = metricsStatus.Providers.Select(p => new ProviderMetricsResponse(
                    ProviderId: p.ProviderId,
                    ProviderType: p.ProviderType,
                    TradesReceived: p.TradesReceived,
                    DepthUpdatesReceived: p.DepthUpdatesReceived,
                    QuotesReceived: p.QuotesReceived,
                    ConnectionAttempts: p.ConnectionAttempts,
                    ConnectionFailures: p.ConnectionFailures,
                    MessagesDropped: p.MessagesDropped,
                    ActiveSubscriptions: p.ActiveSubscriptions,
                    AverageLatencyMs: p.AverageLatencyMs,
                    MinLatencyMs: p.MinLatencyMs,
                    MaxLatencyMs: p.MaxLatencyMs,
                    DataQualityScore: p.DataQualityScore,
                    ConnectionSuccessRate: p.ConnectionSuccessRate,
                    Timestamp: p.Timestamp
                )).ToArray();
                return Results.Json(metrics, jsonOptions);
            }

            // Fallback to configuration-based placeholder data
            var cfg = store.Load();
            var sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
            var fallbackMetrics = sources.Select(s => CreateFallbackMetrics(s)).ToArray();

            return Results.Json(fallbackMetrics, jsonOptions);
        })
        .WithName("GetProviderMetrics")
        .WithDescription("Returns detailed metrics for all providers including throughput, latency, and quality scores.")
        .Produces<ProviderMetricsResponse[]>(200);

        // Single provider metrics
        group.MapGet(UiApiRoutes.ProviderMetrics + "/{providerId}", (ConfigStore store, string providerId) =>
        {
            var metricsStatus = store.TryLoadProviderMetrics();
            var providerMetrics = metricsStatus?.Providers.FirstOrDefault(p =>
                string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

            if (providerMetrics is not null)
            {
                var metrics = new ProviderMetricsResponse(
                    ProviderId: providerMetrics.ProviderId,
                    ProviderType: providerMetrics.ProviderType,
                    TradesReceived: providerMetrics.TradesReceived,
                    DepthUpdatesReceived: providerMetrics.DepthUpdatesReceived,
                    QuotesReceived: providerMetrics.QuotesReceived,
                    ConnectionAttempts: providerMetrics.ConnectionAttempts,
                    ConnectionFailures: providerMetrics.ConnectionFailures,
                    MessagesDropped: providerMetrics.MessagesDropped,
                    ActiveSubscriptions: providerMetrics.ActiveSubscriptions,
                    AverageLatencyMs: providerMetrics.AverageLatencyMs,
                    MinLatencyMs: providerMetrics.MinLatencyMs,
                    MaxLatencyMs: providerMetrics.MaxLatencyMs,
                    DataQualityScore: providerMetrics.DataQualityScore,
                    ConnectionSuccessRate: providerMetrics.ConnectionSuccessRate,
                    Timestamp: providerMetrics.Timestamp
                );
                return Results.Json(metrics, jsonOptions);
            }

            // Fallback
            var cfg = store.Load();
            var source = cfg.DataSources?.Sources?.FirstOrDefault(s =>
                string.Equals(s.Id, providerId, StringComparison.OrdinalIgnoreCase));

            if (source == null)
                return Results.NotFound();

            return Results.Json(CreateFallbackMetrics(source), jsonOptions);
        })
        .WithName("GetProviderMetricsById")
        .WithDescription("Returns detailed metrics for a single provider by ID.")
        .Produces<ProviderMetricsResponse>(200)
        .Produces(404);

        // Provider catalog endpoint - centralized metadata for UI consumption
        // Uses ProviderRegistry when available for runtime-derived catalog data,
        // otherwise falls back to static ProviderCatalog
        group.MapGet(UiApiRoutes.ProviderCatalog, (HttpContext ctx, string? type, [FromServices] ProviderRegistry? registry) =>
        {
            IReadOnlyList<ProviderCatalogEntry> catalogEntries;

            if (registry != null)
            {
                // Use runtime-derived catalog from ProviderRegistry via ProviderTemplateFactory
                catalogEntries = type?.ToLowerInvariant() switch
                {
                    "streaming" => registry.GetProviderCatalogByType(ProviderType.Streaming),
                    "backfill" => registry.GetProviderCatalogByType(ProviderType.Backfill),
                    _ => registry.GetProviderCatalog()
                };
            }
            else
            {
                // Fall back to static catalog
                catalogEntries = type?.ToLowerInvariant() switch
                {
                    "streaming" => ProviderCatalog.GetStreamingProviders(),
                    "backfill" => ProviderCatalog.GetBackfillProviders(),
                    _ => ProviderCatalog.GetAll()
                };
            }

            return Results.Json(new
            {
                providers = catalogEntries,
                totalCount = catalogEntries.Count,
                timestamp = DateTimeOffset.UtcNow,
                source = registry != null ? "registry" : "static"
            }, jsonOptions);
        })
        .WithName("GetProviderCatalog")
        .WithDescription("Returns the provider catalog with metadata. Filter by type using ?type=streaming or ?type=backfill.")
        .Produces(200);

        // Single provider catalog entry
        // Uses ProviderRegistry when available for runtime-derived catalog data
        group.MapGet(UiApiRoutes.ProviderCatalogById, (string providerId, [FromServices] ProviderRegistry? registry) =>
        {
            ProviderCatalogEntry? entry = registry != null
                ? registry.GetProviderCatalogEntry(providerId)
                : ProviderCatalog.Get(providerId);

            if (entry is null)
                return Results.NotFound(new { error = $"Provider '{providerId}' not found in catalog" });

            return Results.Json(entry, jsonOptions);
        })
        .WithName("GetProviderCatalogById")
        .WithDescription("Returns catalog metadata for a single provider by ID.")
        .Produces<ProviderCatalogEntry>(200)
        .Produces(404);

        // Alias: /api/config/data-sources → /api/config/datasources (for backward compatibility with tests)
        group.MapGet("/api/config/data-sources", (ConfigStore store) =>
        {
            var cfg = store.Load();
            return Results.Json(new
            {
                sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>(),
                defaultRealTimeSourceId = cfg.DataSources?.DefaultRealTimeSourceId,
                defaultHistoricalSourceId = cfg.DataSources?.DefaultHistoricalSourceId,
                enableFailover = cfg.DataSources?.EnableFailover ?? true,
                failoverTimeoutSeconds = cfg.DataSources?.FailoverTimeoutSeconds ?? 30
            }, jsonOptions);
        })
        .WithName("GetDataSourcesAlias")
        .WithDescription("Alias for /api/config/datasources for backward compatibility.")
        .Produces(200);

        group.MapPost("/api/config/data-sources", async (ConfigStore store, DataSourceConfigRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest("Name is required.");

            var cfg = store.Load();
            var dataSources = cfg.DataSources ?? new DataSourcesConfig();
            var sources = (dataSources.Sources ?? Array.Empty<DataSourceConfig>()).ToList();

            var id = string.IsNullOrWhiteSpace(req.Id) ? Guid.NewGuid().ToString("N") : req.Id;
            var source = new DataSourceConfig(
                Id: id,
                Name: req.Name,
                Provider: Enum.TryParse<DataSourceKind>(req.Provider, ignoreCase: true, out var p) ? p : DataSourceKind.IB,
                Enabled: req.Enabled,
                Type: Enum.TryParse<DataSourceType>(req.Type, ignoreCase: true, out var t) ? t : DataSourceType.RealTime,
                Priority: req.Priority,
                Alpaca: req.Alpaca?.ToDomain(),
                Polygon: req.Polygon?.ToDomain(),
                IB: req.IB?.ToDomain(),
                Symbols: req.Symbols,
                Description: req.Description,
                Tags: req.Tags
            );

            var idx = sources.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                sources[idx] = source;
            else
                sources.Add(source);

            var next = cfg with { DataSources = dataSources with { Sources = sources.ToArray() } };
            await store.SaveAsync(next);

            return Results.Ok(new { id });
        })
        .WithName("UpsertDataSourceAlias")
        .WithDescription("Alias for /api/config/datasources POST for backward compatibility.")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ProviderMetricsResponse CreateFallbackMetrics(DataSourceConfig source) => new(
        ProviderId: source.Id,
        ProviderType: source.Provider.ToString(),
        TradesReceived: 0,
        DepthUpdatesReceived: 0,
        QuotesReceived: 0,
        ConnectionAttempts: 0,
        ConnectionFailures: 0,
        MessagesDropped: 0,
        ActiveSubscriptions: 0,
        AverageLatencyMs: 0,
        MinLatencyMs: 0,
        MaxLatencyMs: 0,
        DataQualityScore: 0,
        ConnectionSuccessRate: 0,
        Timestamp: DateTimeOffset.UtcNow,
        IsSimulated: true
    );
}
