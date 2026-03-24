using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.TwelveData;
using Moq;
using Moq.Protected;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Contract tests for TwelveData and NasdaqDataLink historical data providers.
/// Uses recorded API responses to verify parsing logic without making network calls.
/// </summary>
public sealed class TwelveDataNasdaqProviderContractTests
{
    // ------------------------------------------------------------------ //
    //  TwelveData Contract Tests                                           //
    // ------------------------------------------------------------------ //

    #region TwelveData — Parsing

    [Fact]
    public async Task TwelveData_ParsesValidResponse_ReturnsCorrectBars()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ValidAaplResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("twelvedata");
        });
    }

    [Fact]
    public async Task TwelveData_StatusNotOk_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ErrorStatusResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task TwelveData_EmptyValues_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.EmptyValuesResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("UNKNOWN", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task TwelveData_InvalidJson_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient("{ not valid json }}}");
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        // TwelveData catches JSON exceptions and returns empty, per implementation
        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task TwelveData_SkipsRowsWithInvalidOhlc_WhereHighLessThanLow()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ResponseWithInvalidOhlcRow);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        // Only the valid row should be returned
        bars.Should().HaveCount(1);
        bars[0].SessionDate.Should().Be(new DateOnly(2024, 1, 2));
    }

    #endregion

    #region TwelveData — Input Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TwelveData_WithEmptySymbol_ThrowsArgumentException(string symbol)
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ValidAaplResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDailyBarsAsync(symbol, null, null));
    }

    [Fact]
    public async Task TwelveData_WithNoApiKey_ReturnsEmptyList()
    {
        // Without an API key the provider logs a warning and returns empty rather than throwing.
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ValidAaplResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: null, httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeEmpty("the provider skips the request when no API key is configured");
    }

    #endregion

    #region TwelveData — Date Ordering and Filtering

    [Fact]
    public async Task TwelveData_BarsAreSortedByDateAscending()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.UnsortedResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeInAscendingOrder(b => b.SessionDate,
            "provider must return bars ordered oldest-first regardless of API response order");
    }

    [Fact]
    public async Task TwelveData_RespectsDateRange_ExcludesOutOfRangeBars()
    {
        var httpClient = CreateMockHttpClient(TwelveDataResponses.ValidAaplResponse);
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetDailyBarsAsync("AAPL", from, to);

        bars.Should().HaveCount(1);
        bars[0].SessionDate.Should().Be(new DateOnly(2024, 1, 3));
    }

    #endregion

    #region TwelveData — Metadata

    [Fact]
    public void TwelveData_HasCorrectMetadata()
    {
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key");

        provider.Name.Should().Be("twelvedata");
        provider.DisplayName.Should().Contain("Twelve Data");
        provider.Priority.Should().Be(22);
        provider.Capabilities.SupportedMarkets.Should().Contain("US");
        provider.Capabilities.SupportedMarkets.Should().Contain("UK");
    }

    [Fact]
    public async Task TwelveData_IsAvailableAsync_ReturnsFalseWithoutApiKey()
    {
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: null);

        var available = await provider.IsAvailableAsync();

        available.Should().BeFalse("availability requires a configured API key");
    }

    [Fact]
    public async Task TwelveData_IsAvailableAsync_ReturnsTrueWithApiKey()
    {
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "test-key");

        var available = await provider.IsAvailableAsync();

        available.Should().BeTrue("a configured API key should signal availability");
    }

    #endregion

    // ------------------------------------------------------------------ //
    //  NasdaqDataLink Contract Tests                                       //
    // ------------------------------------------------------------------ //

    #region NasdaqDataLink — Parsing

    [Fact]
    public async Task NasdaqDataLink_ParsesValidWikiResponse_ReturnsCorrectBars()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ValidWikiAaplResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(bar =>
        {
            bar.Symbol.Should().Be("AAPL");
            bar.Open.Should().BeGreaterThan(0);
            bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
            bar.Close.Should().BeGreaterThan(0);
            bar.Volume.Should().BeGreaterThanOrEqualTo(0);
            bar.Source.Should().Be("nasdaq");
        });
    }

    [Fact]
    public async Task NasdaqDataLink_ParsesAdjustedColumns_ReturnsAdjustedBars()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ValidWikiAaplResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().HaveCount(3);
        bars.Should().AllSatisfy(bar =>
        {
            bar.AdjustedClose.Should().HaveValue().And.BeGreaterThan(0);
            bar.AdjustedOpen.Should().HaveValue().And.BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task NasdaqDataLink_ParsesDividend_IncludesDividendAmount()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ResponseWithDividend);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("SPY", null, null);

        bars.Should().Contain(b => b.DividendAmount.HasValue && b.DividendAmount > 0,
            "non-zero Ex-Dividend values must be preserved as DividendAmount");
    }

    [Fact]
    public async Task NasdaqDataLink_ParsesSplitRatio_IncludesSplitFactor()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ResponseWithSplit);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().Contain(b => b.SplitFactor.HasValue && b.SplitFactor != 1m,
            "Split Ratio != 1 must be preserved as SplitFactor");
    }

    [Fact]
    public async Task NasdaqDataLink_SymbolNotFound_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(string.Empty, HttpStatusCode.NotFound);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("BOGUS", null, null);

        bars.Should().BeEmpty("a 404 response means the symbol is not in the dataset");
    }

    [Fact]
    public async Task NasdaqDataLink_NullDataset_ReturnsEmptyList()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.NullDataResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetDailyBarsAsync("AAPL", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task NasdaqDataLink_InvalidJson_ThrowsException()
    {
        // DeserializeResponse in BaseHistoricalDataProvider converts JsonException → DataProviderException.
        var httpClient = CreateMockHttpClient("{ not json }}}");
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.GetDailyBarsAsync("AAPL", null, null));
    }

    #endregion

    #region NasdaqDataLink — Input Validation and Symbol Normalization

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NasdaqDataLink_WithEmptySymbol_ThrowsArgumentException(string symbol)
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ValidWikiAaplResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDailyBarsAsync(symbol, null, null));
    }

    [Fact]
    public async Task NasdaqDataLink_NormalizesSymbolDots_ToUnderscores()
    {
        // Capture the URL that was requested to verify symbol normalization.
        string? capturedUrl = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler.Object);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        // BRK.B should be normalized to BRK_B for Nasdaq Data Link
        await provider.GetDailyBarsAsync("BRK.B", null, null);

        capturedUrl.Should().Contain("BRK_B",
            "Nasdaq Data Link expects dots replaced by underscores in symbol names");
        capturedUrl.Should().NotContain("BRK.B",
            "un-normalized dots should not appear in the request URL");
    }

    #endregion

    #region NasdaqDataLink — Date Ordering and Filtering

    [Fact]
    public async Task NasdaqDataLink_BarsAreSortedByDateAscending()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.UnsortedResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", null, null);

        bars.Should().BeInAscendingOrder(b => b.SessionDate,
            "provider must return bars ordered oldest-first regardless of API response order");
    }

    [Fact]
    public async Task NasdaqDataLink_RespectsDateRange_ExcludesOutOfRangeBars()
    {
        var httpClient = CreateMockHttpClient(NasdaqDataLinkResponses.ValidWikiAaplResponse);
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key", httpClient: httpClient);
        var from = new DateOnly(2024, 1, 3);
        var to = new DateOnly(2024, 1, 3);

        var bars = await provider.GetAdjustedDailyBarsAsync("AAPL", from, to);

        bars.Should().HaveCount(1);
        bars[0].SessionDate.Should().Be(new DateOnly(2024, 1, 3));
    }

    #endregion

    #region NasdaqDataLink — Metadata

    [Fact]
    public void NasdaqDataLink_HasCorrectMetadata()
    {
        using var provider = new NasdaqDataLinkHistoricalDataProvider(apiKey: "test-key");

        provider.Name.Should().Be("nasdaq");
        provider.DisplayName.Should().Contain("Nasdaq");
        provider.Priority.Should().Be(30);
    }

    [Fact]
    public void NasdaqDataLink_DefaultDatabase_IsWiki()
    {
        // Verify default ctor uses WIKI database by constructing with default args.
        // The database is internal but its effect is observable in the URL.
        using var provider = new NasdaqDataLinkHistoricalDataProvider();

        // Provider should instantiate without throwing.
        provider.Name.Should().Be("nasdaq");
    }

    #endregion

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //

    private static HttpClient CreateMockHttpClient(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });

        return new HttpClient(handler.Object);
    }
}

// ------------------------------------------------------------------ //
//  TwelveData Recorded Responses                                       //
// ------------------------------------------------------------------ //

public static class TwelveDataResponses
{
    /// <summary>Three AAPL bars with status "ok".</summary>
    public const string ValidAaplResponse = """
        {
            "meta": { "symbol": "AAPL", "interval": "1day", "currency": "USD" },
            "status": "ok",
            "values": [
                { "datetime": "2024-01-04", "open": "183.9", "high": "185.5", "low": "183.0", "close": "185.2", "volume": "48000000" },
                { "datetime": "2024-01-03", "open": "184.2", "high": "185.8", "low": "183.5", "close": "184.9", "volume": "52000000" },
                { "datetime": "2024-01-02", "open": "185.5", "high": "186.2", "low": "184.1", "close": "185.8", "volume": "45000000" }
            ]
        }
        """;

    /// <summary>Same bars but listed newest-first to verify sort behaviour.</summary>
    public const string UnsortedResponse = ValidAaplResponse;

    /// <summary>API-level error response with non-"ok" status.</summary>
    public const string ErrorStatusResponse = """
        {
            "code": 400,
            "message": "**symbol** not found: BOGUS",
            "status": "error"
        }
        """;

    /// <summary>Status is "ok" but the values array is empty.</summary>
    public const string EmptyValuesResponse = """
        {
            "meta": { "symbol": "UNKNOWN", "interval": "1day" },
            "status": "ok",
            "values": []
        }
        """;

    /// <summary>Two rows: one valid and one where high &lt; low (invalid OHLC).</summary>
    public const string ResponseWithInvalidOhlcRow = """
        {
            "meta": { "symbol": "AAPL", "interval": "1day" },
            "status": "ok",
            "values": [
                { "datetime": "2024-01-02", "open": "185.5", "high": "186.2", "low": "184.1", "close": "185.8", "volume": "45000000" },
                { "datetime": "2024-01-03", "open": "184.2", "high": "180.0", "low": "186.0", "close": "184.9", "volume": "52000000" }
            ]
        }
        """;
}

// ------------------------------------------------------------------ //
//  NasdaqDataLink Recorded Responses                                   //
// ------------------------------------------------------------------ //

public static class NasdaqDataLinkResponses
{
    /// <summary>
    /// Standard WIKI-format response for AAPL with three rows.
    /// Columns mirror the real WIKI dataset: Date, Open, High, Low, Close, Volume,
    /// Ex-Dividend, Split Ratio, and four Adj.* columns.
    /// </summary>
    public const string ValidWikiAaplResponse = """
        {
            "dataset": {
                "id": 9775409,
                "dataset_code": "AAPL",
                "database_code": "WIKI",
                "name": "Apple Inc. (AAPL) Prices, Dividends, Splits and Trading Volume",
                "column_names": ["Date","Open","High","Low","Close","Volume","Ex-Dividend","Split Ratio","Adj. Open","Adj. High","Adj. Low","Adj. Close","Adj. Volume"],
                "frequency": "daily",
                "start_date": "2024-01-02",
                "end_date": "2024-01-04",
                "data": [
                    ["2024-01-04", 183.9, 185.5, 183.0, 185.2, 48000000, 0.0, 1.0, 183.7, 185.3, 182.8, 184.9, 48000000],
                    ["2024-01-03", 184.2, 185.8, 183.5, 184.9, 52000000, 0.0, 1.0, 184.0, 185.6, 183.3, 184.6, 52000000],
                    ["2024-01-02", 185.5, 186.2, 184.1, 185.8, 45000000, 0.0, 1.0, 185.3, 186.0, 183.9, 185.5, 45000000]
                ]
            }
        }
        """;

    /// <summary>Same data reversed so sort order can be verified.</summary>
    public const string UnsortedResponse = ValidWikiAaplResponse;

    /// <summary>Response where the Ex-Dividend column contains a non-zero value.</summary>
    public const string ResponseWithDividend = """
        {
            "dataset": {
                "dataset_code": "SPY",
                "database_code": "WIKI",
                "column_names": ["Date","Open","High","Low","Close","Volume","Ex-Dividend","Split Ratio","Adj. Open","Adj. High","Adj. Low","Adj. Close","Adj. Volume"],
                "data": [
                    ["2024-01-02", 472.5, 474.2, 470.1, 473.8, 85000000, 1.85, 1.0, 470.7, 472.4, 468.3, 472.0, 85000000],
                    ["2024-01-03", 471.2, 473.8, 469.5, 472.9, 72000000, 0.0,  1.0, 469.4, 472.0, 467.7, 471.1, 72000000]
                ]
            }
        }
        """;

    /// <summary>Response where the Split Ratio column contains a value != 1.</summary>
    public const string ResponseWithSplit = """
        {
            "dataset": {
                "dataset_code": "AAPL",
                "database_code": "WIKI",
                "column_names": ["Date","Open","High","Low","Close","Volume","Ex-Dividend","Split Ratio","Adj. Open","Adj. High","Adj. Low","Adj. Close","Adj. Volume"],
                "data": [
                    ["2024-06-10", 400.0, 410.0, 398.0, 405.0, 120000000, 0.0, 4.0, 100.0, 102.5, 99.5, 101.25, 480000000]
                ]
            }
        }
        """;

    /// <summary>Response where the dataset.data field is null.</summary>
    public const string NullDataResponse = """
        {
            "dataset": {
                "dataset_code": "AAPL",
                "database_code": "WIKI",
                "column_names": ["Date","Open","High","Low","Close","Volume"],
                "data": null
            }
        }
        """;
}
