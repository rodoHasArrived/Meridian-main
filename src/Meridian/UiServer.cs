using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using AppBacktesting = global::Meridian.Application.Backtesting;
using BacktestingEngine = global::Meridian.Backtesting.Engine;
using BacktestingRuntime = global::Meridian.Backtesting;

namespace Meridian;

/// <summary>
/// Embedded HTTP server for the web dashboard UI.
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
        var contentRootPath = Directory.GetCurrentDirectory();
        var webRootPath = StaticAssetPathResolver.ResolveWebRootPath(
            existingWebRootPath: null,
            contentRootPath,
            AppContext.BaseDirectory);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRootPath,
            WebRootPath = webRootPath
        });

        // Minimize logging from ASP.NET Core
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                global::Meridian.Application.Serialization.MarketDataJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        });

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
            new StatusEndpointHandlers(
                Metrics.GetSnapshot,
                () => GetPipelineStatistics(sp),
                () => GetIntegrityEvents(sp),
                () => null));

        // Register session-based authentication service
        builder.Services.AddSingleton<Meridian.Ui.Shared.UserProfileRegistry>();
        builder.Services.AddSingleton<LoginSessionService>();
        builder.Services.AddSingleton<IStrategyRepository, StrategyRunStore>();
        builder.Services.AddSingleton<ISecurityReferenceLookup, SecurityMasterSecurityReferenceLookup>();
        builder.Services.AddSingleton<PortfolioReadService>();
        builder.Services.AddSingleton<LedgerReadService>();
        builder.Services.AddSingleton<StrategyRunReadService>();
        builder.Services.AddSingleton<BacktestingEngine.BacktestEngine>(sp =>
            new BacktestingEngine.BacktestEngine(
                sp.GetRequiredService<ILogger<BacktestingEngine.BacktestEngine>>(),
                sp.GetRequiredService<Storage.Services.StorageCatalogService>(),
                sp.GetService<Contracts.SecurityMaster.ISecurityMasterQueryService>(),
                sp.GetService<BacktestingRuntime.ICorporateActionAdjustmentService>()));
        builder.Services.AddSingleton<AppBacktesting.IBacktestStudioEngine, BacktestingRuntime.MeridianNativeBacktestStudioEngine>();
        builder.Services.AddSingleton<BacktestingRuntime.BacktestStudioRunOrchestrator>();
        builder.Services.AddSingleton<IReconciliationRunRepository, InMemoryReconciliationRunRepository>();
        builder.Services.AddSingleton<ReconciliationProjectionService>();
        builder.Services.AddSingleton<IReconciliationRunService, ReconciliationRunService>();
        builder.Services.AddSingleton<CashFlowProjectionService>();
        builder.Services.AddSingleton<Meridian.Strategies.Promotions.BacktestToLivePromoter>();
        builder.Services.AddSingleton<Meridian.Strategies.Services.PromotionService>();
        builder.Services.AddSingleton<PaperSessionPersistenceService>();
        builder.Services.AddSingleton<StrategyLifecycleManager>();

        // Execution layer — stable REST seam backed by configurable paper/live gateway selection.
        builder.Services.AddSingleton(sp =>
        {
            var dataRoot = sp.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load().DataRoot;
            return new ExecutionAuditTrailOptions(Path.Combine(dataRoot, "execution", "audit"));
        });
        builder.Services.AddSingleton(sp =>
        {
            var dataRoot = sp.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load().DataRoot;
            return new ExecutionOperatorControlOptions(Path.Combine(dataRoot, "execution", "controls"));
        });
        builder.Services.AddSingleton<ExecutionAuditTrailService>();
        builder.Services.AddSingleton<ExecutionOperatorControlService>();
        builder.Services.AddSingleton<IPortfolioState>(_ => new PaperTradingPortfolio(100_000m));
        builder.Services.AddHostedBrokerageGateways();
        builder.Services.AddBrokerageExecution(ApplyExecutionConfiguration);

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
        _app.UseStaticFiles();
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
        var statusHandlers = _app.Services.GetRequiredService<StatusEndpointHandlers>();

        // Host-specific endpoint groups not covered by the shared UI endpoint aggregator.
        var config = _app.Services.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load();
        _app.MapPackagingEndpoints(config.DataRoot);
        _app.MapArchiveMaintenanceEndpoints();

        // Dashboard root page is host-specific and not included in MapUiEndpointsWithStatus.
        _app.MapDashboard();

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

    private static PipelineStatistics GetPipelineStatistics(IServiceProvider services)
    {
        try
        {
            return services.GetService<EventPipeline>()?.GetStatistics()
                ?? new PipelineStatistics(
                    PublishedCount: 0,
                    DroppedCount: 0,
                    ConsumedCount: 0,
                    CurrentQueueSize: 0,
                    PeakQueueSize: 0,
                    QueueCapacity: 0,
                    QueueUtilization: 0,
                    AverageProcessingTimeUs: 0,
                    TimeSinceLastFlush: TimeSpan.Zero,
                    Timestamp: DateTimeOffset.UtcNow);
        }
        catch
        {
            return new PipelineStatistics(
                PublishedCount: 0,
                DroppedCount: 0,
                ConsumedCount: 0,
                CurrentQueueSize: 0,
                PeakQueueSize: 0,
                QueueCapacity: 0,
                QueueUtilization: 0,
                AverageProcessingTimeUs: 0,
                TimeSinceLastFlush: TimeSpan.Zero,
                Timestamp: DateTimeOffset.UtcNow);
        }
    }

    private static IReadOnlyList<DepthIntegrityEvent> GetIntegrityEvents(IServiceProvider services)
    {
        try
        {
            return services.GetService<MarketDepthCollector>()?.GetRecentIntegrityEvents()
                ?? Array.Empty<DepthIntegrityEvent>();
        }
        catch
        {
            return Array.Empty<DepthIntegrityEvent>();
        }
    }

    private static void ApplyExecutionConfiguration(BrokerageConfiguration config)
    {
        config.Gateway = GetEnvironmentValue(
            "MERIDIAN_EXECUTION_GATEWAY",
            "MERIDIAN__EXECUTION__GATEWAY")
            ?? "paper";

        config.LiveExecutionEnabled = GetEnvironmentBool(
            "MERIDIAN_EXECUTION_LIVE_ENABLED",
            "MERIDIAN__EXECUTION__LIVE_ENABLED")
            ?? false;

        config.MaxPositionSize = GetEnvironmentDecimal(
            "MERIDIAN_EXECUTION_MAX_POSITION_SIZE",
            "MERIDIAN__EXECUTION__MAX_POSITION_SIZE")
            ?? 0m;

        config.MaxOrderNotional = GetEnvironmentDecimal(
            "MERIDIAN_EXECUTION_MAX_ORDER_NOTIONAL",
            "MERIDIAN__EXECUTION__MAX_ORDER_NOTIONAL")
            ?? 0m;

        config.MaxOpenOrders = GetEnvironmentInt(
            "MERIDIAN_EXECUTION_MAX_OPEN_ORDERS",
            "MERIDIAN__EXECUTION__MAX_OPEN_ORDERS")
            ?? 0;
    }

    private static string? GetEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool? GetEnvironmentBool(params string[] names)
    {
        var raw = GetEnvironmentValue(names);
        return bool.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static decimal? GetEnvironmentDecimal(params string[] names)
    {
        var raw = GetEnvironmentValue(names);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? GetEnvironmentInt(params string[] names)
    {
        var raw = GetEnvironmentValue(names);
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}
