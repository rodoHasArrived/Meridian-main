using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Globalization;
using System.Text.Json;
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
            .And.Contain("end_date=2024-01-03 23:59:59")
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
    public async Task GetDailyBarsAsync_WhenSymbolIsUnknown_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(StatusErrorResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null, cts.Token);

        bars.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task GetDailyBarsAsync_WhenBodyRejectsCredentials_ThrowsConnectionException(int code)
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(ErrorBody(code, $"Invalid key: {ApiKey}")));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var act = () => provider.GetDailyBarsAsync("MSFT", null, null);

        var error = await act.Should().ThrowAsync<ConnectionException>();
        error.Which.Provider.Should().Be("twelvedata");
        error.Which.Message.Should().NotContain(ApiKey);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenBodyReportsQuota_ThrowsAndPublishesRateLimit()
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(ErrorBody(429, "Quota exceeded")));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));
        var notifications = 0;
        provider.OnRateLimitHit += info =>
        {
            info.IsLimited.Should().BeTrue();
            notifications++;
        };

        var act = () => provider.GetDailyBarsAsync("MSFT", null, null);

        var error = await act.Should().ThrowAsync<RateLimitException>();
        error.Which.Provider.Should().Be("twelvedata");
        error.Which.Symbol.Should().Be("MSFT");
        error.Which.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        provider.GetRateLimitInfo().IsLimited.Should().BeTrue();
        notifications.Should().Be(1);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"ok\"}")]
    [InlineData("{\"status\":\"ok\",\"values\":null}")]
    [InlineData("{\"status\":\"ok\",\"values\":[null]}")]
    [InlineData("{\"status\":\"ok\",\"values\":[{\"datetime\":\"invalid\"}]}")]
    [InlineData("{\"status\":\"error\",\"code\":400,\"message\":\"Invalid interval\"}")]
    [InlineData("{\"status\":\"error\",\"code\":500,\"message\":\"Internal error\"}")]
    public async Task GetDailyBarsAsync_WhenResponseCannotEstablishCoverage_ThrowsProviderException(string body)
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(body));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var act = () => provider.GetDailyBarsAsync("MSFT", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3));

        var error = await act.Should().ThrowAsync<DataProviderException>();
        error.Which.Provider.Should().Be("twelvedata");
        error.Which.Symbol.Should().Be("MSFT");
    }

    [Theory]
    [InlineData("{\"status\":\"ok\",\"values\":[]}")]
    [InlineData("{\"status\":\"error\",\"code\":400,\"message\":\"No data is available on the specified dates. Try setting different start/end dates.\"}")]
    public async Task GetDailyBarsAsync_WhenDateWindowHasNoData_ReturnsEmpty(string body)
    {
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(body));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var bars = await provider.GetDailyBarsAsync("MSFT", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3));

        bars.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_LongDailyHistory_ReadsBoundedWindowsAndDeduplicatesSessions()
    {
        var from = new DateOnly(2000, 1, 1);
        var to = from.AddDays(5001);
        var requests = new List<(DateOnly Start, DateOnly End)>();
        var clock = new TestClock();
        using var handler = new StubHttpMessageHandler(request =>
        {
            var window = ReadWindow(request);
            requests.Add(window);
            (window.End.DayNumber - window.Start.DayNumber + 1).Should().BeLessThanOrEqualTo(5000);
            clock.Advance();
            return JsonResponse(DailyBody(window.Start, window.End));
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler), timeProvider: clock);

        var bars = await provider.GetDailyBarsAsync("BTC/USD", from, to);

        requests.Should().Equal((from, from.AddDays(4999)), (from.AddDays(4999), to));
        bars.Should().HaveCount(5002).And.BeInAscendingOrder(bar => bar.SessionDate);
        bars.Select(bar => bar.SessionDate).Should().OnlyHaveUniqueItems();
        bars[0].SessionDate.Should().Be(from);
        bars[^1].SessionDate.Should().Be(to);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyBarsAsync_EmptyEarlyWindow_ContinuesToLaterHistory()
    {
        var from = new DateOnly(2000, 1, 1);
        var to = from.AddDays(5001);
        var calls = 0;
        var clock = new TestClock();
        using var handler = new StubHttpMessageHandler(request =>
        {
            var window = ReadWindow(request);
            clock.Advance();
            return JsonResponse(++calls == 1
                ? ErrorBody(400, "No data is available on the specified dates.")
                : DailyBody(window.Start, window.End));
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler), timeProvider: clock);

        var bars = await provider.GetDailyBarsAsync("MSFT", from, to);

        bars.Should().HaveCount(3);
        calls.Should().Be(2);
        bars[^1].SessionDate.Should().Be(to);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"status\":\"error\",\"code\":500}")]
    [InlineData("{\"status\":\"error\",\"code\":400,\"message\":\"**symbol** not found: MSFT\"}")]
    public async Task GetDailyBarsAsync_LaterWindowFailure_ThrowsInsteadOfReturningPartialHistory(string failureBody)
    {
        var from = new DateOnly(2000, 1, 1);
        var calls = 0;
        var clock = new TestClock();
        using var handler = new StubHttpMessageHandler(_ =>
        {
            clock.Advance();
            return JsonResponse(++calls == 1 ? DailyBody(from, from) : failureBody);
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler), timeProvider: clock);

        var act = () => provider.GetDailyBarsAsync("MSFT", from, from.AddDays(5001));

        await act.Should().ThrowAsync<DataProviderException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyBarsAsync_RepeatedOutOfRangeWindow_FailsWithoutRetryLoop()
    {
        var from = new DateOnly(2000, 1, 1);
        var clock = new TestClock();
        using var handler = new StubHttpMessageHandler(_ =>
        {
            clock.Advance();
            return JsonResponse(DailyBody(from, from));
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler), timeProvider: clock);

        var act = () => provider.GetDailyBarsAsync("MSFT", from, from.AddDays(5001));

        await act.Should().ThrowAsync<DataProviderException>();
        handler.CallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task GetDailyBarsAsync_LaterWindowHttpFailure_ThrowsInsteadOfReturningPartialHistory(int status)
    {
        var from = new DateOnly(2000, 1, 1);
        var calls = 0;
        var clock = new TestClock();
        using var handler = new StubHttpMessageHandler(_ =>
        {
            clock.Advance();
            return ++calls == 1 ? JsonResponse(DailyBody(from, from))
                : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("Refused") };
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler), timeProvider: clock);

        var act = () => provider.GetDailyBarsAsync("MSFT", from, from.AddDays(5001));

        if (status == 401)
            await act.Should().ThrowAsync<ConnectionException>();
        else if (status == 429)
            await act.Should().ThrowAsync<RateLimitException>();
        else
            await act.Should().ThrowAsync<DataProviderException>();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyBarsAsync_FirstWindowHttp404_ReturnsEmptyWithoutRequestingMoreWindows()
    {
        var from = new DateOnly(2000, 1, 1);
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", from, from.AddDays(5001));

        bars.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_CancelledDuringFirstWindow_DoesNotReturnPartialHistory()
    {
        var from = new DateOnly(2000, 1, 1);
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler(_ =>
        {
            cancellation.Cancel();
            return JsonResponse(DailyBody(from, from));
        });
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var act = () => provider.GetDailyBarsAsync("MSFT", from, from.AddDays(5001), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_InvertedDates_RejectsBeforeHttp()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No HTTP expected."));
        using var provider = new TwelveDataHistoricalDataProvider(ApiKey, new HttpClient(handler));

        var act = () => provider.GetDailyBarsAsync("MSFT", new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 2));

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
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

    private static string ErrorBody(int code, string message)
        => JsonSerializer.Serialize(new { status = "error", code, message });

    private static string DailyBody(DateOnly start, DateOnly end)
        => JsonSerializer.Serialize(new
        {
            status = "ok",
            values = Enumerable.Range(start.DayNumber, end.DayNumber - start.DayNumber + 1).Reverse()
                .Select(day => new
                {
                    datetime = DateOnly.FromDayNumber(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    open = "100", high = "105", low = "95", close = "102", volume = "1000"
                })
        });

    private static (DateOnly Start, DateOnly End) ReadWindow(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
        query["end_date"].Should().EndWith(" 23:59:59");
        return (DateOnly.ParseExact(query["start_date"], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(query["end_date"][..10], "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 6, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance() => _utcNow += TimeSpan.FromSeconds(8);
    }
}
