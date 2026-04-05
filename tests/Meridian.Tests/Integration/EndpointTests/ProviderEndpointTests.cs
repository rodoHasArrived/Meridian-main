using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for provider management API endpoints.
/// Tests provider catalog, status, metrics, comparison, and data source CRUD.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ProviderEndpointTests
{
    private readonly HttpClient _client;

    public ProviderEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/providers/catalog

    [Fact]
    public async Task GetCatalog_ReturnsJsonWithProviders()
    {
        var response = await _client.GetAsync("/api/providers/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
        json.Should().ContainKey("totalCount");
        json["totalCount"].GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetCatalog_FilterByStreaming_ReturnsSubset()
    {
        var response = await _client.GetAsync("/api/providers/catalog?type=streaming");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
    }

    [Fact]
    public async Task GetCatalog_FilterByBackfill_ReturnsSubset()
    {
        var response = await _client.GetAsync("/api/providers/catalog?type=backfill");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
    }

    #endregion

    #region GET /api/providers/catalog/{providerId}

    [Fact]
    public async Task GetCatalogById_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/providers/catalog/nonexistent-provider");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/providers/status

    [Fact]
    public async Task GetProviderStatus_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/providers/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region GET /api/providers/metrics

    [Fact]
    public async Task GetProviderMetrics_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/providers/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetProviderMetricsById_WithInvalidId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/providers/metrics/nonexistent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/providers/comparison

    [Fact]
    public async Task GetProviderComparison_ReturnsJsonWithExpectedShape()
    {
        var response = await _client.GetAsync("/api/providers/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
        json.Should().ContainKey("totalProviders");
        json.Should().ContainKey("healthyProviders");
    }

    #endregion

    #region Data Source CRUD

    [Fact]
    public async Task GetDataSources_ReturnsJsonWithSources()
    {
        var response = await _client.GetAsync("/api/config/datasources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("sources");
        json.Should().ContainKey("enableFailover");
    }

    [Fact]
    public async Task CreateDataSource_WithValidData_ReturnsOk()
    {
        var payload = new
        {
            Name = "Test IB Source",
            Provider = "IB",
            Enabled = true,
            Type = "RealTime",
            Priority = 20,
            Description = "Integration test provider"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasources", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("id");
    }

    [Fact]
    public async Task CreateDataSource_WithMissingName_ReturnsBadRequest()
    {
        var payload = new { Name = "", Provider = "IB", Enabled = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasources", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ToggleDataSource_WithNonexistentId_ReturnsNotFound()
    {
        var payload = new { Enabled = false };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/config/datasources/nonexistent/toggle", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RoutePreview_WithLegacyRealtimeDefaults_ReturnsExplainableSelection()
    {
        var payload = new
        {
            Capability = "RealtimeMarketData"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/provider-operations/route-preview", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json["isRoutable"].GetBoolean().Should().BeTrue();
        json["selectedConnectionId"].GetString().Should().Be("test-alpaca");
    }

    [Fact]
    public async Task ProviderConnectionsCrud_CreateAndFetchConnection()
    {
        var payload = new
        {
            ProviderFamilyId = "ib",
            DisplayName = "Ops Broker",
            ConnectionType = "Brokerage",
            ConnectionMode = "Paper",
            Enabled = true
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var createResponse = await _client.PostAsync("/api/provider-operations/connections", content);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await DeserializeAsync(createResponse);
        var connectionId = created["connectionId"].GetString();
        connectionId.Should().NotBeNullOrWhiteSpace();

        var listResponse = await _client.GetAsync("/api/provider-operations/connections");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Ops Broker");
        body.Should().Contain(connectionId);
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
