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
