using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for provider management API endpoints.
/// Tests provider catalog, status, metrics, comparison, and data source CRUD.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ProviderEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    private readonly HttpClient _providerReadClient;
    private readonly HttpClient _providerMutationClient;
    private readonly HttpClient _credentialMutationClient;

    public ProviderEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;

        // Deliberately holds neither ManageProviders nor AdminMaintenance: the provider reads
        // declare an any-of set, so a platform operator who can only look must still get through.
        _providerReadClient = fixture.CreatePermittedClient(UserPermission.ViewDiagnostics);
        _providerMutationClient = fixture.CreatePermittedClient(UserPermission.ManageProviders);
        _credentialMutationClient = fixture.CreatePermittedClient(
            UserPermission.ManageProviders,
            UserPermission.ManageCredentials);
    }

    public void Dispose()
    {
        _providerReadClient.Dispose();
        _providerMutationClient.Dispose();
        _credentialMutationClient.Dispose();
    }

    #region GET /api/providers/catalog

    [Fact]
    public async Task GetCatalog_ReturnsJsonWithProviders()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
        json.Should().ContainKey("totalCount");
        json.Should().ContainKey("registrationReport");
        json["totalCount"].GetInt32().Should().BeGreaterThan(0);
        var registrationReport = json["registrationReport"];
        registrationReport.ValueKind.Should().Be(JsonValueKind.Object);
        registrationReport.TryGetProperty("isHealthy", out _).Should().BeTrue();
        registrationReport.GetProperty("failures").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetCatalog_FilterByStreaming_ReturnsSubset()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/catalog?type=streaming");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
    }

    [Fact]
    public async Task GetCatalog_FilterByBackfill_ReturnsSubset()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/catalog?type=backfill");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
    }

    #endregion

    #region GET /api/providers/rate-limits

    [Fact]
    public async Task GetRateLimits_ReturnsTypedProviderSnapshots()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/rate-limits");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
        json.Should().ContainKey("timestamp");
        var providers = json["providers"];
        providers.ValueKind.Should().Be(JsonValueKind.Array);
        providers.GetArrayLength().Should().BeGreaterThan(0);
        var provider = providers[0];
        provider.TryGetProperty("provider", out _).Should().BeTrue();
        provider.TryGetProperty("name", out _).Should().BeTrue();
        provider.TryGetProperty("displayName", out _).Should().BeTrue();
        provider.TryGetProperty("stateAvailable", out _).Should().BeTrue();
        provider.TryGetProperty("resetAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetRateLimitHistory_StatesThatHistoryIsNotRetained()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/synthetic/rate-limit-history?hours=12");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json["provider"].GetString().Should().Be("synthetic");
        json["periodHours"].GetInt32().Should().Be(12);
        json["isAvailable"].GetBoolean().Should().BeFalse();
        json["history"].GetArrayLength().Should().Be(0);
        json["message"].GetString().Should().Contain("not retained");
    }

    #endregion

    #region GET /api/providers/catalog/{providerId}

    [Fact]
    public async Task GetCatalogById_WithInvalidId_ReturnsNotFound()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/catalog/nonexistent-provider");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/providers/status

    [Fact]
    public async Task GetProviderStatus_ReturnsJsonArray()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/status");

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
        var response = await _providerReadClient.GetAsync("/api/providers/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetProviderMetricsById_WithInvalidId_ReturnsNotFound()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/metrics/nonexistent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/providers/comparison

    [Fact]
    public async Task GetProviderComparison_ReturnsJsonWithExpectedShape()
    {
        var response = await _providerReadClient.GetAsync("/api/providers/comparison");

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

        var response = await _credentialMutationClient.PostAsync("/api/providers/configure", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json["success"].GetBoolean().Should().BeTrue();
        json["providerId"].GetString().Should().StartWith("polygon");
        json["providerName"].GetString().Should().Be(payload.DisplayName);
        json["error"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ConfigureProvider_WithYahooPayload_PersistsHistoricalDataSource()
    {
        var displayName = $"Yahoo Finance Test {Guid.NewGuid():N}";
        var payload = new
        {
            Kind = "yahoo",
            DisplayName = displayName,
            ApiKey = (string?)null,
            ApiSecret = (string?)null,
            Endpoint = (string?)null,
            Capabilities = new[] { "backfill" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _providerMutationClient.PostAsync("/api/providers/configure", content);
        var result = await DeserializeAsync(response);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "Yahoo is a credential-free historical provider supported by the setup registry: {0}",
            result["error"].GetString());
        result["success"].GetBoolean().Should().BeTrue();
        result["providerId"].GetString().Should().StartWith("yahoo");
        result["providerName"].GetString().Should().Be(displayName);
        result["error"].ValueKind.Should().Be(JsonValueKind.Null);

        var dataSourcesResponse = await _client.GetAsync("/api/config/datasources");
        dataSourcesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dataSources = await DeserializeAsync(dataSourcesResponse);
        var source = dataSources["sources"].EnumerateArray().FirstOrDefault(s =>
            string.Equals(s.GetProperty("name").GetString(), displayName, StringComparison.Ordinal));

        source.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        source.GetProperty("id").GetString().Should().StartWith("yahoo");
        source.GetProperty("type").GetString().Should().Be("Historical");
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

        var response = await _credentialMutationClient.PostAsync("/api/providers/configure", content);

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
        var configureResponse = await _credentialMutationClient.PostAsync("/api/providers/configure", configureContent);
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
            ApiKey = "polygon-secret-key",
            ApiSecret = (string?)null,
            Endpoint = (string?)null,
            Capabilities = new[] { "backfill", "reference" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _credentialMutationClient.PostAsync("/api/providers/configure", configureContent);
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/config/datasources");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("sources");
        var sources = json["sources"];
        var source = sources.EnumerateArray().FirstOrDefault(s =>
            string.Equals(s.GetProperty("name").GetString(), displayName, StringComparison.Ordinal));

        source.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var polygonElement = source.GetProperty("polygon");
        // apiKey should either be absent (WhenWritingNull) or present with a null value
        if (polygonElement.TryGetProperty("apiKey", out var apiKeyElement))
            apiKeyElement.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetDataSources_AliasEndpoint_RedactsProviderCredentials()
    {
        var displayName = $"Alpaca Alias Redaction {Guid.NewGuid():N}";
        var configurePayload = new
        {
            Kind = "alpaca",
            DisplayName = displayName,
            ApiKey = "alpaca-alias-key",
            ApiSecret = "alpaca-alias-secret",
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming" }
        };

        var configureContent = new StringContent(JsonSerializer.Serialize(configurePayload), Encoding.UTF8, "application/json");
        var configureResponse = await _credentialMutationClient.PostAsync("/api/providers/configure", configureContent);
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
    public async Task GetDataSources_DoesNotReturnProviderSecrets()
    {
        var payload = new
        {
            Kind = "alpaca",
            DisplayName = $"Alpaca Secret Test {Guid.NewGuid():N}",
            ApiKey = "sensitive-key",
            ApiSecret = "sensitive-secret",
            Endpoint = (string?)null,
            Capabilities = new[] { "streaming" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var setupResponse = await _credentialMutationClient.PostAsync("/api/providers/configure", content);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/config/datasources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        var sources = json["sources"];
        sources.ValueKind.Should().Be(JsonValueKind.Array);

        var configuredSource = sources.EnumerateArray()
            .FirstOrDefault(source => source.GetProperty("name").GetString() == payload.DisplayName);

        configuredSource.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var alpaca = configuredSource.GetProperty("alpaca");
        alpaca.GetProperty("keyId").GetString().Should().BeEmpty();
        alpaca.GetProperty("secretKey").GetString().Should().BeEmpty();
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

        var response = await _providerMutationClient.PostAsync("/api/config/datasources", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("id");
    }

    [Fact]
    public async Task CreateDataSource_WithMissingName_ReturnsBadRequest()
    {
        var payload = new { Name = "", Provider = "IB", Enabled = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _providerMutationClient.PostAsync("/api/config/datasources", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ToggleDataSource_WithNonexistentId_ReturnsNotFound()
    {
        var payload = new { Enabled = false };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _providerMutationClient.PostAsync("/api/config/datasources/nonexistent/toggle", content);

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
