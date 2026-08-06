using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Meridian.Audit.Compliance;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup;
using Meridian.Core.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Platform.Tracing;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.SecurityMaster;
using Meridian.Domain.Collectors;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Infrastructure.Contracts;
using Meridian.QuantScript;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage.Ledger;
using Meridian.Storage.Runtime;
using Meridian.Ui.Services.Services.Integrations;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private static readonly TimeSpan FailedStartCleanupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Configuration section that binds <see cref="BrokerageConfiguration"/> for the host.</summary>
    public const string ExecutionBrokerageSectionKey = "Execution:Brokerage";

    private readonly WebApplication _app;
    private readonly ILogger<UiServer> _logger;
    private readonly IApplicationLifecycleCoordinator _lifecycle;
    private readonly bool _ownsLifecycle;
    private readonly ApiHostOptions _apiHostOptions;
    private readonly string _configPath;
    private readonly int _port;
    private readonly Func<CancellationToken, Task>? _readinessEvaluator;
    private readonly SemaphoreSlim _databaseReadinessGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleOperationGate = new(1, 1);
    private readonly object _disposeStateGate = new();
    private volatile bool _databaseReadinessCompleted;
    private bool _disposeRequested;
    private bool _applicationStartBegan;
    private bool _applicationStopCompleted;
    private bool _startCompleted;
    private bool _startFailed;
    private Task? _disposeTask;

    /// <summary>
    /// Creates a new UiServer using the centralized ServiceCompositionRoot.
    /// </summary>
    /// <param name="configPath">Path to the configuration file.</param>
    /// <param name="port">HTTP port to listen on.</param>
    /// <param name="lifecycle">Optional process lifecycle coordinator used by local shutdown endpoints.</param>
    /// <param name="apiHostOptions">Optional pre-resolved host options; when omitted they are read from configuration.</param>
    public UiServer(
        string configPath,
        int port = 8080,
        IApplicationLifecycleCoordinator? lifecycle = null,
        ApiHostOptions? apiHostOptions = null)
        : this(
            configPath,
            port,
            lifecycle ?? ApplicationLifecycleCoordinator.Create(Serilog.Log.Logger),
            ownsLifecycle: lifecycle is null,
            apiHostOptions: apiHostOptions)
    {
    }

    internal UiServer(
        string configPath,
        int port,
        IApplicationLifecycleCoordinator lifecycle,
        bool ownsLifecycle,
        ApiHostOptions? apiHostOptions,
        Func<CancellationToken, Task>? readinessEvaluator = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _configPath = configPath;
        _port = port;
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _ownsLifecycle = ownsLifecycle;
        _readinessEvaluator = readinessEvaluator;

        WebApplication? builtApp = null;
        try
        {
            var serverBuildStopwatch = Stopwatch.StartNew();

            // Unified persistence config must resolve before service composition and the
            // ledger/readiness gates below read the per-domain connection-string variables.
            Meridian.Storage.MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

            var persistenceStatus = PersistenceConfigurationStatus.Evaluate();
            if (persistenceStatus.Mode != PersistenceStatusSnapshot.Configured)
            {
                Serilog.Log.Warning(
                    "PERSISTENCE: {PersistenceMode} — store domains without a database: {MissingDomains}. " +
                    "Journal entries, reconciliations, and approvals in these domains are held in memory and will be " +
                    "lost on restart. Set MERIDIAN_DATABASE_URL (or the per-domain MERIDIAN_*_CONNECTION_STRING variables) to persist them.",
                    persistenceStatus.Mode.ToUpperInvariant(),
                    string.Join(", ", persistenceStatus.MissingDomains));
            }

            var contentRootPath = Directory.GetCurrentDirectory();
            var serviceRegistrationStopwatch = Stopwatch.StartNew();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = contentRootPath
            });
            if (File.Exists(configPath))
            {
                builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);
            }

            _apiHostOptions = apiHostOptions ?? ApiHostOptions.FromConfiguration(builder.Configuration, port);
            var resolvedDataRoot = ResolvePersistentDataRoot(configPath);

            // Minimize logging from ASP.NET Core
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.UseUrls(_apiHostOptions.Urls);

            // Allow reflection-based JSON binding for endpoint request types not covered by source-generated contexts.
            // This is required for minimal-API parameter binding (e.g. PackageRequest, ImportRequest).
            // Existing source-generated contexts still take precedence; reflection acts as a fallback only.
            builder.Services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()));
            builder.Services.AddMeridianApiProblemDetails();
            if (_apiHostOptions.AllowedOrigins.Length > 0)
            {
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy(
                        ApiHostOptions.CorsPolicyName,
                        policy => policy
                            .WithOrigins(_apiHostOptions.AllowedOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials());
                });
            }

            // ADR-019: declare the typed deployment posture before feature composition so the
            // production registration policy and the host resolve the same production answer.
            builder.Services.DeclareMeridianDeploymentPosture(_apiHostOptions.ToDeploymentPosture());

            // Use centralized service composition root
            var compositionOptions = CompositionOptions.WebDashboard with { ConfigPath = configPath };
            builder.Services.AddMarketDataServices(compositionOptions);
            builder.Services.AddMutationRateLimiter();
            builder.Services.AddSingleton(_lifecycle);

            var tradeFillPostingOptions = builder.Configuration
                .GetSection(TradeFillLedgerPostingHostOptions.SectionKey)
                .Get<TradeFillLedgerPostingHostOptions>()
                ?? new TradeFillLedgerPostingHostOptions();
            if (tradeFillPostingOptions.Enabled)
            {
                if (!LedgerStartup.IsConfigured())
                {
                    throw new InvalidOperationException(
                        $"{TradeFillLedgerPostingHostOptions.SectionKey}:Enabled requires {LedgerStartup.ConnectionStringVariable} " +
                        $"(or {Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable}) so accepted fills have an authoritative ledger target.");
                }

                var postingContext = tradeFillPostingOptions.BuildContext();
                var tradeFillStoreRoot = Path.Combine(resolvedDataRoot, "execution", "trade-fill-ledger");
                builder.Services.AddTradeFillLedgerPosting(
                    postingContext,
                    sp => new GovernedTradeFillLedgerPostingTarget(
                        sp.GetRequiredService<IGovernedLedgerPostingTarget>(),
                        sp.GetRequiredService<ILedgerJournalStore>()),
                    sp => new WalTradeFillPostingStore(
                        new TradeFillPostingStoreOptions(tradeFillStoreRoot, postingContext),
                        sp.GetRequiredService<ILogger<WalTradeFillPostingStore>>()),
                    _ => new AtomicTradeFillHandoffFailureStore(
                        new TradeFillHandoffFailureStoreOptions(
                            tradeFillStoreRoot,
                            postingContext)),
                    configure: options =>
                    {
                        options.ChannelCapacity = tradeFillPostingOptions.ChannelCapacity;
                        options.DrainTimeout = tradeFillPostingOptions.DrainTimeout;
                        options.CancellationTimeout = tradeFillPostingOptions.CancellationTimeout;
                    });
            }

            builder.Services.AddSingleton(new StrategyDesignStoreOptions(Path.Combine(resolvedDataRoot, "strategies", "designer")));
            builder.Services.AddSingleton(new LoginSessionStoreOptions(Path.Combine(resolvedDataRoot, "identity", "sessions.json")));
            builder.Services.AddWorkstationSharedServices();
            builder.Services.AddStatementFetchSchedulerHostedService();
            builder.Services.AddOmsIntegrationApiHandlers();

            if (_lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle)
            {
                builder.Services.AddSingleton(runtimeLifecycle);
                builder.Services.AddSingleton<IRuntimeLifecycleControlPlane>(runtimeLifecycle);
                builder.Services.AddSingleton<ILifecycleReceiptStore>(new JsonLifecycleReceiptStore(
                    new LifecycleReceiptStoreOptions { DataRoot = resolvedDataRoot }));
                builder.Services.AddSingleton(new RuntimeShutdownOptions());
                builder.Services.AddSingleton<IRuntimeShutdownParticipant, EventPipelineShutdownParticipant>();
                builder.Services.AddSingleton<IRuntimeShutdownSequence, RuntimeShutdownSequence>();
                builder.Services.AddHostedService<LifecycleControlPlaneHostedService>();
                if (!string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable(LifecycleSupervisorBridgeHostedService.PipeEnvironmentVariable)))
                {
                    builder.Services.AddHostedService<LifecycleSupervisorBridgeHostedService>();
                }

                AddLifecycleReadinessChecks(
                    builder.Services,
                    contentRootPath,
                    resolvedDataRoot,
                    builder.Environment);
                builder.Services.AddSingleton<IRuntimeReadinessService, RuntimeReadinessService>();
            }

            builder.Services.AddSingleton<StatusEndpointHandlers>(sp =>
            {
                var pipeline = sp.GetRequiredService<EventPipeline>();
                var depthCollector = sp.GetRequiredService<MarketDepthCollector>();

                return new StatusEndpointHandlers(
                    Metrics.GetSnapshot,
                    pipeline.GetStatistics,
                    () => depthCollector.GetRecentIntegrityEvents(),
                    () => null,
                    degradedModeProvider: () => EvaluateDegradedMode(sp));
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
            builder.Services.AddSingleton(new ExecutionOperatorControlOptions(
                Path.Combine(resolvedDataRoot, "execution", "controls"),
                FailClosedOnMissingOrCorruptSnapshot:
                    ProductionServiceRegistrationPolicy.IsProductionComposition(builder.Services)));
            builder.Services.AddSingleton<ExecutionOperatorControlService>();
            // Governed-approval queue snapshots belong under the writable data root; the
            // queue's AppContext.BaseDirectory default is read-only in installed deployments.
            builder.Services.AddSingleton(new RiskEscalationQueueOptions(
                Path.Combine(resolvedDataRoot, "execution", "risk-escalations", "escalations.json")));
            // Durable paper-session storage root is operator-tunable via
            // "PaperTrading:Sessions:BaseDirectory"; unset keeps the data-root default.
            var paperSessionBaseDirectory = builder.Configuration.GetValue<string?>(
                $"{PaperSessionOptions.SectionKey}:{nameof(PaperSessionOptions.BaseDirectory)}");
            builder.Services.AddSingleton<IPaperSessionStore>(sp =>
                new JsonlFilePaperSessionStore(
                    string.IsNullOrWhiteSpace(paperSessionBaseDirectory)
                        ? Path.Combine(resolvedDataRoot, "execution", "sessions")
                        : paperSessionBaseDirectory,
                    sp.GetRequiredService<ILogger<JsonlFilePaperSessionStore>>()));
            builder.Services.AddSingleton<PaperSessionPersistenceService>();
            builder.Services.AddSingleton<StrategyLifecycleManager>();
            builder.Services.AddSingleton<ICompliancePolicyEngine, CompliancePolicyEngine>();
            // Durable, tamper-evident compliance audit log — persisted so events survive a restart
            // (an in-memory-only log would silently lose all compliance history).
            builder.Services.AddSingleton(
                new Meridian.Audit.Compliance.ImmutableAuditLogService(
                    Path.Combine(resolvedDataRoot, "compliance", "audit", "audit-log.jsonl")));
            builder.Services.AddSingleton<AccessReviewService>();

            // Execution layer — brokerage-configuration-aware gateway composition. The default
            // configuration ("paper", live execution disabled) preserves the paper-first host:
            // the paper gateways below are registered ahead of AddBrokerageExecution's TryAdd
            // fallbacks and now price market fills from the live feed cache. When
            // "Execution:Brokerage" enables live execution with a named gateway, the host skips
            // the paper registrations so AddBrokerageExecution routes orders to the registered
            // brokerage gateway behind the OMS pre-trade gate stack.
            var brokerageConfiguration = builder.Configuration
                .GetSection(ExecutionBrokerageSectionKey)
                .Get<BrokerageConfiguration>()
                ?? new BrokerageConfiguration();
            var usesPaperGateway = !brokerageConfiguration.LiveExecutionEnabled
                || string.IsNullOrWhiteSpace(brokerageConfiguration.Gateway)
                || string.Equals(brokerageConfiguration.Gateway, "paper", StringComparison.OrdinalIgnoreCase);
            builder.Services.AddSingleton(
                builder.Configuration.GetSection(Meridian.Execution.Adapters.PaperTradingGatewayOptions.SectionKey)
                    .Get<Meridian.Execution.Adapters.PaperTradingGatewayOptions>()
                ?? new Meridian.Execution.Adapters.PaperTradingGatewayOptions());
            var configuredOrderManagement = builder.Configuration
                .GetSection(OrderManagementSystemOptions.SectionKey)
                .Get<OrderManagementSystemOptions>() ?? new OrderManagementSystemOptions();
            builder.Services.AddSingleton(new OrderManagementSystemOptions
            {
                MaxRetainedOrders = configuredOrderManagement.MaxRetainedOrders,
                ExecutionChannelCapacity = configuredOrderManagement.ExecutionChannelCapacity,
                CancelAllMaxConcurrency = configuredOrderManagement.CancelAllMaxConcurrency,
                RequireProductionSafetyDependencies =
                    ProductionServiceRegistrationPolicy.IsProductionComposition(builder.Services)
            });
            builder.Services.Configure<Meridian.Execution.Margin.RegTMarginOptions>(
                builder.Configuration.GetSection(Meridian.Execution.Margin.RegTMarginOptions.SectionKey));
            builder.Services.AddHostedBrokerageGateways();
            if (usesPaperGateway)
            {
                builder.Services.AddSingleton<IOrderGateway>(sp =>
                    new Meridian.Execution.Adapters.PaperTradingGateway(
                        sp.GetRequiredService<ILogger<Meridian.Execution.Adapters.PaperTradingGateway>>(),
                        securityMaster: null,
                        options: sp.GetRequiredService<Meridian.Execution.Adapters.PaperTradingGatewayOptions>(),
                        liveFeed: sp.GetService<Meridian.Execution.Adapters.LiveMarketDataCache>()));
            }
            builder.Services.AddSingleton<PaperTradingPortfolio>(_ => new PaperTradingPortfolio(100_000m));
            builder.Services.AddSingleton<IPortfolioState>(sp => sp.GetRequiredService<PaperTradingPortfolio>());
            // Production IPositionTracker projection over the live portfolio state. Gives the
            // safety-critical risk rules (PositionLimitRule, DrawdownCircuitBreaker) a real backing
            // instead of leaving IPositionTracker without any non-test implementation.
            builder.Services.AddSingleton<IPositionTracker>(sp =>
                new PortfolioStatePositionTracker(sp.GetRequiredService<IPortfolioState>()));
            builder.Services.AddSingleton<IOrderManager>(sp =>
            {
                var gateway = sp.GetRequiredService<IExecutionGateway>();
                var logger = sp.GetRequiredService<ILogger<OrderManagementSystem>>();
                // Order routing is fail-closed: an OMS without the mandatory pre-trade risk gate is
                // not a valid host composition in any supported production posture.
                var risk = sp.GetRequiredService<IRiskValidator>();
                var portfolio = sp.GetRequiredService<PaperTradingPortfolio>();
                return new OrderManagementSystem(
                    gateway,
                    logger,
                    riskValidator: risk,
                    securityMasterGate: sp.GetService<ISecurityMasterGate>(),
                    operatorControls: sp.GetService<ExecutionOperatorControlService>(),
                    auditTrail: sp.GetService<ExecutionAuditTrailService>(),
                    portfolioState: portfolio,
                    sessionPersistence: sp.GetService<PaperSessionPersistenceService>(),
                    brokerageConfiguration: sp.GetRequiredService<BrokerageConfiguration>(),
                    liveOrderReadinessGate: sp.GetService<ILiveOrderReadinessGate>(),
                    options: sp.GetRequiredService<OrderManagementSystemOptions>(),
                    tradeEventPublisher: sp.GetService<ITradeEventPublisher>(),
                    tradeFillHandoffFailureStore: sp.GetService<ITradeFillHandoffFailureStore>(),
                    escalationQueue: sp.GetService<RiskEscalationQueueService>());
            });
            if (usesPaperGateway)
            {
                builder.Services.AddSingleton<IExecutionGateway>(sp =>
                    new Meridian.Execution.PaperTradingGateway(
                        sp.GetRequiredService<ILogger<Meridian.Execution.PaperTradingGateway>>(),
                        sp.GetService<ISecurityMasterQueryService>(),
                        sp.GetRequiredService<Meridian.Execution.Adapters.PaperTradingGatewayOptions>(),
                        sp.GetService<Meridian.Execution.Adapters.LiveMarketDataCache>()));
            }

            // Registers BrokerageConfiguration plus the live-mode IExecutionGateway/IOrderGateway
            // selection (TryAdd: the paper registrations above win when paper mode is configured).
            builder.Services.AddBrokerageExecution(config =>
                builder.Configuration.GetSection(ExecutionBrokerageSectionKey).Bind(config));

            // Live trading engine — closes the promotion loop by running promoted paper/live
            // strategies against the live market data feed through the OMS.
            builder.Services.AddLiveTradingEngine(builder.Configuration);

            // Quant Lab — opt-in via "QuantLab:Enabled". Each run uses the dedicated contained worker,
            // but that worker retains the launching identity's file/network permissions. Production and
            // customer distributions therefore remain fail-closed pending OS-profile certification.
            var quantLabEnabled = builder.Configuration.GetValue<bool>("QuantLab:Enabled");
            ProductionServiceRegistrationPolicy.EnsureIsolatedQuantLabIsAllowed(
                builder.Services,
                quantLabEnabled);
            if (quantLabEnabled)
            {
                builder.Services.AddMeridianQuantScript(builder.Configuration);
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

            ValidateAuthenticationTransportSecurity(builder.Environment, _apiHostOptions);
            configureServices?.Invoke(builder.Services);
            serviceRegistrationStopwatch.Stop();

            var appBuildStopwatch = Stopwatch.StartNew();
            builtApp = builder.Build();
            _app = builtApp;
            appBuildStopwatch.Stop();
            _logger = _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<UiServer>();
            _logger.LogInformation(
                "UiServer service graph built (ServiceRegistrationMs={ServiceRegistrationMs}, AppBuildMs={AppBuildMs})",
                serviceRegistrationStopwatch.ElapsedMilliseconds,
                appBuildStopwatch.ElapsedMilliseconds);

            // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
            ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(_app.Services);

            // Standardize fail-closed endpoint failures before authentication and other
            // middleware can short-circuit the request pipeline.
            _app.UseExceptionHandler();
            _app.UseWhen(
                context => context.Request.Path.StartsWithSegments("/api"),
                apiPipeline => apiPipeline.UseStatusCodePages(async statusCodeContext =>
                {
                    var problemDetailsService = statusCodeContext.HttpContext.RequestServices
                        .GetRequiredService<IProblemDetailsService>();
                    await problemDetailsService.WriteAsync(
                        new ProblemDetailsContext
                        {
                            HttpContext = statusCodeContext.HttpContext
                        });
                }));

            // Enable session-based authentication middleware (optional in Development/Test, required elsewhere by default)
            _app.UseLoginSessionAuthentication();
            // Enforce X-Api-Key on /api/* for out-of-band clients when MDC_API_KEY is set.
            // Runs after session auth so browser-workstation requests authenticated by a login
            // session pass without a key; a no-op when MDC_API_KEY is unset.
            _app.UseApiKeyAuthentication();
            _app.UseCookieCsrfProtection();
            _app.UseRateLimiter();
            if (_apiHostOptions.AllowedOrigins.Length > 0)
            {
                _app.UseCors(ApiHostOptions.CorsPolicyName);
            }

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
        catch
        {
            try
            {
                if (builtApp is not null)
                    builtApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanupException)
            {
                Serilog.Log.Warning(
                    cleanupException,
                    "UiServer disposal raised an error after construction failed");
            }

            if (_ownsLifecycle)
            {
                try
                {
                    _lifecycle.Dispose();
                }
                catch (Exception cleanupException)
                {
                    Serilog.Log.Warning(
                        cleanupException,
                        "UiServer lifecycle disposal raised an error after construction failed");
                }
            }

            throw;
        }
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
                return ApiProblemDetails.Forbidden(context);

            var authorizationFailure = ValidateLifecycleAuthorization(context);
            if (authorizationFailure is not null)
                return authorizationFailure;

            if (_lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle)
            {
                var snapshot = runtimeLifecycle.Snapshot with
                {
                    ProcessId = Environment.ProcessId,
                    ProcessName = Process.GetCurrentProcess().ProcessName,
                    Port = _port,
                    ConfigPath = "[redacted]"
                };
                return Results.Ok(snapshot);
            }

            return Results.Ok(new
            {
                processId = Environment.ProcessId,
                processName = Process.GetCurrentProcess().ProcessName,
                startedAtUtc = _lifecycle.StartedAtUtc,
                uptimeSeconds = Math.Round((DateTimeOffset.UtcNow - _lifecycle.StartedAtUtc).TotalSeconds, 3),
                port = _port,
                configPath = "[redacted]",
                shutdownRequested = _lifecycle.IsShutdownRequested,
                shutdownReason = _lifecycle.ShutdownReason
            });
        });

        _app.MapPost("/api/system/shutdown", async (HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return ApiProblemDetails.Forbidden(context);

            var authorizationFailure = ValidateLifecycleAuthorization(context);
            if (authorizationFailure is not null)
                return authorizationFailure;

            if (_lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle)
            {
                LifecycleShutdownRequestDto request;
                try
                {
                    request = context.Request.ContentLength.GetValueOrDefault() > 0
                        ? await System.Text.Json.JsonSerializer.DeserializeAsync(
                            context.Request.Body,
                            LifecycleContractsJsonContext.Default.LifecycleShutdownRequestDto,
                            context.RequestAborted) ?? new LifecycleShutdownRequestDto()
                        : new LifecycleShutdownRequestDto
                        {
                            Reason = LifecycleShutdownReason.HttpLocalShutdown,
                            Detail = "Local lifecycle endpoint requested shutdown"
                        };
                }
                catch (System.Text.Json.JsonException)
                {
                    return ApiProblemDetails.Validation(
                        context,
                        "request",
                        "Invalid lifecycle shutdown request.");
                }

                var accepted = await runtimeLifecycle.RequestShutdownAsync(request, context.RequestAborted);
                context.Response.Headers.Location = accepted.OperationUri;
                return Results.Json(accepted, statusCode: StatusCodes.Status202Accepted);
            }

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

        _app.MapGet("/api/system/shutdown/{operationId}", (string operationId, HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return ApiProblemDetails.Forbidden(context);

            var authorizationFailure = ValidateLifecycleAuthorization(context);
            if (authorizationFailure is not null)
                return authorizationFailure;

            if (_lifecycle is not IRuntimeLifecycleControlPlane runtimeLifecycle ||
                runtimeLifecycle.ActiveShutdownOperation is not { } operation ||
                !string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
            {
                return ApiProblemDetails.NotFound(
                    context,
                    "The requested shutdown operation was not found.");
            }

            return Results.Ok(operation);
        });

        _app.MapGet("/api/system/shutdown/receipts/latest", async (HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return ApiProblemDetails.Forbidden(context);

            var authorizationFailure = ValidateLifecycleAuthorization(context);
            if (authorizationFailure is not null)
                return authorizationFailure;

            var runtimeLifecycle = _lifecycle as IRuntimeLifecycleControlPlane;
            var receipt = runtimeLifecycle?.LatestShutdownReceipt;
            if (receipt is null)
            {
                receipt = await _app.Services
                    .GetRequiredService<ILifecycleReceiptStore>()
                    .ReadLatestHostReceiptAsync(context.RequestAborted);
            }

            return receipt is null
                ? ApiProblemDetails.NotFound(context, "No shutdown receipt was found.")
                : Results.Ok(receipt);
        });
    }

    private IResult? ValidateLifecycleAuthorization(HttpContext context)
    {
        if (IsValidLocalShutdownToken(context))
        {
            return null;
        }

        if (!EndpointAuthorization.TryGetPermissions(context, out _))
        {
            return ApiProblemDetails.Unauthorized(context);
        }

        return EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
            ? null
            : ApiProblemDetails.Forbidden(context);
    }

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

    internal static void ValidateAuthenticationTransportSecurity(IHostEnvironment environment, ApiHostOptions apiHostOptions)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(apiHostOptions);

        if (environment.IsDevelopment() || environment.IsEnvironment("Test"))
            return;

        if (!IsAuthenticationRequired(environment))
            return;

        if (apiHostOptions.AllowInsecureTransportForReverseProxy ||
            HasHttpsBinding(apiHostOptions.Urls) ||
            apiHostOptions.DeploymentMode == MeridianApiDeploymentMode.LocalWorkstation &&
            HasOnlyLoopbackHttpBindings(apiHostOptions.Urls))
            return;

        throw new InvalidOperationException(
            "Authentication is required in production posture, but no HTTPS binding is configured. " +
            "Configure ApiHost:Urls, MERIDIAN_API_URLS, or ASPNETCORE_URLS with an HTTPS URL before startup.");
    }

    internal static bool IsAuthenticationRequired(IHostEnvironment environment)
    {
        var mode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode.Trim().Equals("required", StringComparison.OrdinalIgnoreCase) ||
                   mode.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) &&
                   !(environment.IsDevelopment() || environment.IsEnvironment("Test"));
        }

        return !(environment.IsDevelopment() || environment.IsEnvironment("Test"));
    }

    private static bool HasHttpsBinding(IEnumerable<string> configuredUrls)
    {
        foreach (var url in configuredUrls)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOnlyLoopbackHttpBindings(IEnumerable<string> configuredUrls)
    {
        var hasBinding = false;

        foreach (var url in configuredUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                !parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                !parsed.IsLoopback)
            {
                return false;
            }

            hasBinding = true;
        }

        return hasBinding;
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

    private void AddLifecycleReadinessChecks(
        IServiceCollection services,
        string contentRootPath,
        string resolvedDataRoot,
        IHostEnvironment environment)
    {
        var productionPosture = ProductionServiceRegistrationPolicy.IsProductionComposition(services);

        services.AddSingleton<IRuntimeReadinessCheck>(new DelegateRuntimeReadinessCheck(
            "configuration",
            "Configuration",
            LifecycleCheckRequirement.Required,
            _ => ValueTask.FromResult(File.Exists(_configPath)
                ? new RuntimeReadinessCheckResult(LifecycleCheckStatus.Passing, "Configuration loaded.")
                : new RuntimeReadinessCheckResult(LifecycleCheckStatus.Failing, "Configuration file is unavailable."))));

        services.AddSingleton<IRuntimeReadinessCheck>(new DelegateRuntimeReadinessCheck(
            "data-root",
            "Data root",
            LifecycleCheckRequirement.Required,
            _ => ValueTask.FromResult(Directory.Exists(resolvedDataRoot)
                ? new RuntimeReadinessCheckResult(LifecycleCheckStatus.Passing, "Data root is available.")
                : new RuntimeReadinessCheckResult(LifecycleCheckStatus.Failing, "Data root is unavailable."))));

        services.AddSingleton<IRuntimeReadinessCheck>(new DelegateRuntimeReadinessCheck(
            "authentication",
            "Authentication",
            LifecycleCheckRequirement.Required,
            _ => ValueTask.FromResult(new RuntimeReadinessCheckResult(
                LifecycleCheckStatus.Passing,
                IsAuthenticationRequired(environment)
                    ? "Required authentication policy is active."
                    : "Development authentication policy is active."))));

        services.AddSingleton<IRuntimeReadinessCheck>(new DelegateRuntimeReadinessCheck(
            "workstation-assets",
            "Workstation assets",
            LifecycleCheckRequirement.Required,
            _ =>
            {
                if (!_apiHostOptions.ServeWorkstationAssets)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Passing,
                        "Workstation assets are not required by this host posture."));
                }

                var indexPath = Path.Combine(contentRootPath, "wwwroot", "workstation", "index.html");
                return ValueTask.FromResult(File.Exists(indexPath)
                    ? new RuntimeReadinessCheckResult(LifecycleCheckStatus.Passing, "Workstation bundle is available.")
                    : new RuntimeReadinessCheckResult(LifecycleCheckStatus.Failing, "Workstation bundle is unavailable."));
            }));

        services.AddSingleton<IRuntimeReadinessCheck>(new DelegateRuntimeReadinessCheck(
            "postgresql",
            "PostgreSQL",
            LifecycleCheckRequirement.Required,
            _ =>
            {
                if (!_databaseReadinessCompleted)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Pending,
                        "Database initialization is still running."));
                }

                var persistence = PersistenceConfigurationStatus.Evaluate();
                if (productionPosture && persistence.Mode != PersistenceStatusSnapshot.Configured)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Failing,
                        $"PERSISTENCE: {persistence.Mode.ToUpperInvariant()} — store domains without a database: " +
                        $"{string.Join(", ", persistence.MissingDomains)}. Set MERIDIAN_DATABASE_URL or the per-domain connection strings."));
                }

                return ValueTask.FromResult(persistence.Mode switch
                {
                    PersistenceStatusSnapshot.Configured => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Passing,
                        "Configured PostgreSQL dependencies are ready."),
                    PersistenceStatusSnapshot.Partial => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Degraded,
                        $"PERSISTENCE: PARTIAL — store domains without a database: {string.Join(", ", persistence.MissingDomains)}."),
                    _ => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Degraded,
                        "PERSISTENCE: NONE — every money-path store is in-memory and loses data on restart. " +
                        "Set MERIDIAN_DATABASE_URL to enable persistence.")
                });
            }));

        services.AddSingleton<IRuntimeReadinessCheck>(sp => new DelegateRuntimeReadinessCheck(
            "reporting-durability",
            "Reporting durability",
            productionPosture
                ? LifecycleCheckRequirement.Required
                : LifecycleCheckRequirement.Degradable,
            _ =>
            {
                var capability = sp.GetService<IReportingDeploymentReadinessService>()?.Evaluate();
                if (capability is null)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        productionPosture
                            ? LifecycleCheckStatus.Failing
                            : LifecycleCheckStatus.Degraded,
                        "Reporting deployment capability is unavailable."));
                }

                if (capability.IsReady)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Passing,
                        "Governance, release consistency, casework evidence, certified runs, scheduling, client documents, delivery workers, and receipts use the durable reporting authority."));
                }

                var missing = capability.Components
                    .Where(static component => !component.IsReady)
                    .Select(static component => component.ComponentId);
                return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                    productionPosture
                        ? LifecycleCheckStatus.Failing
                        : LifecycleCheckStatus.Degraded,
                    $"REPORTING: INCOMPLETE — unavailable durable components: {string.Join(", ", missing)}."));
            }));

        services.AddSingleton<IRuntimeReadinessCheck>(sp => new DelegateRuntimeReadinessCheck(
            "event-pipeline",
            "Event pipeline",
            LifecycleCheckRequirement.Required,
            _ =>
            {
                var utilization = sp.GetRequiredService<EventPipeline>().GetStatistics().QueueUtilization;
                var result = utilization switch
                {
                    >= 95 => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Failing,
                        "Event pipeline capacity is exhausted."),
                    >= 80 => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Degraded,
                        "Event pipeline capacity is constrained."),
                    _ => new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Passing,
                        "Event pipeline can accept work.")
                };
                return ValueTask.FromResult(result);
            }));
    }

    /// <summary>
    /// Evaluates the degraded-mode posture surfaced by /api/status: whether the configured
    /// streaming source delivers real or simulated market data, and which store domains run
    /// without persistence. Static build/configuration facts only — no provider is constructed.
    /// </summary>
    private static Meridian.Contracts.Api.DegradedModeStatus EvaluateDegradedMode(IServiceProvider sp)
    {
        var persistence = PersistenceConfigurationStatus.Evaluate();
        var marketDataMode = "unknown";
        string? marketDataDetail = null;

        try
        {
            var configStore = sp.GetService<Meridian.Application.UI.ConfigStore>();
            if (configStore is not null)
            {
                var config = configStore.Load();

                var simulatedSources = ResolveCandidateStreamingSources(config.DataSources, config.DataSource)
                    .Where(IsSimulatedSource)
                    .Select(s => s.ToString().ToLowerInvariant())
                    .Distinct()
                    .ToList();

                if (simulatedSources.Count > 0)
                {
                    marketDataMode = "simulated";
                    marketDataDetail =
                        $"Simulated streaming source(s) configured: {string.Join(", ", simulatedSources)}. " +
                        "'synthetic' generates deterministic synthetic data; 'ib' runs as a random-walk simulator " +
                        "in builds without the IBAPI reference and returns no historical bars.";
                }
                else
                {
                    marketDataMode = "live";
                    marketDataDetail = $"Streaming source '{config.DataSource.ToString().ToLowerInvariant()}'.";
                }
            }
        }
        catch (Exception ex)
        {
            marketDataDetail = $"Market data mode could not be evaluated: {ex.Message}";
        }

        return new Meridian.Contracts.Api.DegradedModeStatus
        {
            MarketDataMode = marketDataMode,
            MarketDataDetail = marketDataDetail,
            PersistenceMode = persistence.Mode,
            MissingPersistenceDomains = persistence.MissingDomains.ToArray()
        };
    }

    /// <summary>
    /// Resolves every <see cref="DataSourceKind"/> that <c>CollectorModeRunner</c> could feed the
    /// streaming pipeline with for the given configuration, so the degraded-mode probe can flag
    /// simulation fail-closed without constructing any provider.
    ///
    /// The top-level <paramref name="topLevelDataSource"/> is always included: without failover it
    /// is the sole streaming client, and with failover it is the emergency fallback taken when every
    /// rule provider fails to construct (CollectorModeRunner falls back to
    /// <c>CreateStreamingClient(ctx.Config.DataSource)</c> once <c>providerMap.Count == 0</c>). A
    /// provider named in a rule that has no registered streaming factory — e.g. a Yahoo failover
    /// source — always fails to construct, so a Synthetic top-level default would silently feed the
    /// pipeline even though the rule provider itself is not simulated.
    ///
    /// With failover enabled the first rule's primary + backup ids are added too (each mapped to its
    /// source's provider, or the top-level source when an id has no matching source entry).
    /// CollectorModeRunner does NOT consult Enabled/Type, so a disabled or historical synthetic
    /// backup named in a rule still counts.
    /// </summary>
    internal static IReadOnlyList<DataSourceKind> ResolveCandidateStreamingSources(
        DataSourcesConfig? failoverCfg, DataSourceKind topLevelDataSource)
    {
        var candidates = new List<DataSourceKind> { topLevelDataSource };
        var failoverRules = failoverCfg?.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
        if (failoverCfg?.EnableFailover == true && failoverRules.Length > 0)
        {
            var rule = failoverRules[0];
            var sources = failoverCfg.Sources ?? Array.Empty<DataSourceConfig>();
            foreach (var providerId in new[] { rule.PrimaryProviderId }.Concat(rule.BackupProviderIds))
            {
                var source = sources.FirstOrDefault(
                    s => string.Equals(s.Id, providerId, StringComparison.OrdinalIgnoreCase));
                candidates.Add(source?.Provider ?? topLevelDataSource);
            }
        }

        return candidates;
    }

    internal static bool IsSimulatedSource(DataSourceKind source) => source switch
    {
        DataSourceKind.Synthetic => true,
        DataSourceKind.IB => Meridian.Infrastructure.Adapters.InteractiveBrokers.IBMarketDataClient.IsSimulationBuild,
        _ => false
    };

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycleOperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposeRequested();
            if (_startFailed)
            {
                throw new InvalidOperationException(
                    "UiServer cannot retry startup after WebApplication.StartAsync began and failed. " +
                    "Dispose this instance and create a new server.");
            }
            if (_startCompleted)
            {
                throw new InvalidOperationException(
                    "UiServer startup is single-use and has already completed for this instance.");
            }

            var stopwatch = Stopwatch.StartNew();
            await EnsureDatabaseReadinessAsync(ct).ConfigureAwait(false);
            try
            {
                _applicationStartBegan = true;
                await _app.StartAsync(ct);
                if (_lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle)
                {
                    runtimeLifecycle.TransitionTo(RuntimeLifecycleState.EvaluatingReadiness, "evaluating-readiness");
                }

                if (_readinessEvaluator is not null)
                {
                    await _readinessEvaluator(ct).ConfigureAwait(false);
                }
                else if (_lifecycle is IRuntimeLifecycleControlPlane)
                {
                    var readiness = _app.Services.GetRequiredService<IRuntimeReadinessService>();
                    await readiness.EvaluateAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception startException)
            {
                _startFailed = true;
                await StopAfterFailedStartAsync(startException).ConfigureAwait(false);
                throw;
            }

            _startCompleted = true;
            _logger.LogInformation(
                "UiServer started on {Urls} in {ElapsedMs} ms",
                string.Join(", ", _app.Urls),
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _lifecycleOperationGate.Release();
        }
    }

    private async Task StopAfterFailedStartAsync(Exception startException)
    {
        using var cleanupTimeout = new CancellationTokenSource(FailedStartCleanupTimeout);
        Task? stopTask = null;
        try
        {
            stopTask = _app.StopAsync(cleanupTimeout.Token);
            await stopTask
                .WaitAsync(cleanupTimeout.Token)
                .ConfigureAwait(false);
            _applicationStopCompleted = true;
            _logger.LogWarning(
                startException,
                "UiServer startup failed after application start began; the application was stopped and this server instance cannot be restarted");
        }
        catch (Exception cleanupException)
        {
            if (stopTask is { IsCompleted: false })
                _ = ObserveLateFailedStartCleanupAsync(stopTask);

            _logger.LogError(
                cleanupException,
                "UiServer cleanup failed after startup error {StartupExceptionType}; the original startup error will be rethrown",
                startException.GetType().Name);
        }
    }

    private async Task ObserveLateFailedStartCleanupAsync(Task stopTask)
    {
        try
        {
            await stopTask.ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            _logger.LogError(
                cleanupException,
                "UiServer startup cleanup completed with a late failure after the bounded wait ended");
        }
    }

    private async Task EnsureDatabaseReadinessAsync(CancellationToken cancellationToken)
    {
        if (_databaseReadinessCompleted)
            return;

        await _databaseReadinessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_databaseReadinessCompleted)
                return;

            var readinessStopwatch = Stopwatch.StartNew();
            await LedgerStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await SecurityMasterStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await DirectLendingStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await AssetOperationsStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await FundAccountsStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await FundStructureStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await BankingStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            await MoneyMarketStartup.EnsureDatabaseReadyAsync(_app.Services, cancellationToken, _logger)
                .ConfigureAwait(false);
            var reportingStartup = _app.Services.GetService<IReportingMigrationStartup>();
            if (reportingStartup is not null)
            {
                await reportingStartup
                    .EnsureReadyAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            _databaseReadinessCompleted = true;
            readinessStopwatch.Stop();
            _logger.LogInformation(
                "UiServer readiness checks completed in {ElapsedMs} ms",
                readinessStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _databaseReadinessGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycleOperationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposeRequested();

            var stopwatch = Stopwatch.StartNew();
            await _app.StopAsync(ct);
            _applicationStopCompleted = true;
            _logger.LogInformation("UiServer stopped in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _lifecycleOperationGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completionToRun = null;
        Task disposeTask;
        lock (_disposeStateGate)
        {
            _disposeRequested = true;
            if (_disposeTask is null)
            {
                completionToRun = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completionToRun.Task;
            }

            disposeTask = _disposeTask;
        }

        if (completionToRun is not null)
            _ = CompleteDisposeAsync(completionToRun);

        return new ValueTask(disposeTask);
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.SetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.SetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _lifecycleOperationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        var failures = new List<Exception>();
        try
        {
            if (_applicationStartBegan && !_applicationStopCompleted)
            {
                var stopFailure = await RunBoundedLifecycleCleanupAsync(
                    cancellationToken => _app.StopAsync(cancellationToken),
                    "UiServer stop during disposal").ConfigureAwait(false);
                if (stopFailure is null)
                    _applicationStopCompleted = true;
                else
                    failures.Add(stopFailure);
            }

            var disposeFailure = await RunBoundedLifecycleCleanupAsync(
                _ => _app.DisposeAsync().AsTask(),
                "UiServer host disposal").ConfigureAwait(false);
            if (disposeFailure is not null)
                failures.Add(disposeFailure);

            try
            {
                if (_ownsLifecycle)
                    _lifecycle.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            _databaseReadinessGate.Dispose();
        }
        finally
        {
            _lifecycleOperationGate.Release();
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("UiServer disposal completed with failures.", failures);
    }

    private async Task<Exception?> RunBoundedLifecycleCleanupAsync(
        Func<CancellationToken, Task> operation,
        string operationName)
    {
        using var timeout = new CancellationTokenSource(FailedStartCleanupTimeout);
        Task? operationTask = null;
        try
        {
            operationTask = operation(timeout.Token);
            await operationTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (operationTask is { IsCompleted: false })
                _ = ObserveLateLifecycleCleanupAsync(operationTask, operationName);

            var failure = new TimeoutException(
                $"{operationName} did not complete within {FailedStartCleanupTimeout.TotalSeconds} seconds.");
            _logger.LogError(failure, "{OperationName} exceeded its shutdown deadline", operationName);
            return failure;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{OperationName} failed", operationName);
            return exception;
        }
    }

    private async Task ObserveLateLifecycleCleanupAsync(Task operationTask, string operationName)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{OperationName} completed with a late failure after the bounded wait ended",
                operationName);
        }
    }

    private void ThrowIfDisposeRequested()
    {
        lock (_disposeStateGate)
        {
            if (_disposeRequested)
                throw new ObjectDisposedException(nameof(UiServer));
        }
    }
}
