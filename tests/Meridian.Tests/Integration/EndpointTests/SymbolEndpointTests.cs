using Meridian.Identity.Auth;
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
public sealed class SymbolEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    private readonly EndpointTestFixture Fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SymbolEndpointTests(EndpointTestFixture fixture)
    {
        Fixture = fixture;
        // Symbol mutations edit the platform configuration's watchlist and require ModifyConfig
        // (W9-GOV-008); the read assertions in this class are unaffected by the header.
        // W9-GOV-008: the symbol reads now declare the family whose store answers them — live
        // quote state, subscription configuration, or the storage catalog — while the mutations
        // keep ModifyConfig. These tests assert payload shape across both, so the client carries
        // the permissions a symbol operator would actually hold rather than only the write one.
        _client = fixture.CreatePermittedClient(
            UserPermission.ModifyConfig,
            UserPermission.ViewConfig,
            UserPermission.ViewMarketData,
            UserPermission.ViewHistoricalData);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task SymbolStatus_ProjectsEachBlockByTheCallersOwnAuthority()
    {
        // One route, two families. The subscription configuration is what GetMonitoredSymbols serves
        // under ViewConfig; the storage block is what the storage-backed symbol reads serve under
        // ViewHistoricalData. Admitting either permission to the composite handed a caller both.
        using var marketDataOnly = Fixture.CreatePermittedClient(UserPermission.ViewMarketData);
        using var historicalOnly = Fixture.CreatePermittedClient(UserPermission.ViewHistoricalData);

        using var marketDataResponse = await marketDataOnly.GetAsync("/api/symbols/AAPL/status");
        marketDataResponse.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "every block this route serves is configuration or historical storage, so a market-data caller would receive an empty envelope");

        using var historicalResponse = await historicalOnly.GetAsync("/api/symbols/AAPL/status");
        historicalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var historicalPayload = JsonDocument.Parse(await historicalResponse.Content.ReadAsStringAsync());
        historicalPayload.RootElement.GetProperty("config").ValueKind.Should().Be(
            JsonValueKind.Null, "a historical reader has no claim on the subscription configuration");
        historicalPayload.RootElement.GetProperty("configured").ValueKind.Should().Be(
            JsonValueKind.Null,
            "the configured flag is the monitored-watchlist membership discriminator, enumerable one probe at a time");

        // A configuration reader sees the membership answer and the configuration itself.
        using var configOnly = Fixture.CreatePermittedClient(UserPermission.ViewConfig);
        using var configResponse = await configOnly.GetAsync("/api/symbols/AAPL/status");
        configResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var configPayload = JsonDocument.Parse(await configResponse.Content.ReadAsStringAsync());
        configPayload.RootElement.GetProperty("configured").ValueKind.Should().BeOneOf(
            JsonValueKind.True, JsonValueKind.False);
        configPayload.RootElement.GetProperty("storage").ValueKind.Should().Be(
            JsonValueKind.Null, "the storage block answers to ViewHistoricalData");

        // The permission-bearing client still sees what it is entitled to.
        using var fullResponse = await _client.GetAsync("/api/symbols/AAPL/status");
        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK);
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
        var payload = new { symbol = "MSFT" };
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
