using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Core.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.SymbolSearch;

/// <summary>
/// OpenFIGI ambiguity tests for exchange-scoped mapping requests and deterministic enrichment behavior.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpenFigiClientAmbiguityTests
{
    private const string ApiKey = "test-openfigi-key";

    private static readonly string AmbiguousAaplMappingResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "shareClassFIGI": "BBG001S5N8V8",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW",
                "securityDescription": "APPLE INC"
              },
              {
                "figi": "BBG000QNH748",
                "compositeFIGI": "BBG000B9Y5X2",
                "shareClassFIGI": "BBG001S5N8V8",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "US",
                "securityDescription": "APPLE INC"
              }
            ]
          }
        ]
        """;

    private static readonly string FilterAmbiguityResponse = """
        {
          "data": [
            {
              "figi": "BBG000B9XRY4",
              "compositeFIGI": "BBG000B9Y5X2",
              "securityType": "Common Stock",
              "marketSector": "Equity",
              "ticker": "AAPL",
              "name": "APPLE INC",
              "exchCode": "UW"
            },
            {
              "figi": "BBG000QNH748",
              "compositeFIGI": "BBG000B9Y5X2",
              "securityType": "Common Stock",
              "marketSector": "Equity",
              "ticker": "AAPL",
              "name": "APPLE INC",
              "exchCode": "US"
            }
          ],
          "total": 2
        }
        """;

    [Fact]
    public async Task LookupByTickerAsync_AmbiguousTicker_SendsExchangeScopedMappingRequestAndPreservesCandidates()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(AmbiguousAaplMappingResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(ApiKey, httpClient);

        var results = await client.LookupByTickerAsync("aapl", exchangeCode: "US", marketSector: "Equity", cts.Token);

        results.Should().HaveCount(2);
        results.Select(r => r.Ticker).Should().OnlyContain(ticker => ticker == "AAPL");
        results.Select(r => r.CompositeFigi).Should().OnlyContain(figi => figi == "BBG000B9Y5X2");
        results.Select(r => r.ExchangeCode).Should().Equal("US", "UW");

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Post);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsoluteUri.Should().Be("https://api.openfigi.com/v3/mapping");
        observedRequest.Headers.GetValues("X-OPENFIGI-APIKEY").Should().Contain(ApiKey);
        observedRequest.Headers.UserAgent.ToString().Should().Contain("Meridian/1.0");

        var requestJson = await observedRequest.Content!.ReadAsStringAsync(cts.Token);
        requestJson.Should().Contain("\"idType\":\"TICKER\"")
            .And.Contain("\"idValue\":\"AAPL\"")
            .And.Contain("\"exchCode\":\"US\"")
            .And.Contain("\"marketSecDes\":\"Equity\"");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task EnrichWithFigiAsync_AmbiguousMapping_PrefersSearchResultExchange()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(AmbiguousAaplMappingResponse));
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(ApiKey, httpClient);
        var searchResults = new[]
        {
            new SymbolSearchResult("AAPL", "Apple Inc.", "US", "Common Stock", null, null, "finnhub")
        };

        var enriched = await client.EnrichWithFigiAsync(searchResults, cts.Token);

        enriched.Should().ContainSingle();
        enriched[0].Symbol.Should().Be("AAPL");
        enriched[0].Source.Should().Be("finnhub");
        enriched[0].Figi.Should().Be("BBG000QNH748");
        enriched[0].CompositeFigi.Should().Be("BBG000B9Y5X2");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_FilterRequest_SendsAmbiguityNarrowingFieldsAndHonorsLimit()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(FilterAmbiguityResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(ApiKey, httpClient);

        var results = await client.SearchAsync(
            "Apple",
            exchangeCode: "US",
            marketSector: "Equity",
            securityType: "Common Stock",
            limit: 1,
            ct: cts.Token);

        results.Should().ContainSingle();
        results[0].Figi.Should().Be("BBG000QNH748");
        results[0].ExchangeCode.Should().Be("US");

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsoluteUri.Should().Be("https://api.openfigi.com/v3/filter");
        var requestJson = await observedRequest.Content!.ReadAsStringAsync(cts.Token);
        requestJson.Should().Contain("\"query\":\"Apple\"")
            .And.Contain("\"exchCode\":\"US\"")
            .And.Contain("\"marketSecDes\":\"Equity\"")
            .And.Contain("\"securityType\":\"Common Stock\"");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task LookupByTickerAsync_WhenCancelledBeforeRequest_ThrowsWithoutHttpCall()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Cancelled OpenFIGI lookup should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(ApiKey, httpClient);

        Func<Task> act = () => client.LookupByTickerAsync("AAPL", ct: cts.Token);

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
