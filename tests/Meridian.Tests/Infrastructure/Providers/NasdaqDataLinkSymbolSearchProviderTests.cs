using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Nasdaq Data Link dataset-search tests for reference discovery and credential gating.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NasdaqDataLinkSymbolSearchProviderTests
{
    private const string ApiKey = "test-nasdaq-key";

    private static readonly string DatasetSearchResponse = """
        {
          "datasets": [
            {
              "id": 9775409,
              "dataset_code": "AAPL",
              "database_code": "EOD",
              "name": "Apple Inc. (AAPL) Stock Prices, Dividends and Splits",
              "description": "End-of-day prices with adjusted fields and corporate action evidence.",
              "frequency": "daily",
              "premium": false
            },
            {
              "id": 9775410,
              "dataset_code": "AAPL",
              "database_code": "WIKI",
              "name": "Apple Inc. historical prices",
              "description": "Legacy WIKI price dataset.",
              "frequency": "daily",
              "premium": false
            },
            {
              "id": 9775411,
              "database_code": "EOD",
              "name": "Malformed row without dataset code",
              "premium": false
            }
          ],
          "meta": {
            "query": "apple",
            "per_page": 100,
            "current_page": 1
          }
        }
        """;

    [Fact]
    public async Task SearchAsync_DatasetSearchPayload_MapsDatasetCodesAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(DatasetSearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("apple", limit: 10, ct: cts.Token);

        results.Should().HaveCount(2);
        results.Select(result => result.Symbol).Should().Equal("EOD/AAPL", "WIKI/AAPL");
        results[0].Name.Should().Be("Apple Inc. (AAPL) Stock Prices, Dividends and Splits");
        results[0].Exchange.Should().Be("EOD");
        results[0].AssetType.Should().Be("Dataset");
        results[0].Country.Should().BeNull();
        results[0].Currency.Should().BeNull();
        results[0].Source.Should().Be("nasdaq");
        results[0].MatchScore.Should().BeGreaterThan(0);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.Host.Should().Be("data.nasdaq.com");
        observedRequest.RequestUri.AbsolutePath.Should().Be("/api/v3/datasets.json");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("query=apple")
            .And.Contain("per_page=100")
            .And.Contain("page=1")
            .And.Contain($"api_key={ApiKey}");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_WithDatabaseFilter_AddsDatabaseCodeAndAppliesClientFilters()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(DatasetSearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync(
            "apple",
            limit: 10,
            assetType: "Dataset",
            exchange: "EOD",
            ct: CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("EOD/AAPL");
        results[0].Exchange.Should().Be("EOD");
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("database_code=EOD");
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated Nasdaq Data Link search should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(string.Empty, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithNasdaqQuotaBody_ThrowsProviderAttributedFailure()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "quandl_error": {
                "code": "QELx01",
                "message": "API limit exceeded."
              }
            }
            """));
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(ApiKey, httpClient);

        var act = () => provider.SearchAsync("AAPL", 10, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("nasdaq");
        exception.Which.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetDetailsAsync_UsesDatasetSearchPayload_ReturnsExactDatasetCode()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(DatasetSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(ApiKey, httpClient);

        var details = await provider.GetDetailsAsync(new SymbolId("EOD/AAPL"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("EOD/AAPL");
        details.Name.Should().Be("Apple Inc. (AAPL) Stock Prices, Dividends and Splits");
        details.Description.Should().Contain("corporate action");
        details.Exchange.Should().Be("EOD");
        details.AssetType.Should().Be("Dataset");
        details.Industry.Should().Be("daily");
        details.Source.Should().Be("nasdaq");
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(DatasetSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkSymbolSearchProvider(ApiKey, httpClient);

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
