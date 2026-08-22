using System;
using System.Text.Json;
using Meridian.Application.Composition;
using Meridian.Application.DirectLending;
using Meridian.PortfolioRecords.FundAccounts;
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
using Meridian.TestSupport;
using Meridian.Ui.Shared;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
    private static readonly object ProviderCatalogBindingLock = new();
    private static EndpointTestFixture? s_activeProviderCatalogOwner;

    private readonly Action? _afterProviderCatalogBound;
    private readonly Action<IServiceCollection>? _configureTestServices;
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private bool _appStopped;
    private string? _tempConfigDir;
    private string? _originalAuthMode;
    private string? _originalApiKey;
    private string? _originalUsername;
    private string? _originalPasswordHash;
    private string? _originalUsers;
    private string? _originalDisableRateLimit;
    private string? _originalUseInMemoryGovernance;
    private string? _originalDotnetEnvironment;
    private string? _originalAspNetCoreEnvironment;
    private string? _originalLeanPath;
    private string? _originalLeanDataPath;
    private string? _originalLeanExportIntervalSeconds;
    private EndpointTestFixture? _previousProviderCatalogOwner;
    private Func<IReadOnlyList<ProviderCatalogEntry>>? _ownedRuntimeCatalogProvider;
    private Func<string, ProviderCatalogEntry?>? _ownedRuntimeCatalogEntryProvider;
    private bool _providerCatalogBindingActive;
    private readonly List<PostgresTestSchemaEnvironmentScope> _databaseSchemaScopes = [];
    private readonly Dictionary<string, string?> _databaseConnectionEnvironment =
        new(StringComparer.Ordinal);

    public HttpClient Client { get; private set; } = null!;
    public string DataRoot { get; private set; } = null!;
    public IServiceProvider Services => _app!.Services;

    public EndpointTestFixture()
    {
    }

    internal EndpointTestFixture(Action afterProviderCatalogBound)
    {
        _afterProviderCatalogBound = afterProviderCatalogBound;
    }

    internal EndpointTestFixture(Action<IServiceCollection> configureTestServices)
    {
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
        _originalAuthMode = Environment.GetEnvironmentVariable("MDC_AUTH_MODE");
        _originalApiKey = Environment.GetEnvironmentVariable("MDC_API_KEY");
        _originalUsername = Environment.GetEnvironmentVariable("MDC_USERNAME");
        _originalPasswordHash = Environment.GetEnvironmentVariable("MDC_PASSWORD_HASH");
        _originalUsers = Environment.GetEnvironmentVariable("MDC_USERS");
        _originalDisableRateLimit = Environment.GetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT");
        _originalUseInMemoryGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");
        _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        _originalLeanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
        _originalLeanDataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH");
        _originalLeanExportIntervalSeconds = Environment.GetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS");
        CaptureDatabaseConnectionEnvironment();
        MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "optional");
        Environment.SetEnvironmentVariable("MDC_API_KEY", null);
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        Environment.SetEnvironmentVariable("MDC_USERS", null);
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        // All TestServer requests share a null RemoteIpAddress which maps to the "unknown"
        // partition key; 10 requests would exhaust the production limit immediately.
        Environment.SetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT", "true");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("LEAN_PATH", null);
        Environment.SetEnvironmentVariable("LEAN_DATA_PATH", null);
        Environment.SetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS", null);

        await ConfigureIsolatedDatabaseSchemasAsync().ConfigureAwait(false);

        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"mdc-endpoint-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempConfigDir);
        DataRoot = Path.Combine(_tempConfigDir, "data");

        var configPath = Path.Combine(_tempConfigDir, "appsettings.json");
        File.WriteAllText(configPath, GetMinimalConfig());

        // Create the status endpoint handlers with test data
        var statusHandlers = CreateTestStatusHandlers();

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Test";

        // Register the Ui.Shared ConfigStore wrapper (endpoints resolve this type).
        // The core ConfigStore (Application.UI.ConfigStore) is registered separately by AddMarketDataServices.
        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.ConfigStore(configPath));
        // This host deliberately exercises file/in-memory workstation seams. A non-null whitespace
        // sentinel prevents AddWorkstationSharedServices from falling back to the process-wide
        // ledger database for Reporting while leaving production configuration semantics unchanged.
        Environment.SetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING", " ");
        builder.Services.AddUiSharedServices(statusHandlers, configPath);
        builder.Services.RemoveAll<Meridian.Ui.Shared.Services.RiskRuleRuntimeOptions>();
        builder.Services.AddSingleton(new Meridian.Ui.Shared.Services.RiskRuleRuntimeOptions(
            Path.Combine(DataRoot, "risk-rules.json")));
        builder.Services.TryAddSingleton<StreamingFailoverRegistry>();
        builder.Services.RemoveAll<ProviderRegistry>();
        var testProviderRegistry = CreateTestProviderRegistry();
        builder.Services.AddSingleton(testProviderRegistry);
        BindProviderCatalogToTestRegistry(testProviderRegistry);
        _afterProviderCatalogBound?.Invoke();
        builder.Services.RemoveAll<IDirectLendingService>();
        builder.Services.AddSingleton<IDirectLendingService, InMemoryDirectLendingService>();
        builder.Services.RemoveAll<IFundStructureService>();
        builder.Services.AddSingleton<IFundStructureService>(sp =>
            new InMemoryFundStructureService(
                sp.GetRequiredService<IFundAccountService>(),
                persistencePath: null));
        _configureTestServices?.Invoke(builder.Services);

        _app = builder.Build();
        await EnsureDatabaseSchemasReadyAsync(_app.Services).ConfigureAwait(false);

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
                    // The host may still be using schemas, files, ambient configuration, and the
                    // catalog binding. Retaining those resources is safer than destroying them
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

        RestoreProviderCatalogCallbacksIfOwned();

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

        for (var index = _databaseSchemaScopes.Count - 1; index >= 0; index--)
        {
            try
            {
                await _databaseSchemaScopes[index].DisposeAsync().ConfigureAwait(false);
                _databaseSchemaScopes.RemoveAt(index);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
            }
        }

        try
        {
            RestoreProcessEnvironment();
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(ex);
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

    private async Task ConfigureIsolatedDatabaseSchemasAsync()
    {
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            "MERIDIAN_LEDGER_SCHEMA",
            "endpoint_ledger").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING",
            "MERIDIAN_ASSET_OPERATIONS_SCHEMA",
            "endpoint_asset_ops").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
            "MERIDIAN_DIRECT_LENDING_SCHEMA",
            "endpoint_direct_lending",
            "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
            "MERIDIAN_FUND_ACCOUNTS_SCHEMA",
            "endpoint_fund_accounts").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
            "MERIDIAN_FUND_STRUCTURE_SCHEMA",
            "endpoint_fund_structure").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "MERIDIAN_REPORTING_SCHEMA",
            "endpoint_reporting",
            "MERIDIAN_LEDGER_CONNECTION_STRING").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
            "MERIDIAN_SCOPED_ACCESS_SCHEMA",
            "endpoint_scoped_access").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            "MERIDIAN_SECURITY_MASTER_SCHEMA",
            "endpoint_security").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_BANKING_CONNECTION_STRING",
            "MERIDIAN_BANKING_SCHEMA",
            "endpoint_banking").ConfigureAwait(false);
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_MONEY_MARKET_CONNECTION_STRING",
            "MERIDIAN_MONEY_MARKET_SCHEMA",
            "endpoint_money_market").ConfigureAwait(false);
    }

    private static async Task EnsureDatabaseSchemasReadyAsync(IServiceProvider services)
    {
        // A direct TestServer host does not run HostStartup's database-initialization hosted
        // service. Mirror its production order so every PostgreSQL adapter selected by
        // AddUiSharedServices uses a migrated fixture-owned schema.
        await LedgerStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await SecurityMasterStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await DirectLendingStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await AssetOperationsStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await FundAccountsStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await FundStructureStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await BankingStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
        await MoneyMarketStartup.EnsureDatabaseReadyAsync(services).ConfigureAwait(false);
    }

    private async Task AddDatabaseSchemaScopeAsync(
        string connectionStringEnvironmentVariable,
        string schemaEnvironmentVariable,
        string schemaPrefix,
        string? fallbackConnectionStringEnvironmentVariable = null)
    {
        var scope = await PostgresTestSchemaEnvironmentScope.CreateIfConfiguredAsync(
                connectionStringEnvironmentVariable,
                schemaEnvironmentVariable,
                schemaPrefix,
                fallbackConnectionStringEnvironmentVariable)
            .ConfigureAwait(false);
        if (scope is not null)
        {
            _databaseSchemaScopes.Add(scope);
        }
    }

    private void CaptureDatabaseConnectionEnvironment()
    {
        foreach (var variable in MeridianDatabaseEnvironment.PropagatedConnectionStringVariables.Concat(
                     [
                         "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
                         "MERIDIAN_REPORTING_CONNECTION_STRING"
                     ]))
        {
            _databaseConnectionEnvironment[variable] =
                Environment.GetEnvironmentVariable(variable);
        }
    }

    private void RestoreProcessEnvironment()
    {
        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", _originalAuthMode);
        Environment.SetEnvironmentVariable("MDC_API_KEY", _originalApiKey);
        Environment.SetEnvironmentVariable("MDC_USERNAME", _originalUsername);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", _originalPasswordHash);
        Environment.SetEnvironmentVariable("MDC_USERS", _originalUsers);
        Environment.SetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT", _originalDisableRateLimit);
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
        Environment.SetEnvironmentVariable("LEAN_PATH", _originalLeanPath);
        Environment.SetEnvironmentVariable("LEAN_DATA_PATH", _originalLeanDataPath);
        Environment.SetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS", _originalLeanExportIntervalSeconds);
        foreach (var pair in _databaseConnectionEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
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

    private void BindProviderCatalogToTestRegistry(ProviderRegistry registry)
    {
        // ProviderCatalog is process-wide. Production composition binds it to the host's
        // IServiceProvider, but this fixture replaces ProviderRegistry after composition.
        // Use an immutable catalog snapshot so a preceding host cannot leave callbacks that
        // resolve services from its disposed container, and so this fixture does not publish
        // callbacks tied to its own disposable registry.
        var entries = registry.GetProviderCatalog().ToArray();
        _ownedRuntimeCatalogProvider = () => entries;
        _ownedRuntimeCatalogEntryProvider = providerId => entries.FirstOrDefault(entry =>
            string.Equals(entry.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        lock (ProviderCatalogBindingLock)
        {
            var candidateOwner = s_activeProviderCatalogOwner;
            var candidateStillOwnsPair =
                candidateOwner is { _providerCatalogBindingActive: true } &&
                ReferenceEquals(
                    ProviderCatalog.RuntimeCatalogProvider,
                    candidateOwner._ownedRuntimeCatalogProvider) &&
                ReferenceEquals(
                    ProviderCatalog.RuntimeCatalogEntryProvider,
                    candidateOwner._ownedRuntimeCatalogEntryProvider);
            if (!candidateStillOwnsPair && candidateOwner is not null)
            {
                candidateOwner._providerCatalogBindingActive = false;
                s_activeProviderCatalogOwner = null;
            }

            _previousProviderCatalogOwner = candidateStillOwnsPair
                ? candidateOwner
                : null;
            ProviderCatalog.InitializeFromRegistry(
                _ownedRuntimeCatalogProvider,
                _ownedRuntimeCatalogEntryProvider);
            _providerCatalogBindingActive = true;
            s_activeProviderCatalogOwner = this;
        }
    }

    private void RestoreProviderCatalogCallbacksIfOwned()
    {
        lock (ProviderCatalogBindingLock)
        {
            if (!_providerCatalogBindingActive)
            {
                return;
            }

            _providerCatalogBindingActive = false;
            if (!ReferenceEquals(s_activeProviderCatalogOwner, this))
            {
                return;
            }

            var callbacksStillOwned =
                ReferenceEquals(ProviderCatalog.RuntimeCatalogProvider, _ownedRuntimeCatalogProvider) &&
                ReferenceEquals(ProviderCatalog.RuntimeCatalogEntryProvider, _ownedRuntimeCatalogEntryProvider);
            if (!callbacksStillOwned)
            {
                // A later non-fixture owner replaced the process-wide callbacks. Do not clobber it.
                s_activeProviderCatalogOwner = null;
                return;
            }

            var priorOwner = _previousProviderCatalogOwner;
            while (priorOwner is not null && !priorOwner._providerCatalogBindingActive)
            {
                priorOwner = priorOwner._previousProviderCatalogOwner;
            }

            if (priorOwner is not null)
            {
                ProviderCatalog.InitializeFromRegistry(
                    priorOwner._ownedRuntimeCatalogProvider!,
                    priorOwner._ownedRuntimeCatalogEntryProvider!);
                s_activeProviderCatalogOwner = priorOwner;
            }
            else
            {
                // Never resurrect arbitrary callbacks from a predecessor host: they may close over
                // a disposed service provider. Only another active EndpointTestFixture is restorable.
                ProviderCatalog.RuntimeCatalogProvider = null;
                ProviderCatalog.RuntimeCatalogEntryProvider = null;
                s_activeProviderCatalogOwner = null;
            }

            _ownedRuntimeCatalogProvider = null;
            _ownedRuntimeCatalogEntryProvider = null;
            _previousProviderCatalogOwner = null;
        }
    }

    private static string GetMinimalConfig()
    {
        var config = new
        {
            DataRoot = "data",
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
