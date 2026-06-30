using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Unit tests for <see cref="FinnhubSymbolSearchProviderRefactored"/> symbol-search behavior.
/// All HTTP calls are stubbed; no live Finnhub token or network access is required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinnhubSymbolSearchProviderTests
{
    private const string ApiKey = "test-finnhub-token";

    private static readonly string SearchResponse = """
        {
          "count": 4,
          "result": [
            {
              "description": "APPLE INC",
              "displaySymbol": "AAPL",
              "symbol": "AAPL",
              "type": "Common Stock"
            },
            {
              "description": "APPLE INC",
              "displaySymbol": "AAPL.SW",
              "symbol": "AAPL.SW",
              "type": "Common Stock"
            },
            {
              "description": "APPLE HOSPITALITY REIT INC",
              "displaySymbol": "APLE",
              "symbol": "APLE",
              "type": "REIT"
            },
            {
              "description": "MISSING SYMBOL SHOULD BE IGNORED",
              "displaySymbol": "MISS",
              "type": "Common Stock"
            }
          ]
        }
        """;

    [Fact]
    public async Task SearchAsync_WithFinnhubSearchPayload_MapsResultsAndSendsToken()
    {
        HttpRequestMessage? observedRequest = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(SearchResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(ApiKey, httpClient);

        var results = await provider.SearchAsync("AAPL", 2, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Select(r => r.Symbol).Should().Equal("AAPL", "AAPL.SW");

        var first = results[0];
        first.Name.Should().Be("APPLE INC");
        first.Exchange.Should().Be("US");
        first.AssetType.Should().Be("Common Stock");
        first.Source.Should().Be("finnhub");
        first.MatchScore.Should().BeGreaterThan(results[1].MatchScore);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/search");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("q=AAPL")
            .And.Contain($"token={ApiKey}");
        observedRequest.Headers.GetValues("X-Finnhub-Token").Should().Contain(ApiKey);
    }

    [Fact]
    public async Task SearchAsync_WithAssetAndExchangeFilters_AppliesClientSideFilters()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(ApiKey, httpClient);

        var results = await provider.SearchAsync(
            "AAPL",
            limit: 10,
            assetType: "Common Stock",
            exchange: "SW",
            ct: CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Symbol.Should().Be("AAPL.SW");
        results[0].Exchange.Should().Be("SW");
    }

    [Fact]
    public async Task SearchAsync_WithBlankQuery_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Blank queries should not call Finnhub."));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(ApiKey, httpClient);

        var results = await provider.SearchAsync("   ", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithoutApiKey_ReturnsEmptyListWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Credential-gated search should not call Finnhub."));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(string.Empty, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithRateLimitResponse_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(ApiKey, httpClient);

        var results = await provider.SearchAsync("AAPL", 10, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithCancelledTokenBeforeRequest_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(SearchResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubSymbolSearchProviderRefactored(ApiKey, httpClient);

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
