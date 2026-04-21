using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Application.Composition;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Master extension methods for registering shared desktop/local API endpoints.
/// Uses ServiceCompositionRoot for centralized service registration.
/// </summary>
public static class UiEndpoints
{
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

        // Register user profile registry (multi-user RBAC) and session-based auth service
        services.AddSingleton<UserProfileRegistry>();
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

        // Register user profile registry (multi-user RBAC) and session-based auth service
        services.AddSingleton<UserProfileRegistry>();
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
        services.TryAddSingleton(PromotionRecordStoreOptions.Default);
        services.TryAddSingleton<IPromotionRecordStore>(sp =>
            new JsonlPromotionRecordStore(
                sp.GetRequiredService<PromotionRecordStoreOptions>(),
                sp.GetRequiredService<ILogger<JsonlPromotionRecordStore>>()));
        services.TryAddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        services.TryAddSingleton<PortfolioReadService>();
        services.TryAddSingleton<LedgerReadService>();
        services.TryAddSingleton<StrategyRunReadService>();
        services.TryAddSingleton<CashFlowProjectionService>();
        services.TryAddSingleton<StrategyRunContinuityService>();
        services.TryAddSingleton<BacktestToLivePromoter>();
        services.TryAddSingleton<PromotionService>();
        services.TryAddSingleton<ISecurityMasterWorkbenchQueryService, SecurityMasterWorkbenchQueryService>();
        services.TryAddSingleton<NavAttributionService>();
        services.TryAddSingleton<ReportGenerationService>();
        services.TryAddSingleton<FundOperationsWorkspaceReadService>();

        // Reconciliation services — required by /api/workstation/reconciliation/* endpoints.
        // InMemoryReconciliationRunRepository is the default; a persistent implementation can
        // override it by registering before AddUiSharedServices is called (TryAdd semantics).
        services.TryAddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        services.TryAddSingleton<ReconciliationProjectionService>();
        services.TryAddSingleton<IReconciliationRunService, ReconciliationRunService>();
    }



    /// <summary>
    /// Maps all UI API endpoints using default JSON serializer options.
    /// </summary>
    public static WebApplication MapUiEndpoints(this WebApplication app)
    {
        var jsonOptions = CreateEndpointJsonOptions();
        var jsonOptionsIndented = CreateEndpointJsonOptions(writeIndented: true);

        return app.MapUiEndpoints(jsonOptions, jsonOptionsIndented);
    }

    /// <summary>
    /// Creates the standard <see cref="JsonSerializerOptions"/> used by UI API endpoints.
    /// Uses camelCase naming and a DefaultJsonTypeInfoResolver so callers can extend the
    /// type-info chain without reflection falling back to a null resolver.
    /// </summary>
    /// <param name="writeIndented">When <c>true</c> the output is pretty-printed.</param>
    public static JsonSerializerOptions CreateEndpointJsonOptions(bool writeIndented = false) =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

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

        // Direct lending endpoints
        app.MapDirectLendingEndpoints(jsonOptions);

        // Fund accounts (custodian and bank) endpoints
        app.MapFundAccountEndpoints(jsonOptions);

        // Organization-rooted governance structure endpoints
        app.MapFundStructureEndpoints(jsonOptions);
        app.MapEnvironmentDesignerEndpoints(jsonOptions);
        // Security Master endpoints
        app.MapSecurityMasterEndpoints(jsonOptions);

        // Credential management endpoints
        app.MapCredentialEndpoints(jsonOptions);

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

        // Paper trading cockpit endpoints
        app.MapExecutionEndpoints(jsonOptions);

        // Promotion workflow endpoints (Backtest → Paper → Live)
        app.MapPromotionEndpoints(jsonOptions);

        // Strategy lifecycle control endpoints (pause/stop/status)
        app.MapStrategyLifecycleEndpoints(jsonOptions);

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

        // Direct lending endpoints
        app.MapDirectLendingEndpoints(jsonOptions);

        // Fund accounts (custodian and bank) endpoints
        app.MapFundAccountEndpoints(jsonOptions);

        // Organization-rooted governance structure endpoints
        app.MapFundStructureEndpoints(jsonOptions);
        app.MapEnvironmentDesignerEndpoints(jsonOptions);
        // Security Master endpoints
        app.MapSecurityMasterEndpoints(jsonOptions);

        // Credential management endpoints
        app.MapCredentialEndpoints(jsonOptions);

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

        // Paper trading cockpit endpoints
        app.MapExecutionEndpoints(jsonOptions);

        // Promotion workflow endpoints (Backtest → Paper → Live)
        app.MapPromotionEndpoints(jsonOptions);

        // Strategy lifecycle control endpoints (pause/stop/status)
        app.MapStrategyLifecycleEndpoints(jsonOptions);

        return app;
    }

    /// <summary>
     /// Rate limiting policy name applied to mutation (POST/PUT/DELETE) endpoints.
     /// </summary>
    public const string MutationRateLimitPolicy = "mutation";

    /// <summary>
    /// Registers a per-IP fixed-window rate limiter for mutation endpoints.
    /// Allows 10 requests per minute per IP with a small queue for bursts.
    /// Set the <c>MDC_DISABLE_RATE_LIMIT=true</c> environment variable to bypass rate
    /// limiting entirely (intended for test environments where all requests share the
    /// same loopback address and a 10/min limit would be exhausted immediately).
    /// </summary>
    private static IServiceCollection AddMutationRateLimiter(this IServiceCollection services)
    {
        // Allow tests (and dev environments) to opt out of rate limiting via env var.
        // In production this variable is absent, so the guard never triggers.
        var disableRateLimit = string.Equals(
            Environment.GetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            if (disableRateLimit)
            {
                options.AddPolicy(MutationRateLimitPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("global"));
                return;
            }

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

}
