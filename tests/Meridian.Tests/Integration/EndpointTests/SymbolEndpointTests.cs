using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for symbol management endpoints (/api/symbols/*).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class SymbolEndpointTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SymbolEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET Endpoints

    [Fact]
    public async Task Symbols_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/symbols");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var symbols = JsonSerializer.Deserialize<JsonElement>(content);
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task SymbolsMonitored_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/symbols/monitored");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SymbolsArchived_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/archived");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SymbolStatus_ForConfiguredSymbol_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/SPY/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SymbolStatistics_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/statistics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SymbolSearch_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/search?q=SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task SymbolTrades_ForConfiguredSymbol_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/SPY/trades");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SymbolDepth_ForConfiguredSymbol_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/SPY/depth");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region POST Endpoints

    [Fact]
    public async Task AddSymbol_WithValidData_ReturnsOk()
    {
        var payload = new { symbols = new[] { "MSFT" } };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/symbols/add", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddSymbol_WithEmptyList_ReturnsBadRequest()
    {
        var payload = new { symbols = Array.Empty<string>() };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/symbols/add", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateSymbol_ReturnsOk()
    {
        var payload = new { symbols = new[] { "SPY" } };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/symbols/validate", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BulkAdd_WithValidData_ReturnsOk()
    {
        var payload = new { symbols = new[] { "GOOG", "AMZN" } };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/symbols/bulk-add", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveSymbol_ForUnknownSymbol_Returns404()
    {
        var response = await _client.PostAsync("/api/symbols/DOESNOTEXIST999/remove", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
