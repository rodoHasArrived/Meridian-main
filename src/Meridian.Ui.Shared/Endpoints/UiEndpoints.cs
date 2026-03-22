using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Application.Composition;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Master extension methods for registering all UI API endpoints.
/// Provides a single entry point for mapping all shared endpoints.
/// Uses ServiceCompositionRoot for centralized service registration.
/// </summary>
public static class UiEndpoints
{
    #region Consolidated Host Setup

    /// <summary>
    /// Configures the application with all UI services and endpoints.
    /// This is the single entry point for setting up the UI host and should be used
    /// instead of calling AddUiSharedServices and MapAllUiEndpoints separately.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    /// <returns>A configured WebApplication ready to run.</returns>
    public static WebApplication BuildUiHost(this WebApplicationBuilder builder, string? configPath = null)
    {
        builder.Services.AddUiSharedServices(configPath);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
            {
                Title = "Meridian API",
                Version = "v1",
                Description = "REST API for the Meridian system"
            });
        });

        var app = builder.Build();

        // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
        ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(app.Services);

        app.UseStaticFiles();
        app.UseApiKeyAuthentication();
        app.UseLoginSessionAuthentication();
        app.UseRateLimiter();

        // Enable Swagger UI in development mode
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Meridian API v1"));
        }

        app.MapAllUiEndpoints();
        return app;
    }

    /// <summary>
    /// Configures the application with UI services, endpoints, and shared status handlers.
    /// This overload allows sharing StatusEndpointHandlers with StatusHttpServer.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="statusHandlers">Pre-configured status endpoint handlers to share.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    /// <returns>A configured WebApplication ready to run.</returns>
    public static WebApplication BuildUiHost(this WebApplicationBuilder builder, StatusEndpointHandlers statusHandlers, string? configPath = null)
    {
        builder.Services.AddUiSharedServices(statusHandlers, configPath);
        var app = builder.Build();

        // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
        ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(app.Services);

        app.UseStaticFiles();
        app.UseApiKeyAuthentication();
        app.UseLoginSessionAuthentication();
        app.UseRateLimiter();
        app.MapUiEndpointsWithStatus(statusHandlers);
        return app;
    }

    #endregion

    #region Service Registration

    /// <summary>
    /// Registers all shared services required by UI endpoints using the centralized composition root.
    /// Replaces the core BackfillCoordinator with the UI-extended version that includes preview functionality.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    public static IServiceCollection AddUiSharedServices(this IServiceCollection services, string? configPath = null)
    {
        // Use the centralized composition root (registers core services)
        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        services.AddMarketDataServices(options);

        // Register session-based authentication service
        services.AddSingleton<LoginSessionService>();

        // Replace core BackfillCoordinator with UI-extended version that includes PreviewAsync
        // The Ui.Shared.Services.BackfillCoordinator wraps the core and adds preview functionality
        services.AddSingleton<Meridian.Ui.Shared.Services.BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<Meridian.Ui.Shared.Services.ConfigStore>();
            return new Meridian.Ui.Shared.Services.BackfillCoordinator(configStore);
        });

        RegisterStrategyWorkstationServices(services);

        services.AddMutationRateLimiter();

        // Register LeanAutoExportService as a background hosted service
        services.AddSingleton<LeanAutoExportService>();
        services.AddHostedService(sp => sp.GetRequiredService<LeanAutoExportService>());

        return services;
    }

    /// <summary>
    /// Registers shared services with a pre-configured StatusEndpointHandlers instance.
    /// Use this when you want to share the same handlers with StatusHttpServer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="statusHandlers">Status endpoint handlers to register.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    public static IServiceCollection AddUiSharedServices(this IServiceCollection services, StatusEndpointHandlers statusHandlers, string? configPath = null)
    {
        // Use the centralized composition root (registers core services)
        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        services.AddMarketDataServices(options);

        // Register session-based authentication service
        services.AddSingleton<LoginSessionService>();

        // Replace core BackfillCoordinator with UI-extended version that includes PreviewAsync
        services.AddSingleton<Meridian.Ui.Shared.Services.BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<Meridian.Ui.Shared.Services.ConfigStore>();
            return new Meridian.Ui.Shared.Services.BackfillCoordinator(configStore);
        });

        RegisterStrategyWorkstationServices(services);

        services.AddSingleton(statusHandlers);
        services.AddMutationRateLimiter();

        // Register LeanAutoExportService as a background hosted service
        services.AddSingleton<LeanAutoExportService>();
        services.AddHostedService(sp => sp.GetRequiredService<LeanAutoExportService>());

        return services;
    }

    /// <summary>
    /// Registers the strategy read-model services used by workstation bootstrap endpoints.
    /// These are registered with TryAdd so richer implementations can override them later.
    /// </summary>
    private static void RegisterStrategyWorkstationServices(IServiceCollection services)
    {
        services.TryAddSingleton<IStrategyRepository, StrategyRunStore>();
        services.TryAddSingleton<PortfolioReadService>();
        services.TryAddSingleton<LedgerReadService>();
        services.TryAddSingleton<StrategyRunReadService>();
    }

    #endregion

    #region Endpoint Mapping

    /// <summary>
    /// Maps all UI API endpoints using default JSON serializer options.
    /// </summary>
    public static WebApplication MapUiEndpoints(this WebApplication app)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonOptionsIndented = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        return app.MapUiEndpoints(jsonOptions, jsonOptionsIndented);
    }

    /// <summary>
    /// Maps all UI API endpoints with custom JSON serializer options.
    /// </summary>
    public static WebApplication MapUiEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions, JsonSerializerOptions? jsonOptionsIndented = null)
    {
        jsonOptionsIndented ??= new JsonSerializerOptions
        {
            PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy,
            WriteIndented = true
        };

        // Map all endpoint groups
        app.MapConfigEndpoints(jsonOptions);
        app.MapBackfillEndpoints(jsonOptions, jsonOptionsIndented);
        app.MapProviderEndpoints(jsonOptions);
        app.MapFailoverEndpoints(jsonOptions);
        app.MapIBEndpoints(jsonOptions);
        app.MapSymbolMappingEndpoints(jsonOptions);
        app.MapLiveDataEndpoints(jsonOptions);
        app.MapSymbolEndpoints(jsonOptions);
        app.MapStorageEndpoints(jsonOptions);
        app.MapStorageQualityEndpoints(jsonOptions);
        app.MapCatalogEndpoints(jsonOptions);

        // Phase 3B endpoint groups
        app.MapHealthEndpoints(jsonOptions);
        app.MapDiagnosticsEndpoints(jsonOptions);
        app.MapBackfillScheduleEndpoints(jsonOptions);
        app.MapAdminEndpoints(jsonOptions);
        app.MapMaintenanceScheduleEndpoints(jsonOptions);
        app.MapAnalyticsEndpoints(jsonOptions);
        app.MapReplayEndpoints(jsonOptions);
        app.MapExportEndpoints(jsonOptions);
        app.MapSubscriptionEndpoints(jsonOptions);
        app.MapSamplingEndpoints(jsonOptions);
        app.MapAlignmentEndpoints(jsonOptions);
        app.MapCronEndpoints(jsonOptions);
        app.MapLeanEndpoints(jsonOptions);
        app.MapMessagingEndpoints(jsonOptions);
        app.MapProviderExtendedEndpoints(jsonOptions);
        app.MapCppTraderEndpoints();
        app.MapIndexEndpoints(jsonOptions);

        // Canonicalization parity dashboard (Phase 2) endpoints are mapped elsewhere to avoid duplicate registrations.

        // Trading calendar endpoints
        app.MapCalendarEndpoints(jsonOptions);

        // Historical data query endpoints (Phase 9A.1)
        app.MapHistoricalEndpoints(jsonOptions);

        // Checkpoint and ingestion job endpoints (P0)
        app.MapCheckpointEndpoints(jsonOptions);

        // Options / Derivatives endpoints
        app.MapOptionsEndpoints(jsonOptions);

        // Map quality drops endpoints (C3/#16)
        var auditTrail = app.Services.GetService<DroppedEventAuditTrail>();
        app.MapQualityDropsEndpoints(auditTrail, jsonOptions);

        // Map data quality monitoring endpoints (C3 - quality metrics exposure)
        var qualityService = app.Services.GetService<DataQualityMonitoringService>();
        if (qualityService is not null)
        {
            app.MapDataQualityEndpoints(qualityService);
        }

        // Map SLA monitoring endpoints
        var slaMonitor = app.Services.GetService<DataFreshnessSlaMonitor>();
        if (slaMonitor is not null)
        {
            app.MapSlaEndpoints(slaMonitor);
        }

        // Resilience: circuit breaker dashboard, cost estimation, compliance report
        app.MapResilienceEndpoints(jsonOptions);

        // Authentication endpoints (login page, login API, logout API)
        app.MapAuthEndpoints();

        // React workstation shell and bootstrap data
        app.MapWorkstationEndpoints(jsonOptions);

        return app;
    }

    /// <summary>
    /// Maps all UI API endpoints including status endpoints.
    /// Use this when StatusEndpointHandlers has been registered in DI.
    /// </summary>
    public static WebApplication MapUiEndpointsWithStatus(this WebApplication app, StatusEndpointHandlers statusHandlers)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonOptionsIndented = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // Map status endpoints using shared handlers
        app.MapStatusEndpoints(statusHandlers, jsonOptions);

        // Map all other endpoint groups
        app.MapConfigEndpoints(jsonOptions);
        app.MapBackfillEndpoints(jsonOptions, jsonOptionsIndented);
        app.MapProviderEndpoints(jsonOptions);
        app.MapFailoverEndpoints(jsonOptions);
        app.MapIBEndpoints(jsonOptions);
        app.MapSymbolMappingEndpoints(jsonOptions);
        app.MapLiveDataEndpoints(jsonOptions);
        app.MapSymbolEndpoints(jsonOptions);
        app.MapStorageEndpoints(jsonOptions);
        app.MapStorageQualityEndpoints(jsonOptions);
        app.MapCatalogEndpoints(jsonOptions);

        // Phase 3B endpoint groups
        app.MapHealthEndpoints(jsonOptions);
        app.MapDiagnosticsEndpoints(jsonOptions);
        app.MapBackfillScheduleEndpoints(jsonOptions);
        app.MapAdminEndpoints(jsonOptions);
        app.MapMaintenanceScheduleEndpoints(jsonOptions);
        app.MapAnalyticsEndpoints(jsonOptions);
        app.MapReplayEndpoints(jsonOptions);
        app.MapExportEndpoints(jsonOptions);
        app.MapSubscriptionEndpoints(jsonOptions);
        app.MapSamplingEndpoints(jsonOptions);
        app.MapAlignmentEndpoints(jsonOptions);
        app.MapCronEndpoints(jsonOptions);
        app.MapLeanEndpoints(jsonOptions);
        app.MapMessagingEndpoints(jsonOptions);
        app.MapProviderExtendedEndpoints(jsonOptions);
        app.MapIndexEndpoints(jsonOptions);

        // Canonicalization parity dashboard (Phase 2)
        app.MapCanonicalizationEndpoints(jsonOptions);

        // Trading calendar endpoints
        app.MapCalendarEndpoints(jsonOptions);

        // Historical data query endpoints (Phase 9A.1)
        app.MapHistoricalEndpoints(jsonOptions);

        // Checkpoint and ingestion job endpoints (P0)
        app.MapCheckpointEndpoints(jsonOptions);

        // Options / Derivatives endpoints
        app.MapOptionsEndpoints(jsonOptions);

        // Map quality drops endpoints (C3/#16 - DroppedEventAuditTrail exposure)
        var auditTrail = app.Services.GetService<DroppedEventAuditTrail>();
        app.MapQualityDropsEndpoints(auditTrail, jsonOptions);

        // Map data quality monitoring endpoints (C3 - quality metrics exposure)
        var qualityService = app.Services.GetService<DataQualityMonitoringService>();
        if (qualityService is not null)
        {
            app.MapDataQualityEndpoints(qualityService);
        }

        // Map SLA monitoring endpoints
        var slaMonitor = app.Services.GetService<DataFreshnessSlaMonitor>();
        if (slaMonitor is not null)
        {
            app.MapSlaEndpoints(slaMonitor);
        }

        // Resilience: circuit breaker dashboard, cost estimation, compliance report
        app.MapResilienceEndpoints(jsonOptions);

        // Authentication endpoints (login page, login API, logout API)
        app.MapAuthEndpoints();

        // React workstation shell and bootstrap data
        app.MapWorkstationEndpoints(jsonOptions);

        return app;
    }

    /// <summary>
    /// Maps the dashboard HTML endpoint at the root path.
    /// </summary>
    public static WebApplication MapDashboard(this WebApplication app)
    {
        app.MapGet("/", (Meridian.Ui.Shared.Services.ConfigStore store) =>
        {
            var html = HtmlTemplateGenerator.Index(store.ConfigPath, store.GetStatusPath(), store.GetBackfillStatusPath());
            return Results.Content(html, "text/html");
        });

        return app;
    }

    /// <summary>
    /// Maps all UI endpoints including the dashboard.
    /// Convenience method that combines MapUiEndpoints and MapDashboard.
    /// </summary>
    public static WebApplication MapAllUiEndpoints(this WebApplication app)
    {
        app.MapDashboard();
        app.MapUiEndpoints();
        return app;
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Rate limiting policy name applied to mutation (POST/PUT/DELETE) endpoints.
    /// </summary>
    public const string MutationRateLimitPolicy = "mutation";

    /// <summary>
    /// Registers a per-IP fixed-window rate limiter for mutation endpoints.
    /// Allows 10 requests per minute per IP with a small queue for bursts.
    /// </summary>
    private static IServiceCollection AddMutationRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(MutationRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    }));
        });

        return services;
    }

    #endregion
}
