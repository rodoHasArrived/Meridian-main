using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for configuration API endpoints.
/// Tests GET/POST/DELETE operations on /api/config/*.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ConfigEndpointTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConfigEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/config

    [Fact]
    public async Task GetConfig_ReturnsJsonWithExpectedFields()
    {
        var response = await _client.GetAsync("/api/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("dataRoot");
        json.Should().ContainKey("dataSource");
        json.Should().ContainKey("symbols");
        json.Should().ContainKey("storage");
    }

    [Fact]
    public async Task GetConfig_ContainsConfiguredSymbols()
    {
        var response = await _client.GetAsync("/api/config");
        var json = await DeserializeAsync(response);

        json.Should().ContainKey("symbols");
        var symbols = json["symbols"];
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        symbols.GetArrayLength().Should().BeGreaterThanOrEqualTo(2,
            "Test config includes SPY and AAPL");
    }

    #endregion

    #region POST /api/config/symbols

    [Fact]
    public async Task AddSymbol_WithValidData_ReturnsOk()
    {
        var payload = new { Symbol = "MSFT", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 10, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddSymbol_WithEmptySymbol_ReturnsBadRequest()
    {
        var payload = new { Symbol = "", SubscribeTrades = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/symbols", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddSymbol_PersistsInConfig()
    {
        var payload = new { Symbol = "TSLA", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        await _client.PostAsync("/api/config/symbols", content);

        // Verify the symbol appears in config
        var configResponse = await _client.GetAsync("/api/config");
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();

        symbols.Should().Contain("TSLA");
    }

    #endregion

    #region DELETE /api/config/symbols/{symbol}

    [Fact]
    public async Task DeleteSymbol_ReturnsOk()
    {
        // First add a symbol to delete
        var addPayload = new { Symbol = "GOOG", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        await _client.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));

        var response = await _client.DeleteAsync("/api/config/symbols/GOOG");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteSymbol_RemovesFromConfig()
    {
        // Add a symbol
        var addPayload = new { Symbol = "NFLX", SubscribeTrades = true, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        await _client.PostAsync("/api/config/symbols",
            new StringContent(JsonSerializer.Serialize(addPayload), Encoding.UTF8, "application/json"));

        // Delete it
        await _client.DeleteAsync("/api/config/symbols/NFLX");

        // Verify it's gone
        var configResponse = await _client.GetAsync("/api/config");
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();

        symbols.Should().NotContain("NFLX");
    }

    #endregion

    #region GET /api/config/derivatives

    [Fact]
    public async Task GetDerivatives_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/config/derivatives");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region POST /api/config/datasource

    [Fact]
    public async Task UpdateDataSource_WithValidValue_ReturnsOk()
    {
        var payload = new { DataSource = "Alpaca" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateDataSource_WithInvalidValue_ReturnsBadRequest()
    {
        var payload = new { DataSource = "InvalidProvider" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasource", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions)!;
    }
}
