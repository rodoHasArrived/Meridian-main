using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="AlphaVantageSymbolSearchProvider"/> symbol-search behavior.
/// All HTTP calls are stubbed; no live Alpha Vantage token or network access is required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlphaVantageSymbolSearchProviderTests
{
    private const string ApiKey = "test-alpha-vantage-key";

    private static readonly string SearchResponse = """
        {
          "bestMatches": [
            {
              "1. symbol": "IBM",
              "2. name": "International Business Machines Corp",
              "3. type": "Equity",
              "4. region": "United States",
              "5. marketOpen": "09:30",
              "6. marketClose": "16:00",
              "7. timezone": "UTC-04",
              "8. currency": "USD",
              "9. matchScore": "1.0000"
            },
            {
              "1. symbol": "IBM.LON",
              "2. name": "International Business Machines Corp",
              "3. type": "Equity",
              "4. region": "United Kingdom",
              "5. marketOpen": "08:00",
              "6. marketClose": "16:30",
              "7. timezone": "UTC+01",
              "8. currency": "GBP",
              "9. matchScore": "0.7500"
            },
            {
              "2. name": "Malformed row without symbol",
              "3. type": "Equity",
              "4. region": "United States",
              "8. currency": "USD",
              "9. matchScore": "0.9000"
            }
          ]
        }
        """;

    private static readonly string RegionalSearchResponse = """
        {
          "bestMatches": [
            {
              "1. symbol": "SHOP",
              "2. name": "Shopify Inc",
              "3. type": "Equity",
              "4. region": "United States",
              "8. currency": "USD",
              "9. matchScore": "0.9000"
            },
            {
              "1. symbol": "SHOP.TRT",
              "2. name": "Shopify Inc",
              "3. type": "Equity",
              "4. region": "Canada",
              "8. currency": "CAD",
              "9. matchScore": "0.8800"
            }
          ]
        }
        """;

    private static readonly string RateLimitResponse = """
        {
          "Information": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 100 calls per day."
        }
        """;

    [Fact]
    public async Task SearchAsync_WithBestMatchesPayload_MapsResultsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(SearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("ibm", 10, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(r => r.Symbol).Should().Equal("IBM", "IBM.LON");
        results[0].Name.Should().Be("International Business Machines Corp");
        results[0].Exchange.Should().Be("United States");
        results[0].AssetType.Should().Be("Equity");
        results[0].Country.Should().Be("US");
        results[0].Currency.Should().Be("USD");
        results[0].Source.Should().Be("alphavantage");
        results[0].MatchScore.Should().Be(100);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/query");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("function=SYMBOL_SEARCH")
            .And.Contain("keywords=ibm")
            .And.Contain($"apikey={ApiKey}");
    }

    [Fact]
    public async Task SearchAsync_WithAssetAndRegionFilters_AppliesClientSideFilters()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RegionalSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync(
            "shop",
            limit: 10,
            assetType: "Equity",
            exchange: "Canada",
            ct: CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("SHOP.TRT");
        results[0].Exchange.Should().Be("Canada");
        results[0].Country.Should().Be("CA");
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated search should not call Alpha Vantage."));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(string.Empty, httpClient);

        var results = await provider.SearchAsync("IBM", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithRateLimitBody_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RateLimitResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("IBM", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailsAsync_UsesSymbolSearchPayload_ReturnsExactMatch()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(ApiKey, httpClient);

        var details = await provider.GetDetailsAsync(new SymbolId("IBM"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("IBM");
        details.Name.Should().Be("International Business Machines Corp");
        details.Exchange.Should().Be("United States");
        details.AssetType.Should().Be("Equity");
        details.Country.Should().Be("US");
        details.Currency.Should().Be("USD");
        details.Source.Should().Be("alphavantage");
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageSymbolSearchProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.SearchAsync("IBM", 10, cts.Token);

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
