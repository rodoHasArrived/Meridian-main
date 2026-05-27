using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for live data endpoints (/api/data/*).
/// These endpoints expose real-time trade, quote, and order book data.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class LiveDataEndpointTests
{
    private readonly HttpClient _client;

    public LiveDataEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task DataHealth_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/data/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Theory]
    [InlineData("/api/data/trades/SPY")]
    [InlineData("/api/data/quotes/SPY")]
    [InlineData("/api/data/orderbook/SPY")]
    [InlineData("/api/data/l3-orderbook/SPY")]
    public async Task DataEndpoint_ForConfiguredSymbol_ReturnsJsonOrServiceUnavailable(string url)
    {
        var response = await _client.GetAsync(url);

        // These return 200 with data or 503 if no live provider is connected
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/api/data/bbo/SPY")]
    [InlineData("/api/data/orderflow/SPY")]
    public async Task DataEndpoint_ForOptionalData_ReturnsValidStatusCode(string url)
    {
        var response = await _client.GetAsync(url);

        // BBO and order flow return 200, 404, or 503 depending on data availability
        ((int)response.StatusCode).Should().BeOneOf(200, 404, 503);
    }

    [Fact]
    public async Task Trades_ReturnsJsonContentType_WhenOk()
    {
        var response = await _client.GetAsync("/api/data/trades/SPY");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        }
    }

    [Fact]
    public async Task QuotesSnapshot_ReturnsJsonOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/data/quotes-snapshot");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            doc.RootElement.TryGetProperty("count", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("quotes", out var quotes).Should().BeTrue();
            quotes.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task QuotesSnapshot_WithSymbolsFilter_ReturnsValidStatusCode()
    {
        var response = await _client.GetAsync("/api/data/quotes-snapshot?symbols=SPY,AAPL");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }
}
