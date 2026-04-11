using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Domain.Collectors;
using Meridian.Execution;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Contracts;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Meridian;

/// <summary>
/// Embedded HTTP server for the desktop-local API surface.
/// Uses ServiceCompositionRoot for centralized service registration.
/// All endpoints are organized in dedicated endpoint classes in Meridian.Ui.Shared/Endpoints/.
/// </summary>
[ImplementsAdr("ADR-001", "UiServer uses centralized composition root")]
[ImplementsAdr("ADR-004", "Large file decomposition - endpoints extracted to dedicated modules")]
public sealed class UiServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions s_jsonOptionsCompact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly WebApplication _app;
    private readonly ILogger<UiServer> _logger;

    /// <summary>
    /// Creates a new UiServer using the centralized ServiceCompositionRoot.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="port">HTTP port to listen on.</param>
    public UiServer(string configPath, int port = 8080)
    {
<<<<<<< HEAD
        var contentRootPath = Directory.GetCurrentDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRootPath
        });
=======
        var builder = WebApplication.CreateBuilder();
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

        // Minimize logging from ASP.NET Core
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Allow reflection-based JSON binding for endpoint request types not covered by source-generated contexts.
        // This is required for minimal-API parameter binding (e.g. PackageRequest, ImportRequest).
        // Existing source-generated contexts still take precedence; reflection acts as a fallback only.
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()));

        // Use centralized service composition root
        var compositionOptions = CompositionOptions.WebDashboard with { ConfigPath = configPath };
        builder.Services.AddMarketDataServices(compositionOptions);

        // Register the Ui.Shared ConfigStore wrapper so endpoint lambdas can resolve it from DI.
        // The wrapper delegates to the core ConfigStore already registered by AddMarketDataServices.
        builder.Services.AddSingleton<Meridian.Ui.Shared.Services.ConfigStore>(sp =>
        {
            var core = sp.GetRequiredService<Meridian.Application.UI.ConfigStore>();
            return new Meridian.Ui.Shared.Services.ConfigStore(core.ConfigPath);
        });

        // Register the Ui.Shared BackfillCoordinator so endpoint lambdas can resolve it from DI.
        builder.Services.AddSingleton<Meridian.Ui.Shared.Services.BackfillCoordinator>(sp =>
        {
            var configStore = sp.GetRequiredService<Meridian.Ui.Shared.Services.ConfigStore>();
            return new Meridian.Ui.Shared.Services.BackfillCoordinator(configStore);
        });

        builder.Services.AddSingleton<StatusEndpointHandlers>(sp =>
        {
            var pipeline = sp.GetRequiredService<EventPipeline>();
            var depthCollector = sp.GetRequiredService<MarketDepthCollector>();

            return new StatusEndpointHandlers(
                Metrics.GetSnapshot,
                pipeline.GetStatistics,
                () => depthCollector.GetRecentIntegrityEvents(),
                () => null);
        });

        // Register session-based authentication service
        builder.Services.AddSingleton<Meridian.Ui.Shared.UserProfileRegistry>();
        builder.Services.AddSingleton<LoginSessionService>();
        builder.Services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        builder.Services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        builder.Services.AddSingleton<PortfolioReadService>();
        builder.Services.AddSingleton<LedgerReadService>();
        builder.Services.AddSingleton<StrategyRunReadService>();
        builder.Services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        builder.Services.AddSingleton<ReconciliationProjectionService>();
        builder.Services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        builder.Services.AddSingleton<CashFlowProjectionService>();
        builder.Services.AddSingleton<Meridian.Strategies.Promotions.BacktestToLivePromoter>();
        builder.Services.AddSingleton<Meridian.Strategies.Services.PromotionService>();
        builder.Services.AddSingleton<PaperSessionPersistenceService>();
        builder.Services.AddSingleton<StrategyLifecycleManager>();

        // Execution layer — paper trading gateway wired for cockpit endpoints
        builder.Services.AddSingleton<IOrderGateway>(sp =>
            new Meridian.Execution.Adapters.PaperTradingGateway(
                sp.GetRequiredService<ILogger<Meridian.Execution.Adapters.PaperTradingGateway>>()));
        builder.Services.AddSingleton<IPortfolioState>(_ => new PaperTradingPortfolio(100_000m));
        builder.Services.AddSingleton<IOrderManager>(sp =>
        {
            var gateway = sp.GetRequiredService<IExecutionGateway>();
            var logger = sp.GetRequiredService<ILogger<OrderManagementSystem>>();
            var risk = sp.GetService<IRiskValidator>();
            return new OrderManagementSystem(gateway, logger, risk);
        });
        builder.Services.AddSingleton<IExecutionGateway>(sp =>
            new Meridian.Execution.PaperTradingGateway(
                sp.GetRequiredService<ILogger<Meridian.Execution.PaperTradingGateway>>()));

        // Register OpenAPI/Swagger services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Meridian API",
                Version = "v1",
                Description = "REST API for the Meridian system. Provides endpoints for real-time data streaming, " +
                              "historical backfill, storage management, provider configuration, and data quality monitoring.",
                Contact = new OpenApiContact
                {
                    Name = "Meridian Team"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT"
                }
            });

            options.TagActionsBy(api =>
            {
                var path = api.RelativePath ?? string.Empty;
                if (path.StartsWith("api/symbols"))
                    return ["Symbols"];
                if (path.StartsWith("api/storage/quality"))
                    return ["Storage Quality"];
                if (path.StartsWith("api/storage"))
                    return ["Storage"];
                if (path.StartsWith("api/config"))
                    return ["Configuration"];
                if (path.StartsWith("api/backfill"))
                    return ["Backfill"];
                if (path.StartsWith("api/providers"))
                    return ["Providers"];
                if (path.StartsWith("api/quality"))
                    return ["Data Quality"];
                if (path.StartsWith("api/sla"))
                    return ["SLA"];
                if (path.StartsWith("api/maintenance"))
                    return ["Maintenance"];
                if (path.StartsWith("api/packaging"))
                    return ["Packaging"];
                if (path.StartsWith("api/failover"))
                    return ["Failover"];
                if (path.StartsWith("api/export"))
                    return ["Export"];
                if (path.StartsWith("api/diagnostics"))
                    return ["Diagnostics"];
                if (path.StartsWith("api/admin"))
                    return ["Admin"];
                if (path.StartsWith("api/live"))
                    return ["Live Data"];
                if (path.StartsWith("api/replay"))
                    return ["Replay"];
                if (path.StartsWith("api/lean"))
                    return ["Lean Integration"];
                if (path.StartsWith("api/messaging"))
                    return ["Messaging"];
                if (path.StartsWith("api/analytics"))
                    return ["Analytics"];
                if (path.StartsWith("api/historical"))
                    return ["Historical"];
                if (path.StartsWith("api/options"))
                    return ["Options"];
                if (path.StartsWith("api/strategies"))
                    return ["Strategies"];
                if (path.StartsWith("api/execution"))
                    return ["Execution"];
                if (path.StartsWith("api/promotion"))
                    return ["Promotion"];
                return ["General"];
            });
        });

        _app = builder.Build();
        _logger = _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<UiServer>();
        SecurityMasterStartup.EnsureDatabaseReady(_app.Services, _logger);
        DirectLendingStartup.EnsureDatabaseReady(_app.Services, _logger);

        // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
        ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(_app.Services);

        // Enable session-based authentication middleware (optional in Development/Test, required elsewhere by default)
        _app.UseLoginSessionAuthentication();

        // Enable Swagger middleware
        _app.UseSwagger();
        _app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Meridian API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Meridian - API Documentation";
        });

        ConfigureRoutes();
    }

    private void ConfigureRoutes()
    {
        // ==================== UNIQUE ENDPOINT MODULES ====================
        // Endpoints not included in MapUiEndpoints and must be registered explicitly.

        // Status API (requires StatusEndpointHandlers, not included in MapUiEndpoints).
        // This registers all health/liveness/readiness probes (/health, /healthz, /ready,
        // /readyz, /live, /livez) with proper handler logic (real readiness checks, full
        // HealthCheckResponse). Do NOT register those routes inline above this call.
        var statusHandlers = _app.Services.GetRequiredService<StatusEndpointHandlers>();
        _app.MapStatusEndpoints(statusHandlers, s_jsonOptions);

        // Data Packaging API (requires dataRoot, not included in MapUiEndpoints)
        var config = _app.Services.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load();
        _app.MapPackagingEndpoints(config.DataRoot);

        // Archive Maintenance API (not included in MapUiEndpoints)
        _app.MapArchiveMaintenanceEndpoints();

<<<<<<< HEAD
        _app.MapUiEndpointsWithStatus(statusHandlers);
=======
        // Canonicalization parity dashboard (not included in MapUiEndpoints)
        _app.MapCanonicalizationEndpoints(s_jsonOptions);

        // Dashboard root page (maps GET / → HTML, not included in MapUiEndpoints)
        _app.MapDashboard();

        // ==================== AGGREGATED ENDPOINT MODULES ====================
        // All remaining API endpoints (config, backfill, storage, providers, etc.)
        _app.MapUiEndpoints(s_jsonOptions);
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _app.StartAsync(ct);
        _logger.LogInformation("UiServer started on {Urls}", string.Join(", ", _app.Urls));
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _app.StopAsync(ct);
        _logger.LogInformation("UiServer stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
