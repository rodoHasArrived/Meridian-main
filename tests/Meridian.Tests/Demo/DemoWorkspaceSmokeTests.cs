using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.TestSupport;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Demo;

/// <summary>
/// Demo-smoke: seeds the isolated demo workspace, boots the workstation host against the seeded demo
/// root, and asserts the key screens render populated — not empty and not HTTP 501. A broken first-run
/// fails this check instead of a first evaluation.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public sealed class DemoWorkspaceSmokeTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly EnvironmentSnapshot _env = new();
    private WebApplication? _app;
    private bool _appStopped;
    private HttpClient? _client;
    private string? _tempRoot;
    private ProviderCatalogTestLease? _providerCatalogLease;
    private readonly List<PostgresTestSchemaEnvironmentScope> _databaseSchemaScopes = [];

    public async Task InitializeAsync()
    {
        try
        {
            await InitializeCoreAsync();
        }
        catch (Exception initializationError)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(
                    "Demo smoke initialization and cleanup failed.",
                    initializationError,
                    cleanupError);
            }

            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        _env.Capture(
            "MDC_AUTH_MODE",
            "MDC_API_KEY",
            "MDC_ANONYMOUS_ROLE",
            "MDC_ANONYMOUS_TENANT",
            "MDC_USERNAME",
            "MDC_PASSWORD_HASH",
            "MDC_USERS",
            "MDC_DISABLE_RATE_LIMIT",
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            DemoWorkspaceLayout.DemoModeEnvironmentVariable,
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT",
            "LEAN_PATH",
            "LEAN_DATA_PATH",
            "LEAN_EXPORT_INTERVAL_SECONDS");
        _env.Capture(MeridianDatabaseEnvironment.PropagatedConnectionStringVariables.ToArray());
        _env.Capture(
            "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
            "MERIDIAN_REPORTING_CONNECTION_STRING");
        MeridianDatabaseEnvironment.ApplyUnifiedDatabaseUrl();

        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "optional");
        Environment.SetEnvironmentVariable("MDC_API_KEY", null);
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_ROLE", nameof(UserRole.Admin));
        Environment.SetEnvironmentVariable("MDC_ANONYMOUS_TENANT", null);
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        Environment.SetEnvironmentVariable("MDC_USERS", null);
        Environment.SetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT", "true");
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        Environment.SetEnvironmentVariable(DemoWorkspaceLayout.DemoModeEnvironmentVariable, "true");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("LEAN_PATH", null);
        Environment.SetEnvironmentVariable("LEAN_DATA_PATH", null);
        Environment.SetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS", null);

        await ConfigureIsolatedDatabaseSchemasAsync();

        _tempRoot = Path.Combine(Path.GetTempPath(), "meridian-demo-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var baseDataRoot = Path.Combine(_tempRoot, "data");

        // Seed the isolated demo workspace exactly as `--seed-demo --seed-only` would.
        var seeder = new DemoWorkspaceSeeder(baseDataRoot);
        await seeder.SeedAsync();

        // Point a workstation host at the seeded demo root via the same generated demo config the host
        // uses, then boot it and assert the served screens are populated.
        var demoConfigPath = Path.Combine(seeder.DemoRoot, "appsettings.demo.json");
        await File.WriteAllTextAsync(
            demoConfigPath,
            $"{{\"dataRoot\":{JsonSerializer.Serialize(seeder.DemoRoot)}}}");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Test";
        builder.Services.AddSingleton(new ConfigStore(demoConfigPath));
        // Keep the demo host on its intended file-backed Reporting seams even when the certification
        // process exports an ambient ledger database. Whitespace is non-null for the fallback lookup
        // and still treated as unconfigured by the composition branch.
        Environment.SetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING", " ");
        builder.Services.AddUiSharedServices(demoConfigPath);

        _app = builder.Build();
        _providerCatalogLease = ProviderCatalogTestLease.Capture(_app.Services);
        _app.UseLoginSessionAuthentication();
        _app.MapWorkstationEndpoints(ServerJsonOptions);
        _app.MapFirstRunEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [Theory]
    [InlineData("/api/workstation/session")]
    [InlineData("/api/workstation/workflows/presets")]
    [InlineData("/api/workstation/settings/feature-capabilities")]
    [InlineData("/api/workstation/first-run/")]
    public async Task ReadOnlyDemoBootstrapRoutes_AcceptTheSeededLocalOperator(string route)
    {
        var response = await _client!.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReconciliationBreakQueueScreen_RendersSeededCaseworkWithProvenance()
    {
        var response = await _client!.GetAsync("/api/workstation/reconciliation/break-queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("SAMPLE-BRK-001");
        body.Should().Contain(DemoTenantBlueprint.SeededSourceSystem);
    }

    [Fact]
    public async Task StrategyScreen_IsNotEmptyOrUnimplemented()
    {
        var response = await _client!.GetAsync("/api/workstation/strategy");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotImplemented);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain(DemoTenantBlueprint.StrategyName);
        }
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
                await _app.StopAsync(stopTimeout.Token);
                _appStopped = true;
            }
            catch (Exception stopError)
            {
                if (!applicationLifetime.ApplicationStopped.IsCancellationRequested)
                {
                    throw new AggregateException(
                        "Demo smoke host did not stop; owned resources were retained.",
                        stopError);
                }

                _appStopped = true;
                cleanupErrors.Add(stopError);
            }
        }

        try
        {
            _providerCatalogLease?.Dispose();
            _providerCatalogLease = null;
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(ex);
        }

        try
        {
            _client?.Dispose();
            _client = null;
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
                await _databaseSchemaScopes[index].DisposeAsync();
                _databaseSchemaScopes.RemoveAt(index);
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
            }
        }

        try
        {
            _env.Restore();
        }
        catch (Exception ex)
        {
            cleanupErrors.Add(ex);
        }

        if (_tempRoot is not null && Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
                _tempRoot = null;
            }
            catch (Exception ex)
            {
                cleanupErrors.Add(ex);
            }
        }

        if (cleanupErrors.Count > 0)
        {
            throw new AggregateException("Demo workspace cleanup failed.", cleanupErrors);
        }
    }

    private async Task ConfigureIsolatedDatabaseSchemasAsync()
    {
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            "MERIDIAN_LEDGER_SCHEMA",
            "demo_ledger");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING",
            "MERIDIAN_ASSET_OPERATIONS_SCHEMA",
            "demo_asset_ops");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
            "MERIDIAN_DIRECT_LENDING_SCHEMA",
            "demo_direct_lending",
            "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING",
            "MERIDIAN_FUND_ACCOUNTS_SCHEMA",
            "demo_fund_accounts");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING",
            "MERIDIAN_FUND_STRUCTURE_SCHEMA",
            "demo_fund_structure");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "MERIDIAN_REPORTING_SCHEMA",
            "demo_reporting",
            "MERIDIAN_LEDGER_CONNECTION_STRING");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
            "MERIDIAN_SCOPED_ACCESS_SCHEMA",
            "demo_scoped_access");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING",
            "MERIDIAN_SECURITY_MASTER_SCHEMA",
            "demo_security");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_BANKING_CONNECTION_STRING",
            "MERIDIAN_BANKING_SCHEMA",
            "demo_banking");
        await AddDatabaseSchemaScopeAsync(
            "MERIDIAN_MONEY_MARKET_CONNECTION_STRING",
            "MERIDIAN_MONEY_MARKET_SCHEMA",
            "demo_money_market");
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
            fallbackConnectionStringEnvironmentVariable);
        if (scope is not null)
        {
            _databaseSchemaScopes.Add(scope);
        }
    }

    private sealed class EnvironmentSnapshot
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public void Capture(params string[] names)
        {
            foreach (var name in names)
            {
                _values[name] = Environment.GetEnvironmentVariable(name);
            }
        }

        public void Restore()
        {
            foreach (var pair in _values)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}

[Trait("Category", "Integration")]
[Collection("Sequential")]
public sealed class DemoWorkspaceSmokeFixtureLifecycleTests
{
    [Fact]
    public async Task Dispose_StopsHostClearsOwnedCatalogAndRestoresDedicatedConnections()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        var originalReportingConnection =
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING");
        var originalDirectLendingConnection =
            Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_CONNECTION_STRING");
        var fixture = new DemoWorkspaceSmokeTests();

        try
        {
            await fixture.InitializeAsync();
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING")
                .Should().Be(" ");

            await fixture.DisposeAsync();

            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            var readCatalog = () => ProviderCatalog.GetAll();
            readCatalog.Should().NotThrow();
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING")
                .Should().Be(originalReportingConnection);
            Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_CONNECTION_STRING")
                .Should().Be(originalDirectLendingConnection);
        }
        finally
        {
            await fixture.DisposeAsync();
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
            Environment.SetEnvironmentVariable(
                "MERIDIAN_REPORTING_CONNECTION_STRING",
                originalReportingConnection);
            Environment.SetEnvironmentVariable(
                "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
                originalDirectLendingConnection);
        }
    }
}
