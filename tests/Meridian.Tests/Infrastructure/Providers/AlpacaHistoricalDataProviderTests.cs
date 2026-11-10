using System.Net;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class AlpacaHistoricalDataProviderTests
{
    [Theory]
    [InlineData("iex")]
    [InlineData("sip")]
    [InlineData("delayed_sip")]
    [InlineData("boats")]
    [InlineData("overnight")]
    [InlineData("otc")]
    public void Constructor_DocumentedFeed_DoesNotThrow(string feed)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            feed: feed,
            httpClient: new HttpClient());

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ExposesFullCapabilitySurface()
    {
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            httpClient: new HttpClient());

        sut.Capabilities.Should().Be(Meridian.Infrastructure.Adapters.Core.HistoricalDataCapabilities.FullFeatured);
        sut.SupportsAdjustedPrices.Should().BeTrue();
        sut.SupportsIntraday.Should().BeTrue();
        sut.SupportsQuotes.Should().BeTrue();
        sut.SupportsTrades.Should().BeTrue();
        sut.SupportsAuctions.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_TracksRequestCountDeterministically()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"bars\":[{\"t\":\"2024-01-03T00:00:00Z\",\"o\":10,\"h\":11,\"l\":9,\"c\":10.5,\"v\":1000}],\"next_page_token\":null}")
        });
        using var httpClient = new HttpClient(handler);
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            rateLimitPerMinute: 3,
            httpClient: httpClient);

        var bars = await sut.GetAdjustedDailyBarsAsync("AAPL", new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 3));

        bars.Should().HaveCount(1);
        sut.GetRateLimitInfo().RequestsMade.Should().Be(1);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalseOnDegradedResponse()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler);
        var sut = new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            httpClient: httpClient);

        var available = await sut.IsAvailableAsync();

        available.Should().BeFalse();
        sut.GetRateLimitInfo().RequestsMade.Should().Be(1);
    }

    [Theory]
    [InlineData("spin-off")]
    [InlineData("split,spin-off")]
    [InlineData("split,dividend,spin-off")]
    public void Constructor_DocumentedAdjustment_DoesNotThrow(string adjustment)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            adjustment: adjustment,
            httpClient: new HttpClient());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("raw,split")]
    [InlineData("all,spin-off")]
    [InlineData("split,unknown")]
    public void Constructor_InvalidAdjustment_ThrowsArgumentException(string adjustment)
    {
        var act = () => new AlpacaHistoricalDataProvider(
            keyId: "test-key",
            secretKey: "test-secret",
            adjustment: adjustment,
            httpClient: new HttpClient());

        act.Should().Throw<ArgumentException>();
    }

    // The fixtures below are the shapes the market-data host returns on its multi-symbol routes:
    // results keyed by symbol, a numeric trade id, and date-only auction days.

    [Fact]
    public async Task GetHistoricalTradesAsync_ForOneSymbol_ReadsTheKeyedResponseAndNumericTradeIds()
    {
        Uri? observed = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observed = request.RequestUri;
            return Json("""
                {"next_page_token":null,"trades":{"ACME":[
                  {"c":["@","I"],"i":3769,"p":348.18,"s":30,"t":"2026-11-09T15:00:00.024890014Z","x":"V","z":"C"},
                  {"c":["@"],"i":3770,"p":348.175,"s":40,"t":"2026-11-09T15:00:00.06997462Z","x":"V","z":"C"}]}}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var sut = new AlpacaHistoricalDataProvider("test-key", "test-secret", httpClient: httpClient);
        // 10:00 at UTC-5 is 15:00 UTC; the request must carry the instant, not the local clock time.
        var start = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(-5));

        var result = await sut.GetHistoricalTradesAsync("ACME", start, start.AddMinutes(1));

        result.Trades.Select(trade => (trade.Symbol, trade.TradeId, trade.Price, trade.Size, trade.Exchange)).Should().Equal(
            ("ACME", "3769", 348.18m, 30L, "V"),
            ("ACME", "3770", 348.175m, 40L, "V"));
        result.Trades[0].Conditions.Should().Equal("@", "I");
        observed!.AbsolutePath.Should().Be("/v2/stocks/trades", "the per-symbol route returns a bare array this provider cannot read");
        var query = System.Web.HttpUtility.ParseQueryString(observed.Query);
        query["symbols"].Should().Be("ACME");
        query["feed"].Should().Be("iex");
        query["start"].Should().Be("2026-11-09T15:00:00Z");
        query["end"].Should().Be("2026-11-09T15:01:00Z");
    }

    [Fact]
    public async Task GetHistoricalQuotesAsync_ForOneSymbol_ReadsTheKeyedResponse()
    {
        Uri? observed = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observed = request.RequestUri;
            return Json("""
                {"next_page_token":null,"quotes":{"ACME":[
                  {"ap":349,"as":120,"ax":"V","bp":347.99,"bs":40,"bx":"V","c":["R"],"t":"2026-11-09T15:00:00.058960167Z","z":"C"}]}}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var sut = new AlpacaHistoricalDataProvider("test-key", "test-secret", httpClient: httpClient);
        var start = new DateTimeOffset(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

        var result = await sut.GetHistoricalQuotesAsync("ACME", start, start.AddMinutes(1));

        result.Quotes.Should().ContainSingle().Which.Should().Match<Meridian.Contracts.Domain.Models.HistoricalQuote>(quote =>
            quote.Symbol == "ACME" && quote.AskPrice == 349m && quote.AskSize == 120 && quote.BidPrice == 347.99m
            && quote.BidSize == 40 && quote.AskExchange == "V" && quote.BidExchange == "V" && quote.Tape == "C");
        observed!.AbsolutePath.Should().Be("/v2/stocks/quotes");
        System.Web.HttpUtility.ParseQueryString(observed.Query)["symbols"].Should().Be("ACME");
    }

    [Fact]
    public async Task GetHistoricalAuctionsAsync_SendsNoFeedBecauseTheApiRefusesAnyButTheConsolidatedOne()
    {
        Uri? observed = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observed = request.RequestUri;
            return Json("""
                {"next_page_token":null,"auctions":{"ACME":[{"d":"2026-11-06",
                  "o":[{"c":"O","p":339.12,"s":728909,"t":"2026-11-06T14:30:01.162692158Z","x":"Q"}],
                  "c":[{"c":"6","p":342.37,"s":8640080,"t":"2026-11-06T21:00:00.211761388Z","x":"Q"}]}]}}
                """);
        });
        using var httpClient = new HttpClient(handler);
        // The provider's default feed is IEX, which the auctions endpoint answers with HTTP 400.
        using var sut = new AlpacaHistoricalDataProvider("test-key", "test-secret", feed: "iex", httpClient: httpClient);

        var result = await sut.GetHistoricalAuctionsAsync("ACME", new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6));

        var auction = result.Auctions.Should().ContainSingle().Subject;
        auction.Symbol.Should().Be("ACME");
        auction.SessionDate.Should().Be(new DateOnly(2026, 11, 6));
        auction.OpeningAuctions.Should().ContainSingle().Which.Price.Should().Be(339.12m);
        auction.ClosingAuctions.Should().ContainSingle().Which.Price.Should().Be(342.37m);
        observed!.AbsolutePath.Should().Be("/v2/stocks/auctions");
        var query = System.Web.HttpUtility.ParseQueryString(observed.Query);
        query["symbols"].Should().Be("ACME");
        query["feed"].Should().BeNull();
        query["start"].Should().Be("2026-11-06");
        // A date-only end covers that session; reaching into the next day is refused as
        // "recent" data on accounts without a real-time consolidated entitlement.
        query["end"].Should().Be("2026-11-06");
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
