using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Domain.Collectors;
using Meridian.Execution;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Contracts;
using Meridian.QuantScript;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
    public const string LocalShutdownTokenHeader = "X-Meridian-Shutdown-Token";

    private readonly WebApplication _app;
    private readonly ILogger<UiServer> _logger;
    private readonly IApplicationLifecycleCoordinator _lifecycle;
    private readonly bool _ownsLifecycle;
    private readonly string _configPath;
    private readonly int _port;

    /// <summary>
    /// Creates a new UiServer using the centralized ServiceCompositionRoot.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="port">HTTP port to listen on.</param>
    /// <param name="lifecycle">Optional process lifecycle coordinator used by local shutdown endpoints.</param>
    public UiServer(
        string configPath,
        int port = 8080,
        IApplicationLifecycleCoordinator? lifecycle = null)
    {
        var serverBuildStopwatch = Stopwatch.StartNew();
        _configPath = configPath;
        _port = port;
        _lifecycle = lifecycle ?? ApplicationLifecycleCoordinator.Create(Serilog.Log.Logger);
        _ownsLifecycle = lifecycle is null;

        var contentRootPath = Directory.GetCurrentDirectory();
        var serviceRegistrationStopwatch = Stopwatch.StartNew();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRootPath
        });
        var resolvedDataRoot = ResolvePersistentDataRoot(configPath);

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
        builder.Services.AddSingleton(_lifecycle);

        builder.Services.AddSingleton(new StrategyDesignStoreOptions(Path.Combine(resolvedDataRoot, "strategies", "designer")));
        builder.Services.AddWorkstationSharedServices();

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

        builder.Services.AddSingleton<IReconciliationGovernanceAuditStore>(_ =>
            new JsonlReconciliationGovernanceAuditStore(Path.Combine(resolvedDataRoot, "reconciliation", "governance-audit.jsonl")));
        builder.Services.AddSingleton<ReconciliationGovernanceService>();
        // Durable promotion-record store is required by PromotionService; without it
        // /api/promotion/approve and /api/promotion/reject fail DI resolution at runtime.
        builder.Services.AddSingleton<IPromotionRecordStore>(sp =>
            new JsonlPromotionRecordStore(
                Path.Combine(resolvedDataRoot, "strategies", "promotions"),
                sp.GetRequiredService<ILogger<JsonlPromotionRecordStore>>()));
        builder.Services.AddSingleton(new ExecutionAuditTrailOptions(Path.Combine(resolvedDataRoot, "execution", "audit")));
        builder.Services.AddSingleton<ExecutionAuditTrailService>();
        builder.Services.AddSingleton(new ExecutionOperatorControlOptions(Path.Combine(resolvedDataRoot, "execution", "controls")));
        builder.Services.AddSingleton<ExecutionOperatorControlService>();
        builder.Services.AddSingleton<IPaperSessionStore>(sp =>
            new JsonlFilePaperSessionStore(
                Path.Combine(resolvedDataRoot, "execution", "sessions"),
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

        // Quant Lab — opt-in via configuration "QuantLab:Enabled". Off by default because the
        // engine compiles and executes arbitrary C# in-process; enable only on a trusted host.
        var quantLabEnabled = builder.Configuration.GetValue<bool>("QuantLab:Enabled");
        if (quantLabEnabled)
        {
            builder.Services.AddMeridianQuantScript();
        }

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
        serviceRegistrationStopwatch.Stop();

        var appBuildStopwatch = Stopwatch.StartNew();
        _app = builder.Build();
        appBuildStopwatch.Stop();
        _logger = _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<UiServer>();
        _logger.LogInformation(
            "UiServer service graph built (ServiceRegistrationMs={ServiceRegistrationMs}, AppBuildMs={AppBuildMs})",
            serviceRegistrationStopwatch.ElapsedMilliseconds,
            appBuildStopwatch.ElapsedMilliseconds);

        var readinessStopwatch = Stopwatch.StartNew();
        LedgerStartup.EnsureDatabaseReady(_app.Services, _logger);
        SecurityMasterStartup.EnsureDatabaseReady(_app.Services, _logger);
        DirectLendingStartup.EnsureDatabaseReady(_app.Services, _logger);
        readinessStopwatch.Stop();
        _logger.LogInformation("UiServer readiness checks completed in {ElapsedMs} ms", readinessStopwatch.ElapsedMilliseconds);

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

        var routeStopwatch = Stopwatch.StartNew();
        ConfigureRoutes();
        routeStopwatch.Stop();
        serverBuildStopwatch.Stop();
        _logger.LogInformation(
            "UiServer configured routes in {RouteMapMs} ms; constructor completed in {ElapsedMs} ms",
            routeStopwatch.ElapsedMilliseconds,
            serverBuildStopwatch.ElapsedMilliseconds);
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

        MapLifecycleEndpoints();

        _app.MapUiEndpointsWithStatus(statusHandlers);

    }

    private void MapLifecycleEndpoints()
    {
        _app.MapGet("/api/system/lifecycle", (HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (!IsAuthorizedLifecycleRequest(context))
                return Results.Unauthorized();

            return Results.Ok(new
            {
                processId = Environment.ProcessId,
                processName = Process.GetCurrentProcess().ProcessName,
                startedAtUtc = _lifecycle.StartedAtUtc,
                uptimeSeconds = Math.Round((DateTimeOffset.UtcNow - _lifecycle.StartedAtUtc).TotalSeconds, 3),
                port = _port,
                configPath = _configPath,
                shutdownRequested = _lifecycle.IsShutdownRequested,
                shutdownReason = _lifecycle.ShutdownReason
            });
        });

        _app.MapPost("/api/system/shutdown", async (HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            if (!IsAuthorizedLifecycleRequest(context))
                return Results.Unauthorized();

            await _lifecycle.RequestShutdownAsync(
                "http-local-shutdown",
                "Local lifecycle endpoint requested shutdown",
                context.RequestAborted);

            return Results.Json(new
            {
                accepted = true,
                processId = Environment.ProcessId,
                shutdownRequested = _lifecycle.IsShutdownRequested
            }, statusCode: StatusCodes.Status202Accepted);
        });
    }

    private bool IsAuthorizedLifecycleRequest(HttpContext context)
        => context.Items.ContainsKey(LoginSessionMiddleware.CurrentUserKey)
            || IsValidLocalShutdownToken(context);

    private bool IsValidLocalShutdownToken(HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(_lifecycle.LocalShutdownToken))
            return false;

        var supplied = context.Request.Headers[LocalShutdownTokenHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(_lifecycle.LocalShutdownToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return suppliedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is null || IPAddress.IsLoopback(remoteIp);
    }

    internal static string ResolvePersistentDataRoot(string configPath)
    {
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var configuredDataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, null);
                return MeridianPathDefaults.ResolveDataRoot(configPath, configuredDataRoot);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return MeridianPathDefaults.ResolveDataRoot(configPath, null);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await _app.StartAsync(ct);
        _logger.LogInformation(
            "UiServer started on {Urls} in {ElapsedMs} ms",
            string.Join(", ", _app.Urls),
            stopwatch.ElapsedMilliseconds);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await _app.StopAsync(ct);
        _logger.LogInformation("UiServer stopped in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        if (_ownsLifecycle)
        {
            _lifecycle.Dispose();
        }
    }
}
