using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class YahooFinanceHistoricalDataProviderTests
{
    [Fact]
    public async Task GetDailyBarsAsync_PrefersAdjustedValuesAndFiltersRequestedRange()
    {
        var httpClient = CreateHttpClient(_ => CreateJsonResponse(
            BuildDailyResponse(
                symbol: "AAPL",
                timestamps:
                [
                    new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                    new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()
                ],
                opens: [100m, 101m],
                highs: [110m, 111m],
                lows: [90m, 91m],
                closes: [105m, 106m],
                volumes: [1000L, 1200L],
                adjustedCloses: [95m, 96m])));

        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetDailyBarsAsync(
            "AAPL",
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 2));

        bars.Should().ContainSingle();
        var bar = bars[0];
        bar.Symbol.Should().Be("AAPL");
        bar.Source.Should().Be("yahoo");
        bar.SessionDate.Should().Be(new DateOnly(2024, 1, 2));
        bar.Open.Should().BeApproximately(101m * (96m / 106m), 0.0000001m);
        bar.High.Should().BeApproximately(111m * (96m / 106m), 0.0000001m);
        bar.Low.Should().BeApproximately(91m * (96m / 106m), 0.0000001m);
        bar.Close.Should().Be(96m);
        bar.Volume.Should().Be(1200L);
    }

    [Fact]
    public async Task GetAdjustedDailyBarsAsync_ParsesDividendAndSplitSignals()
    {
        var timestamp = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var httpClient = CreateHttpClient(_ => CreateJsonResponse(
            BuildDailyResponse(
                symbol: "SPY",
                timestamps: [timestamp],
                opens: [100m],
                highs: [110m],
                lows: [90m],
                closes: [100m],
                volumes: [5000L],
                adjustedCloses: [50m],
                dividends: new Dictionary<string, decimal> { [timestamp.ToString()] = 1.25m })));

        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync(
            "SPY",
            from: new DateOnly(2024, 2, 1),
            to: new DateOnly(2024, 2, 1));

        bars.Should().ContainSingle();
        var bar = bars[0];
        bar.Symbol.Should().Be("SPY");
        bar.Source.Should().Be("yahoo");
        bar.SessionDate.Should().Be(new DateOnly(2024, 2, 1));
        bar.AdjustedClose.Should().Be(50m);
        bar.SplitFactor.Should().Be(0.5m);
        bar.DividendAmount.Should().Be(1.25m);
    }

    [Theory]
    [InlineData("2024-01-01T23:00:00Z", "2024-01-02")]
    [InlineData("2024-07-02T00:00:00Z", "2024-07-02")]
    public async Task GetDailyBarsAsync_AustralianExactDate_FetchesAndFiltersExchangeSession(
        string sessionOpenUtc,
        string sessionDateValue)
    {
        var sessionDate = DateOnly.Parse(sessionDateValue);
        var sessionOpen = DateTimeOffset.Parse(sessionOpenUtc);
        Uri? observedUri = null;
        using var httpClient = CreateHttpClient(request =>
        {
            observedUri = request.RequestUri;
            var query = ParseQuery(request.RequestUri!);
            var period1 = long.Parse(query["period1"]);
            var period2 = long.Parse(query["period2"]);
            // Behave like the provider: timestamps outside the outgoing request
            // cannot be recovered later by the local date filter.
            var timestamps = new[] { sessionOpen.AddDays(-1), sessionOpen, sessionOpen.AddDays(1) }
                .Select(value => value.ToUnixTimeSeconds())
                .Where(value => value >= period1 && value < period2)
                .ToArray();
            return CreateJsonResponse(BuildConstantDailyResponse(timestamps, "Australia/Sydney", 36_000));
        });
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetDailyBarsAsync("BHP.AX", sessionDate, sessionDate);

        var bar = bars.Should().ContainSingle().Subject;
        bar.SessionDate.Should().Be(sessionDate);
        bar.SequenceNumber.Should().Be(sessionDate.DayNumber);
        var observedQuery = ParseQuery(observedUri!);
        long.Parse(observedQuery["period1"]).Should().Be(
            new DateTimeOffset(sessionDate.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds());
        long.Parse(observedQuery["period2"]).Should().Be(
            new DateTimeOffset(sessionDate.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("2024-04-05T13:30:00Z", "2024-04-06", 36000)]
    [InlineData("2024-04-07T13:30:00Z", "2024-04-07", 39600)]
    [InlineData("2024-10-04T13:30:00Z", "2024-10-04", 39600)]
    [InlineData("2024-10-06T13:30:00Z", "2024-10-07", 36000)]
    public async Task GetAdjustedDailyBarsAsync_UsesHistoricalExchangeTimezoneRulesAcrossDst(
        string timestampUtc,
        string expectedDateValue,
        long staleCurrentOffset)
    {
        var expectedDate = DateOnly.Parse(expectedDateValue);
        using var httpClient = CreateHttpClient(_ => CreateJsonResponse(BuildConstantDailyResponse(
            [DateTimeOffset.Parse(timestampUtc).ToUnixTimeSeconds()], "Australia/Sydney", staleCurrentOffset)));
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("BHP.AX", expectedDate, expectedDate);

        bars.Should().ContainSingle().Which.SessionDate.Should().Be(expectedDate);
    }

    [Theory]
    [InlineData(null, 39600L, "2024-01-02")]
    [InlineData("Unavailable/ExchangeZone", 39600L, "2024-01-02")]
    [InlineData(null, null, "2024-01-01")]
    public async Task GetAdjustedDailyBarsAsync_WhenExchangeZoneUnavailable_UsesOffsetThenUtc(
        string? exchangeTimeZone,
        long? offset,
        string expectedDateValue)
    {
        var expectedDate = DateOnly.Parse(expectedDateValue);
        using var httpClient = CreateHttpClient(_ => CreateJsonResponse(BuildConstantDailyResponse(
            [DateTimeOffset.Parse("2024-01-01T23:00:00Z").ToUnixTimeSeconds()], exchangeTimeZone, offset)));
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("BHP.AX", expectedDate, expectedDate);

        bars.Should().ContainSingle().Which.SessionDate.Should().Be(expectedDate);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
        => uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));

    private static string BuildConstantDailyResponse(long[] timestamps, string? exchangeTimeZone, long? offset)
        => BuildDailyResponse(
            "BHP.AX",
            timestamps,
            Enumerable.Repeat(100m, timestamps.Length).ToArray(),
            Enumerable.Repeat(110m, timestamps.Length).ToArray(),
            Enumerable.Repeat(90m, timestamps.Length).ToArray(),
            Enumerable.Repeat(100m, timestamps.Length).ToArray(),
            Enumerable.Repeat(1000L, timestamps.Length).ToArray(),
            Enumerable.Repeat(100m, timestamps.Length).ToArray(),
            exchangeTimeZone: exchangeTimeZone,
            offset: offset);

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new CallbackHttpMessageHandler(responder));

    private static HttpResponseMessage CreateJsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string BuildDailyResponse(
        string symbol,
        long[] timestamps,
        decimal[] opens,
        decimal[] highs,
        decimal[] lows,
        decimal[] closes,
        long[] volumes,
        decimal[] adjustedCloses,
        IReadOnlyDictionary<string, decimal>? dividends = null,
        string? exchangeTimeZone = null,
        long? offset = null)
    {
        var payload = new
        {
            chart = new
            {
                result = new object[]
                {
                    new
                    {
                        meta = new
                        {
                            symbol,
                            exchangeTimezoneName = exchangeTimeZone,
                            gmtoffset = offset
                        },
                        timestamp = timestamps,
                        indicators = new
                        {
                            quote = new object[]
                            {
                                new
                                {
                                    open = opens,
                                    high = highs,
                                    low = lows,
                                    close = closes,
                                    volume = volumes
                                }
                            },
                            adjclose = new object[]
                            {
                                new
                                {
                                    adjclose = adjustedCloses
                                }
                            }
                        },
                        events = dividends is null
                            ? null
                            : new
                            {
                                dividends = dividends.ToDictionary(
                                    pair => pair.Key,
                                    pair => new
                                    {
                                        amount = pair.Value
                                    })
                            }
                    }
                },
                error = (object?)null
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private sealed class CallbackHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
