using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly EndpointTestFixture _fixture;

    public ProviderEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
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

    #region POST /api/providers/configure

    [Fact]
    public async Task ConfigureProvider_WithValidPolygonPayload_ReturnsSetupResult()
    {
        var payload = new
        {
            Kind = "polygon",
            DisplayName = $"Polygon Test {Guid.NewGuid():N}",
            ApiKey = "test-key",
            ApiSecret = (string?)null,
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming", "backfill", "reference" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/configure", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json["success"].GetBoolean().Should().BeTrue();
        json["providerId"].GetString().Should().StartWith("polygon");
        json["providerName"].GetString().Should().Be(payload.DisplayName);
        json["error"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ConfigureProvider_WithUnsupportedProvider_ReturnsBadRequestResult()
    {
        var payload = new
        {
            Kind = "databento",
            DisplayName = "Databento Trial",
            ApiKey = "test-key",
            ApiSecret = (string?)null,
            Endpoint = (string?)null,
            Capabilities = new[] { "backfill" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/providers/configure", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await DeserializeAsync(response);
        json["success"].GetBoolean().Should().BeFalse();
        json["providerName"].GetString().Should().Be("Databento Trial");
        json["error"].GetString().Should().Contain("not yet supported");
    }

    #endregion

    #region Data Source CRUD


    [Fact]
    public async Task GetDataSources_RedactsProviderCredentials()
    {
        var displayName = $"Alpaca Redaction {Guid.NewGuid():N}";
        var configurePayload = new
        {
            Kind = "alpaca",
            DisplayName = displayName,
            ApiKey = "alpaca-key",
            ApiSecret = "alpaca-secret",
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _client.PostAsync("/api/providers/configure", configureContent);
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/config/datasources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("sources");
        var sources = json["sources"];
        var source = sources.EnumerateArray().FirstOrDefault(s =>
            string.Equals(s.GetProperty("name").GetString(), displayName, StringComparison.Ordinal));

        source.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        source.GetProperty("alpaca").GetProperty("keyId").GetString().Should().BeEmpty();
        source.GetProperty("alpaca").GetProperty("secretKey").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task GetDataSources_RedactsPolygonApiKey()
    {
        var displayName = $"Polygon Redaction {Guid.NewGuid():N}";
        var configurePayload = new
        {
            Kind = "polygon",
            DisplayName = displayName,
            ApiKey = "poly-secret-key",
            ApiSecret = (string?)null,
            Endpoint = (string?)null,
            Capabilities = new[] { "backfill" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _client.PostAsync("/api/providers/configure", configureContent);
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/config/datasources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("sources");
        var sources = json["sources"];
        var source = sources.EnumerateArray().FirstOrDefault(s =>
            string.Equals(s.GetProperty("name").GetString(), displayName, StringComparison.Ordinal));

        source.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        source.GetProperty("polygon").GetProperty("apiKey").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetDataSources_AliasEndpoint_RedactsAlpacaCredentials()
    {
        var displayName = $"Alpaca Alias {Guid.NewGuid():N}";
        var configurePayload = new
        {
            Kind = "alpaca",
            DisplayName = displayName,
            ApiKey = "alias-key",
            ApiSecret = "alias-secret",
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _client.PostAsync("/api/providers/configure", configureContent);
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/config/data-sources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("sources");
        var sources = json["sources"];
        var source = sources.EnumerateArray().FirstOrDefault(s =>
            string.Equals(s.GetProperty("name").GetString(), displayName, StringComparison.Ordinal));

        source.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        source.GetProperty("alpaca").GetProperty("keyId").GetString().Should().BeEmpty();
        source.GetProperty("alpaca").GetProperty("secretKey").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateDataSource_WithRedactedCredentials_PreservesStoredSecrets()
    {
        // Arrange: create an Alpaca source with real credentials via the provider setup form
        var displayName = $"Alpaca Preserve {Guid.NewGuid():N}";
        var configurePayload = new
        {
            Kind = "alpaca",
            DisplayName = displayName,
            ApiKey = "preserve-key",
            ApiSecret = "preserve-secret",
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _client.PostAsync("/api/providers/configure", configureContent);
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var configureJson = await DeserializeAsync(configureResponse);
        var providerId = configureJson["providerId"].GetString()!;

        // Act: round-trip the redacted GET response back as an update, changing only priority
        var updatePayload = new
        {
            Id = providerId,
            Name = displayName,
            Provider = "Alpaca",
            Enabled = true,
            Type = "RealTime",
            Priority = 77,
            Alpaca = new { KeyId = "", SecretKey = "", Feed = "iex" }  // redacted sentinels
        };

        var updateContent = new StringContent(JsonSerializer.Serialize(updatePayload), Encoding.UTF8, "application/json");
        var updateResponse = await _client.PostAsync("/api/config/datasources", updateContent);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert: stored credentials were preserved despite the sentinel values in the request
        var store = _fixture.Services.GetRequiredService<Meridian.Ui.Shared.Services.ConfigStore>();
        var cfg = store.Load();
        var storedSource = cfg.DataSources?.Sources?.FirstOrDefault(s =>
            string.Equals(s.Id, providerId, StringComparison.OrdinalIgnoreCase));

        storedSource.Should().NotBeNull();
        storedSource!.Alpaca.Should().NotBeNull();
        storedSource.Alpaca!.KeyId.Should().Be("preserve-key");
        storedSource.Alpaca.SecretKey.Should().Be("preserve-secret");
        storedSource.Priority.Should().Be(77);
    }

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

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
