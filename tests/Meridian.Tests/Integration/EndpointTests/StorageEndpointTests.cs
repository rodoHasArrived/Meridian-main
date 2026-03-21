using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for storage endpoints (/api/storage/*),
/// storage quality endpoints (/api/storage/quality/*),
/// and symbol mapping endpoints (/api/symbol-mappings).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class StorageEndpointTests
{
    private readonly HttpClient _client;

    public StorageEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region Storage Endpoints

    [Fact]
    public async Task StorageProfiles_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/profiles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task StorageStats_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task StorageBreakdown_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/breakdown");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Storage Quality Endpoints

    [Fact]
    public async Task StorageQualitySummary_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/quality/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task StorageQualityScores_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/quality/scores");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task StorageQualityPerSymbol_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/storage/quality/symbol/SPY");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Symbol Mapping Endpoints

    [Fact]
    public async Task SymbolMappings_ReturnsJson()
    {
        var response = await _client.GetAsync("/api/symbols/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task AddSymbolMapping_ReturnsOk()
    {
        var payload = new
        {
            canonicalSymbol = "BRK.B"
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/symbols/mappings", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteSymbolMapping_ForUnknown_Returns404()
    {
        var response = await _client.DeleteAsync("/api/symbols/mappings/DOESNOTEXIST999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
