using System.Net;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for the Data Catalog endpoints (/api/catalog/*).
/// Validates search, timeline, symbols list, and coverage summary endpoints.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class CatalogEndpointTests
{
    private readonly HttpClient _client;

    public CatalogEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region /api/catalog/search

    [Fact]
    public async Task CatalogSearch_NoParams_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_WithSymbol_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search?symbol=SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_WithNaturalLanguageQuery_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search?q=AAPL+trades+2025");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_WithTypeFilter_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search?symbol=SPY&type=trades");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_WithDateRange_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search?symbol=SPY&from=2025-01-01&to=2025-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_WithPagination_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/search?skip=0&take=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSearch_ResponseContainsTotalCount()
    {
        var response = await _client.GetAsync("/api/catalog/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("totalCount");
    }

    #endregion

    #region /api/catalog/symbols

    [Fact]
    public async Task CatalogSymbols_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/symbols");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogSymbols_ResponseContainsExpectedFields()
    {
        var response = await _client.GetAsync("/api/catalog/symbols");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("totalSymbols");
        body.Should().Contain("symbols");
        body.Should().Contain("generatedAt");
    }

    #endregion

    #region /api/catalog/timeline

    [Fact]
    public async Task CatalogTimeline_NoParams_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogTimeline_WithSymbolFilter_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/timeline?symbol=SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogTimeline_ResponseContainsTimelineField()
    {
        var response = await _client.GetAsync("/api/catalog/timeline");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("timeline");
        body.Should().Contain("generatedAt");
    }

    [Fact]
    public async Task CatalogTimeline_WithDateRange_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/timeline?from=2025-01-01&to=2025-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region /api/catalog/coverage

    [Fact]
    public async Task CatalogCoverage_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/catalog/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CatalogCoverage_ResponseContainsExpectedFields()
    {
        var response = await _client.GetAsync("/api/catalog/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("totalSymbols");
        body.Should().Contain("totalEvents");
        body.Should().Contain("totalBytes");
        body.Should().Contain("generatedAt");
    }

    #endregion
}
