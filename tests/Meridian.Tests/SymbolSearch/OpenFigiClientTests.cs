using System.Net;
using System.Text;
using FluentAssertions;
using Meridian.Application.Subscriptions.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Xunit;

namespace Meridian.Tests.SymbolSearch;

/// <summary>
/// Unit tests for the OpenFigiClient class.
/// Tests FIGI lookup functionality and bulk operations.
/// </summary>
public class OpenFigiClientTests : IDisposable
{
    private OpenFigiClient? _client;

    public void Dispose()
    {
        _client?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNoApiKey_CreatesInstance()
    {
        // Act
        _client = new OpenFigiClient();

        // Assert
        _client.Should().NotBeNull();
        _client.Name.Should().Be("openfigi");
        _client.DisplayName.Should().Be("OpenFIGI");
    }

    [Fact]
    public void Constructor_WithApiKey_CreatesInstance()
    {
        // Act
        _client = new OpenFigiClient(apiKey: "test-api-key");

        // Assert
        _client.Should().NotBeNull();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        _client = new OpenFigiClient();

        // Act & Assert - Should not throw
        _client.Dispose();
        _client.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task LookupByTickerAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        _client = new OpenFigiClient();
        _client.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _client.LookupByTickerAsync("AAPL"));
    }

    [Fact]
    public async Task SearchAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        _client = new OpenFigiClient();
        _client.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _client.SearchAsync("apple"));
    }

    #endregion

    #region Input Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyResults(string? query)
    {
        // Arrange
        _client = new OpenFigiClient();

        // Act
        var results = await _client.SearchAsync(query ?? "");

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region Bulk Lookup Tests

    [Fact]
    public async Task BulkLookupByTickersAsync_WithEmptyList_ReturnsEmptyDictionary()
    {
        // Arrange
        _client = new OpenFigiClient();

        // Act
        var results = await _client.BulkLookupByTickersAsync(Array.Empty<string>());

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task BulkLookupByTickersAsync_DeduplicatesTickers()
    {
        // Arrange
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"data\":[{\"figi\":\"BBG000B9XRY4\",\"ticker\":\"AAPL\"}]}]", Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(handler);
        _client = new OpenFigiClient(httpClient: httpClient);
        var tickers = new[] { "AAPL", "aapl", "AAPL", "Aapl" }; // All same ticker, different cases

        // Act
        var results = await _client.BulkLookupByTickersAsync(tickers);

        // Assert - Should only have one key
        results.Keys.Should().HaveCount(1);
        handler.CallCount.Should().Be(1, "deduplicated tickers should issue a single mapping request");
    }

    #endregion

    #region EnrichWithFigi Tests

    [Fact]
    public async Task EnrichWithFigiAsync_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange
        _client = new OpenFigiClient();

        // Act
        var results = await _client.EnrichWithFigiAsync(Array.Empty<SymbolSearchResult>());

        // Assert
        results.Should().BeEmpty();
    }

    #endregion
}

/// <summary>
/// Unit tests for FigiMapping model.
/// </summary>
public class FigiMappingTests
{
    [Fact]
    public void Constructor_WithFigi_CreatesValidInstance()
    {
        // Act
        var mapping = new FigiMapping(Figi: "BBG000B9XRY4");

        // Assert
        mapping.Figi.Should().Be("BBG000B9XRY4");
        mapping.CompositeFigi.Should().BeNull();
        mapping.Ticker.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        // Act
        var mapping = new FigiMapping(
            Figi: "BBG000B9XRY4",
            CompositeFigi: "BBG000B9Y5X2",
            SecurityType: "Common Stock",
            MarketSector: "Equity",
            Ticker: "AAPL",
            Name: "Apple Inc.",
            ExchangeCode: "US",
            ShareClassFigi: "BBG001S5N8V8",
            SecurityDescription: "APPLE INC"
        );

        // Assert
        mapping.Figi.Should().Be("BBG000B9XRY4");
        mapping.CompositeFigi.Should().Be("BBG000B9Y5X2");
        mapping.SecurityType.Should().Be("Common Stock");
        mapping.MarketSector.Should().Be("Equity");
        mapping.Ticker.Should().Be("AAPL");
        mapping.Name.Should().Be("Apple Inc.");
        mapping.ExchangeCode.Should().Be("US");
        mapping.ShareClassFigi.Should().Be("BBG001S5N8V8");
        mapping.SecurityDescription.Should().Be("APPLE INC");
    }
}

/// <summary>
/// Unit tests for FigiLookupRequest model.
/// </summary>
public class FigiLookupRequestTests
{
    [Fact]
    public void Constructor_WithTickerIdType_CreatesValidInstance()
    {
        // Act
        var request = new FigiLookupRequest(IdType: "ID_TICKER", IdValue: "AAPL");

        // Assert
        request.IdType.Should().Be("ID_TICKER");
        request.IdValue.Should().Be("AAPL");
        request.ExchCode.Should().BeNull();
        request.MarketSecDes.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithIsinIdType_CreatesValidInstance()
    {
        // Act
        var request = new FigiLookupRequest(
            IdType: "ID_ISIN",
            IdValue: "US0378331005",
            MarketSecDes: "Equity");

        // Assert
        request.IdType.Should().Be("ID_ISIN");
        request.IdValue.Should().Be("US0378331005");
        request.MarketSecDes.Should().Be("Equity");
    }

    [Fact]
    public void Constructor_WithExchangeCode_SetsExchCode()
    {
        // Act
        var request = new FigiLookupRequest(
            IdType: "ID_TICKER",
            IdValue: "AAPL",
            ExchCode: "US");

        // Assert
        request.ExchCode.Should().Be("US");
    }
}


/// <summary>
/// Contract tests for OpenFigiClient — verify JSON parsing logic with recorded API responses.
/// </summary>
public sealed class OpenFigiClientParsingTests
{
    #region LookupByTickerAsync — Parsing

    [Fact]
    public async Task LookupByTicker_ParsesValidMappingResponse_ReturnsFigiMappings()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidAaplMappingResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByTickerAsync("AAPL");

        results.Should().HaveCount(1);
        results[0].Figi.Should().Be("BBG000B9XRY4");
        results[0].CompositeFigi.Should().Be("BBG000B9Y5X2");
        results[0].Ticker.Should().Be("AAPL");
        results[0].Name.Should().Be("APPLE INC");
        results[0].SecurityType.Should().Be("Common Stock");
        results[0].MarketSector.Should().Be("Equity");
        results[0].ExchangeCode.Should().Be("UW");
    }

    [Fact]
    public async Task LookupByTicker_WhenApiReturnsErrorField_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.TickerNotFoundResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByTickerAsync("BOGUS");

        results.Should().BeEmpty("an error field in the mapping response means the ticker was not found");
    }

    [Fact]
    public async Task LookupByTicker_WhenApiReturns429_ThrowsHttpRequestException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"error\":\"Too Many Requests\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LookupByTickerAsync("AAPL"));
    }

    [Fact]
    public async Task LookupByTicker_WhenApiReturnsNonSuccessStatus_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"message\":\"Internal error\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByTickerAsync("AAPL");

        results.Should().BeEmpty("non-success HTTP responses other than 429 should return empty rather than throw");
    }

    [Fact]
    public async Task LookupByTicker_WhenResponseContainsMultipleMappings_ReturnsAll()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.MultipleAaplMappingsResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByTickerAsync("AAPL");

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Ticker.Should().Be("AAPL"));
    }

    #endregion

    #region SearchAsync — Parsing

    [Fact]
    public async Task SearchAsync_ParsesValidFilterResponse_ReturnsResults()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidSearchResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.SearchAsync("Apple");

        results.Should().HaveCount(2);
        results[0].Figi.Should().Be("BBG000B9XRY4");
        results[0].Name.Should().Be("APPLE INC");
        results[1].Figi.Should().Be("BBG000B9Y5X2");
    }

    [Fact]
    public async Task SearchAsync_WhenApiReturnsNonSuccess_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"Bad Request\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.SearchAsync("Apple");

        results.Should().BeEmpty("non-success responses from the filter endpoint should return empty");
    }

    [Fact]
    public async Task SearchAsync_WhenResponseHasEmptyData_ReturnsEmpty()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[],\"total\":0}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.SearchAsync("xyzunknown");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit_TruncatesResults()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidSearchResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        // Response has 2 items; request only 1
        var results = await client.SearchAsync("Apple", limit: 1);

        results.Should().HaveCount(1);
    }

    #endregion

    #region LookupByIsinAsync / LookupByCusipAsync / LookupBySedolAsync — Parsing

    [Fact]
    public async Task LookupByIsin_ParsesValidMappingResponse_ReturnsFigiMappings()
    {
        using var handler = new StubHttpMessageHandler(req =>
        {
            // Verify the request body contains the correct idType
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            body.Should().Contain("\"ID_ISIN\"", "ISIN lookup must set idType to ID_ISIN");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidIsinMappingResponse, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByIsinAsync("US0378331005");

        results.Should().HaveCount(1);
        results[0].Figi.Should().Be("BBG000B9XRY4");
        results[0].Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task LookupByCusip_ParsesValidMappingResponse_ReturnsFigiMappings()
    {
        using var handler = new StubHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            body.Should().Contain("\"ID_CUSIP\"", "CUSIP lookup must set idType to ID_CUSIP");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidCusipMappingResponse, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByCusipAsync("037833100");

        results.Should().HaveCount(1);
        results[0].Figi.Should().Be("BBG000B9XRY4");
        results[0].Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task LookupBySedol_ParsesValidMappingResponse_ReturnsFigiMappings()
    {
        using var handler = new StubHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            body.Should().Contain("\"ID_SEDOL\"", "SEDOL lookup must set idType to ID_SEDOL");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidSedolMappingResponse, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupBySedolAsync("2046251");

        results.Should().HaveCount(1);
        results[0].Figi.Should().Be("BBG000B9XRY4");
        results[0].Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task LookupByIsin_WhenApiReturnsErrorField_ReturnsEmptyList()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.TickerNotFoundResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var results = await client.LookupByIsinAsync("US9999999999");

        results.Should().BeEmpty("error field in response means ISIN was not found");
    }

    [Fact]
    public async Task LookupByCusip_WhenApiReturns429_ThrowsHttpRequestException()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"error\":\"Too Many Requests\"}", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.LookupByCusipAsync("037833100"));
    }

    [Fact]
    public async Task BulkLookupByTickersAsync_WithMoreThan100Tickers_BatchesRequests()
    {
        var callCount = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"data\":[{\"figi\":\"BBG000B9XRY4\",\"ticker\":\"T\"}]}]",
                    Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        // 110 unique tickers should require 2 batches (100 + 10)
        var tickers = Enumerable.Range(1, 110).Select(i => $"SYM{i:D3}").ToArray();
        await client.BulkLookupByTickersAsync(tickers);

        callCount.Should().Be(2, "110 tickers should be split into 2 API calls of 100 each");
    }

    #endregion

    #region EnrichWithFigiAsync — Enrichment

    [Fact]
    public async Task EnrichWithFigiAsync_AddsCorrectFigiToMatchedSymbols()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.ValidAaplMappingResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var searchResults = new[]
        {
            new SymbolSearchResult("AAPL", "Apple Inc.", "NASDAQ", "Equity", null, null, "alpaca")
        };

        var enriched = await client.EnrichWithFigiAsync(searchResults);

        enriched.Should().HaveCount(1);
        enriched[0].Figi.Should().Be("BBG000B9XRY4");
        enriched[0].CompositeFigi.Should().Be("BBG000B9Y5X2");
    }

    [Fact]
    public async Task EnrichWithFigiAsync_WhenNoFigiFound_LeavesOriginalResultUnchanged()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenFigiResponses.TickerNotFoundResponse, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        using var client = new OpenFigiClient(httpClient: httpClient);

        var searchResults = new[]
        {
            new SymbolSearchResult("BOGUS", "Unknown Corp", "NYSE", "Equity", null, null, "alpaca")
        };

        var enriched = await client.EnrichWithFigiAsync(searchResults);

        enriched.Should().HaveCount(1);
        enriched[0].Figi.Should().BeNull("symbol not found in OpenFIGI should leave Figi unset");
        enriched[0].Symbol.Should().Be("BOGUS");
    }

    #endregion
}

/// <summary>
/// Recorded OpenFIGI API responses for use in contract tests.
/// </summary>
internal static class OpenFigiResponses
{
    /// <summary>Single AAPL equity result from the /v3/mapping endpoint.</summary>
    public const string ValidAaplMappingResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW",
                "shareClassFIGI": "BBG001S5N8V8",
                "securityDescription": "APPLE INC"
              }
            ]
          }
        ]
        """;

    /// <summary>Two AAPL mappings returned in a single response (e.g., different exchanges).</summary>
    public const string MultipleAaplMappingsResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW"
              },
              {
                "figi": "BBG000QNH748",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "US"
              }
            ]
          }
        ]
        """;

    /// <summary>Mapping response where the entry has an error (ticker not found).</summary>
    public const string TickerNotFoundResponse = """
        [
          {
            "error": "No identifier found."
          }
        ]
        """;

    /// <summary>Mapping response for ISIN US0378331005 (AAPL).</summary>
    public const string ValidIsinMappingResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW"
              }
            ]
          }
        ]
        """;

    /// <summary>Mapping response for CUSIP 037833100 (AAPL).</summary>
    public const string ValidCusipMappingResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW"
              }
            ]
          }
        ]
        """;

    /// <summary>Mapping response for SEDOL 2046251 (AAPL).</summary>
    public const string ValidSedolMappingResponse = """
        [
          {
            "data": [
              {
                "figi": "BBG000B9XRY4",
                "compositeFIGI": "BBG000B9Y5X2",
                "securityType": "Common Stock",
                "marketSector": "Equity",
                "ticker": "AAPL",
                "name": "APPLE INC",
                "exchCode": "UW"
              }
            ]
          }
        ]
        """;

    /// <summary>Filter endpoint response with two Apple matches.</summary>
    public const string ValidSearchResponse = """
        {
          "data": [
            {
              "figi": "BBG000B9XRY4",
              "compositeFIGI": "BBG000B9Y5X2",
              "securityType": "Common Stock",
              "marketSector": "Equity",
              "ticker": "AAPL",
              "name": "APPLE INC",
              "exchCode": "UW"
            },
            {
              "figi": "BBG000B9Y5X2",
              "compositeFIGI": "BBG000B9Y5X2",
              "securityType": "Common Stock",
              "marketSector": "Equity",
              "ticker": "AAPL",
              "name": "APPLE INC",
              "exchCode": "US"
            }
          ],
          "total": 2
        }
        """;
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_responder(request));
    }
}
