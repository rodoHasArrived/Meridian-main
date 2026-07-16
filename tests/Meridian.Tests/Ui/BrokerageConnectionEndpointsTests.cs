using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Meridian;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
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

        var response = await app.GetTestClient().GetAsync("/api/brokerage-connections/alpaca/status");

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
                new StringContent("{\"account_number\":\"PA-ENDPOINT\"}", Encoding.UTF8, "application/json"))));
        });

        var response = await app.GetTestClient().PostAsync(
            "/api/brokerage-connections/alpaca/connect",
            JsonContent(new { keyId = "endpoint-key", secretKey = "endpoint-secret", environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await ReadAsync<BrokerageConnectionStatusDto>(response);
        status.ProviderId.Should().Be("alpaca");
        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.Environment.Should().Be("paper");
        status.ExternalAccountId.Should().Be("PA-ENDPOINT");
        status.MaskedKeyId.Should().NotContain("endpoint-key");
        status.LastError.Should().BeNull();
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName).Should().NotBe("endpoint-key");
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName).Should().NotBe("endpoint-secret");
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        var stored = await store.ReadForProviderAsync("alpaca");
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be("endpoint-key");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://paper-api.alpaca.markets/v2/account");
    }

    [Fact]
    public async Task PostAlpacaConnect_TradeDeskRolePermissions_ReturnsForbidden()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(_ => { }, RolePermissions.For(UserRole.TradeDesk));

        var response = await app.GetTestClient().PostAsync(
            "/api/brokerage-connections/alpaca/connect",
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

        var response = await app.GetTestClient().DeleteAsync("/api/brokerage-connections/alpaca");

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

    [Fact(Skip = "Full UiServer BackfillCoordinator resolution is a long-running integration fixture; provider factory credential registration is covered by ProviderFactoryCredentialContextTests.")]
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
            await using var server = new UiServer(configPath, GetFreeTcpPort());
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
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ViewTrades | UserPermission.ManageCredentials)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(
            Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-endpoints", Guid.NewGuid().ToString("N"))));
        builder.Services.AddSingleton<AlpacaBrokerageConnectionService>();
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
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

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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
