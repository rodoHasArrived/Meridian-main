using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Credentials;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Meridian;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.DataIntegration.Reconciliation;
using Meridian.Contracts.Reconciliation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Meridian.Tests.Ui;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class BrokerageConnectionEndpointsTests
{
    [Fact]
    public async Task GetAlpacaStatus_WhenUserLacksViewTrades_ReturnsForbidden()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(
            _ => { },
            UserPermission.ManageCredentials);

        var response = await app.GetTestClient().GetAsync(AlpacaRoute("/status"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAlpacaConnect_VerifiesAccountAndReturnsMaskedStatus()
    {
        using var env = AlpacaEnvScope.Clear();
        HttpRequestMessage? capturedRequest = null;
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(new CapturingStubHandler(
                request => capturedRequest = request,
                new StringContent("{\"account_number\":\"" + OwnedAccount + "\"}", Encoding.UTF8, "application/json"))));
        });

        var response = await app.GetTestClient().PostAsync(
            AlpacaRoute("/connect"),
            JsonContent(new { keyId = "endpoint-key", secretKey = "endpoint-secret", environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadAsync<BrokerageConnectionStatusDto>(response);
        status.ProviderId.Should().Be("alpaca");
        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.Environment.Should().Be("paper");
        status.ExternalAccountId.Should().Be(OwnedAccount);
        status.MaskedKeyId.Should().NotContain("endpoint-key");
        status.LastError.Should().BeNull();
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName).Should().NotBe("endpoint-key");
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName).Should().NotBe("endpoint-secret");
        var store = (IScopedProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        var stored = await store.ReadScopedAsync("alpaca", new ProviderCredentialScope(TestTenant, OwnedConnection, OwnedAccount, "paper"));
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be("endpoint-key");
        (await store.ReadForProviderAsync("alpaca")).Should().BeNull("the provider-wide record must stay untouched");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://paper-api.alpaca.markets/v2/account");
    }

    [Fact]
    public async Task PostAlpacaConnect_TradeDeskRolePermissions_ReturnsForbidden()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(_ => { }, RolePermissions.For(UserRole.TradeDesk));

        var response = await app.GetTestClient().PostAsync(
            AlpacaRoute("/connect"),
            JsonContent(new { keyId = "trade-desk-key", secretKey = "trade-desk-secret", environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAlpacaConnection_WhenUserLacksManageCredentials_ReturnsForbidden()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(
            _ => { },
            UserPermission.ViewTrades);

        var response = await app.GetTestClient().DeleteAsync(AlpacaRoute());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UiServer_RegistersBrokerageConnectionServices_ForMappedBrokerageRoutes()
    {
        using var governance = UiServerDevelopmentEnvironmentScope.Enable();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "ui-server-brokerage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(root));

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var app = GetServerApp(server);

            app.Services.GetService<BrokerageConnectionOptions>().Should().NotBeNull();
            app.Services.GetService<BrokerageConnectionService>().Should().NotBeNull();
            app.Services.GetService<AlpacaBrokerageConnectionService>().Should().NotBeNull();
            app.Services.GetService<ProviderConnectionLifecycleService>().Should().NotBeNull();
            app.Services.GetService<IProviderCredentialStore>().Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UiServer_BackfillCoordinator_UsesRegisteredHistoricalProviders()
    {
        using var governance = UiServerDevelopmentEnvironmentScope.Enable();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "ui-server-backfill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(root, dataSource: "Synthetic"));

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var app = GetServerApp(server);

            app.Services.GetRequiredService<BackfillCoordinator>().Should().NotBeNull();
            var registry = app.Services.GetRequiredService<ProviderRegistry>();
            var providerNames = registry.GetBackfillProviders()
                .Select(static provider => provider.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            providerNames.Should().Contain(name =>
                string.Equals(name, "stooq", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "yahoo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "synthetic", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UiServer_RegistersLifecycleRoutes_ForManagedShutdown()
    {
        using var governance = UiServerDevelopmentEnvironmentScope.Enable();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "ui-server-lifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(root));

        try
        {
            // The endpoint data source only materializes once the pipeline builds, so the server
            // must start — but on 127.0.0.1:0, where Kestrel assigns a free port atomically. The
            // old version pre-reserved a port with a bind-release-rebind helper (Kestrel refuses
            // the default localhost URL with port 0), and that raced parallel tests for the
            // released port, making this test flake under full-suite load.
            await using var server = new UiServer(
                configPath,
                port: 0,
                apiHostOptions: new ApiHostOptions { Urls = ["http://127.0.0.1:0"] });
            await server.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var app = GetServerApp(server);

            var routes = app.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText)
                .ToArray();

            routes.Should().Contain("/api/system/lifecycle");
            routes.Should().Contain("/api/system/shutdown");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UiServer_ResolvesReconciliationServices_FromCompositionRoot()
    {
        using var governance = UiServerDevelopmentEnvironmentScope.Enable();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "ui-server-reconciliation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, CreateMinimalConfig(root));

        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var app = GetServerApp(server);

            app.Services.GetRequiredService<ICanonicalStatementStore>().Should().NotBeNull();
            app.Services.GetRequiredService<IReconciliationCaseStore>().Should().NotBeNull();
            app.Services.GetRequiredService<IReconciliationBreakStore>().Should().NotBeNull();
            app.Services.GetRequiredService<IBrokerStatementService>().Should().NotBeNull();
            app.Services.GetRequiredService<IStatementRunWorkflowService>().Should().NotBeNull();
            app.Services.GetRequiredService<IReconciliationApiService>().Should().NotBeNull();
            app.Services.GetRequiredService<IStatementFetchScheduleStore>().Should().NotBeNull();
            app.Services.GetRequiredService<StatementFetchScheduleRunner>().Should().NotBeNull();
            var scheduler = app.Services.GetRequiredService<StatementFetchSchedulerService>();
            app.Services.GetServices<IHostedService>().Should().ContainSingle(
                hostedService => ReferenceEquals(hostedService, scheduler),
                "the primary UiServer must start the existing persisted statement-schedule worker");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AlpacaConnectAndRevoke_RetainAuthenticatedAuditActor()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => { }, new StringContent("{\"account_number\":\"" + OwnedAccount + "\"}", Encoding.UTF8, "application/json")))));
        var client = app.GetTestClient();
        var response = await client.PostAsync(AlpacaRoute("/connect"), JsonContent(new
        {
            keyId = "audit-key",
            secretKey = "audit-secret",
            environment = "paper",
            requestedBy = "forged-operator"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(AlpacaRoute())).StatusCode.Should().Be(HttpStatusCode.OK);
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        var path = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var audit = (await File.ReadAllLinesAsync(path)).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        audit.Should().HaveCount(3);
        audit.Should().OnlyContain(entry => entry.GetProperty("actor").GetString() == "brokerage-test-operator");
    }

    [Fact]
    public async Task AlpacaMutation_WithoutActorRejectsConnectAndRevoke()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(_ => { }, includeActor: false);
        var client = app.GetTestClient();
        (await client.PostAsync(AlpacaRoute("/connect"), JsonContent(new
        {
            keyId = "audit-key",
            secretKey = "audit-secret",
            requestedBy = "forged-operator"
        }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(AlpacaRoute())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        File.Exists(app.Services.GetRequiredService<IProviderCredentialStore>().VaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task AlpacaRoutes_WithoutConnectionId_UseTheTenantsSingleOwnedPaperConnection()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => { }, new StringContent("{\"account_number\":\"" + OwnedAccount + "\"}", Encoding.UTF8, "application/json")))));
        var client = app.GetTestClient();

        var connect = await client.PostAsync("/api/brokerage-connections/alpaca/connect", JsonContent(new
        {
            keyId = "endpoint-key",
            secretKey = "endpoint-secret",
            environment = "paper"
        }));

        connect.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<BrokerageConnectionStatusDto>(connect)).State.Should().Be(BrokerageConnectionStateDto.Connected);
        var store = (IScopedProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        (await store.ReadScopedAsync("alpaca", new ProviderCredentialScope(TestTenant, OwnedConnection, OwnedAccount, "paper")))!
            .Get("KeyId").Should().Be("endpoint-key");
        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
        var status = await ReadAsync<BrokerageConnectionStatusDto>(await client.GetAsync("/api/brokerage-connections/alpaca/status"));
        status.ExternalAccountId.Should().Be(OwnedAccount);
        (await client.GetAsync("/api/brokerage-connections/alpaca/status?connectionId=")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync("/api/brokerage-connections/alpaca")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AlpacaRoutes_ForTenantWithoutOwnedConnection_StoreNothingAndNeverUseAnotherTenantsRecord()
    {
        using var env = AlpacaEnvScope.Clear();
        var providerCalls = 0;
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => providerCalls++, new StringContent("{\"account_number\":\"" + OwnedAccount + "\"}", Encoding.UTF8, "application/json")))),
            tenantId: "tenant-without-connections");
        var client = app.GetTestClient();

        var statusResponse = await client.GetAsync("/api/brokerage-connections/alpaca/status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadAsync<BrokerageConnectionStatusDto>(statusResponse);
        status.IsConfigured.Should().BeFalse();
        status.ExternalAccountId.Should().BeNull();
        status.Warnings.Should().ContainSingle().Which.Should().Contain("No owned Alpaca paper connection");
        (await client.PostAsync("/api/brokerage-connections/alpaca/connect", JsonContent(new
        {
            keyId = "endpoint-key",
            secretKey = "endpoint-secret",
            environment = "paper"
        }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync("/api/brokerage-connections/alpaca")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(AlpacaRoute("/status"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        providerCalls.Should().Be(0);
        File.Exists(app.Services.GetRequiredService<IProviderCredentialStore>().VaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task AlpacaRoutes_ForAnotherTenantsConnection_AreForbidden()
    {
        using var env = AlpacaEnvScope.Clear();
        var providerCalls = 0;
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => providerCalls++, new StringContent("{\"account_number\":\"PA-FOREIGN\"}", Encoding.UTF8, "application/json")))));
        var client = app.GetTestClient();
        const string foreign = "?connectionId=" + ForeignConnection;

        (await client.GetAsync("/api/brokerage-connections/alpaca/status" + foreign)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/brokerage-connections/alpaca/connect" + foreign, JsonContent(new
        {
            keyId = "endpoint-key",
            secretKey = "endpoint-secret",
            environment = "paper"
        }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync("/api/brokerage-connections/alpaca" + foreign)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        providerCalls.Should().Be(0);
        File.Exists(app.Services.GetRequiredService<IProviderCredentialStore>().VaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task PostAlpacaConnect_ForAnotherAccountOrEnvironment_ReturnsBadRequestWithoutStoring()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => { }, new StringContent("{\"account_number\":\"PA-SOMEONE-ELSE\"}", Encoding.UTF8, "application/json")))));
        var client = app.GetTestClient();

        var wrongAccount = await client.PostAsync(AlpacaRoute("/connect"), JsonContent(new
        {
            keyId = "endpoint-key",
            secretKey = "endpoint-secret",
            environment = "paper"
        }));
        var wrongEnvironment = await client.PostAsync(AlpacaRoute("/connect"), JsonContent(new
        {
            keyId = "endpoint-key",
            secretKey = "endpoint-secret",
            environment = "live"
        }));

        wrongAccount.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongAccount.Content.ReadAsStringAsync()).Should().NotContain("endpoint-secret");
        wrongEnvironment.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        File.Exists(app.Services.GetRequiredService<IProviderCredentialStore>().VaultPath).Should().BeFalse();
    }

    private const string TestTenant = "tenant-test";
    private const string OwnedConnection = "alpaca-paper-owned";
    private const string OwnedAccount = "PA-ENDPOINT";
    private const string ForeignConnection = "alpaca-paper-foreign";

    private static string AlpacaRoute(string suffix = "")
        => "/api/brokerage-connections/alpaca" + suffix + "?connectionId=" + OwnedConnection;

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ViewTrades | UserPermission.ManageCredentials,
        bool includeActor = true,
        string tenantId = TestTenant)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-endpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new Meridian.Application.UI.ConfigStore(Path.Combine(root, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: root));
        var connections = new ProviderConnectionService(config);
        await connections.UpsertForTenantAsync(new(OwnedConnection, "alpaca", "Owned paper", ExternalAccountId: OwnedAccount), TestTenant, "paper");
        await connections.UpsertForTenantAsync(new(ForeignConnection, "alpaca", "Foreign paper", ExternalAccountId: "PA-FOREIGN"), "tenant-other", "paper");
        builder.Services.AddSingleton(new ConfigStore(config.ConfigPath));
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(root));
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddSingleton<AlpacaBrokerageConnectionService>();
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            if (includeActor)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "brokerage-test-operator";
            await next();
        });
        app.MapBrokerageConnectionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static WebApplication GetServerApp(UiServer server)
    {
        var field = typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var app = field!.GetValue(server) as WebApplication;
        app.Should().NotBeNull();
        return app!;
    }

    private static string CreateMinimalConfig(string root, string dataSource = "IB")
    {
        var config = new
        {
            DataRoot = Path.Combine(root, "data"),
            Compress = false,
            DataSource = dataSource,
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
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
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

    private sealed class UiServerDevelopmentEnvironmentScope : IDisposable
    {
        private readonly string? _originalUseInMemoryGovernance;
        private readonly string? _originalAspNetCoreEnvironment;
        private readonly string? _originalDotnetEnvironment;

        private UiServerDevelopmentEnvironmentScope()
        {
            _originalUseInMemoryGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");
            _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        }

        public static UiServerDevelopmentEnvironmentScope Enable() => new();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        }
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }

    private sealed class CapturingStubHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _capture;
        private readonly HttpContent _content;

        public CapturingStubHandler(Action<HttpRequestMessage> capture, HttpContent content)
        {
            _capture = capture;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _capture(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CloneContent(_content)
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class AlpacaEnvScope : IDisposable
    {
        private static readonly string[] Names =
        [
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName,
            AlpacaCredentialEnvironment.TradingEnvironmentName,
            "APCA_API_KEY_ID",
            "APCA_API_SECRET_KEY",
            "ALPACA_BROKERAGE_CONNECTED_AT",
            "ALPACA_BROKERAGE_VERIFIED_AT",
            "ALPACA_BROKERAGE_ACCOUNT_ID",
            "ALPACA_BROKERAGE_LAST_ERROR"
        ];

        private readonly Dictionary<string, (string? Process, string? User)> _values;

        private AlpacaEnvScope()
        {
            _values = Names.ToDictionary(static name => name, static name => (
                Process: Environment.GetEnvironmentVariable(name),
                User: ReadUserEnvironment(name)));
        }

        public static AlpacaEnvScope Clear()
        {
            var scope = new AlpacaEnvScope();
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
                TrySetUserEnvironment(name, null);
            }

            return scope;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value.Process);
                TrySetUserEnvironment(name, value.User);
            }
        }

        private static string? ReadUserEnvironment(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
                return null;
            }
        }

        private static void TrySetUserEnvironment(string name, string? value)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
            }
            catch (PlatformNotSupportedException)
            {
                // Process-level restore is enough where user env storage is not available.
            }
        }
    }

    private static HttpContent CloneContent(HttpContent content)
    {
        var raw = content.ReadAsStringAsync().GetAwaiter().GetResult();
        var mediaType = content.Headers.ContentType?.MediaType ?? "application/json";
        return new StringContent(raw, Encoding.UTF8, mediaType);
    }
}
