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
    private readonly WebApplication _app;
    private readonly ILogger<UiServer> _logger;

    /// <summary>
    /// Creates a new UiServer using the centralized ServiceCompositionRoot.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="port">HTTP port to listen on.</param>
    public UiServer(string configPath, int port = 8080)
    {
        var contentRootPath = Directory.GetCurrentDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRootPath
        });

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
        builder.Services.AddSingleton(PromotionRecordStoreOptions.Default);
        builder.Services.AddSingleton<IPromotionRecordStore, JsonlPromotionRecordStore>();
        builder.Services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        builder.Services.AddSingleton<PortfolioReadService>();
        builder.Services.AddSingleton<LedgerReadService>();
        builder.Services.AddSingleton<StrategyRunReadService>();
        builder.Services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        builder.Services.AddSingleton<IStrategyLedgerReconciliationSourceAdapter, StrategyLedgerReconciliationSourceAdapter>();
        builder.Services.AddSingleton<IStrategyPortfolioReconciliationSourceAdapter, StrategyPortfolioReconciliationSourceAdapter>();
        builder.Services.AddSingleton<IInternalCashReconciliationSourceAdapter, BankInternalCashReconciliationSourceAdapter>();
        builder.Services.AddSingleton<IExternalStatementSource, NullExternalStatementSource>();
        builder.Services.AddSingleton<IExternalStatementReconciliationSourceAdapter, ExternalStatementReconciliationSourceAdapter>();
        builder.Services.AddSingleton<ReconciliationProjectionService>();
        builder.Services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        builder.Services.AddSingleton<CashFlowProjectionService>();
        builder.Services.AddSingleton<StrategyRunContinuityService>();
        builder.Services.AddSingleton(BrokeragePortfolioSyncOptions.Default);
        builder.Services.AddSingleton<BrokeragePortfolioSyncService>();
        builder.Services.AddSingleton(Dk1TrustGateReadinessOptions.Default);
        builder.Services.AddSingleton<Dk1TrustGateReadinessService>();
        builder.Services.AddSingleton<TradingOperatorReadinessService>();
        builder.Services.AddSingleton<StrategyRunReviewPacketService>();
        builder.Services.AddSingleton<WorkstationWorkflowSummaryService>();
        builder.Services.AddSingleton<Meridian.Strategies.Promotions.BacktestToLivePromoter>();
        // Durable promotion-record store is required by PromotionService; without it
        // /api/promotion/approve and /api/promotion/reject fail DI resolution at runtime.
        builder.Services.AddSingleton<IPromotionRecordStore>(sp =>
            new JsonlPromotionRecordStore(
                Path.Combine(contentRootPath, "data", "promotions"),
                sp.GetRequiredService<ILogger<JsonlPromotionRecordStore>>()));
        builder.Services.AddSingleton<Meridian.Strategies.Services.PromotionService>();
        builder.Services.AddSingleton<Meridian.Application.SecurityMaster.ISecurityMasterWorkbenchQueryService, Meridian.Application.SecurityMaster.SecurityMasterWorkbenchQueryService>();
        builder.Services.AddSingleton(ExecutionAuditTrailOptions.Default);
        builder.Services.AddSingleton<ExecutionAuditTrailService>();
        builder.Services.AddSingleton(ExecutionOperatorControlOptions.Default);
        builder.Services.AddSingleton<ExecutionOperatorControlService>();
        builder.Services.AddSingleton<IPaperSessionStore>(sp =>
            new JsonlFilePaperSessionStore(
                Path.Combine(AppContext.BaseDirectory, "data", "execution", "sessions"),
                sp.GetRequiredService<ILogger<JsonlFilePaperSessionStore>>()));
        builder.Services.AddSingleton<PaperSessionPersistenceService>();
        builder.Services.AddSingleton<StrategyLifecycleManager>();

        // Execution layer — paper trading gateway wired for cockpit endpoints
        builder.Services.AddSingleton<IOrderGateway>(sp =>
            new Meridian.Execution.Adapters.PaperTradingGateway(
                sp.GetRequiredService<ILogger<Meridian.Execution.Adapters.PaperTradingGateway>>()));
        builder.Services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
        builder.Services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
        builder.Services.AddSingleton<IOrderManager>(sp =>
        {
            var gateway = sp.GetRequiredService<IExecutionGateway>();
            var logger = sp.GetRequiredService<ILogger<OrderManagementSystem>>();
            var risk = sp.GetService<IRiskValidator>();
            var portfolio = sp.GetRequiredService<PaperTradingPortfolio>();
            return new OrderManagementSystem(
                gateway,
                logger,
                riskValidator: risk,
                operatorControls: sp.GetService<ExecutionOperatorControlService>(),
                auditTrail: sp.GetService<ExecutionAuditTrailService>(),
                portfolioState: portfolio,
                sessionPersistence: sp.GetService<PaperSessionPersistenceService>());
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

        // Resolve the shared status handlers once and let the shared UI mapper own the
        // actual status-route registration.
        var statusHandlers = _app.Services.GetRequiredService<StatusEndpointHandlers>();

        // Data Packaging API (requires dataRoot, not included in MapUiEndpoints)
        var config = _app.Services.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load();
        _app.MapPackagingEndpoints(config.DataRoot);

        // Archive Maintenance API (not included in MapUiEndpoints)
        _app.MapArchiveMaintenanceEndpoints();

        _app.MapUiEndpointsWithStatus(statusHandlers);

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
