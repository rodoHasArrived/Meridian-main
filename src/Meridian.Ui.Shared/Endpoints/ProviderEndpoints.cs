using System.Text.Json;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
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
    private static DataSourceConfig RedactSecrets(DataSourceConfig source)
    {
        return source with
        {
            Alpaca = source.Alpaca is null
                ? null
                : source.Alpaca with
                {
                    KeyId = string.Empty,
                    SecretKey = string.Empty
                },
            Polygon = source.Polygon is null
                ? null
                : source.Polygon with
                {
                    ApiKey = null
                }
        };
    }

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
                sources = (cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>())
                    .Select(RedactSecrets)
                    .ToArray(),
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
        group.MapPost(UiApiRoutes.ConfigDataSources, async (
            HttpContext context,
            ConfigStore store,
            DataSourceConfigRequest req) =>
        {
            var authorizationFailure = RequireDataSourceMutationPermission(context, req);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(400)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // Delete data source
        group.MapDelete(UiApiRoutes.ConfigDataSources + "/{id}", async (
            HttpContext context,
            ConfigStore store,
            string id) =>
        {
            var authorizationFailure = RequireProviderMutationPermission(context);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(200)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // Toggle data source enabled status
        group.MapPost(UiApiRoutes.ConfigDataSources + "/{id}/toggle", async (
            HttpContext context,
            ConfigStore store,
            string id,
            ToggleRequest req) =>
        {
            var authorizationFailure = RequireProviderMutationPermission(context);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(404)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // Set default data sources
        group.MapPost(UiApiRoutes.ConfigDataSourcesDefaults, async (
            HttpContext context,
            ConfigStore store,
            DefaultSourcesRequest req) =>
        {
            var authorizationFailure = RequireProviderMutationPermission(context);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(200)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        // Update failover settings
        group.MapPost(UiApiRoutes.ConfigDataSourcesFailover, async (
            HttpContext context,
            ConfigStore store,
            FailoverSettingsRequest req) =>
        {
            var authorizationFailure = RequireProviderMutationPermission(context);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(200)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapPost(UiApiRoutes.ProviderConfigure, async (
            HttpContext context,
            [FromBody] ProviderSetupRequest req,
            [FromServices] ProviderSetupService setupService) =>
        {
            if (!HasProviderSetupPermission(context, req))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = await setupService.ConfigureAsync(req, context.RequestAborted).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result, jsonOptions)
                : Results.BadRequest(result);
        })
        .WithName("ConfigureProvider")
        .WithDescription("Creates a provider data-source configuration from the browser provider setup form.")
        .Produces<ProviderSetupResult>(200)
        .Produces<ProviderSetupResult>(400)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

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

        group.MapGet(UiApiRoutes.ProviderReadiness, async (
            [FromServices] ProviderReadinessService service,
            CancellationToken ct) =>
        {
            var readiness = await service.GetReadinessAsync(ct).ConfigureAwait(false);
            return Results.Json(readiness, jsonOptions);
        })
        .WithName("GetProviderReadiness")
        .WithDescription("Returns the shared provider readiness command-center model across credentials, health, degradation, and evidence.")
        .Produces<ProviderReadinessSummaryDto>(200);

        // Provider status
        group.MapGet(UiApiRoutes.ProviderStatus, (ConfigStore store, [FromServices] ProviderRegistry? registry) =>
        {
            var cfg = store.Load();
            var sources = cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
            var metricsStatus = store.TryLoadProviderMetrics();
            var diagnosticsByProviderId = ProviderConnectionDiagnosticsProjection.BuildByProviderId(registry);

            var status = sources.Select(s =>
            {
                var realMetrics = metricsStatus?.Providers.FirstOrDefault(p =>
                    string.Equals(p.ProviderId, s.Id, StringComparison.OrdinalIgnoreCase));
                var diagnostics = ProviderConnectionDiagnosticsProjection.Find(
                    diagnosticsByProviderId,
                    s.Id,
                    s.Name,
                    s.Provider.ToString());

                return new ProviderStatusResponse(
                    ProviderId: s.Id,
                    Name: s.Name,
                    ProviderType: s.Provider.ToString(),
                    IsConnected: diagnostics?.IsConnected ?? realMetrics?.IsConnected ?? s.Enabled,
                    IsEnabled: s.Enabled,
                    Priority: s.Priority,
                    ActiveSubscriptions: diagnostics?.ActiveSubscriptions ?? (int)(realMetrics?.ActiveSubscriptions ?? 0),
                    LastHeartbeat: diagnostics?.LastHeartbeatReceivedAt ?? realMetrics?.Timestamp ?? DateTimeOffset.UtcNow,
                    LifecycleState: diagnostics?.LifecycleState,
                    WebSocketState: diagnostics?.WebSocketState,
                    IsReconnecting: diagnostics?.IsReconnecting,
                    LastHeartbeatReceivedAt: diagnostics?.LastHeartbeatReceivedAt,
                    LastMessageReceivedAt: diagnostics?.LastMessageReceivedAt,
                    LastReconnectAttemptAt: diagnostics?.LastReconnectAttemptAt,
                    ReconnectAttempts: diagnostics?.ReconnectAttempts,
                    LastFailureKind: diagnostics?.LastFailureKind,
                    FailedSubscriptions: diagnostics?.FailedSubscriptions,
                    RecoveringSubscriptions: diagnostics?.RecoveringSubscriptions,
                    LastSubscriptionMessageAt: diagnostics?.LastSubscriptionMessageAt
                );
            }).ToList();

            var knownProviderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
            {
                knownProviderKeys.Add(source.Id);
                knownProviderKeys.Add(source.Name);
                knownProviderKeys.Add(source.Provider.ToString());
            }

            if (registry is not null)
            {
                foreach (var provider in registry.GetAllProviders())
                {
                    if (knownProviderKeys.Contains(provider.Name) || knownProviderKeys.Contains(provider.DisplayName))
                    {
                        continue;
                    }

                    var diagnostics = ProviderConnectionDiagnosticsProjection.Find(
                        diagnosticsByProviderId,
                        provider.Name,
                        provider.DisplayName);

                    status.Add(new ProviderStatusResponse(
                        ProviderId: provider.Name,
                        Name: provider.DisplayName,
                        ProviderType: provider.ProviderType.ToString(),
                        IsConnected: diagnostics?.IsConnected ?? provider.IsEnabled,
                        IsEnabled: provider.IsEnabled,
                        Priority: provider.Priority,
                        ActiveSubscriptions: diagnostics?.ActiveSubscriptions ?? 0,
                        LastHeartbeat: diagnostics?.LastHeartbeatReceivedAt,
                        LifecycleState: diagnostics?.LifecycleState,
                        WebSocketState: diagnostics?.WebSocketState,
                        IsReconnecting: diagnostics?.IsReconnecting,
                        LastHeartbeatReceivedAt: diagnostics?.LastHeartbeatReceivedAt,
                        LastMessageReceivedAt: diagnostics?.LastMessageReceivedAt,
                        LastReconnectAttemptAt: diagnostics?.LastReconnectAttemptAt,
                        ReconnectAttempts: diagnostics?.ReconnectAttempts,
                        LastFailureKind: diagnostics?.LastFailureKind,
                        FailedSubscriptions: diagnostics?.FailedSubscriptions,
                        RecoveringSubscriptions: diagnostics?.RecoveringSubscriptions,
                        LastSubscriptionMessageAt: diagnostics?.LastSubscriptionMessageAt));
                }
            }

            return Results.Json(status.ToArray(), jsonOptions);
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
                sources = (cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>())
                    .Select(RedactSecrets)
                    .ToArray(),
                defaultRealTimeSourceId = cfg.DataSources?.DefaultRealTimeSourceId,
                defaultHistoricalSourceId = cfg.DataSources?.DefaultHistoricalSourceId,
                enableFailover = cfg.DataSources?.EnableFailover ?? true,
                failoverTimeoutSeconds = cfg.DataSources?.FailoverTimeoutSeconds ?? 30
            }, jsonOptions);
        })
        .WithName("GetDataSourcesAlias")
        .WithDescription("Alias for /api/config/datasources for backward compatibility.")
        .Produces(200);

        group.MapPost("/api/config/data-sources", async (
            HttpContext context,
            ConfigStore store,
            DataSourceConfigRequest req) =>
        {
            var authorizationFailure = RequireDataSourceMutationPermission(context, req);
            if (authorizationFailure is not null)
            {
                return authorizationFailure;
            }

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
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
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

    private static bool HasProviderSetupPermission(HttpContext context, ProviderSetupRequest request)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out var permissions))
        {
            return false;
        }

        var required = UserPermission.ManageProviders;
        if (HasSubmittedCredentials(request))
        {
            required |= UserPermission.ManageCredentials;
        }

        return (permissions & required) == required;
    }

    private static IResult? RequireProviderMutationPermission(HttpContext context)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out _))
        {
            return Results.Unauthorized();
        }

        return EndpointAuthorization.HasPermission(context, UserPermission.ManageProviders)
            ? null
            : EndpointHelpers.Forbidden();
    }

    private static IResult? RequireDataSourceMutationPermission(
        HttpContext context,
        DataSourceConfigRequest request)
    {
        var authorizationFailure = RequireProviderMutationPermission(context);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        if (HasSubmittedCredentials(request) &&
            !EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials))
        {
            return EndpointHelpers.Forbidden();
        }

        return null;
    }

    private static bool HasSubmittedCredentials(ProviderSetupRequest request)
        => !string.IsNullOrWhiteSpace(request.ApiKey) || !string.IsNullOrWhiteSpace(request.ApiSecret);

    private static bool HasSubmittedCredentials(DataSourceConfigRequest request)
        => !string.IsNullOrWhiteSpace(request.Alpaca?.KeyId)
            || !string.IsNullOrWhiteSpace(request.Alpaca?.SecretKey)
            || !string.IsNullOrWhiteSpace(request.Polygon?.ApiKey);
}
