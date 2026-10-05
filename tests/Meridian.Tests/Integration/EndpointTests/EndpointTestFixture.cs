using System;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Application.DirectLending;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Identity.Auth;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Failover;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Storage;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Meridian.Identity;
using Xunit;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Per-class test fixture that sets up an in-memory ASP.NET Core test server
/// with all UI endpoints mapped. Uses TestServer for zero-network-overhead
/// request/response testing.
/// </summary>
public sealed class EndpointTestFixture : IAsyncLifetime
{
    private readonly Action? _afterServicesConfigured;
    private readonly Action<IServiceCollection>? _configureTestServices;
    private WebApplication? _app;
    private bool _appStopped;
    private string? _tempConfigDir;

    // This configuration deliberately has no environment provider. Missing settings remain
    // missing even if another host (or a developer's shell) configures the process differently.
    public IConfigurationRoot Configuration { get; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MDC_AUTH_MODE"] = "optional",
            ["MDC_DISABLE_RATE_LIMIT"] = "true",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["ASPNETCORE_ENVIRONMENT"] = "Test"
        })
        .Build();

    public HttpClient Client { get; private set; } = null!;
    public string DataRoot { get; private set; } = null!;
    public IServiceProvider Services => _app!.Services;

    public EndpointTestFixture()
    {
    }

    internal EndpointTestFixture(Action afterServicesConfigured)
    {
        _afterServicesConfigured = afterServicesConfigured;
    }

    internal EndpointTestFixture(Action<IServiceCollection> configureTestServices)
    {
        _configureTestServices = configureTestServices;
    }

    internal EndpointTestFixture(
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configureTestServices = null)
    {
        foreach (var (key, value) in settings)
        {
            Configuration[key] = value;
        }

        _configureTestServices = configureTestServices;
    }

    /// <summary>
    /// Header understood only by the in-memory test host (see <see cref="InitializeAsync"/>) that grants
    /// the listed <see cref="UserPermission"/> flag names to the current request.
    /// </summary>
    public const string TestPermissionsHeader = "X-Test-Permissions";

    /// <summary>
    /// Creates a non-redirecting client whose requests are pre-authorized with the supplied permissions
    /// via <see cref="TestPermissionsHeader"/>. The caller is responsible for disposing the client.
    /// </summary>
    public HttpClient CreatePermittedClient(params UserPermission[] permissions)
    {
        var client = CreateNoRedirectClient();
        client.DefaultRequestHeaders.Add(
            TestPermissionsHeader,
            string.Join(',', permissions.Select(permission => permission.ToString())));
        return client;
    }

    /// <summary>
    /// A signed-in operator holding exactly these permissions. Use this for routes that require a
    /// validated session; <see cref="CreatePermittedClient"/> supplies a permission snapshot with no
    /// actor, which such routes refuse by design.
    /// </summary>
    public HttpClient CreateSessionClient(params UserPermission[] permissions)
    {
        var client = CreatePermittedClient(permissions);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "session");
        return client;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by the in-memory TestServer that does NOT
    /// automatically follow redirects. Use this to inspect 3xx responses directly.
    /// The caller is responsible for disposing the returned client.
    /// </summary>
    public HttpClient CreateNoRedirectClient()
    {
        var testServer = _app!.GetTestServer();
        return new HttpClient(testServer.CreateHandler()) { BaseAddress = new Uri("http://localhost/") };
    }

    public async Task InitializeAsync()
    {
        try
        {
            await InitializeCoreAsync().ConfigureAwait(false);
        }
        catch (Exception initializationError)
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(
                    "Endpoint test fixture initialization and cleanup failed.",
                    initializationError,
                    cleanupError);
            }

            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"mdc-endpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempConfigDir);
        DataRoot = Path.Combine(_tempConfigDir, "data");
        Configuration["MERIDIAN_DATA_UPLOAD_ROOT"] ??= Path.Combine(DataRoot, "data-uploads");
        Configuration["Strategies:CoveredCall:DataRootOverride"] ??= DataRoot;

        var configPath = Path.Combine(_tempConfigDir, "appsettings.json");
        File.WriteAllText(configPath, GetMinimalConfig());

        // Create the status endpoint handlers with test data
        var statusHandlers = CreateTestStatusHandlers();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test",
            ContentRootPath = _tempConfigDir,
            Args = []
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(Configuration);
        builder.Services.AddSingleton(new AuthenticationConfiguration(Configuration));

        // Register the Ui.Shared ConfigStore wrapper (endpoints resolve this type).
        // The core ConfigStore (Application.UI.ConfigStore) is registered separately by AddMarketDataServices.
        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));
        builder.Services.AddUiSharedServices(statusHandlers, configPath, Configuration);
        builder.Services.RemoveAll<Meridian.Ui.Shared.Services.RiskRuleRuntimeOptions>();
        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.RiskRuleRuntimeOptions(
            Path.Combine(DataRoot, "risk-rules.json")));
        builder.Services.TryAddSingleton<StreamingFailoverRegistry>();
        builder.Services.RemoveAll<ProviderRegistry>();
        builder.Services.AddSingleton(_ => CreateTestProviderRegistry());
        builder.Services.RemoveAll<IProviderCatalog>();
        builder.Services.AddSingleton<IProviderCatalog>(services =>
        {
            var registry = services.GetRequiredService<ProviderRegistry>();
            return new RuntimeProviderCatalog(registry.GetProviderCatalog, registry.GetProviderCatalogEntry);
        });
        _afterServicesConfigured?.Invoke();
        builder.Services.RemoveAll<IDirectLendingService>();
        builder.Services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        // Endpoint behavior uses explicit fixture-owned account/structure fakes. Keep the host's
        // strict tenant options and request gates; local migration refusal is covered separately.
        var testAccounts = new InMemoryFundAccountService();
        builder.Services.RemoveAll<IFundAccountService>();
        builder.Services.RemoveAll<IAccountManagementService>();
        builder.Services.RemoveAll<IAccountQueryService>();
        builder.Services.AddSingleton<IFundAccountService>(testAccounts);
        builder.Services.AddSingleton<IAccountManagementService>(testAccounts);
        builder.Services.AddSingleton<IAccountQueryService>(testAccounts);
        builder.Services.RemoveAll<IFundStructureService>();
        builder.Services.AddSingleton<IFundStructureService>(sp =>
            new InMemoryFundStructureService(
                sp.GetRequiredService<IFundAccountService>(),
                persistencePath: null));
        _configureTestServices?.Invoke(builder.Services);

        _app = builder.Build();

        // Mirror the production UiServer pipeline order: session auth first so the API-key
        // gate can exempt session-authenticated browser requests. The X-Test-Auth marker
        // middleware emulates LoginSessionMiddleware's session validation (setting the
        // CurrentUser items), so it must also run before the API-key gate.
        _app.UseLoginSessionAuthentication();
        _app.Use(next => async context =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Auth", out var mode) &&
                StringComparer.Ordinal.Equals(mode.ToString(), "directlending-admin"))
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "endpoint-test";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Admin;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = RolePermissions.For(UserRole.Admin);
            }
            else if (context.Request.Headers.TryGetValue("X-Test-Auth", out mode) &&
                     StringComparer.Ordinal.Equals(mode.ToString(), "session"))
            {
                // Actor only: the permission set stays with X-Test-Permissions, so this models a
                // signed-in operator holding exactly what the test declares. Deliberately separate from
                // CreatePermittedClient, whose actor-less snapshot is itself a principal shape the
                // codebase has a rule about -- see
                // NonSessionPrincipalAuthorizationTests.PermissionSnapshotWithoutAnActor_*.
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "session-operator";
            }
            else if (context.Request.Headers.TryGetValue("X-Test-Auth", out mode) &&
                     StringComparer.Ordinal.Equals(mode.ToString(), "fund-accounting"))
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "fund-ops";
                context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = UserRole.Accounting;
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = RolePermissions.For(UserRole.Accounting);
            }

            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "endpoint-test-tenant";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "endpoint-test-tenant";

            await next(context);
        });
        _app.UseApiKeyAuthentication();
        _app.UseCookieCsrfProtection();

        // Test-only affordance: lets endpoint tests exercise permission-gated routes on the shared
        // host without a full login round-trip by declaring the caller's permissions through the
        // X-Test-Permissions header (comma-separated UserPermission flag names). This middleware is
        // wired ONLY into the in-memory test host; the production UiServer pipeline never includes it,
        // so it cannot be used to bypass authorization in a real deployment. Mirrors the
        // context.Items[CurrentUserPermissionsKey] injection used by ReferenceDataEndpointAuthorizationTests.
        _app.Use(async (HttpContext context, Func<Task> next) =>
        {
            if (context.Request.Headers.TryGetValue(TestPermissionsHeader, out var rawPermissions))
            {
                RolePermissions.TryParsePermissionNames(
                    rawPermissions.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    out var permissions,
                    out _);
                if (permissions != UserPermission.None)
                {
                    context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                }
            }

            await next();
        });
        // Mirrors the production pipeline's pre-binding guard for account-administration
        // mutations. Registered after the X-Test-Permissions stub so header-declared permissions
        // are visible to it, exactly as upstream authenticators' permissions are in production.
        _app.UseAccountAdministrationGuard();
        // Mirrors the production pipeline's pre-binding enforcement of declared mutation
        // authorization, in the same order relative to the permission-contributing middleware.
        _app.UseMutationAuthorizationGuard();

        var config = _app.Services.GetRequiredService<Meridian.Application.UI.ConfigStore>().Load();
        _app.MapPackagingEndpoints(config.DataRoot);
        _app.MapArchiveMaintenanceEndpoints();
        _app.MapUiEndpointsWithStatus(statusHandlers);

        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        var cleanupErrors = new List<Exception>();

        if (_app is not null && !_appStopped)
        {
            var applicationLifetime = _app.Services.GetRequiredService<IHostApplicationLifetime>();
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _app.StopAsync(stopTimeout.Token).ConfigureAwait(false);
                _appStopped = true;
            }
            catch (Exception stopError)
            {
                if (!applicationLifetime.ApplicationStopped.IsCancellationRequested)
                {
                    // The host may still be using its files and services. Retaining those
                    // resources is safer than destroying them
                    // beneath live hosted services; fail closed instead of poisoning cleanup order.
                    throw new AggregateException(
                        "Endpoint test fixture host did not stop; owned resources were retained.",
                        stopError);
                }

                // Host.StopAsync aggregates hosted-service stop errors after completing the stop
                // lifecycle. Once ApplicationStopped is signaled, cleanup is safe and must proceed
                // so a single faulty StopAsync implementation cannot poison every later class.
                _appStopped = true;
                cleanupErrors.Add(stopError);
            }
        }

        try
        {
            Client?.Dispose();
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(ex);
        }

        if (_app is not null)
        {
            try
            {
                await _app.DisposeAsync();
                _app = null;
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
            }
        }

        if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
        {
            try
            {
                Directory.Delete(_tempConfigDir, recursive: true);
                _tempConfigDir = null;
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
            }
        }

        if (cleanupErrors.Count > 0)
        {
            throw new AggregateException("Endpoint test fixture cleanup failed.", cleanupErrors);
        }
    }

    private static StatusEndpointHandlers CreateTestStatusHandlers()
    {
        Func<MetricsSnapshot> metricsProvider = () => new MetricsSnapshot(
            Published: 500, Dropped: 2, Integrity: 1, Trades: 400,
            DepthUpdates: 80, Quotes: 20, HistoricalBars: 100,
            EventsPerSecond: 500.0, TradesPerSecond: 400.0,
            DepthUpdatesPerSecond: 80.0, HistoricalBarsPerSecond: 100.0,
            DropRate: 0.004, AverageLatencyUs: 50.0, MinLatencyUs: 5.0,
            MaxLatencyUs: 200.0, LatencySampleCount: 500,
            Gc0Collections: 0, Gc1Collections: 0, Gc2Collections: 0,
            Gc0Delta: 0, Gc1Delta: 0, Gc2Delta: 0,
            MemoryUsageMb: 80.0, HeapSizeMb: 40.0,
            Timestamp: DateTimeOffset.UtcNow);

        Func<PipelineStatistics> pipelineProvider = () => new PipelineStatistics(
            PublishedCount: 500, DroppedCount: 2, ConsumedCount: 498,
            CurrentQueueSize: 5, PeakQueueSize: 100, QueueCapacity: 100000,
            QueueUtilization: 0.00005, AverageProcessingTimeUs: 25.0,
            TimeSinceLastFlush: TimeSpan.FromSeconds(1),
            Timestamp: DateTimeOffset.UtcNow);

        Func<IReadOnlyList<DepthIntegrityEvent>> integrityProvider =
            () => Array.Empty<DepthIntegrityEvent>();

        return new StatusEndpointHandlers(metricsProvider, pipelineProvider, integrityProvider);
    }

    private static ProviderRegistry CreateTestProviderRegistry()
    {
        var registry = new ProviderRegistry();
        registry.Register(new NoOpMarketDataClient(), priorityOverride: 100);
        registry.Register(new SyntheticHistoricalDataProvider(), priorityOverride: 10);
        registry.Register(new StooqHistoricalDataProvider(), priorityOverride: 20);
        registry.Register(new YahooFinanceHistoricalDataProvider(), priorityOverride: 30);
        return registry;
    }

    private string GetMinimalConfig()
    {
        var config = new
        {
            DataRoot,
            Compress = false,
            DataSource = "IB",
            Symbols = new[]
            {
                new
                {
                    Symbol = "SPY",
                    SubscribeTrades = true,
                    SubscribeDepth = true,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                },
                new
                {
                    Symbol = "AAPL",
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    DepthLevels = 10,
                    SecurityType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            },
            DataSources = new
            {
                Sources = new[]
                {
                    new
                    {
                        Id = "test-alpaca",
                        Name = "Test Alpaca",
                        Provider = "Alpaca",
                        Enabled = true,
                        Type = "RealTime",
                        Priority = 10,
                        Description = "Test Alpaca provider"
                    }
                },
                DefaultRealTimeSourceId = "test-alpaca",
                EnableFailover = true,
                FailoverTimeoutSeconds = 30,
                HealthCheckIntervalSeconds = 10,
                AutoRecover = true,
                FailoverRules = new[]
                {
                    new
                    {
                        Id = "test-rule-1",
                        PrimaryProviderId = "test-alpaca",
                        BackupProviderIds = new[] { "test-backup" },
                        FailoverThreshold = 3,
                        RecoveryThreshold = 5,
                        DataQualityThreshold = 70,
                        MaxLatencyMs = 100
                    }
                }
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "SPY" }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}
