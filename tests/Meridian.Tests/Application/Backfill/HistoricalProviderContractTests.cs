using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Moq;
using Moq.Protected;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Contract tests for historical data providers using recorded API responses.
/// These tests verify that providers correctly parse real API responses without making network calls.
/// </summary>
public class HistoricalProviderContractTests
{
    #region Yahoo Finance Contract Tests

    [Fact]
    public async Task YahooFinance_ParsesValidResponse_ReturnsCorrectBars()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidAaplResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Open);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Close);
            bar.Low.Should().BeLessThanOrEqualTo(bar.Open);
            bar.Low.Should().BeLessThanOrEqualTo(bar.Close);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("yahoo");
        });
    }

    [Fact]
    public async Task YahooFinance_ParsesResponseWithDividends_IncludesDividendData()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ResponseWithDividends);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetAdjustedDailyBarsAsync("SPY", null, null);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().Contain(b => b.DividendAmount.HasValue && b.DividendAmount > 0);
    }

    [Fact]
    public async Task YahooFinance_ParsesResponseWithSplits_CalculatesAdjustmentFactor()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ResponseWithSplit);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().Contain(b => b.AdjustedClose.HasValue);
    }

    [Fact]
    public async Task YahooFinance_HandlesEmptyResult_ReturnsEmptyList()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.EmptyResult);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null);

        // Assert
        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task YahooFinance_HandlesNullValues_SkipsInvalidBars()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ResponseWithNullValues);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        // Assert
        bars.Should().AllSatisfy(bar =>
        {
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThan(0);
            bar.Low.Should().BeGreaterThan(0);
            bar.Close.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task YahooFinance_RespectDateRange_FiltersCorrectly()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidAaplResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 4);

        // Act
        var bars = await provider.GetDailyBarsAsync("AAPL", from, to);

        // Assert
        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public async Task YahooFinance_WithInvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateMockHttpClient("{ invalid json }");
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    [Fact]
    public async Task YahooFinance_WithHttpError_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateMockHttpClient("Not Found", HttpStatusCode.NotFound);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("INVALID", null, null));

        ex.Message.Should().Contain("404");
    }

    [Fact]
    public async Task YahooFinance_WithEmptySymbol_ThrowsArgumentException()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidAaplResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetDailyBarsAsync("", null, null));
    }

    [Fact]
    public async Task YahooFinance_BarsAreSortedByDate()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidAaplResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        // Assert
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    #endregion

    #region Preferred Stock Tests (PCG-A)

    [Fact]
    public async Task YahooFinance_ParsesPreferredStockPCGA_ReturnsCorrectBars()
    {
        // Arrange - PCG-A is Pacific Gas & Electric Preferred Stock Series A
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidPcgAResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("PCG-A", null, null);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().HaveCount(5);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("PCG-A");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("yahoo");
        });
    }

    [Fact]
    public async Task YahooFinance_PreferredStockPCGA_ParsesCorrectPriceValues()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidPcgAResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("PCG-A", null, null);
        var firstBar = bars.First();

        // Assert - Verify specific price values from the mock response
        firstBar.SessionDate.Should().Be(new DateOnly(2024, 1, 2));
        firstBar.Open.Should().Be(16.75m);
        firstBar.High.Should().Be(16.95m);
        firstBar.Low.Should().Be(16.60m);
        firstBar.Close.Should().Be(16.85m);
        firstBar.Volume.Should().Be(12500);
    }

    [Fact]
    public async Task YahooFinance_PreferredStockPCGA_RespectsDateRange()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidPcgAResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 5);

        // Act
        var bars = await provider.GetDailyBarsAsync("PCG-A", from, to);

        // Assert
        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public async Task YahooFinance_PreferredStockPCGA_IncludesDividendData()
    {
        // Arrange - Preferred stocks typically have regular dividend payments
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.PcgAWithDividend);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetAdjustedDailyBarsAsync("PCG-A", null, null);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().Contain(b => b.DividendAmount.HasValue && b.DividendAmount > 0);
    }

    [Fact]
    public async Task YahooFinance_PreferredStockPCGA_BarsAreSortedByDate()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(YahooFinanceResponses.ValidPcgAResponse);
        using var provider = new YahooFinanceHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("PCG-A", null, null);

        // Assert
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    #endregion

    #region Stooq Contract Tests

    [Fact]
    public async Task Stooq_ParsesValidCsvResponse_ReturnsCorrectBars()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ValidSpyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("SPY", null, null);

        // Assert
        bars.Should().HaveCount(5);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("SPY");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("stooq");
        });
    }

    [Fact]
    public async Task Stooq_ParsesRealCsvFormat_ExtractsAllFields()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ValidSpyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("SPY", null, null);
        var firstBar = bars.First();

        // Assert
        firstBar.SessionDate.Should().Be(new DateOnly(2024, 1, 2));
        firstBar.Open.Should().Be(472.45m);
        firstBar.High.Should().Be(474.20m);
        firstBar.Low.Should().Be(470.10m);
        firstBar.Close.Should().Be(473.15m);
        firstBar.Volume.Should().Be(85000000);
    }

    [Fact]
    public async Task Stooq_RespectsDateRange_FiltersCorrectly()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ValidSpyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 4);

        // Act
        var bars = await provider.GetDailyBarsAsync("SPY", from, to);

        // Assert
        bars.Should().HaveCount(2);
        bars.Should().AllSatisfy(bar =>
        {
            bar.SessionDate.Should().BeOnOrAfter(from);
            bar.SessionDate.Should().BeOnOrBefore(to);
        });
    }

    [Fact]
    public async Task Stooq_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.EmptyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null);

        // Assert
        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task Stooq_WithMalformedRows_SkipsInvalidLines()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ResponseWithMalformedRows);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("SPY", null, null);

        // Assert
        bars.Should().HaveCount(2); // Only valid rows
        bars.Should().AllSatisfy(bar =>
        {
            bar.Open.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task Stooq_WithHttpError_ThrowsInvalidOperationException()
    {
        // Arrange
        var httpClient = CreateMockHttpClient("Error", HttpStatusCode.InternalServerError);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetDailyBarsAsync("SPY", null, null));
    }

    [Fact]
    public async Task Stooq_WithEmptySymbol_ThrowsArgumentException()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ValidSpyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetDailyBarsAsync("", null, null));
    }

    [Fact]
    public async Task Stooq_BarsAreSortedByDate()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(StooqResponses.ValidSpyResponse);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        var bars = await provider.GetDailyBarsAsync("SPY", null, null);

        // Assert
        bars.Should().BeInAscendingOrder(b => b.SessionDate);
    }

    [Fact]
    public async Task Stooq_NormalizesSymbol_ConvertsDotToHyphen()
    {
        // Arrange - The URL should have the normalized symbol
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(StooqResponses.ValidSpyResponse, Encoding.UTF8, "text/csv")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var provider = new StooqHistoricalDataProvider(httpClient);

        // Act
        await provider.GetDailyBarsAsync("BRK.B", null, null);

        // Assert - Should normalize BRK.B to brk-b
        capturedUrl.Should().Contain("brk-b.us");
    }

    #endregion

    #region Provider Metadata Tests

    [Fact]
    public void YahooFinance_HasCorrectMetadata()
    {
        // Arrange
        using var provider = new YahooFinanceHistoricalDataProvider();

        // Assert
        provider.Name.Should().Be("yahoo");
        provider.DisplayName.Should().Contain("Yahoo");
        provider.Priority.Should().Be(22);
        provider.SupportsAdjustedPrices.Should().BeTrue();
        provider.SupportsDividends.Should().BeTrue();
        provider.SupportsSplits.Should().BeTrue();
        provider.SupportsIntraday.Should().BeTrue();
        provider.SupportedMarkets.Should().Contain("US");

        provider.Should().BeAssignableTo<IHistoricalAggregateBarProvider>();
        var aggregateProvider = (IHistoricalAggregateBarProvider)provider;
        aggregateProvider.SupportedGranularities.Should().Contain([
            DataGranularity.Minute1,
            DataGranularity.Minute5,
            DataGranularity.Minute15,
            DataGranularity.Minute30,
            DataGranularity.Hour1,
            DataGranularity.Hour4
        ]);
    }

    [Fact]
    public void Stooq_HasCorrectMetadata()
    {
        // Arrange
        var provider = new StooqHistoricalDataProvider();

        // Assert
        provider.Name.Should().Be("stooq");
        provider.DisplayName.Should().Contain("Stooq");
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

/// <summary>
/// Recorded Yahoo Finance API responses for contract testing.
/// </summary>
public static class YahooFinanceResponses
{
    public const string ValidAaplResponse = """
    {
        "chart": {
            "result": [{
                "meta": {
                    "currency": "USD",
                    "symbol": "AAPL",
                    "exchangeName": "NMS",
                    "instrumentType": "EQUITY",
                    "regularMarketPrice": 185.92
                },
                "timestamp": [1704204600, 1704291000, 1704377400],
                "indicators": {
                    "quote": [{
                        "open": [185.5, 184.2, 183.9],
                        "high": [186.2, 185.8, 185.5],
                        "low": [184.1, 183.5, 183.0],
                        "close": [185.8, 184.9, 185.2],
                        "volume": [45000000, 52000000, 48000000]
                    }],
                    "adjclose": [{
                        "adjclose": [185.5, 184.6, 184.9]
                    }]
                }
            }],
            "error": null
        }
    }
    """;

    public const string ResponseWithDividends = """
    {
        "chart": {
            "result": [{
                "meta": {"symbol": "SPY", "currency": "USD"},
                "timestamp": [1704204600, 1704291000],
                "events": {
                    "dividends": {
                        "1704204600": {"amount": 1.85, "date": 1704204600}
                    }
                },
                "indicators": {
                    "quote": [{
                        "open": [472.5, 471.2],
                        "high": [474.2, 473.8],
                        "low": [470.1, 469.5],
                        "close": [473.8, 472.9],
                        "volume": [85000000, 72000000]
                    }],
                    "adjclose": [{
                        "adjclose": [472.0, 471.1]
                    }]
                }
            }],
            "error": null
        }
    }
    """;

    public const string ResponseWithSplit = """
    {
        "chart": {
            "result": [{
                "meta": {"symbol": "AAPL", "currency": "USD"},
                "timestamp": [1704204600],
                "events": {
                    "splits": {
                        "1704204600": {"numerator": 4, "denominator": 1, "splitRatio": "4:1"}
                    }
                },
                "indicators": {
                    "quote": [{
                        "open": [400.0],
                        "high": [405.0],
                        "low": [398.0],
                        "close": [402.5],
                        "volume": [100000000]
                    }],
                    "adjclose": [{
                        "adjclose": [100.625]
                    }]
                }
            }],
            "error": null
        }
    }
    """;

    public const string EmptyResult = """
    {
        "chart": {
            "result": [{
                "meta": {"symbol": "UNKNOWN"},
                "timestamp": null,
                "indicators": {
                    "quote": [{}]
                }
            }],
            "error": null
        }
    }
    """;

    public const string ResponseWithNullValues = """
    {
        "chart": {
            "result": [{
                "meta": {"symbol": "AAPL", "currency": "USD"},
                "timestamp": [1704204600, 1704291000, 1704377400],
                "indicators": {
                    "quote": [{
                        "open": [185.5, null, 183.9],
                        "high": [186.2, null, 185.5],
                        "low": [184.1, null, 183.0],
                        "close": [185.8, null, 185.2],
                        "volume": [45000000, null, 48000000]
                    }],
                    "adjclose": [{
                        "adjclose": [185.5, null, 184.9]
                    }]
                }
            }],
            "error": null
        }
    }
    """;

    /// <summary>
    /// Valid response for PCG-A (Pacific Gas and Electric Preferred Stock Series A).
    /// Preferred stocks typically have lower volume and stable price ranges.
    /// </summary>
    public const string ValidPcgAResponse = """
    {
        "chart": {
            "result": [{
                "meta": {
                    "currency": "USD",
                    "symbol": "PCG-PA",
                    "exchangeName": "NYQ",
                    "instrumentType": "EQUITY",
                    "regularMarketPrice": 17.10
                },
                "timestamp": [1704204600, 1704291000, 1704377400, 1704463800, 1704722400],
                "indicators": {
                    "quote": [{
                        "open": [16.75, 16.85, 16.90, 17.00, 17.05],
                        "high": [16.95, 17.00, 17.05, 17.15, 17.20],
                        "low": [16.60, 16.75, 16.80, 16.90, 16.95],
                        "close": [16.85, 16.92, 16.98, 17.08, 17.10],
                        "volume": [12500, 15200, 8900, 11300, 9800]
                    }],
                    "adjclose": [{
                        "adjclose": [16.85, 16.92, 16.98, 17.08, 17.10]
                    }]
                }
            }],
            "error": null
        }
    }
    """;

    /// <summary>
    /// PCG-A response with dividend event (preferred stocks pay regular dividends).
    /// </summary>
    public const string PcgAWithDividend = """
    {
        "chart": {
            "result": [{
                "meta": {
                    "currency": "USD",
                    "symbol": "PCG-PA",
                    "exchangeName": "NYQ",
                    "instrumentType": "EQUITY"
                },
                "timestamp": [1704204600, 1704291000],
                "events": {
                    "dividends": {
                        "1704204600": {"amount": 0.3125, "date": 1704204600}
                    }
                },
                "indicators": {
                    "quote": [{
                        "open": [16.75, 16.50],
                        "high": [16.95, 16.70],
                        "low": [16.60, 16.40],
                        "close": [16.85, 16.55],
                        "volume": [25000, 18500]
                    }],
                    "adjclose": [{
                        "adjclose": [16.5375, 16.55]
                    }]
                }
            }],
            "error": null
        }
    }
    """;
}

/// <summary>
/// Recorded Stooq API responses for contract testing.
/// </summary>
public static class StooqResponses
{
    public const string ValidSpyResponse = """
    Date,Open,High,Low,Close,Volume
    2024-01-02,472.45,474.20,470.10,473.15,85000000
    2024-01-03,473.00,475.50,472.00,474.80,92000000
    2024-01-04,474.50,476.00,473.50,475.20,78000000
    2024-01-05,475.00,477.20,474.00,476.50,81000000
    2024-01-08,476.30,478.00,475.50,477.10,75000000
    """;

    public const string EmptyResponse = """
    Date,Open,High,Low,Close,Volume
    """;

    public const string ResponseWithMalformedRows = """
    Date,Open,High,Low,Close,Volume
    2024-01-02,472.45,474.20,470.10,473.15,85000000
    invalid-date,abc,def,ghi,jkl,mno
    2024-01-03,473.00,475.50,472.00,474.80,92000000
    2024-01-04
    """;
}
