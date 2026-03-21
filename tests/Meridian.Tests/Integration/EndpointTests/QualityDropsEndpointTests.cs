using System.Net;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for the /api/quality/drops endpoint.
/// Part of B1/#7 and D4 improvements.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class QualityDropsEndpointTests : EndpointIntegrationTestBase
{
    public QualityDropsEndpointTests(EndpointTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task QualityDropsEndpoint_ReturnsJson()
    {
        var response = await GetAsync("/api/quality/drops");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        Assert.Contains("application/json", contentType);
    }

    [Fact]
    public async Task QualityDropsEndpoint_ReturnsValidStructure()
    {
        var response = await GetAsync("/api/quality/drops");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Check for required fields
        Assert.True(root.TryGetProperty("totalDropped", out _), "Missing totalDropped field");
        Assert.True(root.TryGetProperty("dropsBySymbol", out _), "Missing dropsBySymbol field");
        Assert.True(root.TryGetProperty("timestamp", out _), "Missing timestamp field");
    }

    [Fact]
    public async Task QualityDropsBySymbol_ReturnsJson()
    {
        var response = await GetAsync("/api/quality/drops/AAPL");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        Assert.Contains("application/json", contentType);
    }

    [Fact]
    public async Task QualityDropsBySymbol_ReturnsValidStructure()
    {
        var response = await GetAsync("/api/quality/drops/SPY");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Check for required fields
        Assert.True(root.TryGetProperty("symbol", out _), "Missing symbol field");
        Assert.True(root.TryGetProperty("dropped", out _), "Missing dropped field");
        Assert.True(root.TryGetProperty("totalDropped", out _), "Missing totalDropped field");
        Assert.True(root.TryGetProperty("timestamp", out _), "Missing timestamp field");
    }

    [Fact]
    public async Task QualityDropsBySymbol_HandlesUppercaseSymbol()
    {
        var response = await GetAsync("/api/quality/drops/TSLA");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QualityDropsBySymbol_HandlesLowercaseSymbol()
    {
        var response = await GetAsync("/api/quality/drops/tsla");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QualityDropsBySymbol_HandlesSpecialCharacters()
    {
        // Test with symbol containing special characters (some brokers use formats like BRK.B)
        var response = await GetAsync("/api/quality/drops/BRK.B");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task QualityDropsBySymbol_HandlesEmptySymbol(string symbol)
    {
        // URL encoding will handle spaces, but endpoint should handle gracefully
        var encoded = Uri.EscapeDataString(symbol);
        var response = await GetAsync($"/api/quality/drops/{encoded}");
        // Should still return OK with 0 drops for invalid/empty symbol
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task QualityDropsEndpoint_AuditTrailNotConfigured_ReturnsValidResponse()
    {
        // Even without audit trail, endpoint should return valid response with message
        var response = await GetAsync("/api/quality/drops");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Should have totalDropped = 0 when no audit trail
        if (root.TryGetProperty("totalDropped", out var totalDropped))
        {
            Assert.True(totalDropped.GetInt64() >= 0);
        }
    }
}
