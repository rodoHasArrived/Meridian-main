using System.Net;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Guards Alpaca's non-paginated asset search against unfiltered responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlpacaSymbolSearchProviderTests
{
    private const string KeyId = "test-alpaca-key";
    private const string SecretKey = "test-alpaca-secret";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requests_IncludeBothAuthenticationHeaders_AfterCredentialInitialization(bool details)
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.GetValues("APCA-API-KEY-ID").Should().Equal(KeyId);
            request.Headers.GetValues("APCA-API-SECRET-KEY").Should().Equal(SecretKey);
            request.Headers.Accept.Select(header => header.MediaType).Should().Equal("application/json");
            request.RequestUri!.AbsolutePath.Should().Be(details ? "/v2/assets/AAPL" : "/v2/assets");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(details
                    ? "{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}"
                    : "[{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        if (details)
            (await provider.GetDetailsAsync(new Meridian.Contracts.Domain.SymbolId("AAPL"))).Should().NotBeNull();
        else
            (await provider.SearchAsync("AAPL", 1, CancellationToken.None)).Should().ContainSingle();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_WithoutAssetType_RequestsOnlyEquities()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        var results = await provider.SearchAsync("AAPL", limit: 1, CancellationToken.None);

        results.Should().BeEmpty();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("status=active")
            .And.Contain("asset_class=us_equity");
    }

    [Theory]
    [InlineData("crypto", "crypto")]
    [InlineData("option", "us_option")]
    [InlineData("bond", "fixed_income")]
    public async Task SearchAsync_WithAssetType_RequestsTheSelectedAssetClass(string assetType, string expectedAssetClass)
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient);

        var results = await provider.SearchAsync("BTC", limit: 1, assetType: assetType, ct: CancellationToken.None);

        results.Should().BeEmpty();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"asset_class={expectedAssetClass}");
    }

    [Theory]
    [InlineData(true, "paper-api.alpaca.markets")]
    [InlineData(false, "api.alpaca.markets")]
    public async Task Requests_UseTheTradingHostOfTheSelectedEnvironment(bool useSandbox, string expectedHost)
    {
        var hosts = new List<string>();
        using var handler = new StubHttpMessageHandler(request =>
        {
            hosts.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath == "/v2/assets"
                    ? "[{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}]"
                    : "{\"symbol\":\"AAPL\",\"name\":\"Apple\",\"tradable\":true}")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient, useSandbox: useSandbox);

        (await provider.SearchAsync("AAPL", 1, CancellationToken.None)).Should().ContainSingle();
        (await provider.GetDetailsAsync(new Meridian.Contracts.Domain.SymbolId("AAPL"))).Should().NotBeNull();

        // A paper key pair authenticates only against the paper host, and a live pair only live.
        hosts.Should().Equal(expectedHost, expectedHost);
    }

    [Fact]
    public async Task CryptoAssets_WithStringEncodedIncrements_AreSearchableAndCarryTradingEligibility()
    {
        // Shape of the Trading API's crypto asset: the order increments are JSON strings while the
        // maintenance margin is a JSON number. One string-encoded field used to discard the list.
        const string crypto = """
            {"id":"276e2673-764b-4ab6-a611-caf665ca6340","class":"crypto","exchange":"CRYPTO","symbol":"BTC/USD",
             "name":"Bitcoin  / US Dollar","status":"active","tradable":true,"marginable":false,
             "maintenance_margin_requirement":100,"margin_requirement_long":"100","margin_requirement_short":"100",
             "shortable":false,"easy_to_borrow":false,"fractionable":true,"attributes":["fractional_eh_enabled"],
             "min_order_size":"0.000013362","min_trade_increment":"0.000000001","price_increment":"0.000000001"}
            """;
        using var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath == "/v2/assets" ? $"[{crypto}]" : crypto)
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(KeyId, SecretKey, httpClient, useSandbox: true);

        var results = await provider.SearchAsync("BTC", limit: 5, assetType: "crypto", ct: CancellationToken.None);
        var details = await provider.GetDetailsAsync(new Meridian.Contracts.Domain.SymbolId("BTC/USD"));

        results.Should().ContainSingle().Which.Should().Match<Meridian.Core.Subscriptions.Models.SymbolSearchResult>(result =>
            result.Symbol == "BTC/USD" && result.AssetType == "Crypto");
        details.Should().NotBeNull();
        details!.TradingEligibility.Should().NotBeNull();
        details.TradingEligibility!.MinimumOrderSize.Should().Be(0.000013362m);
        details.TradingEligibility.MinimumTradeIncrement.Should().Be(0.000000001m);
        details.TradingEligibility.PriceIncrement.Should().Be(0.000000001m);
        details.TradingEligibility.MaintenanceMarginRequirement.Should().Be(100m);
        details.TradingEligibility.IsFractionable.Should().BeTrue();
    }
}
