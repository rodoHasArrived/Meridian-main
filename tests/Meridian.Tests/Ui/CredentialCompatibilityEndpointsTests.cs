using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class CredentialCompatibilityEndpointsTests
{
    [Fact]
    public async Task LegacyCredentialPost_SavesEncryptedStoreWithoutMutatingEnvironment()
    {
        using var env = new EnvironmentScope(
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName);
        await using var app = await CreateAppAsync(_ => { });
        var store = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();

        var response = await app.GetTestClient().PostAsync(
            "/api/credentials/alpaca",
            JsonContent(new
            {
                ALPACA_KEY_ID = "legacy-post-key",
                ALPACA_SECRET_KEY = "legacy-post-secret",
                environment = "paper"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName).Should().BeNull();
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName).Should().BeNull();

        var stored = await store.ReadForProviderAsync("alpaca");
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be("legacy-post-key");
        stored.Get("SecretKey").Should().Be("legacy-post-secret");

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        vaultText.Should().NotContain("legacy-post-key");
        vaultText.Should().NotContain("legacy-post-secret");

        var statusResponse = await app.GetTestClient().GetAsync("/api/credentials/alpaca");
        var statusJson = await statusResponse.Content.ReadAsStringAsync();
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        statusJson.Should().Contain("\"credentialSource\":\"LocalEncryptedStore\"");
        statusJson.Should().NotContain("legacy-post-key");
        statusJson.Should().NotContain("legacy-post-secret");
    }

    [Fact]
    public async Task LegacyCredentialDelete_RemovesLocalStoreButLeavesEnvironmentFallback()
    {
        using var env = new EnvironmentScope("POLYGON_API_KEY");
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", "legacy-polygon-key");
        await using var app = await CreateAppAsync(_ => { });

        await app.GetTestClient().PostAsync(
            "/api/credentials/polygon",
            JsonContent(new { POLYGON_API_KEY = "local-polygon-key" }));

        var response = await app.GetTestClient().DeleteAsync("/api/credentials/polygon");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Environment.GetEnvironmentVariable("POLYGON_API_KEY").Should().Be("legacy-polygon-key");

        var status = await app.GetTestClient().GetAsync("/api/credentials/polygon");
        var statusJson = await status.Content.ReadAsStringAsync();
        statusJson.Should().Contain("\"credentialSource\":\"Environment\"");
        statusJson.Should().NotContain("local-polygon-key");
    }

    [Fact]
    public async Task ProviderCredentialTestConnection_WrapsSharedStoreVerification()
    {
        using var env = new EnvironmentScope(
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName);
        HttpRequestMessage? capturedRequest = null;
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(new CapturingStubHandler(
                request => capturedRequest = request,
                new StringContent("{\"account_number\":\"PA-COMPAT\"}", Encoding.UTF8, "application/json"))));
        });

        var response = await app.GetTestClient().PostAsync(
            "/api/providers/alpaca/test-connection",
            JsonContent(new ProviderCredentialRequest(
                "alpaca",
                new Dictionary<string, string>
                {
                    ["KeyId"] = "compat-key",
                    ["SecretKey"] = "compat-secret"
                })));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderConnectionTest>(response);
        result.IsConnected.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("APCA-API-KEY-ID").Should().ContainSingle().Which.Should().Be("compat-key");

        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        var stored = await store.ReadForProviderAsync("alpaca");
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be("compat-key");
        stored.Get("SecretKey").Should().Be("compat-secret");
    }

    [Fact]
    public async Task CredentialRoutes_RequireManageCredentials()
    {
        await using var app = await CreateAppAsync(_ => { }, UserPermission.ViewTrades);

        var response = await app.GetTestClient().GetAsync("/api/credentials");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacyCredentialPost_WithTradeDeskRolePermissions_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(_ => { }, RolePermissions.For(UserRole.TradeDesk));

        var response = await app.GetTestClient().PostAsync(
            "/api/credentials/alpaca",
            JsonContent(new
            {
                ALPACA_KEY_ID = "trade-desk-key",
                ALPACA_SECRET_KEY = "trade-desk-secret",
                environment = "paper"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ManageCredentials)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-compat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot = Path.Combine(root, "data") }));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(Path.Combine(root, "data")));
        builder.Services.AddSingleton(new ConfigStore(configPath));
        builder.Services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.UseRateLimiter();
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        app.MapProviderCredentialEndpoints(jsonOptions);
        app.MapCredentialEndpoints(jsonOptions);

        await app.StartAsync();
        return app;
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

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _values;

        public EnvironmentScope(params string[] names)
        {
            _values = names.ToDictionary(static name => name, static name => Environment.GetEnvironmentVariable(name));
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value);
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
