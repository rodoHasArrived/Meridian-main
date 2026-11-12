using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class RobinhoodOptionsChainPaginationTests
{
    [Fact]
    public async Task GetStrikesAsync_SuccessDisposesEveryResponseAndPacesEveryRequest()
    {
        using var handler = new Handler((_, index) => index switch
        {
            1 => Json("""{"results":[{"id":"equity"}]}"""),
            2 => Json("""{"results":[{"id":"chain"}]}"""),
            3 => Contracts("https://api.robinhood.com/options/instruments/?cursor=second", "100"),
            _ => Contracts(null, "110")
        });
        using var limiter = new RateLimiter(100, TimeSpan.FromHours(1));
        using var provider = Create(handler, limiter);

        var strikes = await provider.GetStrikesAsync("SPY", new DateOnly(2026, 12, 18));

        strikes.Should().Equal(100m, 110m);
        handler.Requests.Should().Be(4);
        handler.Contents.Should().HaveCount(4).And.OnlyContain(content => content.IsDisposed);
        handler.Authorizations.Should().OnlyContain(value => value == "Bearer synthetic-token");
        limiter.GetStatus().RequestsInWindow.Should().Be(4);
    }

    [Theory]
    [InlineData("https://foreign.example/options/instruments/")]
    [InlineData("http://api.robinhood.com/options/instruments/")]
    [InlineData("https://api.robinhood.com:444/options/instruments/")]
    [InlineData("https://user@api.robinhood.com/options/instruments/")]
    public async Task GetStrikesAsync_UntrustedNextUrlIsRejectedBeforeCredentialsCanBeSent(string next)
    {
        using var handler = Setup((_, _) => Contracts(next, "100"));
        using var limiter = new RateLimiter(100, TimeSpan.FromHours(1));
        using var provider = Create(handler, limiter);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetStrikesAsync("SPY", new DateOnly(2026, 12, 18), deadline.Token));

        handler.Requests.Should().Be(3);
        handler.Hosts.Should().OnlyContain(host => host == "api.robinhood.com");
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    [Fact]
    public async Task GetStrikesAsync_RepeatedCursorStopsBeforeRequestingThePageTwice()
    {
        using var handler = Setup((_, _) => Contracts("https://api.robinhood.com/options/instruments/?cursor=repeat", "100"));
        using var limiter = new RateLimiter(100, TimeSpan.FromHours(1));
        using var provider = Create(handler, limiter);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetStrikesAsync("SPY", new DateOnly(2026, 12, 18), deadline.Token));

        handler.Requests.Should().Be(4);
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    [Fact]
    public async Task GetStrikesAsync_FailedLaterPageDoesNotReturnThePartialChain()
    {
        using var handler = Setup((_, index) => index == 3
            ? Contracts("https://api.robinhood.com/options/instruments/?cursor=second", "100")
            : new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new TrackingContent("untrusted error") });
        using var limiter = new RateLimiter(100, TimeSpan.FromHours(1));
        using var provider = Create(handler, limiter);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetStrikesAsync("SPY", new DateOnly(2026, 12, 18)));

        exception.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.Requests.Should().Be(4);
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    [Fact]
    public async Task GetStrikesAsync_ExhaustedRateLimitHonorsCancellationBeforeSending()
    {
        using var handler = Setup((_, _) => Contracts(null, "100"));
        using var limiter = new RateLimiter(1, TimeSpan.FromHours(1));
        limiter.RecordRequest();
        using var provider = Create(handler, limiter);
        using var cancellation = new CancellationTokenSource();
        var fetch = provider.GetStrikesAsync("SPY", new DateOnly(2026, 12, 18), cancellation.Token);
        handler.Requests.Should().Be(0);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetChainSnapshotAsync_FailedOrInvalidMarketDataBatchNeverReturnsPartialSnapshot(bool invalidEnvelope)
    {
        using var handler = Setup((_, index) => index switch
        {
            3 => Json("""{"results":[{"url":"https://api.robinhood.com/options/instruments/contract/","chain_symbol":"SPY","expiration_date":"2026-12-18","strike_price":"100","type":"call"}],"next":null}"""),
            4 => Json("""{"last_trade_price":"100"}"""),
            _ => invalidEnvelope ? Json("{}") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new TrackingContent("outage") }
        });
        using var limiter = new RateLimiter(100, TimeSpan.FromHours(1));
        using var provider = Create(handler, limiter);
        Func<Task> fetch = async () => { await provider.GetChainSnapshotAsync("SPY", new DateOnly(2026, 12, 18)); };

        if (invalidEnvelope)
            await fetch.Should().ThrowAsync<InvalidOperationException>();
        else
            await fetch.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().Be(5);
        handler.Contents.Should().OnlyContain(content => content.IsDisposed);
    }

    private static RobinhoodOptionsChainProvider Create(Handler handler, RateLimiter limiter) => new(
        new Factory(handler), NullLogger<RobinhoodOptionsChainProvider>.Instance, "synthetic-token", limiter);

    private static Handler Setup(Func<HttpRequestMessage, int, HttpResponseMessage> page) => new((request, index) => index switch
    {
        1 => Json("""{"results":[{"id":"equity"}]}"""),
        2 => Json("""{"results":[{"id":"chain"}]}"""),
        _ => page(request, index)
    });

    private static HttpResponseMessage Contracts(string? next, string strike) => Json(JsonSerializer.Serialize(new
    {
        results = new[] { new { id = Guid.NewGuid().ToString(), strike_price = strike } },
        next
    }));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new TrackingContent(body) };

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TrackingContent(string body) : StringContent(body, System.Text.Encoding.UTF8, "application/json")
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public List<TrackingContent> Contents { get; } = [];
        public List<string> Hosts { get; } = [];
        public List<string?> Authorizations { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            var response = respond(request, ++Requests);
            if (response.Content is TrackingContent content)
                Contents.Add(content);
            return Task.FromResult(response);
        }
    }
}
