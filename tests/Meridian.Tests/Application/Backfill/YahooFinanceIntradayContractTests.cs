using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Xunit;

namespace Meridian.Tests.Backfill;

public sealed class YahooFinanceIntradayContractTests
{
    [Theory]
    [InlineData(DataGranularity.Minute1, 1)]
    [InlineData(DataGranularity.Minute15, 15)]
    [InlineData(DataGranularity.Hour1, 60)]
    public async Task YahooFinance_ParsesIntradayAggregates(
        DataGranularity granularity,
        int intervalMinutes)
    {
        var sessionDate = new DateOnly(2024, 1, 2);
        var sessionStart = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var timestamps = new[]
        {
            sessionStart.ToUnixTimeSeconds(),
            sessionStart.AddMinutes(intervalMinutes).ToUnixTimeSeconds()
        };

        var httpClient = CreateHttpClient(_ => CreateJsonResponse(
            BuildIntradayResponse(
                symbol: "AAPL",
                gmtOffsetSeconds: -5 * 3600,
                regularStartUnix: sessionStart.ToUnixTimeSeconds(),
                regularEndUnix: sessionStart.AddHours(7).ToUnixTimeSeconds(),
                timestamps: timestamps,
                opens: [100m, 101m],
                highs: [101m, 102m],
                lows: [99m, 100m],
                closes: [100.5m, 101.5m],
                volumes: [1000L, 1200L])));

        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetAggregateBarsAsync("AAPL", granularity, sessionDate, sessionDate);

        bars.Should().HaveCount(2);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Source.Should().Be("yahoo");
            bar.Timeframe.Should().Be(granularity is DataGranularity.Hour1 ? AggregateTimeframe.Hour : AggregateTimeframe.Minute);
        });
        bars[0].StartTime.Should().Be(sessionStart);
        bars[0].EndTime.Should().Be(sessionStart.AddMinutes(intervalMinutes));
        bars[1].StartTime.Should().Be(sessionStart.AddMinutes(intervalMinutes));
        bars[1].EndTime.Should().Be(sessionStart.AddMinutes(intervalMinutes * 2));
    }

    [Fact]
    public async Task YahooFinance_Minute1Backfill_ChunksRequestsIntoEightDayWindows()
    {
        var requestedUris = new List<Uri>();
        var httpClient = CreateHttpClient(request =>
        {
            requestedUris.Add(request.RequestUri!);
            return CreateJsonResponse(
                BuildIntradayResponse(
                    symbol: "AAPL",
                    gmtOffsetSeconds: -5 * 3600,
                    regularStartUnix: new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                    regularEndUnix: new DateTimeOffset(2024, 1, 2, 21, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                    timestamps: [new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds()],
                    opens: [100m],
                    highs: [101m],
                    lows: [99m],
                    closes: [100.5m],
                    volumes: [1000L]));
        });

        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        await provider.GetAggregateBarsAsync(
            "AAPL",
            DataGranularity.Minute1,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 10));

        requestedUris.Should().HaveCount(2);
        requestedUris.Should().OnlyContain(uri => uri.Query.Contains("interval=1m", StringComparison.OrdinalIgnoreCase));
        requestedUris.Should().OnlyContain(uri => uri.Query.Contains("includePrePost=false", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task YahooFinance_Hour4Rollup_KeepsTrailingPartialSessionBucket()
    {
        var sessionStart = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var timestamps = Enumerable.Range(0, 7)
            .Select(i => sessionStart.AddHours(i).ToUnixTimeSeconds())
            .ToArray();

        var httpClient = CreateHttpClient(_ => CreateJsonResponse(
            BuildIntradayResponse(
                symbol: "AAPL",
                gmtOffsetSeconds: -5 * 3600,
                regularStartUnix: sessionStart.ToUnixTimeSeconds(),
                regularEndUnix: sessionStart.AddHours(6).AddMinutes(30).ToUnixTimeSeconds(),
                timestamps: timestamps,
                opens: [100m, 101m, 102m, 103m, 104m, 105m, 106m],
                highs: [101m, 102m, 103m, 104m, 105m, 106m, 107m],
                lows: [99m, 100m, 101m, 102m, 103m, 104m, 105m],
                closes: [100.5m, 101.5m, 102.5m, 103.5m, 104.5m, 105.5m, 106.5m],
                volumes: [100L, 110L, 120L, 130L, 140L, 150L, 160L])));

        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        var bars = await provider.GetAggregateBarsAsync(
            "AAPL",
            DataGranularity.Hour4,
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 2));

        bars.Should().HaveCount(2);

        bars[0].StartTime.Should().Be(sessionStart);
        bars[0].EndTime.Should().Be(sessionStart.AddHours(4));
        bars[0].Open.Should().Be(100m);
        bars[0].High.Should().Be(104m);
        bars[0].Low.Should().Be(99m);
        bars[0].Close.Should().Be(103.5m);
        bars[0].Volume.Should().Be(460L);

        bars[1].StartTime.Should().Be(sessionStart.AddHours(4));
        bars[1].EndTime.Should().Be(sessionStart.AddHours(6).AddMinutes(30));
        bars[1].Open.Should().Be(104m);
        bars[1].High.Should().Be(107m);
        bars[1].Low.Should().Be(103m);
        bars[1].Close.Should().Be(106.5m);
        bars[1].Volume.Should().Be(450L);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new CallbackHttpMessageHandler(responder));

    private static HttpResponseMessage CreateJsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string BuildIntradayResponse(
        string symbol,
        long gmtOffsetSeconds,
        long regularStartUnix,
        long regularEndUnix,
        long[] timestamps,
        decimal[] opens,
        decimal[] highs,
        decimal[] lows,
        decimal[] closes,
        long[] volumes)
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
                            gmtoffset = gmtOffsetSeconds,
                            currentTradingPeriod = new
                            {
                                regular = new
                                {
                                    start = regularStartUnix,
                                    end = regularEndUnix,
                                    gmtoffset = gmtOffsetSeconds
                                }
                            }
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
                            }
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
