using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for historical data query API endpoints.
/// Tests query operations, symbol listing, and date range retrieval.
/// Implements Phase 1A.4 and Phase 9B.1 from the roadmap.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class HistoricalEndpointTests
{
    private readonly HttpClient _client;

    public HistoricalEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/historical - Query Historical Data

    [Fact]
    public async Task QueryHistoricalData_WithoutSymbol_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/historical");

        // ASP.NET Core returns 400 for missing required parameters
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task QueryHistoricalData_WithSymbol_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task QueryHistoricalData_WithSymbol_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Should have standard response structure
        doc.RootElement.TryGetProperty("success", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("symbol", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("totalRecords", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("records", out var records).Should().BeTrue();
        records.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task QueryHistoricalData_WithDateRange_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY&from=2024-01-01&to=2024-01-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Verify date range is reflected in response
        doc.RootElement.TryGetProperty("from", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("to", out _).Should().BeTrue();
    }

    [Fact]
    public async Task QueryHistoricalData_WithLimit_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY&limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task QueryHistoricalData_WithDataType_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY&dataType=trades");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        doc.RootElement.TryGetProperty("dataType", out _).Should().BeTrue();
    }

    #endregion

    #region GET /api/historical/symbols - List Available Symbols

    [Fact]
    public async Task GetAvailableSymbols_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical/symbols");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetAvailableSymbols_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/api/historical/symbols");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Should return object with symbols array and count
        doc.RootElement.TryGetProperty("symbols", out var symbols).Should().BeTrue();
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.TryGetProperty("count", out var count).Should().BeTrue();
        count.ValueKind.Should().Be(JsonValueKind.Number);
    }

    #endregion

    #region GET /api/historical/{symbol}/daterange - Get Date Range

    [Fact]
    public async Task GetSymbolDateRange_WithoutSymbol_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/historical//daterange");

        // Should return 400 or 404 depending on routing behavior
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSymbolDateRange_WithSymbol_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical/SPY/daterange");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetSymbolDateRange_WithSymbol_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/api/historical/SPY/daterange");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Should have date range structure
        doc.RootElement.TryGetProperty("symbol", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("hasData", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("fileCount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetSymbolDateRange_WithInvalidSymbol_ReturnsOkWithHasDataFalse()
    {
        var response = await _client.GetAsync("/api/historical/NONEXISTENT/daterange");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Should indicate no data available
        doc.RootElement.TryGetProperty("hasData", out var hasData).Should().BeTrue();
        hasData.GetBoolean().Should().BeFalse();
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task QueryHistoricalData_WithEmptySymbol_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/historical?symbol=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task QueryHistoricalData_WithInvalidDateFormat_ReturnsOkOrBadRequest()
    {
        // Invalid date format should be handled gracefully
        var response = await _client.GetAsync("/api/historical?symbol=SPY&from=invalid-date");

        // ASP.NET Core may return 400 for invalid date parsing
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    #endregion

    #region Performance and Limits

    [Fact]
    public async Task QueryHistoricalData_WithLargeLimit_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY&limit=1000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task QueryHistoricalData_WithSkipAndLimit_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/historical?symbol=SPY&skip=10&limit=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);

        // Verify pagination-related fields are present
        doc.RootElement.TryGetProperty("totalRecords", out _).Should().BeTrue();
    }

    #endregion
}
