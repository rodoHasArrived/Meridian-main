using System.Net;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class OAuthTokenRefreshFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshFailure_DoesNotExposeProviderSecrets_AndCanRetry(bool transportFailure)
    {
        const string secret = "provider-echoed-bearer-secret";
        var root = Path.Combine(Path.GetTempPath(), "meridian-oauth-errors", Guid.NewGuid().ToString("N"));
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var handler = new RetryHandler(transportFailure, secret);
        using var client = new HttpClient(handler);
        try
        {
            await using var service = new OAuthTokenRefreshService(root, httpClient: client, logger: logger);
            service.RegisterProvider(new OAuthProviderConfig("alpaca", "client",
                ClientSecret: secret, TokenEndpoint: "https://provider.example/token"));
            var original = new OAuthToken("original-access", "Bearer", DateTimeOffset.UtcNow.AddHours(1),
                RefreshToken: secret);
            await service.StoreTokenAsync("alpaca", original);
            var failures = new List<string>();
            service.OnRefreshFailed += (_, error) => failures.Add(error);

            var failed = await service.RefreshTokenAsync("alpaca");

            failed.Success.Should().BeFalse();
            failed.Token.Should().BeNull();
            failed.Error.Should().Be(transportFailure ? "Token refresh failed." : "Token refresh failed: HTTP 400.");
            failures.Should().ContainSingle().Which.Should().Be(failed.Error);
            service.GetToken("alpaca").Should().Be(original);
            sink.Events.Should().OnlyContain(entry => entry.Exception == null);
            string.Join("\n", sink.Events.Select(entry => entry.RenderMessage())).Should().NotContain(secret);

            var recovered = await service.RefreshTokenAsync("alpaca");

            recovered.Success.Should().BeTrue();
            recovered.Token!.AccessToken.Should().Be("replacement-access");
            recovered.Token.RefreshToken.Should().Be("replacement-refresh");
            handler.Calls.Should().Be(2);
            failures.Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class RetryHandler(bool transportFailure, string secret) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                if (transportFailure)
                    throw new HttpRequestException(secret, new InvalidOperationException(secret));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    ReasonPhrase = secret,
                    Content = new StringContent(secret)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"replacement-access","refresh_token":"replacement-refresh","expires_in":3600}""")
            });
        }
    }
}
