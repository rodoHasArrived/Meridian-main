using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Nasdaq Data Link tests for dataset request shape, adjusted-column mapping, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NasdaqDataLinkHistoricalDataProviderTests
{
    private const string ApiKey = "test-nasdaq-key";

    private static readonly string WikiDatasetResponse = """
        {
          "dataset": {
            "dataset_code": "BRK_B",
            "database_code": "EOD",
            "column_names": [
              "Date",
              "Open",
              "High",
              "Low",
              "Close",
              "Volume",
              "Ex-Dividend",
              "Split Ratio",
              "Adj. Open",
              "Adj. High",
              "Adj. Low",
              "Adj. Close",
              "Adj. Volume"
            ],
            "data": [
              ["2024-01-04", 378.40, 380.00, 376.90, 379.75, 2400000, 0.24, 2.0, 189.20, 190.00, 188.45, 189.875, 4800000],
              ["2024-01-03", 372.80, 376.25, 371.10, 374.50, 2180000, 0.0, 1.0, 186.40, 188.125, 185.55, 187.25, 4360000],
              ["2024-01-02", 370.10, 372.00, 367.80, 369.60, 1930000, 0.0, 1.0, 185.05, 186.00, 183.90, 184.80, 3860000],
              ["2024-01-01", 0.00, 372.00, 367.80, 369.60, 900000, 0.0, 1.0, 0.00, 186.00, 183.90, 184.80, 1800000]
            ]
          }
        }
        """;

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_DatasetPayload_MapsAdjustedColumnsAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(WikiDatasetResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(ApiKey, database: "EOD", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetAdjustedDailyBarsAsync("brk.b", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "BRK.B");
        bars.Select(b => b.Source).Should().OnlyContain(source => source == "nasdaq");

        bars[0].SessionDate.Should().Be(from);
        bars[0].Open.Should().Be(370.10m);
        bars[0].High.Should().Be(372.00m);
        bars[0].Low.Should().Be(367.80m);
        bars[0].Close.Should().Be(369.60m);
        bars[0].Volume.Should().Be(1_930_000);
        bars[0].AdjustedOpen.Should().Be(185.05m);
        bars[0].AdjustedHigh.Should().Be(186.00m);
        bars[0].AdjustedLow.Should().Be(183.90m);
        bars[0].AdjustedClose.Should().Be(184.80m);
        bars[0].AdjustedVolume.Should().Be(3_860_000);
        bars[0].SequenceNumber.Should().Be(from.DayNumber);

        bars[1].SessionDate.Should().Be(to);
        bars[1].AdjustedOpen.Should().Be(186.40m);
        bars[1].AdjustedHigh.Should().Be(188.125m);
        bars[1].AdjustedLow.Should().Be(185.55m);
        bars[1].AdjustedClose.Should().Be(187.25m);
        bars[1].AdjustedVolume.Should().Be(4_360_000);
        bars[1].DividendAmount.Should().BeNull();
        bars[1].SplitFactor.Should().BeNull();

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.Host.Should().Be("data.nasdaq.com");
        observedRequest.RequestUri.AbsolutePath.Should().Be("/api/v3/datasets/EOD/BRK_B.json");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"api_key={ApiKey}")
            .And.Contain("start_date=2024-01-02")
            .And.Contain("end_date=2024-01-03");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_DatasetPayload_PrefersAdjustedValuesForBackfill()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(WikiDatasetResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(ApiKey, database: "EOD", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync(
            "BRK.B",
            new DateOnly(2024, 1, 4),
            new DateOnly(2024, 1, 4),
            cts.Token);

        bars.Should().ContainSingle();
        var bar = bars[0];
        bar.Open.Should().Be(189.20m);
        bar.High.Should().Be(190.00m);
        bar.Low.Should().Be(188.45m);
        bar.Close.Should().Be(189.875m);
        bar.Volume.Should().Be(4_800_000);
    }

    [Fact]
    public async Task IsAvailableAsync_WithApiKey_UsesMetadataEndpointAndQueryCredential()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse("""{ "dataset": { "dataset_code": "AAPL" } }""");
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(ApiKey, database: "EOD", httpClient: httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v3/datasets/EOD/AAPL/metadata.json");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain($"api_key={ApiKey}");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task IsAvailableAsync_WithoutApiKey_UsesMetadataEndpointWithoutCredential()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse("""{ "dataset": { "dataset_code": "AAPL" } }""");
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: null, database: "WIKI", httpClient: httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v3/datasets/WIKI/AAPL/metadata.json");
        observedRequest.RequestUri.Query.Should().BeEmpty("Nasdaq Data Link credentials are optional for configured free datasets");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_WhenHttp429_ThrowsAndRecordsRetryAfter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate limited", Encoding.UTF8, "text/plain")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(25));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(ApiKey, httpClient: httpClient);

        Func<Task> act = () => provider.GetAdjustedDailyBarsAsync("AAPL", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("nasdaq");
        exception.Which.Symbol.Should().Be("AAPL");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(25));
        var rateLimitInfo = provider.GetRateLimitInfo();
        rateLimitInfo.IsLimited.Should().BeTrue();
        rateLimitInfo.RetryAfter.Should().NotBeNull();
        rateLimitInfo.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(1));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
