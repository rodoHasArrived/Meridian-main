using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for storage endpoints (/api/storage/*),
/// storage quality endpoints (/api/storage/quality/*),
/// and symbol mapping endpoints (/api/symbol-mappings).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class StorageEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _client;

    public StorageEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        // Storage and storage-quality reads describe the state of the stored market data and
        // require one of ViewHistoricalData / ViewDiagnostics / ManageStorage (W9-GOV-008).
        _client = fixture.CreatePermittedClient(UserPermission.ViewHistoricalData);
    }

    public void Dispose() => _client.Dispose();

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
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/symbols/mappings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task CanonicalSymbolRegistry_ReturnsAdditiveIdentityAndMigrationContract()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ViewConfig);
        var response = await client.GetAsync("/api/symbols/registry");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.TryGetProperty("registryVersion", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("resolutionMode", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("recentMismatches", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("migrations", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("symbols", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AddSymbolMapping_ReturnsOk()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        var payload = new
        {
            canonicalSymbol = "BRK.B"
        };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/symbols/mappings", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteSymbolMapping_ForUnknown_Returns404()
    {
        using var client = _fixture.CreatePermittedClient(UserPermission.ModifyConfig);
        var response = await client.DeleteAsync("/api/symbols/mappings/DOESNOTEXIST999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
