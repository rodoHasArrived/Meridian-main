using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific FRED series-search tests for economic indicator discovery and credential gating.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FredSymbolSearchProviderTests
{
    private const string ApiKey = "test-fred-key";

    private static readonly string SeriesSearchResponse = """
        {
          "count": 2,
          "seriess": [
            {
              "id": "UNRATE",
              "title": "Unemployment Rate",
              "frequency": "Monthly",
              "frequency_short": "M",
              "units": "Percent",
              "seasonal_adjustment": "Seasonally Adjusted",
              "popularity": 96,
              "notes": "The unemployment rate represents the number of unemployed as a percentage of the labor force."
            },
            {
              "id": "U6RATE",
              "title": "Total unemployed plus marginally attached workers",
              "frequency": "Monthly",
              "frequency_short": "M",
              "popularity": 80,
              "notes": "Broader labor underutilization series."
            },
            {
              "title": "Malformed row without id",
              "frequency": "Monthly",
              "popularity": 100
            }
          ]
        }
        """;

    [Fact]
    public async Task SearchAsync_SeriesSearchPayload_MapsEconomicSeriesAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(SeriesSearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("unemployment", limit: 10, ct: cts.Token);

        results.Should().HaveCount(2);
        results.Select(result => result.Symbol).Should().Equal("UNRATE", "U6RATE");
        results[0].Name.Should().Be("Unemployment Rate");
        results[0].Exchange.Should().Be("FRED");
        results[0].AssetType.Should().Be("Economic Series");
        results[0].Country.Should().Be("US");
        results[0].Currency.Should().BeNull();
        results[0].Source.Should().Be("fred");
        results[0].MatchScore.Should().BeGreaterThanOrEqualTo(96);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/fred/series/search");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("search_text=unemployment")
            .And.Contain($"api_key={ApiKey}")
            .And.Contain("file_type=json")
            .And.Contain("order_by=search_rank")
            .And.Contain("sort_order=asc");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_WithAssetAndExchangeFilters_AppliesEconomicSeriesFilter()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SeriesSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync(
            "unemployment",
            limit: 10,
            assetType: "Economic Series",
            exchange: "FRED",
            ct: CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(result => result.Exchange == "FRED" && result.AssetType == "Economic Series");
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated FRED search should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(string.Empty, httpClient);

        var results = await provider.SearchAsync("UNRATE", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithFredErrorBody_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "error_code": 400,
              "error_message": "Bad Request. The value for variable api_key is not registered."
            }
            """));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(ApiKey, httpClient);

        var results = await provider.SearchAsync("UNRATE", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailsAsync_UsesSeriesSearchPayload_ReturnsExactSeries()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SeriesSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(ApiKey, httpClient);

        var details = await provider.GetDetailsAsync(new SymbolId("UNRATE"), CancellationToken.None);

        details.Should().NotBeNull();
        details!.Symbol.Should().Be("UNRATE");
        details.Name.Should().Be("Unemployment Rate");
        details.Description.Should().Contain("unemployed");
        details.Exchange.Should().Be("FRED");
        details.AssetType.Should().Be("Economic Series");
        details.Industry.Should().Be("M");
        details.Country.Should().Be("US");
        details.Source.Should().Be("fred");
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SeriesSearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new FredSymbolSearchProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.SearchAsync("UNRATE", 10, cts.Token);

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
