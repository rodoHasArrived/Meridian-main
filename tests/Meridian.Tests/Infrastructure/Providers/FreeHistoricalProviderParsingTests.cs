using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Application.Exceptions;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Behavioral tests for free historical data providers using stubbed HTTP responses.
/// Covers happy-path parsing, empty responses, HTTP error codes, and date filtering.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TwelveDataParsingTests
{
    private static readonly string TwelveDataOkResponse = """
        {
          "status": "ok",
          "values": [
            { "datetime": "2024-01-03", "open": "185.00", "high": "187.50", "low": "184.20", "close": "186.80", "volume": "75000000" },
            { "datetime": "2024-01-02", "open": "182.50", "high": "185.10", "low": "181.90", "close": "184.90", "volume": "62000000" },
            { "datetime": "2024-01-01", "open": "180.00", "high": "183.00", "low": "179.50", "close": "182.00", "volume": "55000000" }
          ]
        }
        """;

    private static readonly string TwelveDataErrorResponse = """
        { "status": "error", "message": "Invalid API key" }
        """;

    private static readonly string TwelveDataEmptyResponse = """
        { "status": "ok", "values": [] }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidResponse_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwelveDataOkResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        var first = bars[0];
        first.Symbol.Should().Be("AAPL");
        first.Open.Should().Be(180.00m);
        first.High.Should().Be(183.00m);
        first.Low.Should().Be(179.50m);
        first.Close.Should().Be(182.00m);
        first.Volume.Should().Be(55_000_000);
        first.Source.Should().Be("twelvedata");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenApiReturnsErrorStatus_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwelveDataErrorResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenValuesArrayIsEmpty_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwelveDataEmptyResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp429_ThrowsRateLimitException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate limited", Encoding.UTF8, "text/plain")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<RateLimitException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithDateFilter_ReturnsOnlyBarsInRange()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwelveDataOkResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);
        var bars = await provider.GetDailyBarsAsync("AAPL", from, to, CancellationToken.None);

        bars.Should().HaveCount(2);
        bars.Should().AllSatisfy(b =>
        {
            b.SessionDate.CompareTo(from).Should().BeGreaterThanOrEqualTo(0);
            b.SessionDate.CompareTo(to).Should().BeLessThanOrEqualTo(0);
        });
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithNullSymbol_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync(null!, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithEmptySymbol_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Behavioral tests for <see cref="StooqHistoricalDataProvider"/> using stubbed HTTP responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StooqParsingTests
{
    private const string StooqCsvResponse =
        "Date,Open,High,Low,Close,Volume\r\n" +
        "2024-01-03,185.00,187.50,184.20,186.80,75000000\r\n" +
        "2024-01-02,182.50,185.10,181.90,184.90,62000000\r\n" +
        "2024-01-01,180.00,183.00,179.50,182.00,55000000\r\n";

    private const string StooqEmptyCsv =
        "Date,Open,High,Low,Close,Volume\r\n";

    [Fact]
    public async Task GetDailyBarsAsync_WithValidCsv_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(StooqCsvResponse, Encoding.UTF8, "text/csv")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        var first = bars[0];
        first.Open.Should().Be(180.00m);
        first.High.Should().Be(183.00m);
        first.Low.Should().Be(179.50m);
        first.Close.Should().Be(182.00m);
        first.Volume.Should().Be(55_000_000);
        first.Source.Should().Be("stooq");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithHeaderOnlyCsv_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(StooqEmptyCsv, Encoding.UTF8, "text/csv")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp404_ThrowsInvalidOperationException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("INVALID", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithDateFilter_ReturnsOnlyBarsInRange()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(StooqCsvResponse, Encoding.UTF8, "text/csv")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new StooqHistoricalDataProvider(httpClient: httpClient);

        var from = new DateOnly(2024, 1, 2);
        var to = new DateOnly(2024, 1, 3);
        var bars = await provider.GetDailyBarsAsync("AAPL", from, to, CancellationToken.None);

        bars.Should().HaveCount(2);
        bars.Should().AllSatisfy(b =>
        {
            b.SessionDate.CompareTo(from).Should().BeGreaterThanOrEqualTo(0);
            b.SessionDate.CompareTo(to).Should().BeLessThanOrEqualTo(0);
        });
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithNullSymbol_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();
        using var provider = new StooqHistoricalDataProvider(httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync(null!, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Behavioral tests for <see cref="FinnhubHistoricalDataProvider"/> using stubbed HTTP responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinnhubParsingTests
{
    // Unix timestamps for 2024-01-01, 2024-01-02, 2024-01-03
    private const long Ts20240101 = 1704067200L;
    private const long Ts20240102 = 1704153600L;
    private const long Ts20240103 = 1704240000L;

    private static string BuildOkResponse() =>
        $$"""
        {
          "c": [182.00, 184.90, 186.80],
          "h": [183.00, 185.10, 187.50],
          "l": [179.50, 181.90, 184.20],
          "o": [180.00, 182.50, 185.00],
          "s": "ok",
          "t": [{{Ts20240101}}, {{Ts20240102}}, {{Ts20240103}}],
          "v": [55000000, 62000000, 75000000]
        }
        """;

    private static readonly string NoDataResponse = """
        { "s": "no_data" }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidResponse_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildOkResponse(), Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3),
            CancellationToken.None);

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars[0].Open.Should().Be(180.00m);
        bars[0].Volume.Should().Be(55_000_000);
        bars[0].Source.Should().Be("finnhub");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenStatusIsNoData_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(NoDataResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3),
            CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp429_ThrowsRateLimitException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Too Many Requests", Encoding.UTF8, "text/plain")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("AAPL",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3),
            CancellationToken.None);

        await act.Should().ThrowAsync<RateLimitException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithNullSymbol_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync(null!,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Behavioral tests for <see cref="NasdaqDataLinkHistoricalDataProvider"/> using stubbed HTTP responses.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NasdaqDataLinkParsingTests
{
    private static readonly string NasdaqOkResponse = """
        {
          "dataset": {
            "id": 9775687,
            "dataset_code": "AAPL",
            "database_code": "WIKI",
            "name": "Apple Inc (AAPL) Prices, Dividends, Splits and Trading Volume",
            "column_names": ["Date","Open","High","Low","Close","Volume","Ex-Dividend","Split Ratio","Adj. Open","Adj. High","Adj. Low","Adj. Close","Adj. Volume"],
            "data": [
              ["2024-01-03", 185.00, 187.50, 184.20, 186.80, 75000000, 0.0, 1.0, 185.00, 187.50, 184.20, 186.80, 75000000],
              ["2024-01-02", 182.50, 185.10, 181.90, 184.90, 62000000, 0.0, 1.0, 182.50, 185.10, 181.90, 184.90, 62000000],
              ["2024-01-01", 180.00, 183.00, 179.50, 182.00, 55000000, 0.0, 1.0, 180.00, 183.00, 179.50, 182.00, 55000000]
            ],
            "start_date": "2024-01-01",
            "end_date": "2024-01-03",
            "frequency": "daily"
          }
        }
        """;

    private static readonly string NasdaqEmptyResponse = """
        {
          "dataset": {
            "dataset_code": "AAPL",
            "database_code": "WIKI",
            "column_names": ["Date","Open","High","Low","Close","Volume","Ex-Dividend","Split Ratio","Adj. Open","Adj. High","Adj. Low","Adj. Close","Adj. Volume"],
            "data": []
          }
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidResponse_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(NasdaqOkResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars[0].SessionDate.Should().Be(new DateOnly(2024, 1, 1));
        bars[0].Open.Should().Be(180.00m);
        bars[0].Source.Should().Be("nasdaq");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithEmptyDataArray_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(NasdaqEmptyResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenHttp404_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"quandl_error\":{\"code\":\"QECx02\",\"message\":\"You have submitted an incorrect Quandl code.\"}}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        // NasdaqDataLink returns empty on 404 (symbol not found)
        var bars = await provider.GetDailyBarsAsync("INVALID", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithNullSymbol_ThrowsArgumentException()
    {
        using var httpClient = new HttpClient();
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync(null!, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

/// <summary>
/// Behavioral tests for <see cref="FredHistoricalDataProvider"/> using stubbed HTTP responses.
/// Covers happy-path observation parsing, dot-value skipping, empty arrays, and missing API key.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FredParsingTests
{
    private static readonly string FredValidResponse = """
        {
          "observations": [
            { "date": "2024-01-01", "value": "21000.00" },
            { "date": "2024-01-02", "value": "21250.50" },
            { "date": "2024-01-03", "value": "21567.89" }
          ]
        }
        """;

    private static readonly string FredWithDotValueResponse = """
        {
          "observations": [
            { "date": "2024-01-01", "value": "21000.00" },
            { "date": "2024-01-02", "value": "." },
            { "date": "2024-01-03", "value": "21567.89" }
          ]
        }
        """;

    private static readonly string FredEmptyObservationsResponse = """
        {
          "observations": []
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidObservations_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FredValidResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("GDP", null, null, CancellationToken.None);

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        var first = bars[0];
        first.Symbol.Should().Be("GDP");
        first.Open.Should().Be(21000.00m);
        first.High.Should().Be(21000.00m);
        first.Low.Should().Be(21000.00m);
        first.Close.Should().Be(21000.00m);
        first.Source.Should().Be("fred");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithDotValueObservation_SkipsMissingValues()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FredWithDotValueResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("GDP", null, null, CancellationToken.None);

        bars.Should().HaveCount(2, because: "the observation with value '.' must be skipped");
        bars.Should().NotContain(b => b.SessionDate == new DateOnly(2024, 1, 2));
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithEmptyObservationsArray_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(FredEmptyObservationsResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new FredHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("GDP", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenApiKeyMissing_ThrowsInvalidOperationException()
    {
        using var httpClient = new HttpClient();
        using var provider = new FredHistoricalDataProvider(apiKey: "", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("GDP", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FRED API key*");
    }
}

/// <summary>
/// Behavioral tests for <see cref="AlphaVantageHistoricalDataProvider"/> using stubbed HTTP responses.
/// Covers happy-path OHLCV parsing, error message body, and rate-limit detection.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AlphaVantageParsingTests
{
    private static readonly string AlphaVantageValidResponse = """
        {
          "Meta Data": {
            "1. Information": "Daily Time Series with Splits and Dividend Events",
            "2. Symbol": "AAPL"
          },
          "Time Series (Daily)": {
            "2024-01-03": {
              "1. open": "185.00",
              "2. high": "187.50",
              "3. low": "184.20",
              "4. close": "186.80",
              "5. adjusted close": "186.80",
              "6. volume": "75000000",
              "7. dividend amount": "0.0000",
              "8. split coefficient": "1.0"
            },
            "2024-01-02": {
              "1. open": "182.50",
              "2. high": "185.10",
              "3. low": "181.90",
              "4. close": "184.90",
              "5. adjusted close": "184.90",
              "6. volume": "62000000",
              "7. dividend amount": "0.0000",
              "8. split coefficient": "1.0"
            }
          }
        }
        """;

    private static readonly string AlphaVantageErrorResponse = """
        {
          "Error Message": "Invalid API call. Please retry or visit the documentation for TIME_SERIES_DAILY_ADJUSTED."
        }
        """;

    private static readonly string AlphaVantageRateLimitResponse = """
        {
          "Information": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 100 calls per day."
        }
        """;

    [Fact]
    public async Task GetDailyBarsAsync_WithValidTimeSeries_ParsesAllBars()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AlphaVantageValidResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        bars.Should().HaveCount(2);
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        var first = bars[0];
        first.Symbol.Should().Be("AAPL");
        first.SessionDate.Should().Be(new DateOnly(2024, 1, 2));
        first.Open.Should().Be(182.50m);
        first.High.Should().Be(185.10m);
        first.Low.Should().Be(181.90m);
        first.Close.Should().Be(184.90m);
        first.Volume.Should().Be(62_000_000);
        first.Source.Should().Be("alphavantage");
    }

    [Fact]
    public async Task GetDailyBarsAsync_WithErrorMessageInBody_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AlphaVantageErrorResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("INVALID", null, null, CancellationToken.None);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_WhenRateLimited_ThrowsHttpRequestException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AlphaVantageRateLimitResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "stub-key", httpClient: httpClient);

        Func<Task> act = () => provider.GetDailyBarsAsync("AAPL", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*rate limit*");
    }
}
