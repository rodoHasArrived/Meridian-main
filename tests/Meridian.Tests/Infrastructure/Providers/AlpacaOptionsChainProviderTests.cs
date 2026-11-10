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
    public void TryParseOccRightAndStrike_RefusesSymbolsItCannotRead(string ticker)
    {
        AlpacaOptionsChainProvider.TryParseOccRightAndStrike(ticker, out _, out _).Should().BeFalse();
    }

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
        public void Dispose() => client.Dispose();
    }
}
