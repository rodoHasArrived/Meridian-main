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
        IReadOnlyDictionary<string, decimal>? dividends = null)
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
                            symbol
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
