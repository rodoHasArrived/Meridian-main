using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="TiingoSymbolSearchProvider"/> symbol-search behavior.
/// All HTTP calls are stubbed; no live Tiingo token or network access is required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TiingoSymbolSearchProviderTests
{
    private const string ApiToken = "test-tiingo-token";

    private static readonly string SearchResponse = """
        [
          {
            "ticker": "aapl",
            "name": "Apple Inc",
            "assetType": "Stock",
            "exchangeCode": "NASDAQ",
            "exchangeName": "NASDAQ Stock Exchange",
            "countryCode": "US",
            "currency": "USD",
            "isActive": true
          },
          {
            "ticker": "AAPL.P",
            "name": "Apple Preferred",
            "assetType": "Stock",
            "exchangeCode": "NYSE",
            "exchangeName": "New York Stock Exchange",
            "countryCode": "US",
            "currency": "USD",
            "isActive": false
          },
          {
            "name": "Malformed row without ticker",
            "assetType": "Stock",
            "exchangeCode": "NASDAQ",
            "isActive": true
          }
        ]
        """;

    private static readonly string RegionalSearchResponse = """
        [
          {
            "ticker": "SHOP",
            "name": "Shopify Inc",
            "assetType": "Stock",
            "exchangeCode": "NYSE",
            "countryCode": "US",
            "currency": "USD",
            "isActive": true
          },
          {
            "ticker": "SHOP",
            "name": "Shopify Inc",
            "assetType": "Stock",
            "exchangeCode": "TSX",
            "countryCode": "CA",
            "currency": "CAD",
            "isActive": true
          },
          {
            "ticker": "SHOP.DB",
            "name": "Shopify Convertible Debenture",
            "assetType": "Bond",
            "exchangeCode": "TSX",
            "countryCode": "CA",
            "currency": "CAD",
            "isActive": true
          }
        ]
        """;

    private static readonly string ErrorResponse = """
        {
          "detail": "Invalid token"
        }
        """;

    [Fact]
    public async Task SearchAsync_WithUtilitiesPayload_MapsResultsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(SearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(ApiToken, httpClient);

        var results = await provider.SearchAsync("aapl", 10, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("AAPL");
        results[0].Name.Should().Be("Apple Inc");
        results[0].Exchange.Should().Be("NASDAQ");
        results[0].AssetType.Should().Be("Stock");
        results[0].Country.Should().Be("US");
        results[0].Currency.Should().Be("USD");
        results[0].Source.Should().Be("tiingo");
        results[0].MatchScore.Should().BeGreaterThan(0);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/tiingo/utilities/search");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("query=aapl")
            .And.Contain($"token={ApiToken}");
        observedRequest.Headers.Authorization.Should().NotBeNull();
        observedRequest.Headers.Authorization!.Scheme.Should().Be("Token");
        observedRequest.Headers.Authorization.Parameter.Should().Be(ApiToken);
    }

    [Fact]
    public async Task SearchAsync_WithAssetAndExchangeFilters_AppliesClientSideFilters()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RegionalSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(ApiToken, httpClient);

        var results = await provider.SearchAsync(
            "shop",
            limit: 10,
            assetType: "Stock",
            exchange: "TSX",
            ct: CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("SHOP");
        results[0].Exchange.Should().Be("TSX");
        results[0].Country.Should().Be("CA");
        results[0].Currency.Should().Be("CAD");
    }

    [Fact]
    public async Task SearchAsync_WithoutApiToken_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated search should not call Tiingo."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(string.Empty, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithErrorBody_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(ErrorResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(ApiToken, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailsAsync_UsesSymbolSearchPayload_ReturnsExactMatch()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(ApiToken, httpClient);

        var details = await provider.GetDetailsAsync(new SymbolId("AAPL"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("AAPL");
        details.Name.Should().Be("Apple Inc");
        details.Exchange.Should().Be("NASDAQ");
        details.AssetType.Should().Be("Stock");
        details.Country.Should().Be("US");
        details.Currency.Should().Be("USD");
        details.Source.Should().Be("tiingo");
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TiingoSymbolSearchProvider(ApiToken, httpClient);

        Func<Task> act = () => provider.SearchAsync("AAPL", 10, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(0);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
