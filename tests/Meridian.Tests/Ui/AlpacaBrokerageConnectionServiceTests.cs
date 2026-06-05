using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaBrokerageConnectionServiceTests
{
    [Fact]
    public async Task GetStatus_WithApcaAliases_ReturnsConfiguredMaskedPaperStatus()
    {
        using var env = AlpacaEnvScope.Clear();
        Environment.SetEnvironmentVariable("APCA_API_KEY_ID", "alias-key-1234");
        Environment.SetEnvironmentVariable("APCA_API_SECRET_KEY", "alias-secret");
        var service = CreateService(new ConstantStubHandler(HttpStatusCode.OK, BuildAccountResponse()), out var store);

        var status = await service.GetStatusAsync();

        status.ProviderId.Should().Be("alpaca");
        status.IsConfigured.Should().BeTrue();
        status.State.Should().Be(BrokerageConnectionStateDto.Disconnected);
        status.Environment.Should().Be("paper");
        status.MaskedKeyId.Should().EndWith("1234");
        status.MaskedKeyId.Should().NotContain("alias-key");
        status.Warnings.Should().Contain(warning => warning.Contains("/v2/account", StringComparison.OrdinalIgnoreCase));
        DeleteStore(store);
    }

    [Fact]
    public async Task ConnectAsync_WithPaperKeys_VerifiesAccountAndWritesEncryptedStoreCredentials()
    {
        using var env = AlpacaEnvScope.Clear();
        HttpRequestMessage? capturedRequest = null;
        var service = CreateService(new CapturingStubHandler(
            request => capturedRequest = request,
            HttpStatusCode.OK,
            BuildAccountResponse("PA123")),
            out var store);

        var status = await service.ConnectAsync(new AlpacaBrokerageConnectionRequestDto(
            KeyId: "paper-key",
            SecretKey: "paper-secret",
            Environment: "paper"));

        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.IsConnected.Should().BeTrue();
        status.Environment.Should().Be("paper");
        status.ExternalAccountId.Should().Be("PA123");
        status.VerifiedAt.Should().NotBeNull();
        status.MaskedKeyId.Should().NotContain("paper-key");
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName).Should().NotBe("paper-key");
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName).Should().NotBe("paper-secret");
        var stored = await store.ReadForProviderAsync("alpaca");
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be("paper-key");
        stored.Get("SecretKey").Should().Be("paper-secret");
        (await File.ReadAllTextAsync(store.VaultPath)).Should().NotContain("paper-secret");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://paper-api.alpaca.markets/v2/account");
        capturedRequest.Headers.GetValues("APCA-API-KEY-ID").Should().ContainSingle().Which.Should().Be("paper-key");
        capturedRequest.Headers.GetValues("APCA-API-SECRET-KEY").Should().ContainSingle().Which.Should().Be("paper-secret");
        DeleteStore(store);
    }

    [Fact]
    public async Task ConnectAsync_WithLiveEnvironment_RequiresExplicitLiveOptIn()
    {
        using var env = AlpacaEnvScope.Clear();
        HttpRequestMessage? capturedRequest = null;
        var service = CreateService(new CapturingStubHandler(
            request => capturedRequest = request,
            HttpStatusCode.OK,
            BuildAccountResponse("LA123")),
            out var store);

        var status = await service.ConnectAsync(new AlpacaBrokerageConnectionRequestDto(
            KeyId: "live-key",
            SecretKey: "live-secret",
            Environment: "live"));

        status.State.Should().Be(BrokerageConnectionStateDto.Connected);
        status.Environment.Should().Be("live");
        status.Warnings.Should().Contain(warning => warning.Contains("Live Alpaca endpoint", StringComparison.OrdinalIgnoreCase));
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://api.alpaca.markets/v2/account");
        DeleteStore(store);
    }

    [Fact]
    public async Task ConnectAsync_WhenVerificationFails_DegradesWithoutLeakingSecret()
    {
        using var env = AlpacaEnvScope.Clear();
        var service = CreateService(new ConstantStubHandler(
            HttpStatusCode.Unauthorized,
            new StringContent("{\"message\":\"invalid\"}", Encoding.UTF8, "application/json")),
            out var store);

        var status = await service.ConnectAsync(new AlpacaBrokerageConnectionRequestDto(
            KeyId: "bad-key",
            SecretKey: "super-secret-value",
            Environment: "paper"));

        status.State.Should().Be(BrokerageConnectionStateDto.Degraded);
        status.IsConnected.Should().BeFalse();
        status.LastError.Should().Contain("401");
        status.LastError.Should().NotContain("super-secret-value");
        status.Warnings.Should().NotContain(warning => warning.Contains("super-secret-value", StringComparison.Ordinal));
        status.MaskedKeyId.Should().NotContain("bad-key");
        DeleteStore(store);
    }

    private static AlpacaBrokerageConnectionService CreateService(
        HttpMessageHandler handler,
        out FileProviderCredentialStore store)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "alpaca-brokerage", Guid.NewGuid().ToString("N"));
        store = new FileProviderCredentialStore(root);
        return new AlpacaBrokerageConnectionService(
            NullLogger<AlpacaBrokerageConnectionService>.Instance,
            new StubHttpClientFactory(handler),
            store);
    }

    private static void DeleteStore(FileProviderCredentialStore store)
    {
        var root = Directory.GetParent(store.VaultPath)?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static StringContent BuildAccountResponse(string accountNumber = "PA-DEMO") =>
        new(
            JsonSerializer.Serialize(new
            {
                id = "alpaca-account-id",
                account_number = accountNumber
            }),
            Encoding.UTF8,
            "application/json");

    private sealed class ConstantStubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly HttpContent _content;

        public ConstantStubHandler(HttpStatusCode statusCode, HttpContent content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = CloneContent(_content) });
        }
    }

    private sealed class CapturingStubHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _capture;
        private readonly HttpStatusCode _statusCode;
        private readonly HttpContent _content;

        public CapturingStubHandler(Action<HttpRequestMessage> capture, HttpStatusCode statusCode, HttpContent content)
        {
            _capture = capture;
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _capture(request);
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = CloneContent(_content) });
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
            "ALPACA__KEYID",
            "ALPACA__SECRETKEY",
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
