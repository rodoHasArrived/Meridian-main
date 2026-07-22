using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="TwelveDataSymbolSearchProvider"/> symbol-search behavior.
/// All HTTP calls are stubbed; no live Twelve Data token or network access is required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TwelveDataSymbolSearchProviderTests
{
    private const string ApiKey = "test-twelve-data-key";

    private static readonly string SearchResponse = """
        {
          "data": [
            {
              "symbol": "AAPL",
              "instrument_name": "Apple Inc",
              "exchange": "NASDAQ",
              "mic_code": "XNGS",
              "exchange_timezone": "America/New_York",
              "instrument_type": "Common Stock",
              "country": "United States",
              "currency": "USD"
            },
            {
              "symbol": "AAPL",
              "instrument_name": "Apple Inc",
              "exchange": "BATS",
              "mic_code": "BATS",
              "instrument_type": "ETF",
              "country": "United States",
              "currency": "USD"
            },
            {
              "instrument_name": "Malformed row without symbol",
              "exchange": "NASDAQ",
              "instrument_type": "Common Stock",
              "country": "United States",
              "currency": "USD"
            }
          ],
          "status": "ok"
        }
        """;

    private static readonly string RegionalSearchResponse = """
        {
          "data": [
            {
              "symbol": "SHOP",
              "instrument_name": "Shopify Inc",
              "exchange": "NYSE",
              "mic_code": "XNYS",
              "instrument_type": "Common Stock",
              "country": "United States",
              "currency": "USD"
            },
            {
              "symbol": "SHOP",
              "instrument_name": "Shopify Inc",
              "exchange": "TSX",
              "mic_code": "XTSE",
              "instrument_type": "Common Stock",
              "country": "Canada",
              "currency": "CAD"
            }
          ],
          "status": "ok"
        }
        """;

    private static readonly string ErrorResponse = """
        {
          "code": 400,
          "message": "symbol is required",
          "status": "error"
        }
        """;

    [Fact]
    public async Task SearchAsync_WithSymbolSearchPayload_MapsResultsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(SearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("aapl", 10, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(r => r.Symbol).Should().Equal("AAPL", "AAPL");
        results[0].Name.Should().Be("Apple Inc");
        results[0].Exchange.Should().Be("NASDAQ");
        results[0].AssetType.Should().Be("Common Stock");
        results[0].Country.Should().Be("US");
        results[0].Currency.Should().Be("USD");
        results[0].Source.Should().Be("twelvedata");
        results[0].MatchScore.Should().BeGreaterThan(0);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/symbol_search");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("symbol=aapl")
            .And.Contain("outputsize=120")
            .And.Contain($"apikey={ApiKey}");
    }

    [Fact]
    public async Task SearchAsync_WithAssetAndExchangeFilters_AppliesClientSideFilters()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RegionalSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync(
            "shop",
            limit: 10,
            assetType: "Common Stock",
            exchange: "TSX",
            ct: CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("SHOP");
        results[0].Exchange.Should().Be("TSX");
        results[0].Country.Should().Be("CA");
        results[0].Currency.Should().Be("CAD");
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated search should not call Twelve Data."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(string.Empty, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithStatusError_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(ErrorResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailsAsync_UsesSymbolSearchPayload_ReturnsExactMatch()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(ApiKey, httpClient);

        var details = await provider.GetDetailsAsync(new SymbolId("AAPL"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("AAPL");
        details.Name.Should().Be("Apple Inc");
        details.Exchange.Should().Be("NASDAQ");
        details.AssetType.Should().Be("Common Stock");
        details.Country.Should().Be("US");
        details.Currency.Should().Be("USD");
        details.Source.Should().Be("twelvedata");
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataSymbolSearchProvider(ApiKey, httpClient);

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
