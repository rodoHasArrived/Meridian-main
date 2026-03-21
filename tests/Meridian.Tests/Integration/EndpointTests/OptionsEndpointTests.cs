using System.Net;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for options/derivatives endpoints (/api/options/*).
/// These endpoints expose option chain, quote, greeks, and summary data.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class OptionsEndpointTests
{
    private readonly HttpClient _client;

    public OptionsEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Theory]
    [InlineData("/api/options/expirations/AAPL")]
    [InlineData("/api/options/expirations/SPY")]
    public async Task OptionsExpirations_ReturnsJsonOrServiceUnavailable(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        }
    }

    [Fact]
    public async Task OptionsStrikes_WithValidExpiration_ReturnsJsonOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/options/strikes/AAPL/2026-03-21");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task OptionsStrikes_WithInvalidExpiration_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/options/strikes/AAPL/not-a-date");

        // Should return 400 for invalid date, or 503 if service not available
        ((int)response.StatusCode).Should().BeOneOf(400, 503);
    }

    [Theory]
    [InlineData("/api/options/chains/AAPL")]
    [InlineData("/api/options/chains/SPY?expiration=2026-03-21")]
    public async Task OptionsChains_ReturnsValidStatusCode(string url)
    {
        var response = await _client.GetAsync(url);

        // 200 with data, 404 no data, or 503 service unavailable
        ((int)response.StatusCode).Should().BeOneOf(200, 404, 503);
    }

    [Fact]
    public async Task OptionsChains_WithInvalidExpiration_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/options/chains/AAPL?expiration=invalid");

        // Should return 400 for invalid date, or 503 if service not available
        ((int)response.StatusCode).Should().BeOneOf(400, 503);
    }

    [Fact]
    public async Task OptionsQuotesByUnderlying_ReturnsJsonOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/options/quotes/AAPL");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        }
    }

    [Fact]
    public async Task OptionsSummary_ReturnsJsonOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/options/summary");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        }
    }

    [Fact]
    public async Task OptionsTrackedUnderlyings_ReturnsJsonOrServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/options/underlyings");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        }
    }

    [Fact]
    public async Task OptionsRefresh_Post_ReturnsValidStatusCode()
    {
        var content = new StringContent(
            "{\"underlyingSymbol\":\"AAPL\",\"expiration\":\"2026-03-21\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/options/refresh", content);

        // 200 success, 400 invalid input, or 503 service unavailable
        ((int)response.StatusCode).Should().BeOneOf(200, 400, 503);
    }
}
