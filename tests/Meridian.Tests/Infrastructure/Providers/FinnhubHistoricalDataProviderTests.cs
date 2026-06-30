using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Finnhub tests for stock/candle request shape, parsing, intraday normalization, and rate-limit handling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinnhubHistoricalDataProviderTests
{
    private const string ApiKey = "test-finnhub-key";

    [Fact]
    public async Task GetDailyBarsAsync_CandlePayload_MapsFiltersAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CandlePayload(
                (UnixSeconds(2024, 1, 1), 188.00m, 189.50m, 187.25m, 188.75m, 990_000m),
                (UnixSeconds(2024, 1, 2), 189.00m, 190.00m, 188.75m, 189.50m, 1_000_000m),
                (UnixSeconds(2024, 1, 3), 191.00m, 192.00m, 190.50m, 191.25m, 1_100_000m),
                (UnixSeconds(2024, 1, 4), 192.50m, 193.00m, 0.00m, 192.00m, 1_200_000m),
                (UnixSeconds(2024, 1, 5), 193.50m, 194.00m, 192.50m, 193.00m, 1_300_000m)));
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(ApiKey, httpClient);
        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 4);

        var bars = await provider.GetDailyBarsAsync("msft", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(bar => bar.SessionDate);
        bars.Select(bar => bar.Symbol).Should().OnlyContain(symbol => symbol == "MSFT");
        bars.Select(bar => bar.Source).Should().OnlyContain(source => source == "finnhub");

        bars[0].SessionDate.Should().Be(from);
        bars[0].Open.Should().Be(189.00m);
        bars[0].High.Should().Be(190.00m);
        bars[0].Low.Should().Be(188.75m);
        bars[0].Close.Should().Be(189.50m);
        bars[0].Volume.Should().Be(1_000_000);
        bars[0].SequenceNumber.Should().Be(from.DayNumber);

        bars[1].SessionDate.Should().Be(new DateOnly(2024, 1, 3));
        bars[1].Open.Should().Be(191.00m);
        bars[1].High.Should().Be(192.00m);
        bars[1].Low.Should().Be(190.50m);
        bars[1].Close.Should().Be(191.25m);
        bars[1].Volume.Should().Be(1_100_000);

        observedRequest.Should().NotBeNull();
        observedRequest!.Method.Should().Be(HttpMethod.Get);
        observedRequest.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/stock/candle");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("symbol=MSFT")
            .And.Contain("resolution=D")
            .And.Contain($"from={UnixSeconds(2024, 1, 2)}")
            .And.Contain($"to={EndOfDayUnixSeconds(2024, 1, 4)}")
            .And.Contain($"token={ApiKey}");
        observedRequest.Headers.GetValues("X-Finnhub-Token").Should().ContainSingle(ApiKey);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenApiKeyMissing_ThrowsWithoutHttpCall()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Missing Finnhub credentials should not call HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(string.Empty, httpClient);

        var available = await provider.IsAvailableAsync(CancellationToken.None);
        Func<Task> act = () => provider.GetDailyBarsAsync("MSFT", null, null, CancellationToken.None);

        available.Should().BeFalse();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Finnhub API key*");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetIntradayBarsAsync_NormalizesResolutionAndFiltersWindow()
    {
        HttpRequestMessage? observedRequest = null;
        var beforeWindow = new DateTimeOffset(2024, 1, 2, 14, 25, 0, TimeSpan.Zero);
        var from = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 1, 2, 14, 35, 0, TimeSpan.Zero);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(CandlePayload(
                (beforeWindow.ToUnixTimeSeconds(), 189.00m, 190.00m, 188.75m, 189.50m, 10_000m),
                (from.ToUnixTimeSeconds(), 191.00m, 192.00m, 190.50m, 191.25m, 11_000m),
                (to.ToUnixTimeSeconds(), 192.50m, 193.00m, 192.00m, 192.75m, 12_000m)));
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(ApiKey, httpClient);

        var bars = await provider.GetIntradayBarsAsync("msft", "5m", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(bar => bar.Timestamp);
        bars.Select(bar => bar.Symbol).Should().OnlyContain(symbol => symbol == "MSFT");
        bars.Select(bar => bar.Interval).Should().OnlyContain(interval => interval == "5min");
        bars.Select(bar => bar.Source).Should().OnlyContain(source => source == "finnhub");
        bars[0].Timestamp.Should().Be(from);
        bars[0].Open.Should().Be(191.00m);
        bars[0].Volume.Should().Be(11_000);
        bars[1].Timestamp.Should().Be(to);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/stock/candle");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("symbol=MSFT")
            .And.Contain("resolution=5")
            .And.Contain($"from={from.ToUnixTimeSeconds()}")
            .And.Contain($"to={to.ToUnixTimeSeconds()}")
            .And.Contain($"token={ApiKey}");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenNoDataStatus_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse("""{ "s": "no_data" }"""));
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(ApiKey, httpClient);

        var bars = await provider.GetDailyBarsAsync("MSFT", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), cts.Token);

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
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(25));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("MSFT", null, null, cts.Token);

        var exception = await act.Should().ThrowAsync<RateLimitException>();
        exception.Which.Provider.Should().Be("finnhub");
        exception.Which.Symbol.Should().Be("MSFT");
        exception.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(25));
        var rateLimitInfo = provider.GetRateLimitInfo();
        rateLimitInfo.IsLimited.Should().BeTrue();
        rateLimitInfo.RetryAfter.Should().NotBeNull();
        rateLimitInfo.RetryAfter!.Value.Should().BeCloseTo(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(1));
    }

    private static long UnixSeconds(int year, int month, int day)
        => new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static long EndOfDayUnixSeconds(int year, int month, int day)
        => new DateTimeOffset(new DateOnly(year, month, day).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string CandlePayload(params (long Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume)[] rows)
    {
        var timestamps = JoinValues(rows.Select(row => row.Timestamp));
        var opens = JoinValues(rows.Select(row => row.Open));
        var highs = JoinValues(rows.Select(row => row.High));
        var lows = JoinValues(rows.Select(row => row.Low));
        var closes = JoinValues(rows.Select(row => row.Close));
        var volumes = JoinValues(rows.Select(row => row.Volume));

        return $$"""
            {
              "c": [{{closes}}],
              "h": [{{highs}}],
              "l": [{{lows}}],
              "o": [{{opens}}],
              "s": "ok",
              "t": [{{timestamps}}],
              "v": [{{volumes}}]
            }
            """;
    }

    private static string JoinValues(IEnumerable<long> values)
        => string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));

    private static string JoinValues(IEnumerable<decimal> values)
        => string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
