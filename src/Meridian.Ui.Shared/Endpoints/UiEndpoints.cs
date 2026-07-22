using System.Text.Json;
using System.Threading.RateLimiting;
using Meridian.Application.Composition;
using Meridian.Application.Monitoring;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using ApplicationStatusEndpointHandlers = Meridian.Application.UI.StatusEndpointHandlers;

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
        return services.AddUiSharedServicesCore(configPath, statusHandlers: null);
    }

    /// <summary>
    /// Registers shared services with a pre-configured StatusEndpointHandlers instance.
    /// Use this when you want to share the same handlers with StatusHttpServer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="statusHandlers">Status endpoint handlers to register.</param>
    /// <param name="configPath">Optional path to configuration file.</param>
    public static IServiceCollection AddUiSharedServices(this IServiceCollection services, ApplicationStatusEndpointHandlers statusHandlers, string? configPath = null)
    {
        ArgumentNullException.ThrowIfNull(statusHandlers);
        return services.AddUiSharedServicesCore(configPath, statusHandlers);
    }

    private static IServiceCollection AddUiSharedServicesCore(
        this IServiceCollection services,
        string? configPath,
        ApplicationStatusEndpointHandlers? statusHandlers)
    {
        var options = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        services.AddMarketDataServices(options);
        services.AddWorkstationSharedServices();
        if (statusHandlers is not null)
        {
            services.AddSingleton(statusHandlers);
        }

        services.AddMutationRateLimiter();
        services.AddLeanAutoExportHostedService();
        services.AddStatementFetchSchedulerHostedService();
        return services;
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
        => app.MapUiEndpointGroups(
            jsonOptions,
            jsonOptionsIndented,
            statusHandlers: null,
            mapCanonicalizationEndpoints: false);

    private static WebApplication MapUiEndpointGroups(
        this WebApplication app,
        JsonSerializerOptions jsonOptions,
        JsonSerializerOptions? jsonOptionsIndented,
        ApplicationStatusEndpointHandlers? statusHandlers,
        bool mapCanonicalizationEndpoints)
    {
        jsonOptionsIndented ??= new JsonSerializerOptions
        {
            PropertyNamingPolicy = jsonOptions.PropertyNamingPolicy,
            WriteIndented = true
        };

        if (statusHandlers is not null)
        {
            app.MapStatusEndpoints(statusHandlers, jsonOptions);
        }

        // Map all endpoint groups
        app.MapConfigEndpoints(jsonOptions);
        app.MapBackfillEndpoints(jsonOptions, jsonOptionsIndented);
        app.MapProviderEndpoints(jsonOptions);
        app.MapFailoverEndpoints(jsonOptions);
        app.MapIBEndpoints(jsonOptions);
        app.MapSymbolMappingEndpoints(jsonOptions);
        app.MapLiveDataEndpoints(jsonOptions);
        app.MapSymbolEndpoints(jsonOptions);

        // Data ingestion and operator onboarding endpoints
        app.MapDemoModeEndpoints(jsonOptions);
        app.MapBackfillValidationEndpoints(jsonOptions);
        app.MapProviderConnectionEndpoints(jsonOptions);
        app.MapProviderCredentialEndpoints(jsonOptions);
        app.MapProviderRoutingEndpoints(jsonOptions);
        app.MapPlaidEndpoints(jsonOptions);
        app.MapAccountingSystemEndpoints(jsonOptions);

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
        app.MapOmsIntegrationEndpoints(jsonOptions);
        app.MapProviderExtendedEndpoints(jsonOptions);
        app.MapProviderDataProjectionEndpoints(jsonOptions);
        app.MapProviderModuleEndpoints(jsonOptions);
        app.MapIndexEndpoints(jsonOptions);

        if (mapCanonicalizationEndpoints)
        {
            app.MapCanonicalizationEndpoints(jsonOptions);
        }

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
        app.MapLedgerEndpoints(jsonOptions);

        // Portfolio-wide cash ladder and liquidity scenarios
        app.MapPortfolioCashLadderEndpoints(jsonOptions);

        // Organization-rooted governance structure endpoints
        app.MapFundStructureEndpoints(jsonOptions);
        app.MapReportingGovernanceEndpoints(jsonOptions);
        app.MapSecureReportingDistributionEndpoints();
        app.MapReportingRunStreamEndpoints(jsonOptions);
        app.MapEnvironmentDesignerEndpoints(jsonOptions);
        // Security Master endpoints
        app.MapSecurityMasterEndpoints(jsonOptions);
        app.MapBondReferenceEndpoints(jsonOptions);
        app.MapOptionReferenceEndpoints(jsonOptions);
        app.MapOptionChainEndpoints(jsonOptions);
        app.MapEquityReferenceEndpoints(jsonOptions);
        app.MapFutureReferenceEndpoints(jsonOptions);
        app.MapFxSpotReferenceEndpoints(jsonOptions);
        app.MapSwapReferenceEndpoints(jsonOptions);
        app.MapCommodityReferenceEndpoints(jsonOptions);
        app.MapCryptoReferenceEndpoints(jsonOptions);
        app.MapDepositReferenceEndpoints(jsonOptions);
        app.MapMoneyMarketFundReferenceEndpoints(jsonOptions);
        app.MapCertificateOfDepositReferenceEndpoints(jsonOptions);
        app.MapEdgarReferenceDataEndpoints(jsonOptions);

        // Credential management endpoints
        app.MapCredentialEndpoints(jsonOptions);

        // Read-only brokerage OAuth connection endpoints
        app.MapBrokerageConnectionEndpoints(jsonOptions);

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
        app.MapInitialAccountBootstrapEndpoints();

        // React workstation shell and bootstrap data
        app.MapWorkstationEndpoints(jsonOptions);
        app.MapFirstRunEndpoints();
        app.MapEvidenceEndpoints(jsonOptions);

        // Paper trading cockpit endpoints
        app.MapExecutionEndpoints(jsonOptions);
        app.MapRiskEndpoints(jsonOptions);
        app.MapComplianceEndpoints(jsonOptions);

        // Promotion workflow endpoints (Backtest → Paper → Live)
        app.MapPromotionEndpoints(jsonOptions);

        // Strategy lifecycle control endpoints (pause/stop/status)
        app.MapStrategyLifecycleEndpoints(jsonOptions);

        // Covered-call strategy backtest endpoints (slice 1)
        app.MapCoveredCallEndpoints(jsonOptions);

        // Quant Lab (gated by host configuration "QuantLab:Enabled" — endpoints respond
        // 503 when the engine is not registered, so it is safe to map unconditionally).
        app.MapQuantLabEndpoints(jsonOptions);

        return app;
    }

    /// <summary>
    /// Maps all UI API endpoints including status endpoints.
    /// Use this when StatusEndpointHandlers has been registered in DI.
    /// </summary>
    public static WebApplication MapUiEndpointsWithStatus(this WebApplication app, ApplicationStatusEndpointHandlers statusHandlers)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonOptionsIndented = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        return app.MapUiEndpointGroups(
            jsonOptions,
            jsonOptionsIndented,
            statusHandlers,
            mapCanonicalizationEndpoints: true);
    }

    /// <summary>
    /// Rate limiting policy name applied to mutation (POST/PUT/DELETE) endpoints.
    /// </summary>
    public const string MutationRateLimitPolicy = "mutation";

    /// <summary>
    /// Direct-lending command limiter. Kept separate from the general mutation budget so loan
    /// servicing writes have an explicit, independently auditable abuse-control boundary.
    /// </summary>
    public const string DirectLendingMutationRateLimitPolicy = "direct-lending-mutation";

    /// <summary>
    /// Registers a per-IP fixed-window rate limiter for mutation endpoints.
    /// Allows 10 requests per minute per IP with a small queue for bursts.
    /// Set the <c>MDC_DISABLE_RATE_LIMIT=true</c> environment variable to bypass rate
    /// limiting entirely (intended for test environments where all requests share the
    /// same loopback address and a 10/min limit would be exhausted immediately).
    /// </summary>
    public static IServiceCollection AddMutationRateLimiter(this IServiceCollection services)
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
                options.AddPolicy(DirectLendingMutationRateLimitPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("direct-lending-global"));
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

            options.AddPolicy(DirectLendingMutationRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ??
                                  httpContext.Connection.RemoteIpAddress?.ToString() ??
                                  "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

}
