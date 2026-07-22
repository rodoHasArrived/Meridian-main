using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Twelve Data tests for time_series request shape, credential gating, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TwelveDataHistoricalDataProviderTests
{
    private const string ApiKey = "test-twelve-data-key";

    private static readonly string TimeSeriesResponse = """
        {
          "meta": {
            "symbol": "MSFT",
            "interval": "1day",
            "currency": "USD"
          },
          "status": "ok",
          "values": [
            {
              "datetime": "2024-01-04",
              "open": "378.40000",
              "high": "380.00000",
              "low": "376.90000",
              "close": "379.75000",
              "volume": "2400000"
            },
            {
              "datetime": "2024-01-03",
              "open": "372.80000",
              "high": "376.25000",
              "low": "371.10000",
              "close": "374.50000",
              "volume": "2180000"
            },
            {
              "datetime": "2024-01-02",
              "open": "370.10000",
              "high": "372.00000",
              "low": "367.80000",
              "close": "369.60000",
              "volume": "1930000"
            },
            {
              "datetime": "2024-01-02",
              "open": "0.00000",
              "high": "372.00000",
              "low": "367.80000",
              "close": "369.60000",
              "volume": "900000"
            }
          ]
        }
        """;

    private static readonly string StatusErrorResponse = """
        {
          "code": 400,
          "message": "**symbol** not found: UNKNOWN",
          "status": "error"
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_TimeSeriesPayload_MapsFiltersAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(TimeSeriesResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetDailyBarsAsync("MSFT", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "MSFT");
        bars.Select(b => b.Source).Should().OnlyContain(source => source == "twelvedata");

        bars[0].SessionDate.Should().Be(from);
        bars[0].Open.Should().Be(370.10000m);
        bars[0].High.Should().Be(372.00000m);
        bars[0].Low.Should().Be(367.80000m);
        bars[0].Close.Should().Be(369.60000m);
        bars[0].Volume.Should().Be(1_930_000);
        bars[0].SequenceNumber.Should().Be(from.DayNumber);

        bars[1].SessionDate.Should().Be(to);
        bars[1].Open.Should().Be(372.80000m);
        bars[1].High.Should().Be(376.25000m);
        bars[1].Low.Should().Be(371.10000m);
        bars[1].Close.Should().Be(374.50000m);
        bars[1].Volume.Should().Be(2_180_000);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsoluteUri.Should().StartWith("https://api.twelvedata.com/time_series?");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("symbol=MSFT")
            .And.Contain("interval=1day")
            .And.Contain("start_date=2024-01-02")
            .And.Contain("end_date=2024-01-03")
            .And.Contain("outputsize=5000")
            .And.Contain("format=JSON")
            .And.Contain($"apikey={ApiKey}");
        observedRequest.Headers.Authorization.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenApiKeyMissing_ReturnsEmptyWithoutHttpCall()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Missing Twelve Data credentials should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(string.Empty, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);
        var bars = await provider.GetDailyBarsAsync("MSFT", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), cts.Token);

        available.Should().BeFalse();
        bars.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenBodyStatusIsError_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(StatusErrorResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null, cts.Token);

        bars.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp429_ThrowsAndRecordsRetryAfter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate limited", Encoding.UTF8, "text/plain")
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("MSFT", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("twelvedata");
        exception.Which.Symbol.Should().Be("MSFT");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
        var rateLimitInfo = provider.GetRateLimitInfo();
        rateLimitInfo.IsLimited.Should().BeTrue();
        rateLimitInfo.RetryAfter.Should().NotBeNull();
        rateLimitInfo.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(1));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
