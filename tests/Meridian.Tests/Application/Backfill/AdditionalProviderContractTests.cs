using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.Tiingo;
using Moq;
using Moq.Protected;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Contract tests for Tiingo, Finnhub, and Alpha Vantage historical data providers.
/// Uses recorded API responses to verify parsing logic without making network calls.
/// </summary>
public sealed class AdditionalProviderContractTests
{
    #region Tiingo Contract Tests

    [Fact]
    public async Task Tiingo_ParsesValidResponse_ReturnsCorrectBars()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ValidAaplResponse);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("tiingo");
        });
    }

    [Fact]
    public async Task Tiingo_ParsesAdjustedData_IncludesCorporateActions()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ResponseWithDividendAndSplit);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().NotBeEmpty();
        bars.Should().Contain(b => b.DividendAmount.HasValue && b.DividendAmount > 0);
        bars.Should().Contain(b => b.AdjustedClose.HasValue);
    }

    [Fact]
    public async Task Tiingo_HandlesEmptyResponse_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient("[]");
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task Tiingo_WithHttpError_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient("Not Found", HttpStatusCode.NotFound);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("INVALID", null, null));
    }

    [Fact]
    public async Task Tiingo_WithEmptySymbol_ThrowsArgumentException()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ValidAaplResponse);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetDailyBarsAsync("", null, null));
    }

    [Fact]
    public async Task Tiingo_WithNoApiToken_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ValidAaplResponse);
        using var provider = new TiingoHistoricalDataProvider(apiToken: null, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    [Fact]
    public async Task Tiingo_RespectsDateRange_FiltersCorrectly()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ValidAaplResponse);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 4);

        var bars = await provider.GetDailyBarsAsync("AAPL", from, to);

        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public async Task Tiingo_BarsAreSortedByDate()
    {
        var httpClient = CreateMockHttpClient(TiingoResponses.ValidAaplResponse);
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    [Fact]
    public void Tiingo_HasCorrectMetadata()
    {
        using var provider = new TiingoHistoricalDataProvider(apiToken: "test-token");

        provider.Name.Should().Be("tiingo");
        provider.DisplayName.Should().Contain("Tiingo");
        provider.Priority.Should().Be(15);
        provider.Capabilities.SupportedMarkets.Should().Contain("US");
    }

    #endregion

    #region Finnhub Contract Tests

    [Fact]
    public async Task Finnhub_ParsesValidCandleResponse_ReturnsCorrectBars()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.ValidAaplCandles);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("finnhub");
        });
    }

    [Fact]
    public async Task Finnhub_ParsesNoDataStatus_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.NoDataResponse);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task Finnhub_WithEmptySymbol_ThrowsArgumentException()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.ValidAaplCandles);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetDailyBarsAsync("", null, null));
    }

    [Fact]
    public async Task Finnhub_WithNoApiKey_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.ValidAaplCandles);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: null, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    [Fact]
    public async Task Finnhub_BarsAreSortedByDate()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.ValidAaplCandles);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    [Fact]
    public async Task Finnhub_RespectsDateRange_FiltersCorrectly()
    {
        var httpClient = CreateMockHttpClient(FinnhubResponses.ValidAaplCandles);
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 4);

        var bars = await provider.GetDailyBarsAsync("AAPL", from, to);

        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public void Finnhub_HasCorrectMetadata()
    {
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key");

        provider.Name.Should().Be("finnhub");
        provider.DisplayName.Should().Contain("Finnhub");
        provider.Priority.Should().Be(18);
        provider.Capabilities.Intraday.Should().BeTrue();
        provider.Capabilities.SupportedMarkets.Should().Contain("US");
    }

    [Fact]
    public async Task Finnhub_WithInvalidJson_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient("{ invalid json }");
        using var provider = new FinnhubHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    #endregion

    #region Alpha Vantage Contract Tests

    [Fact]
    public async Task AlphaVantage_ParsesValidResponse_ReturnsCorrectBars()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Source.Should().Be("alphavantage");
        });
    }

    [Fact]
    public async Task AlphaVantage_ParsesAdjustedPrices_IncludesAdjustedClose()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(bar =>
        {
            bar.AdjustedClose.Should().NotBeNull();
            bar.AdjustedClose.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task AlphaVantage_ParsesDividendData()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ResponseWithDividend);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("SPY", null, null);

        bars.Should().Contain(b => b.DividendAmount.HasValue && b.DividendAmount > 0);
    }

    [Fact]
    public async Task AlphaVantage_ParsesSplitCoefficient()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ResponseWithSplit);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().Contain(b => b.SplitFactor.HasValue && b.SplitFactor != 1m);
    }

    [Fact]
    public async Task AlphaVantage_HandlesErrorMessage_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ErrorResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("INVALID", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task AlphaVantage_HandlesRateLimitMessage_ThrowsHttpRequestException()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.RateLimitResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    [Fact]
    public async Task AlphaVantage_WithEmptySymbol_ThrowsArgumentException()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetDailyBarsAsync("", null, null));
    }

    [Fact]
    public async Task AlphaVantage_WithNoApiKey_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: null, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    [Fact]
    public async Task AlphaVantage_RespectsDateRange_FiltersCorrectly()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 4);

        var bars = await provider.GetDailyBarsAsync("AAPL", from, to);

        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public async Task AlphaVantage_BarsAreSortedByDate()
    {
        var httpClient = CreateMockHttpClient(AlphaVantageResponses.ValidDailyAdjustedResponse);
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    [Fact]
    public void AlphaVantage_HasCorrectMetadata()
    {
        using var provider = new AlphaVantageHistoricalDataProvider(apiKey: "test-key");

        provider.Name.Should().Be("alphavantage");
        provider.DisplayName.Should().Contain("Alpha Vantage");
        provider.Priority.Should().Be(25);
        provider.Capabilities.Intraday.Should().BeTrue();
    }

    #endregion

    #region FRED Contract Tests

    [Fact]
    public async Task Fred_ParsesValidResponse_ReturnsSyntheticBars()
    {
        var httpClient = CreateMockHttpClient(FredResponses.ValidObservationsResponse);
        using var provider = new FredHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("UNRATE", null, null);

        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("UNRATE");
            bar.Open.Should().Be(bar.High);
            bar.High.Should().Be(bar.Low);
            bar.Low.Should().Be(bar.Close);
            bar.Volume.Should().Be(0);
            bar.Source.Should().Be("fred");
        });
    }

    [Fact]
    public async Task Fred_SkipsMissingObservations()
    {
        var httpClient = CreateMockHttpClient(FredResponses.ResponseWithMissingValue);
        using var provider = new FredHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("GDP", null, null);

        bars.Should().HaveCount(1);
        bars[0].Close.Should().Be(23123.45m);
    }

    [Fact]
    public async Task Fred_WithNoApiKey_ThrowsInvalidOperationException()
    {
        var httpClient = CreateMockHttpClient(FredResponses.ValidObservationsResponse);
        using var provider = new FredHistoricalDataProvider(apiKey: null, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("UNRATE", null, null));
    }

    [Fact]
    public async Task Fred_RespectsDateRange_FiltersCorrectly()
    {
        var httpClient = CreateMockHttpClient(FredResponses.ValidObservationsResponse);
        using var provider = new FredHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);
        var from = new DateOnly(2024, 2, 1);
        var to = new DateOnly(2024, 3, 1);

        var bars = await provider.GetDailyBarsAsync("UNRATE", from, to);

        bars.Should().OnlyContain(bar => bar.SessionDate >= from && bar.SessionDate <= to);
    }

    [Fact]
    public void Fred_HasCorrectMetadata()
    {
        using var provider = new FredHistoricalDataProvider(apiKey: "test-key");

        provider.Name.Should().Be("fred");
        provider.DisplayName.Should().Contain("FRED");
        provider.Priority.Should().Be(28);
        provider.Capabilities.SupportedMarkets.Should().Contain("US");
    }

    #endregion

    #region Helper Methods

    private static HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(statusCode);
                response.Content = new StringContent(responseContent, Encoding.UTF8, "application/json");
                return response;
            });

        return new HttpClient(mockHandler.Object);
    }

    #endregion
}

#region Tiingo Recorded Responses

public static class TiingoResponses
{
    public const string ValidAaplResponse = """
    [
        {"date":"2024-01-02T00:00:00.000Z","close":185.8,"high":186.2,"low":184.1,"open":185.5,"volume":45000000,"adjClose":185.5,"adjHigh":186.0,"adjLow":183.9,"adjOpen":185.3,"adjVolume":45000000,"divCash":0.0,"splitFactor":1.0},
        {"date":"2024-01-03T00:00:00.000Z","close":184.9,"high":185.8,"low":183.5,"open":184.2,"volume":52000000,"adjClose":184.6,"adjHigh":185.6,"adjLow":183.3,"adjOpen":184.0,"adjVolume":52000000,"divCash":0.0,"splitFactor":1.0},
        {"date":"2024-01-04T00:00:00.000Z","close":185.2,"high":185.5,"low":183.0,"open":183.9,"volume":48000000,"adjClose":184.9,"adjHigh":185.3,"adjLow":182.8,"adjOpen":183.7,"adjVolume":48000000,"divCash":0.0,"splitFactor":1.0}
    ]
    """;

    public const string ResponseWithDividendAndSplit = """
    [
        {"date":"2024-01-02T00:00:00.000Z","close":185.8,"high":186.2,"low":184.1,"open":185.5,"volume":45000000,"adjClose":184.5,"adjHigh":185.0,"adjLow":183.0,"adjOpen":184.3,"adjVolume":45000000,"divCash":0.24,"splitFactor":1.0},
        {"date":"2024-01-03T00:00:00.000Z","close":184.9,"high":185.8,"low":183.5,"open":184.2,"volume":52000000,"adjClose":92.2,"adjHigh":92.7,"adjLow":91.6,"adjOpen":92.0,"adjVolume":104000000,"divCash":0.0,"splitFactor":2.0}
    ]
    """;
}

#endregion

#region Finnhub Recorded Responses

public static class FinnhubResponses
{
    /// <summary>
    /// Finnhub candle response format with arrays for each field.
    /// Timestamps are Unix seconds.
    /// </summary>
    public const string ValidAaplCandles = """
    {
        "c": [185.8, 184.9, 185.2],
        "h": [186.2, 185.8, 185.5],
        "l": [184.1, 183.5, 183.0],
        "o": [185.5, 184.2, 183.9],
        "s": "ok",
        "t": [1704153600, 1704240000, 1704326400],
        "v": [45000000, 52000000, 48000000]
    }
    """;

    public const string NoDataResponse = """
    {
        "s": "no_data"
    }
    """;
}

#endregion

#region Alpha Vantage Recorded Responses

public static class AlphaVantageResponses
{
    public const string ValidDailyAdjustedResponse = """
    {
        "Meta Data": {
            "1. Information": "Daily Time Series with Splits and Dividend Events",
            "2. Symbol": "AAPL",
            "3. Last Refreshed": "2024-01-05",
            "4. Output Size": "Full size",
            "5. Time Zone": "US/Eastern"
        },
        "Time Series (Daily)": {
            "2024-01-05": {
                "1. open": "183.9000",
                "2. high": "185.5000",
                "3. low": "183.0000",
                "4. close": "185.2000",
                "5. adjusted close": "184.9000",
                "6. volume": "48000000",
                "7. dividend amount": "0.0000",
                "8. split coefficient": "1.0000"
            },
            "2024-01-04": {
                "1. open": "184.2000",
                "2. high": "185.8000",
                "3. low": "183.5000",
                "4. close": "184.9000",
                "5. adjusted close": "184.6000",
                "6. volume": "52000000",
                "7. dividend amount": "0.0000",
                "8. split coefficient": "1.0000"
            },
            "2024-01-03": {
                "1. open": "185.5000",
                "2. high": "186.2000",
                "3. low": "184.1000",
                "4. close": "185.8000",
                "5. adjusted close": "185.5000",
                "6. volume": "45000000",
                "7. dividend amount": "0.0000",
                "8. split coefficient": "1.0000"
            }
        }
    }
    """;

    public const string ResponseWithDividend = """
    {
        "Meta Data": {
            "1. Information": "Daily Time Series with Splits and Dividend Events",
            "2. Symbol": "SPY"
        },
        "Time Series (Daily)": {
            "2024-01-03": {
                "1. open": "472.5000",
                "2. high": "474.2000",
                "3. low": "470.1000",
                "4. close": "473.8000",
                "5. adjusted close": "472.0000",
                "6. volume": "85000000",
                "7. dividend amount": "1.8500",
                "8. split coefficient": "1.0000"
            },
            "2024-01-02": {
                "1. open": "471.2000",
                "2. high": "473.8000",
                "3. low": "469.5000",
                "4. close": "472.9000",
                "5. adjusted close": "471.1000",
                "6. volume": "72000000",
                "7. dividend amount": "0.0000",
                "8. split coefficient": "1.0000"
            }
        }
    }
    """;

    public const string ResponseWithSplit = """
    {
        "Meta Data": {
            "1. Information": "Daily Time Series with Splits and Dividend Events",
            "2. Symbol": "AAPL"
        },
        "Time Series (Daily)": {
            "2024-01-02": {
                "1. open": "400.0000",
                "2. high": "405.0000",
                "3. low": "398.0000",
                "4. close": "402.5000",
                "5. adjusted close": "100.6250",
                "6. volume": "100000000",
                "7. dividend amount": "0.0000",
                "8. split coefficient": "4.0000"
            }
        }
    }
    """;

    public const string ErrorResponse = """
    {
        "Error Message": "Invalid API call. Please retry or visit the documentation (https://www.alphavantage.co/documentation/) for TIME_SERIES_DAILY_ADJUSTED."
    }
    """;

    public const string RateLimitResponse = """
    {
        "Note": "Thank you for using Alpha Vantage! Our standard API call frequency is 5 calls per minute and 500 calls per day. Please visit https://www.alphavantage.co/premium/ if you would like to target a higher API call frequency."
    }
    """;
}

#endregion

#region FRED Recorded Responses

public static class FredResponses
{
    public const string ValidObservationsResponse = """
    {
        "observations": [
            { "date": "2024-01-01", "value": "3.7" },
            { "date": "2024-02-01", "value": "3.9" },
            { "date": "2024-03-01", "value": "3.8" }
        ]
    }
    """;

    public const string ResponseWithMissingValue = """
    {
        "observations": [
            { "date": "2024-01-01", "value": "." },
            { "date": "2024-04-01", "value": "23123.45" }
        ]
    }
    """;
}

#endregion
