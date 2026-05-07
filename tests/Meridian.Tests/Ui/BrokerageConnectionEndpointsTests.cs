using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

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
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://paper-api.alpaca.markets/v2/account");
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

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ViewTrades | UserPermission.ManageCredentials)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<AlpacaBrokerageConnectionService>();
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapBrokerageConnectionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

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
