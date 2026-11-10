using System.Net;
using System.Text;
using System.Web;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Contract discovery fixtures mirror the Trading API <c>/v2/options/contracts</c> response as
/// the paper host serves it: string-encoded strikes, an expiration window that must be stated,
/// and <c>next_page_token</c> paging.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlpacaOptionsChainProviderTests
{
    private const string KeyId = "test-alpaca-key";
    private const string SecretKey = "test-alpaca-secret";

    [Theory]
    [InlineData(true, "paper-api.alpaca.markets")]
    [InlineData(false, "api.alpaca.markets")]
    public async Task GetExpirationsAsync_ReadsTradingApiContractsAcrossTheForwardHorizon(bool useSandbox, string expectedHost)
    {
        Uri? observed = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observed = request.RequestUri;
            request.Headers.GetValues("APCA-API-KEY-ID").Should().Equal(KeyId);
            request.Headers.GetValues("APCA-API-SECRET-KEY").Should().Equal(SecretKey);
            return Json(Contracts(null,
                Contract("ACME261218C00100000", "2026-12-18", "100"),
                Contract("ACME261120P00095500", "2026-11-20", "95.5"),
                Contract("ACME261120C00100000", "2026-11-20", "100")));
        });
        using var provider = CreateSut(handler, useSandbox);

        var expirations = await provider.GetExpirationsAsync("ACME");

        expirations.Should().Equal(new DateOnly(2026, 11, 20), new DateOnly(2026, 12, 18));
        observed!.Host.Should().Be(expectedHost, "contracts are a Trading API resource in the credentials' environment");
        observed.AbsolutePath.Should().Be("/v2/options/contracts");
        var query = HttpUtility.ParseQueryString(observed.Query);
        query["underlying_symbols"].Should().Be("ACME");
        query["status"].Should().Be("active");
        query["limit"].Should().Be("10000");
        query["expiration_date"].Should().BeNull();
        // Without a stated bound the endpoint returns only contracts expiring by the upcoming weekend.
        DateOnly.ParseExact(query["expiration_date_lte"]!, "yyyy-MM-dd").Should()
            .BeAfter(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(4));
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStrikesAsync_ReadsStringEncodedStrikesForOneExpiration()
    {
        Uri? observed = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observed = request.RequestUri;
            return Json(Contracts(null,
                Contract("ACME261120C00270000", "2026-11-20", "270"),
                Contract("ACME261120P00270000", "2026-11-20", "270"),
                Contract("ACME261120C00272500", "2026-11-20", "272.5")));
        });
        using var provider = CreateSut(handler, useSandbox: true);

        var strikes = await provider.GetStrikesAsync("ACME", new DateOnly(2026, 11, 20));

        strikes.Should().Equal(270m, 272.5m);
        var query = HttpUtility.ParseQueryString(observed!.Query);
        query["expiration_date"].Should().Be("2026-11-20");
        query["expiration_date_lte"].Should().BeNull();
    }

    [Fact]
    public async Task ContractDiscovery_FollowsNextPageTokenUntilTheLastPage()
    {
        var pageTokens = new List<string?>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            var token = HttpUtility.ParseQueryString(request.RequestUri!.Query)["page_token"];
            pageTokens.Add(token);
            return token switch
            {
                null => Json(Contracts("Mg==", Contract("ACME261120C00100000", "2026-11-20", "100"))),
                "Mg==" => Json(Contracts("Mw==", Contract("ACME261120C00105000", "2026-11-20", "105"))),
                _ => Json(Contracts(null, Contract("ACME261120C00110000", "2026-11-20", "110"))),
            };
        });
        using var provider = CreateSut(handler, useSandbox: true);

        var strikes = await provider.GetStrikesAsync("ACME", new DateOnly(2026, 11, 20));

        strikes.Should().Equal(100m, 105m, 110m);
        pageTokens.Should().Equal(null, "Mg==", "Mw==");
    }

    [Fact]
    public async Task ContractDiscovery_WhenPagingNeverEnds_ReturnsNothingRatherThanAPartialChain()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            Json(Contracts("Mg==", Contract("ACME261120C00100000", "2026-11-20", "100"))));
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetStrikesAsync("ACME", new DateOnly(2026, 11, 20))).Should().BeEmpty();
        handler.CallCount.Should().Be(2, "a repeated page token ends the walk");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ContractDiscovery_WhenALaterPageFails_ReturnsNothingRatherThanAPartialChain(HttpStatusCode status)
    {
        var calls = 0;
        using var handler = new StubHttpMessageHandler(_ => ++calls == 1
            ? Json(Contracts("Mg==", Contract("ACME261120C00100000", "2026-11-20", "100")))
            : new HttpResponseMessage(status) { Content = new StringContent("{\"message\":\"refused\"}") });
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetExpirationsAsync("ACME")).Should().BeEmpty();
        (await provider.GetStrikesAsync("ACME", new DateOnly(2026, 11, 20))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"option_contracts\":null,\"next_page_token\":null}")]
    public async Task ContractDiscovery_WhenAPageCarriesNoContractList_ReturnsNothing(string body)
    {
        using var handler = new StubHttpMessageHandler(_ => Json(body));
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetExpirationsAsync("ACME")).Should().BeEmpty();
        handler.CallCount.Should().Be(1, "a page that is not a listing ends the walk");
    }

    [Fact]
    public async Task ContractDiscovery_ForAnUnderlyingWithNoContracts_IsACompleteEmptyListing()
    {
        using var handler = new StubHttpMessageHandler(_ => Json(Contracts(null)));
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetExpirationsAsync("ACME")).Should().BeEmpty();
        (await provider.GetStrikesAsync("ACME", new DateOnly(2026, 11, 20))).Should().BeEmpty();
    }

    [Fact]
    public async Task ContractDiscovery_WhenARequestTimesOut_IsAFailedReadNotACancellation()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("The request timed out."));
        using var provider = CreateSut(handler, useSandbox: true);

        // The caller's token is not cancelled; an HttpClient timeout must not surface as one.
        (await provider.GetExpirationsAsync("ACME")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("AAPL261111C00270000", OptionRight.Call, 270)]
    [InlineData("AAPL261111P00272500", OptionRight.Put, 272.5)]
    // The root itself contains a C; only the character before the strike digits is the right.
    [InlineData("CVX261120P00150000", OptionRight.Put, 150)]
    [InlineData("C261120P00065000", OptionRight.Put, 65)]
    [InlineData("PCG261120C00017500", OptionRight.Call, 17.5)]
    public void TryParseOccRightAndStrike_ReadsTheRightFromItsOwnPosition(string ticker, OptionRight expectedRight, double expectedStrike)
    {
        AlpacaOptionsChainProvider.TryParseOccRightAndStrike(ticker, out var right, out var strike).Should().BeTrue();

        right.Should().Be(expectedRight);
        strike.Should().Be((decimal)expectedStrike);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ACME")]
    [InlineData("ACME261120X00100000")]
    [InlineData("ACME261120C0010000A")]
    [InlineData("ACME261120C00000000")]
    // The six characters before the right must be the expiration digits.
    [InlineData("ACMEXXXXXXC00100000")]
    public void TryParseOccRightAndStrike_RefusesSymbolsItCannotRead(string ticker)
    {
        AlpacaOptionsChainProvider.TryParseOccRightAndStrike(ticker, out _, out _).Should().BeFalse();
    }

    // Shape of the market-data snapshot payload: no underlying price and no open interest; the
    // session volume is on dailyBar, and greeks are present only where the API computed them.
    private const string ChainSnapshots = """
        {"next_page_token":null,"snapshots":{
          "CVX261218C00150000":{"dailyBar":{"c":11.72,"h":12.9,"l":10.25,"n":530,"o":10.25,"t":"2026-11-09T05:00:00Z","v":2223,"vw":11.7},
            "greeks":{"delta":0.5422,"gamma":0.0144,"rho":0.1854,"theta":-0.1625,"vega":0.4485},"impliedVolatility":0.2429,
            "latestQuote":{"ap":12,"as":143,"ax":"M","bp":11.72,"bs":111,"bx":"E","c":"A","t":"2026-11-09T20:59:59.752426754Z"},
            "latestTrade":{"c":"I","p":11.7,"s":1,"t":"2026-11-09T20:59:23.468250322Z","x":"Q"}},
          "CVX261218P00150000":{"dailyBar":{"c":3.1,"h":3.4,"l":2.9,"n":40,"o":3.0,"t":"2026-11-09T05:00:00Z","v":310,"vw":3.1},
            "latestQuote":{"ap":3.2,"as":8,"ax":"C","bp":3.0,"bs":9,"bx":"C","c":" ","t":"2026-11-09T20:59:59.9Z"},
            "latestTrade":{"c":"g","p":3.1,"s":2,"t":"2026-11-09T20:13:41.6Z","x":"M"}},
          "CVX261218C00155000":{"latestQuote":{"ap":9.1,"as":5,"ax":"C","bp":8.8,"bs":6,"bx":"C","c":" ","t":"2026-11-09T20:59:59.9Z"}},
          "CVX261218C00160000":{"latestQuote":{"ap":6.9,"as":5,"ax":"C","bp":6.6,"bs":6,"bx":"C","c":" ","t":"2026-11-09T20:59:59.9Z"}},
          "CVX261218C00145000":{"latestQuote":{"ap":15.4,"as":5,"ax":"C","bp":15.0,"bs":6,"bx":"C","c":" ","t":"2026-11-09T20:59:59.9Z"}}}}
        """;

    private const string LatestTrade =
        """{"symbol":"CVX","trade":{"c":["@","T"],"i":25989,"p":151.37,"s":80,"t":"2026-11-09T21:55:55.204736504Z","x":"V","z":"C"}}""";

    [Fact]
    public async Task GetChainSnapshotAsync_JoinsUnderlyingPriceOpenInterestAndSessionVolume()
    {
        var requests = new List<Uri>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath switch
            {
                "/v2/stocks/CVX/trades/latest" => Json(LatestTrade),
                "/v1beta1/options/snapshots/CVX" => Json(ChainSnapshots),
                "/v2/options/contracts" => Json(Contracts(null,
                    Contract("CVX261218C00150000", "2026-12-18", "150"),
                    Contract("CVX261218P00150000", "2026-12-18", "150"))),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        using var provider = CreateSut(handler, useSandbox: true);

        var chain = await provider.GetChainSnapshotAsync("CVX", new DateOnly(2026, 12, 18));

        (chain is not null).Should().BeTrue("a snapshot needs only quotes and an underlying price");
        chain!.UnderlyingPrice.Should().Be(151.37m, "the snapshot payload has no underlying price; the latest trade supplies it");
        chain.Strikes.Should().Equal(145m, 150m, 155m, 160m);
        chain.Calls.Select(call => call.Symbol).Should().BeEquivalentTo(
            "CVX261218C00145000", "CVX261218C00150000", "CVX261218C00155000", "CVX261218C00160000");
        // The underlying's root contains a C; the put must still be a put.
        chain.Puts.Should().ContainSingle().Which.Symbol.Should().Be("CVX261218P00150000");

        var call = chain.Calls.Single(quote => quote.Symbol == "CVX261218C00150000");
        call.BidPrice.Should().Be(11.72m);
        call.AskPrice.Should().Be(12m);
        call.LastPrice.Should().Be(11.7m);
        call.UnderlyingPrice.Should().Be(151.37m);
        call.Volume.Should().Be(2223, "volume is the session's, not the size of the last print");
        call.OpenInterest.Should().Be(2, "open interest is joined from the contract listing");
        call.Delta.Should().Be(0.5422m);
        call.ImpliedVolatility.Should().Be(0.2429m);
        chain.Calls.Single(quote => quote.Symbol == "CVX261218C00155000").OpenInterest.Should().BeNull(
            "a contract absent from the listing has unknown open interest, not zero");

        requests.Single(uri => uri.AbsolutePath == "/v1beta1/options/snapshots/CVX").Should().Match<Uri>(uri =>
            uri.Host == "data.alpaca.markets" && uri.Query.Contains("expiration_date=2026-12-18") && uri.Query.Contains("feed=indicative"));
        requests.Single(uri => uri.AbsolutePath == "/v2/stocks/CVX/trades/latest").Host.Should().Be("data.alpaca.markets");
        requests.Single(uri => uri.AbsolutePath == "/v2/options/contracts").Host.Should().Be("paper-api.alpaca.markets");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_WithStrikeRange_KeepsTheStrikesNearestTheMoney()
    {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/stocks/CVX/trades/latest" => Json(LatestTrade),
            "/v1beta1/options/snapshots/CVX" => Json(ChainSnapshots),
            _ => Json(Contracts(null)),
        });
        using var provider = CreateSut(handler, useSandbox: true);

        var chain = await provider.GetChainSnapshotAsync("CVX", new DateOnly(2026, 12, 18), strikeRange: 1);

        // Underlying 151.37: the closest strike is 150, with one strike kept on each side.
        chain!.Strikes.Should().Equal(145m, 150m, 155m);
        chain.Calls.Select(call => call.Contract.Strike).Should().BeEquivalentTo([145m, 150m, 155m]);
        chain.Puts.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"message\":\"no trade found for CVX\"}")]
    [InlineData(HttpStatusCode.OK, "{\"symbol\":\"CVX\",\"trade\":null}")]
    [InlineData(HttpStatusCode.OK, "{\"symbol\":\"CVX\",\"trade\":{\"p\":0}}")]
    public async Task ChainAndQuote_WithoutAnUnderlyingPrice_ProduceNothingAndRequestNoSnapshots(HttpStatusCode status, string body)
    {
        var paths = new List<string>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/v2/stocks/CVX/trades/latest"
                ? new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : Json(ChainSnapshots);
        });
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetChainSnapshotAsync("CVX", new DateOnly(2026, 12, 18))).Should().BeNull();
        (await provider.GetOptionQuoteAsync(CvxCall)).Should().BeNull();

        paths.Should().OnlyContain(path => path == "/v2/stocks/CVX/trades/latest");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_WhenSnapshotPagingNeverEnds_ReturnsNothing()
    {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/stocks/CVX/trades/latest" => Json(LatestTrade),
            _ => Json(ChainSnapshots.Replace("\"next_page_token\":null", "\"next_page_token\":\"again\"", StringComparison.Ordinal)),
        });
        using var provider = CreateSut(handler, useSandbox: true);

        (await provider.GetChainSnapshotAsync("CVX", new DateOnly(2026, 12, 18))).Should().BeNull(
            "part of a chain is never presented as the chain");
    }

    [Fact]
    public async Task GetOptionQuoteAsync_UsesTheSingleContractRouteAndTheUnderlyingPrice()
    {
        var requests = new List<Uri>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath switch
            {
                "/v2/stocks/CVX/trades/latest" => Json(LatestTrade),
                // The per-underlying chain route answers 400 to a symbols filter.
                "/v1beta1/options/snapshots" => Json(ChainSnapshots),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"message\":\"unexpected query parameter(s): symbols\"}"),
                },
            };
        });
        using var provider = CreateSut(handler, useSandbox: true);

        var quote = await provider.GetOptionQuoteAsync(CvxCall);

        (quote is not null).Should().BeTrue();
        quote!.Symbol.Should().Be("CVX261218C00150000");
        quote.BidPrice.Should().Be(11.72m);
        quote.AskPrice.Should().Be(12m);
        quote.UnderlyingPrice.Should().Be(151.37m);
        quote.Volume.Should().Be(2223);
        var snapshotRequest = requests.Single(uri => uri.AbsolutePath == "/v1beta1/options/snapshots");
        snapshotRequest.Host.Should().Be("data.alpaca.markets");
        HttpUtility.ParseQueryString(snapshotRequest.Query)["symbols"].Should().Be("CVX261218C00150000");
    }

    private static readonly Meridian.Contracts.Domain.Models.OptionContractSpec CvxCall = new(
        UnderlyingSymbol: "CVX",
        Strike: 150m,
        Expiration: new DateOnly(2026, 12, 18),
        Right: OptionRight.Call,
        Style: OptionStyle.American,
        Multiplier: 100,
        Exchange: "ALPACA",
        Currency: "USD",
        OccSymbol: "CVX261218C00150000",
        InstrumentType: InstrumentType.EquityOption);

    private static DisposableProvider CreateSut(HttpMessageHandler handler, bool useSandbox)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false);
        return new DisposableProvider(
            new AlpacaOptionsChainProvider(Substitute.For<IHttpClientFactory>(), KeyId, SecretKey, httpClient, useSandbox: useSandbox),
            httpClient);
    }

    private static string Contract(string symbol, string expiration, string strike) =>
        $$"""
        {"id":"6573092f-1c86-45c9-8c9e-212936aab4cc","symbol":"{{symbol}}","name":"fixture","status":"active","tradable":true,
         "expiration_date":"{{expiration}}","root_symbol":"ACME","underlying_symbol":"ACME","type":"call","style":"american",
         "strike_price":"{{strike}}","multiplier":"100","size":"100","open_interest":"2","open_interest_date":"2026-11-06",
         "close_price":"81.5","close_price_date":"2026-11-09","ppind":true}
        """;

    private static string Contracts(string? nextPageToken, params string[] contracts) =>
        $$"""{"option_contracts":[{{string.Join(',', contracts)}}],"next_page_token":{{(nextPageToken is null ? "null" : $"\"{nextPageToken}\"")}}}""";

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    /// <summary>Forwards to the provider and releases the client the test supplied to it.</summary>
    private sealed class DisposableProvider(AlpacaOptionsChainProvider provider, HttpClient client) : IDisposable
    {
        public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(string symbol) => provider.GetExpirationsAsync(symbol);
        public Task<IReadOnlyList<decimal>> GetStrikesAsync(string symbol, DateOnly expiration) => provider.GetStrikesAsync(symbol, expiration);
        public Task<Meridian.Contracts.Domain.Models.OptionChainSnapshot?> GetChainSnapshotAsync(string symbol, DateOnly expiration, int? strikeRange = null) =>
            provider.GetChainSnapshotAsync(symbol, expiration, strikeRange);
        public Task<Meridian.Contracts.Domain.Models.OptionQuote?> GetOptionQuoteAsync(Meridian.Contracts.Domain.Models.OptionContractSpec contract) =>
            provider.GetOptionQuoteAsync(contract);
        public void Dispose() => client.Dispose();
    }
}
