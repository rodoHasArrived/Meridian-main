using System.Diagnostics;
using System.Net;
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

    private readonly WebApplication _app;
    private readonly ILogger<UiServer> _logger;
    private readonly IApplicationLifecycleCoordinator _lifecycle;
    private readonly bool _ownsLifecycle;
    private readonly ApiHostOptions _apiHostOptions;
    private readonly string _configPath;
    private readonly int _port;
    private volatile bool _databaseReadinessCompleted;

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
                    $"{TradeFillLedgerPostingHostOptions.SectionKey}:Enabled requires {LedgerStartup.ConnectionStringVariable} so accepted fills have an authoritative ledger target.");
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
        builder.Services.AddSingleton(new ExecutionOperatorControlOptions(
            Path.Combine(resolvedDataRoot, "execution", "controls"),
            FailClosedOnMissingOrCorruptSnapshot:
                ProductionServiceRegistrationPolicy.IsProductionComposition(builder.Services)));
        builder.Services.AddSingleton<ExecutionOperatorControlService>();
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

        // Execution layer — paper trading gateway wired for cockpit endpoints
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
        builder.Services.AddSingleton<IOrderGateway>(sp =>
            new Meridian.Execution.Adapters.OmsGovernedExecutionOrderGateway(
                sp.GetRequiredService<IExecutionGateway>()));
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
                operatorControls: sp.GetService<ExecutionOperatorControlService>(),
                auditTrail: sp.GetService<ExecutionAuditTrailService>(),
                portfolioState: portfolio,
                sessionPersistence: sp.GetService<PaperSessionPersistenceService>(),
                options: sp.GetRequiredService<OrderManagementSystemOptions>(),
                tradeEventPublisher: sp.GetService<ITradeEventPublisher>(),
                tradeFillHandoffFailureStore: sp.GetService<ITradeFillHandoffFailureStore>());
        });
        builder.Services.AddSingleton<IExecutionGateway>(sp =>
            new Meridian.Execution.PaperTradingGateway(
                sp.GetRequiredService<ILogger<Meridian.Execution.PaperTradingGateway>>(),
                sp.GetService<ISecurityMasterQueryService>(),
                sp.GetRequiredService<Meridian.Execution.Adapters.PaperTradingGatewayOptions>()));

        // Quant Lab — opt-in via configuration "QuantLab:Enabled". Off by default because the
        // engine compiles and executes arbitrary C# in-process. Production/customer distributions
        // fail closed until execution is moved behind a separately isolated worker boundary.
        var quantLabEnabled = builder.Configuration.GetValue<bool>("QuantLab:Enabled");
        ProductionServiceRegistrationPolicy.EnsureInProcessQuantLabIsAllowed(
            builder.Services,
            quantLabEnabled);
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

        ValidateAuthenticationTransportSecurity(builder.Environment, _apiHostOptions);
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
        FundAccountsStartup.EnsureDatabaseReady(_app.Services, _logger);
        FundStructureStartup.EnsureDatabaseReady(_app.Services, _logger);
        BankingStartup.EnsureDatabaseReady(_app.Services, _logger);
        MoneyMarketStartup.EnsureDatabaseReady(_app.Services, _logger);
        _databaseReadinessCompleted = true;
        readinessStopwatch.Stop();
        _logger.LogInformation("UiServer readiness checks completed in {ElapsedMs} ms", readinessStopwatch.ElapsedMilliseconds);

        // Wire Polly circuit breaker callbacks to CircuitBreakerStatusService
        ServiceCompositionRoot.InitializeCircuitBreakerCallbackRouter(_app.Services);

        // Enable session-based authentication middleware (optional in Development/Test, required elsewhere by default)
        _app.UseLoginSessionAuthentication();
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
                return Results.StatusCode(StatusCodes.Status403Forbidden);

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
                    return Results.BadRequest(new { error = "Invalid lifecycle shutdown request." });
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
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var authorizationFailure = ValidateLifecycleAuthorization(context);
            if (authorizationFailure is not null)
                return authorizationFailure;

            if (_lifecycle is not IRuntimeLifecycleControlPlane runtimeLifecycle ||
                runtimeLifecycle.ActiveShutdownOperation is not { } operation ||
                !string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            return Results.Ok(operation);
        });

        _app.MapGet("/api/system/shutdown/receipts/latest", async (HttpContext context) =>
        {
            if (!IsLoopbackRequest(context))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

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

            return receipt is null ? Results.NotFound() : Results.Ok(receipt);
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
            return Results.Unauthorized();
        }

        return EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
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

                var configured = HasConfiguredLocalPostgreSql();
                if (productionPosture && !configured)
                {
                    return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                        LifecycleCheckStatus.Failing,
                        "A required local PostgreSQL connection is not configured."));
                }

                return ValueTask.FromResult(new RuntimeReadinessCheckResult(
                    LifecycleCheckStatus.Passing,
                    configured
                        ? "Configured PostgreSQL dependencies are ready."
                        : "PostgreSQL is not required by this development posture."));
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

    private static bool HasConfiguredLocalPostgreSql()
    {
        string[] variables =
        [
            "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
            "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
            "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING"
        ];
        return variables.Any(variable =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await _app.StartAsync(ct);
        if (_lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle)
        {
            runtimeLifecycle.TransitionTo(RuntimeLifecycleState.EvaluatingReadiness, "evaluating-readiness");
            var readiness = _app.Services.GetRequiredService<IRuntimeReadinessService>();
            await readiness.EvaluateAsync(ct);
        }
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
