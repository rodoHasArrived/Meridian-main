using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Provider-specific Alpha Vantage tests for the intraday free-tier path and rate-limit failure mode.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlphaVantageHistoricalDataProviderTests
{
    private const string ApiKey = "test-alpha-vantage-key";

    private static readonly string IntradayResponse = """
        {
          "Meta Data": {
            "1. Information": "Intraday (5min) open, high, low, close prices and volume",
            "2. Symbol": "AAPL",
            "4. Interval": "5min"
          },
          "Time Series (5min)": {
            "2024-01-02 09:40:00": {
              "1. open": "186.2500",
              "2. high": "186.9000",
              "3. low": "186.1000",
              "4. close": "186.7500",
              "5. volume": "1289000"
            },
            "2024-01-02 09:35:00": {
              "1. open": "185.1000",
              "2. high": "186.3000",
              "3. low": "184.9500",
              "4. close": "186.2500",
              "5. volume": "1420000"
            },
            "2024-01-02 09:30:00": {
              "1. open": "184.5000",
              "2. high": "185.4000",
              "3. low": "184.2000",
              "4. close": "185.1000",
              "5. volume": "2150000"
            },
            "2024-01-02 09:25:00": {
              "1. open": "0.0000",
              "2. high": "184.0000",
              "3. low": "183.8000",
              "4. close": "183.9500",
              "5. volume": "900000"
            }
          }
        }
        """;

    private static readonly string EmptyIntradayResponse = """
        {
          "Meta Data": {
            "1. Information": "Intraday (5min) open, high, low, close prices and volume",
            "2. Symbol": "AAPL",
            "4. Interval": "5min"
          }
        }
        """;

    private static readonly string RateLimitResponse = """
        {
          "Information": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 100 calls per day."
        }
        """;

    [Fact]
    public async Task GetIntradayBarsAsync_NormalSessionOpen_ParsesFiltersAndRequestShape()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse(IntradayResponse);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);
        var from = ParseExchangeTimestamp("2024-01-02 09:35:00");
        var to = ParseExchangeTimestamp("2024-01-02 09:40:00");

        var bars = await provider.GetIntradayBarsAsync("aapl", "5 minute", from, to, cts.Token);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.Timestamp);
        bars.Select(b => b.Interval).Should().OnlyContain(interval => interval == "5min");
        bars.Select(b => b.Symbol).Should().OnlyContain(symbol => symbol == "AAPL");
        bars.Select(b => b.Source).Should().OnlyContain(source => source == "alphavantage");
        bars[0].Timestamp.Should().Be(from);
        bars[0].Open.Should().Be(185.1000m);
        bars[0].High.Should().Be(186.3000m);
        bars[0].Low.Should().Be(184.9500m);
        bars[0].Close.Should().Be(186.2500m);
        bars[0].Volume.Should().Be(1_420_000);

        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.AbsoluteUri.Should().StartWith("https://www.alphavantage.co/query?");
        observedRequest.RequestUri.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("function=TIME_SERIES_INTRADAY")
            .And.Contain("symbol=AAPL")
            .And.Contain("interval=5min")
            .And.Contain("outputsize=full")
            .And.Contain("adjusted=true")
            .And.Contain("extended_hours=true")
            .And.Contain($"apikey={ApiKey}");
    }

    [Fact]
    public async Task GetIntradayBarsAsync_WhenBodyOmitsIntervalSeries_ReturnsEmptyList()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(EmptyIntradayResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);

        var bars = await provider.GetIntradayBarsAsync("AAPL", "5min", null, null, cts.Token);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIntradayBarsAsync_WhenBodyContainsRateLimitMessage_ThrowsRateLimitException()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RateLimitResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.GetIntradayBarsAsync("AAPL", "5min", null, null, cts.Token);

        await act.Should().ThrowAsync<RateLimitException>()
            .WithMessage("*rate limit*");
    }

    [Fact]
    public async Task GetIntradayBarsAsync_WithUnsupportedInterval_ThrowsBeforeHttpCall()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Unsupported intervals should fail before HTTP."));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);

        Func<Task> act = () => provider.GetIntradayBarsAsync("AAPL", "2min", null, null, cts.Token);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("interval")
            .WithMessage("*Unsupported interval*");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task IsAvailableAsync_WhenGlobalQuoteRespondsWithoutRateLimit_ReturnsTrue()
    {
        HttpRequestMessage? observedRequest = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(request =>
        {
            observedRequest = request;
            return JsonResponse("""{ "Global Quote": { "01. symbol": "AAPL" } }""");
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeTrue();
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().NotBeNull();
        observedRequest.RequestUri!.GetComponents(UriComponents.Query, UriFormat.Unescaped)
            .Should().Contain("function=GLOBAL_QUOTE")
            .And.Contain("symbol=AAPL")
            .And.Contain($"apikey={ApiKey}");
    }

    [Fact]
    public async Task IsAvailableAsync_WhenGlobalQuoteIsRateLimited_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new StubHttpMessageHandler(_ => JsonResponse(RateLimitResponse));
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(ApiKey, httpClient);

        var available = await provider.IsAvailableAsync(cts.Token);

        available.Should().BeFalse();
    }

    private static DateTimeOffset ParseExchangeTimestamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
