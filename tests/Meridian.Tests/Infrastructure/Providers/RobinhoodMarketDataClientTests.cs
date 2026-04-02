using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Robinhood;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="RobinhoodMarketDataClient"/>.
/// All tests use a stub HTTP handler — no real network calls are made.
/// </summary>
public sealed class RobinhoodMarketDataClientTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static RobinhoodMarketDataClient CreateSut(
        HttpMessageHandler handler,
        string? accessToken = "test-token",
        out CapturingPublisher publisher)
    {
        publisher = new CapturingPublisher();
        var quoteCollector = new QuoteCollector(publisher);

        if (accessToken is not null)
            Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", accessToken);
        else
            Environment.SetEnvironmentVariable("ROBINHOOD_ACCESS_TOKEN", null);

        return new RobinhoodMarketDataClient(
            new StubHttpClientFactory(handler),
            quoteCollector,
            NullLogger<RobinhoodMarketDataClient>.Instance);
    }

    private static StringContent BuildQuoteResponse(string symbol,
        string bid = "185.00", string ask = "185.50", long bidSize = 100, long askSize = 200)
    {
        var payload = new
        {
            results = new[]
            {
                new
                {
                    symbol,
                    bid_price = bid,
                    ask_price = ask,
                    bid_size = bidSize,
                    ask_size = askSize,
                    updated_at = "2024-01-02T10:00:00Z"
                }
            }
        };
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    // ── Provider metadata ────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsRobinhoodLive()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        ((Meridian.Infrastructure.Adapters.Core.IProviderMetadata)sut).ProviderId.Should().Be("robinhood-live");
    }

    [Fact]
    public void IsEnabled_WithToken_ReturnsTrue()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            accessToken: "some-token", out _);
        sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WithoutToken_ReturnsFalse()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            accessToken: null, out _);
        sut.IsEnabled.Should().BeFalse();
    }

    // ── ConnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_NoToken_ThrowsConnectionException()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")),
            accessToken: null, out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<Meridian.Application.Exceptions.ConnectionException>()
            .WithMessage("*ROBINHOOD_ACCESS_TOKEN*");
    }

    [Fact]
    public async Task ConnectAsync_ValidToken_Connects()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().NotThrowAsync();
        await sut.DisconnectAsync(cts.Token);
    }

    // ── Subscription stubs ────────────────────────────────────────────────

    [Fact]
    public void SubscribeTrades_ReturnsMinusOne()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig { Symbol = "AAPL" };
        sut.SubscribeTrades(cfg).Should().Be(-1);
    }

    [Fact]
    public void SubscribeMarketDepth_ReturnsMinusOne()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig { Symbol = "AAPL" };
        sut.SubscribeMarketDepth(cfg).Should().Be(-1);
    }

    [Fact]
    public void SubscribeQuotes_ReturnsPositiveId()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var cfg = new Meridian.Contracts.Configuration.SymbolConfig { Symbol = "AAPL" };
        sut.SubscribeQuotes(cfg).Should().BeGreaterThan(0);
    }

    [Fact]
    public void SubscribeQuotes_MultipleSymbols_ReturnsUniqueIds()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        var id1 = sut.SubscribeQuotes(new Meridian.Contracts.Configuration.SymbolConfig { Symbol = "AAPL" });
        var id2 = sut.SubscribeQuotes(new Meridian.Contracts.Configuration.SymbolConfig { Symbol = "MSFT" });
        id1.Should().NotBe(id2);
    }

    // ── Dispose / DisposeAsync ────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_WhenConnected_DisconnectsCleanly()
    {
        var sut = CreateSut(new StubHttpHandler(HttpStatusCode.OK, new StringContent("{}")), out _);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        var act = () => sut.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    // ── Stub infrastructure ──────────────────────────────────────────────

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly HttpContent _content;

        public StubHttpHandler(HttpStatusCode statusCode, HttpContent content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = _content });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.robinhood.com/") };
    }

    private sealed class CapturingPublisher : IMarketEventPublisher
    {
        public List<MarketEvent> Published { get; } = new();

        public bool TryPublish(in MarketEvent evt)
        {
            Published.Add(evt);
            return true;
        }
    }
}
