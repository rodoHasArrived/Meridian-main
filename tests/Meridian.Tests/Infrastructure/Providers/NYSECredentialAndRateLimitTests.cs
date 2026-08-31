using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.DataSources;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class NYSECredentialAndRateLimitTests
{
    [Fact]
    public async Task ValidateCredentialsAsync_WhenOauthFails_ReturnsFalse()
    {
        var source = CreateSource(
            new QueueHttpHandler(
                new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":\"invalid_client\"}")
                }));

        var result = await source.ValidateCredentialsAsync();

        result.Should().BeFalse();
        await source.DisposeAsync();
    }

    [Fact]
    public async Task TestConnectivityAsync_WhenStatusEndpointIsRateLimited_ReturnsFalse()
    {
        var source = CreateSource(
            new QueueHttpHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token-123\",\"token_type\":\"bearer\",\"expires_in\":3600}")
                },
                new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"message\":\"too many requests\"}")
                }));

        var result = await source.TestConnectivityAsync();

        result.Should().BeFalse();
        await source.DisposeAsync();
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_WhenRateLimited_ThrowsTypedExceptionAndExposesResetState()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"message\":\"too many requests\"}")
        };
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        var source = CreateSource(
            new QueueHttpHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token-123\",\"token_type\":\"bearer\",\"expires_in\":3600}")
                },
                throttled));

        var act = () => source.GetAdjustedDailyBarsAsync("SPY");

        var error = await act.Should().ThrowAsync<RateLimitException>();
        error.Which.Provider.Should().Be("nyse");
        error.Which.Symbol.Should().Be("SPY");
        error.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
        var snapshot = source.GetRateLimitDiagnosticsSnapshot();
        snapshot.IsRateLimited.Should().BeTrue();
        snapshot.ResetAt.Should().NotBeNull();
        snapshot.Reason.Should().Be("provider-response:GetAdjustedDailyBars");
        await source.DisposeAsync();
    }

    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenOauthRateLimited_ThrowsTypedException()
    {
        var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(20));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(
            _ => new HttpClient(new QueueHttpHandler(throttled), disposeHandler: false));
        using var auth = new NyseAccessTokenProvider(
            new NYSEOptions
            {
                ApiKey = "nyse-test-key",
                ApiSecret = "nyse-test-secret",
                ClientId = "nyse-test-client"
            },
            factory,
            Substitute.For<Serilog.ILogger>());

        var act = () => auth.EnsureAuthenticatedAsync(CancellationToken.None);

        var error = await act.Should().ThrowAsync<RateLimitException>();
        error.Which.Provider.Should().Be("nyse");
        error.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(20));
    }

    private static NYSEDataSource CreateSource(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new NYSEDataSource(
            new NYSEOptions
            {
                ApiKey = "nyse-test-key",
                ApiSecret = "nyse-test-secret",
                ClientId = "nyse-test-client"
            },
            factory);
    }

    private sealed class QueueHttpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _responses.Should().NotBeEmpty($"a stub response must be configured for {request.Method} {request.RequestUri}");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
