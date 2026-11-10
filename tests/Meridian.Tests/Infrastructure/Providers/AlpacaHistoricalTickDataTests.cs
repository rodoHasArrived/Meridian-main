using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Historical quotes, trades and auctions as the real Market Data API returns them. The
/// single-symbol routes answer with a bare array, trade ids are numbers, and auctions exist only
/// on SIP; each of those made a one-symbol request fail before these fixtures existed.
/// </summary>
public sealed class AlpacaHistoricalTickDataTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetHistoricalQuotesAsync_OneSymbol_UsesTheKeyedRouteAndReadsTheQuotes()
    {
        var handler = new RecordingHandler(_ =>
            """
            {"next_page_token":null,"quotes":{"SPY":[
              {"ap":740.87,"as":40,"ax":"V","bp":740.53,"bs":80,"bx":"V","c":["R"],"t":"2026-11-06T15:00:00.010137513Z","z":"B"}]}}
            """);
        using var sut = CreateSut(handler);

        var result = await sut.GetHistoricalQuotesAsync("SPY", Start, Start.AddMinutes(1));

        var quote = result.Quotes.Should().ContainSingle().Subject;
        quote.Symbol.Should().Be("SPY");
        quote.BidPrice.Should().Be(740.53m);
        quote.AskPrice.Should().Be(740.87m);
        quote.BidSize.Should().Be(80);
        result.HasMore.Should().BeFalse();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be("/v2/stocks/quotes", "the single-symbol route returns an array the keyed model cannot read");
        request.Query.Should().Contain("symbols=SPY");
    }

    [Fact]
    public async Task GetHistoricalTradesAsync_NumericTradeId_IsKeptAsTheTradeIdentity()
    {
        var handler = new RecordingHandler(_ =>
            """
            {"next_page_token":null,"trades":{"SPY":[
              {"c":[" ","F","I"],"i":52983563378034,"p":740.77,"s":12,"t":"2026-11-06T15:00:00.024615058Z","x":"V","z":"B"},
              {"c":[" ","I"],"i":"text-id","p":740.69,"s":6,"t":"2026-11-06T15:00:00.039374223Z","x":"V","z":"B"}]}}
            """);
        using var sut = CreateSut(handler);

        var result = await sut.GetHistoricalTradesAsync("SPY", Start, Start.AddMinutes(1));

        result.Trades.Select(trade => trade.TradeId).Should().Equal("52983563378034", "text-id");
        result.Trades[0].Price.Should().Be(740.77m);
        result.Trades[0].Size.Should().Be(12);
        handler.Requests.Should().ContainSingle().Which.AbsolutePath.Should().Be("/v2/stocks/trades");
    }

    [Fact]
    public async Task GetHistoricalTradesAsync_Limit_StopsPagingAndReportsThatMoreRemains()
    {
        var handler = new RecordingHandler(_ =>
            """
            {"next_page_token":"more","trades":{"SPY":[
              {"i":1,"p":740.77,"s":12,"t":"2026-11-06T15:00:00.1Z","x":"V","z":"B"},
              {"i":2,"p":740.69,"s":6,"t":"2026-11-06T15:00:00.2Z","x":"V","z":"B"}]}}
            """);
        using var sut = CreateSut(handler);

        var result = await sut.GetHistoricalTradesAsync("SPY", Start, Start.AddMinutes(1), limit: 2);

        result.Trades.Should().HaveCount(2);
        result.HasMore.Should().BeTrue("the remaining page token says the window holds more");
        handler.Requests.Should().ContainSingle("a limit bounds the result instead of paging through the whole window")
            .Which.Query.Should().Contain("limit=2");
    }

    [Fact]
    public async Task GetHistoricalQuotesAsync_NoLimit_FollowsEveryPage()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.Query.Contains("page_token=more", StringComparison.Ordinal)
            ? """{"next_page_token":null,"quotes":{"SPY":[{"ap":2,"as":1,"ax":"V","bp":1,"bs":1,"bx":"V","t":"2026-11-06T15:00:01Z"}]}}"""
            : """{"next_page_token":"more","quotes":{"SPY":[{"ap":2,"as":1,"ax":"V","bp":1,"bs":1,"bx":"V","t":"2026-11-06T15:00:00Z"}]}}""");
        using var sut = CreateSut(handler);

        var result = await sut.GetHistoricalQuotesAsync("SPY", Start, Start.AddMinutes(1));

        result.Quotes.Should().HaveCount(2);
        result.HasMore.Should().BeFalse();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetHistoricalAuctionsAsync_SendsNoFeedAndReadsTheSessionDateAsACalendarDate()
    {
        var handler = new RecordingHandler(_ =>
            """
            {"auctions":{"SPY":[{"d":"2026-11-05",
              "o":[{"c":"O","p":765.54,"s":172727,"t":"2026-11-05T14:30:00.468654359Z","x":"P"}],
              "c":[{"c":"6","p":733,"s":2650711,"t":"2026-11-05T21:00:00.201925033Z","x":"P"}]}]},
             "next_page_token":null}
            """);
        using var sut = CreateSut(handler, feed: "iex");

        var result = await sut.GetHistoricalAuctionsAsync("SPY", new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5));

        var auction = result.Auctions.Should().ContainSingle().Subject;
        auction.SessionDate.Should().Be(new DateOnly(2026, 11, 5));
        auction.OpeningAuctions.Should().ContainSingle().Which.Price.Should().Be(765.54m);
        auction.ClosingAuctions.Should().ContainSingle().Which.Size.Should().Be(2650711);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be("/v2/stocks/auctions");
        request.Query.Should().NotContain("feed=", "Alpaca rejects an auctions request that names any feed but SIP");
    }

    private static AlpacaHistoricalDataProvider CreateSut(HttpMessageHandler handler, string feed = "iex") =>
        new(keyId: "test-key", secretKey: "test-secret", feed: feed, httpClient: new HttpClient(handler));

    private sealed class RecordingHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
