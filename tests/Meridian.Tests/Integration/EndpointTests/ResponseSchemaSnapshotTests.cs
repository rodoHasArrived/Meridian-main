using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Schema snapshot tests that lock the JSON structure of key API responses.
/// Part of Phase 0 — Baseline &amp; Safety Rails (refactor-map Step 0.1).
///
/// These tests verify that API responses contain the expected fields with the
/// correct value kinds. A deliberate change to any response schema will cause
/// the corresponding assertion to fail, providing an early warning before
/// downstream consumers break.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ResponseSchemaSnapshotTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ResponseSchemaSnapshotTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region /api/status

    [Fact]
    public async Task Status_Schema_ContainsRequiredFields()
    {
        var json = await GetJsonObjectAsync("/api/status");

        // Verify the core fields defined in StatusResponse
        json.Should().ContainKey("isConnected");
        json.Should().ContainKey("timestampUtc");
        json.Should().ContainKey("uptime");
        json.Should().ContainKey("metrics");
        json.Should().ContainKey("pipeline");
    }

    [Fact]
    public async Task Status_Schema_MetricsFieldsAreNumeric()
    {
        var json = await GetJsonObjectAsync("/api/status");

        // The status endpoint embeds a metrics snapshot; verify the key numeric fields
        // are present if metrics are included at this level.
        if (json.ContainsKey("published"))
        {
            json["published"].ValueKind.Should().Be(JsonValueKind.Number);
        }

        if (json.ContainsKey("dropped"))
        {
            json["dropped"].ValueKind.Should().Be(JsonValueKind.Number);
        }
    }

    #endregion

    #region /api/health

    [Fact]
    public async Task Health_Schema_ContainsStatusAndChecks()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        var json = await DeserializeObjectAsync(response);

        json.Should().ContainKey("status");
        json["status"].ValueKind.Should().Be(JsonValueKind.String);

        json.Should().ContainKey("checks");
        json["checks"].ValueKind.Should().BeOneOf(JsonValueKind.Array, JsonValueKind.Object);
    }

    [Fact]
    public async Task Health_Schema_StatusValueIsKnownString()
    {
        var response = await _client.GetAsync("/health");
        var json = await DeserializeObjectAsync(response);

        var status = json["status"].GetString();
        status.Should().BeOneOf("healthy", "degraded", "unhealthy",
            "The health status value should be one of the known status strings");
    }

    #endregion

    #region /api/config

    [Fact]
    public async Task Config_Schema_ContainsRequiredTopLevelFields()
    {
        var json = await GetJsonObjectAsync("/api/config");

        json.Should().ContainKey("dataRoot");
        json.Should().ContainKey("dataSource");
        json.Should().ContainKey("symbols");
        json.Should().ContainKey("storage");
    }

    [Fact]
    public async Task Config_Schema_DataRootIsString()
    {
        var json = await GetJsonObjectAsync("/api/config");

        json["dataRoot"].ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Config_Schema_DataSourceIsString()
    {
        var json = await GetJsonObjectAsync("/api/config");

        json["dataSource"].ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Config_Schema_SymbolsIsArray()
    {
        var json = await GetJsonObjectAsync("/api/config");

        json["symbols"].ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Config_Schema_SymbolEntryContainsRequiredFields()
    {
        var json = await GetJsonObjectAsync("/api/config");
        var symbols = json["symbols"];
        symbols.GetArrayLength().Should().BeGreaterThan(0, "test fixture should configure at least one symbol");

        var first = symbols[0];
        first.TryGetProperty("symbol", out _).Should().BeTrue("each symbol entry must have a 'symbol' field");
        first.TryGetProperty("subscribeTrades", out _).Should().BeTrue("each symbol entry must have 'subscribeTrades'");
        first.TryGetProperty("subscribeDepth", out _).Should().BeTrue("each symbol entry must have 'subscribeDepth'");
    }

    [Fact]
    public async Task Config_Schema_StorageIsObject()
    {
        var json = await GetJsonObjectAsync("/api/config");

        json["storage"].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Config_Schema_StorageContainsNamingConvention()
    {
        var json = await GetJsonObjectAsync("/api/config");
        var storage = json["storage"];

        storage.TryGetProperty("namingConvention", out var naming).Should().BeTrue(
            "storage section must include 'namingConvention'");
        naming.ValueKind.Should().Be(JsonValueKind.String);
    }

    #endregion

    #region /api/providers/catalog

    [Fact]
    public async Task ProviderCatalog_Schema_ContainsProvidersAndTotalCount()
    {
        var json = await GetJsonObjectAsync("/api/providers/catalog");

        json.Should().ContainKey("providers");
        json["providers"].ValueKind.Should().Be(JsonValueKind.Array);

        json.Should().ContainKey("totalCount");
        json["totalCount"].ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task ProviderCatalog_Schema_TotalCountMatchesArrayLength()
    {
        var json = await GetJsonObjectAsync("/api/providers/catalog");

        var totalCount = json["totalCount"].GetInt32();
        var arrayLength = json["providers"].GetArrayLength();
        totalCount.Should().Be(arrayLength);
    }

    [Fact]
    public async Task ProviderCatalog_Schema_EntryContainsRequiredFields()
    {
        var json = await GetJsonObjectAsync("/api/providers/catalog");
        var providers = json["providers"];

        if (providers.GetArrayLength() > 0)
        {
            var entry = providers[0];
            // Every catalog entry must have a providerId/displayName and id/name aliases for backward compatibility
            entry.TryGetProperty("providerId", out _).Should().BeTrue("catalog entry must have 'providerId'");
            entry.TryGetProperty("displayName", out _).Should().BeTrue("catalog entry must have 'displayName'");
            entry.TryGetProperty("id", out _).Should().BeTrue("catalog entry must expose 'id' alias for backward compatibility");
            entry.TryGetProperty("name", out _).Should().BeTrue("catalog entry must expose 'name' alias for backward compatibility");
        }
    }

    #endregion

    #region /api/providers/status

    [Fact]
    public async Task ProviderStatus_Schema_IsJsonArray()
    {
        var response = await _client.GetAsync("/api/providers/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region /api/providers/metrics

    [Fact]
    public async Task ProviderMetrics_Schema_IsJsonArray()
    {
        var response = await _client.GetAsync("/api/providers/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region /api/providers/comparison

    [Fact]
    public async Task ProviderComparison_Schema_ContainsExpectedFields()
    {
        var json = await GetJsonObjectAsync("/api/providers/comparison");

        json.Should().ContainKey("providers");
        json["providers"].ValueKind.Should().Be(JsonValueKind.Array);

        json.Should().ContainKey("totalProviders");
        json["totalProviders"].ValueKind.Should().Be(JsonValueKind.Number);

        json.Should().ContainKey("healthyProviders");
        json["healthyProviders"].ValueKind.Should().Be(JsonValueKind.Number);
    }

    #endregion

    #region /api/backfill/providers

    [Fact]
    public async Task BackfillProviders_Schema_IsJsonArray()
    {
        var response = await _client.GetAsync("/api/backfill/providers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region /api/backfill/status

    [Fact]
    public async Task BackfillStatus_Schema_Returns404WhenNoBackfillRan()
    {
        var response = await _client.GetAsync("/api/backfill/status");

        // 404 is the expected schema when no backfill has run
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region /api/backfill/progress

    [Fact]
    public async Task BackfillProgress_Schema_ContainsMessageField()
    {
        var json = await GetJsonObjectAsync("/api/backfill/progress");

        // When no backfill is active, the response should contain a message field
        json.Should().ContainKey("message");
        json["message"].ValueKind.Should().Be(JsonValueKind.String);
    }

    #endregion

    #region /api/config/datasources

    [Fact]
    public async Task DataSources_Schema_ContainsSourcesAndFailoverFields()
    {
        var json = await GetJsonObjectAsync("/api/config/datasources");

        json.Should().ContainKey("sources");
        json["sources"].ValueKind.Should().Be(JsonValueKind.Array);

        json.Should().ContainKey("enableFailover");
        json["enableFailover"].ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Fact]
    public async Task DataSources_Schema_SourceEntryContainsRequiredFields()
    {
        var json = await GetJsonObjectAsync("/api/config/datasources");
        var sources = json["sources"];

        if (sources.GetArrayLength() > 0)
        {
            var entry = sources[0];
            entry.TryGetProperty("id", out _).Should().BeTrue("data source entry must have 'id'");
            entry.TryGetProperty("name", out _).Should().BeTrue("data source entry must have 'name'");
            entry.TryGetProperty("provider", out _).Should().BeTrue("data source entry must have 'provider'");
            entry.TryGetProperty("enabled", out _).Should().BeTrue("data source entry must have 'enabled'");
        }
    }

    #endregion

    #region /api/failover/config

    [Fact]
    public async Task FailoverConfig_Schema_ContainsExpectedFields()
    {
        var json = await GetJsonObjectAsync("/api/failover/config");

        json.Should().ContainKey("enableFailover");
        json.Should().ContainKey("healthCheckIntervalSeconds");
        json.Should().ContainKey("autoRecover");
    }

    [Fact]
    public async Task FailoverConfig_Schema_FieldTypesCorrect()
    {
        var json = await GetJsonObjectAsync("/api/failover/config");

        json["enableFailover"].ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        json["healthCheckIntervalSeconds"].ValueKind.Should().Be(JsonValueKind.Number);
        json["autoRecover"].ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    #endregion

    #region /metrics (Prometheus)

    [Fact]
    public async Task Metrics_Schema_IsPlainTextPrometheusFormat()
    {
        var response = await _client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        var content = await response.Content.ReadAsStringAsync();
        // Prometheus format uses lines starting with '#' for HELP/TYPE and metric_name for values
        content.Should().NotBeEmpty();
    }

    #endregion

    #region /api/backpressure

    [Fact]
    public async Task Backpressure_Schema_IsJsonObject()
    {
        var response = await _client.GetAsync("/api/backpressure");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
    }

    #endregion

    #region Helpers

    private async Task<Dictionary<string, JsonElement>> GetJsonObjectAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {url} should return 200 OK");
        return await DeserializeObjectAsync(response);
    }

    private static async Task<Dictionary<string, JsonElement>> DeserializeObjectAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions)!;
    }

    #endregion
}
