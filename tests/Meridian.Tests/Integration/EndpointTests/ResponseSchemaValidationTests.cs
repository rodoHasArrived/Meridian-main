using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Validates that core endpoint responses conform to their expected JSON schemas.
/// Ensures field presence, types, and structural contracts are not accidentally broken.
/// Part of B2 (tranche 1): endpoint integration coverage for health/status/config.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class ResponseSchemaValidationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ResponseSchemaValidationTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region /api/status schema

    [Fact]
    public async Task Status_MetricsBlock_ContainsExpectedFields()
    {
        var json = await GetJsonAsync("/api/status");

        json.Should().ContainKey("metrics");
        var metrics = json["metrics"];
        metrics.ValueKind.Should().Be(JsonValueKind.Object);

        // Verify core metrics fields present
        var metricsObj = metrics.EnumerateObject().Select(p => p.Name).ToHashSet();
        metricsObj.Should().Contain("published");
        metricsObj.Should().Contain("dropped");
        metricsObj.Should().Contain("eventsPerSecond");
        metricsObj.Should().Contain("dropRate");
    }

    [Fact]
    public async Task Status_PipelineBlock_ContainsExpectedFields()
    {
        var json = await GetJsonAsync("/api/status");

        json.Should().ContainKey("pipeline");
        var pipeline = json["pipeline"];
        pipeline.ValueKind.Should().Be(JsonValueKind.Object);

        var pipelineObj = pipeline.EnumerateObject().Select(p => p.Name).ToHashSet();
        pipelineObj.Should().Contain("publishedCount");
        pipelineObj.Should().Contain("droppedCount");
        pipelineObj.Should().Contain("queueUtilization");
    }

    [Fact]
    public async Task Status_MetricsValues_AreNumeric()
    {
        var json = await GetJsonAsync("/api/status");
        var metrics = json["metrics"];

        metrics.GetProperty("published").ValueKind.Should().Be(JsonValueKind.Number);
        metrics.GetProperty("dropped").ValueKind.Should().Be(JsonValueKind.Number);
        metrics.GetProperty("eventsPerSecond").ValueKind.Should().Be(JsonValueKind.Number);
    }

    #endregion

    #region /api/health schema

    [Fact]
    public async Task Health_Response_ContainsStatusField()
    {
        var response = await _client.GetAsync("/api/health");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);

        var json = await DeserializeResponseAsync(response);
        json.Should().ContainKey("status");
        json["status"].ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Health_Response_ContainsTimestamp()
    {
        var response = await _client.GetAsync("/api/health");
        var json = await DeserializeResponseAsync(response);
        json.Should().ContainKey("timestamp");
    }

    [Fact]
    public async Task Health_ChecksArray_HasExpectedShape()
    {
        var response = await _client.GetAsync("/api/health");
        var json = await DeserializeResponseAsync(response);
        json.Should().ContainKey("checks");

        var checks = json["checks"];
        // Checks should be an array (even if empty)
        checks.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region /api/health/summary schema

    [Fact]
    public async Task HealthSummary_Providers_HasExpectedSubfields()
    {
        var json = await GetJsonAsync("/api/health/summary");

        json.Should().ContainKey("providers");
        var providers = json["providers"];
        providers.ValueKind.Should().Be(JsonValueKind.Object);

        var providerFields = providers.EnumerateObject().Select(p => p.Name).ToHashSet();
        providerFields.Should().Contain("streaming");
        providerFields.Should().Contain("backfill");
        providerFields.Should().Contain("symbolSearch");
        providerFields.Should().Contain("totalEnabled");
    }

    [Fact]
    public async Task HealthSummary_ProviderCounts_AreNonNegative()
    {
        var json = await GetJsonAsync("/api/health/summary");
        var providers = json["providers"];

        providers.GetProperty("streaming").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        providers.GetProperty("backfill").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        providers.GetProperty("totalEnabled").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region /api/config schema

    [Fact]
    public async Task Config_SymbolEntries_HaveRequiredFields()
    {
        var json = await GetJsonAsync("/api/config");

        json.Should().ContainKey("symbols");
        var symbols = json["symbols"];
        symbols.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var symbol in symbols.EnumerateArray())
        {
            symbol.ValueKind.Should().Be(JsonValueKind.Object);
            var fields = symbol.EnumerateObject().Select(p => p.Name).ToHashSet();
            fields.Should().Contain("symbol");
            fields.Should().Contain("subscribeTrades");
        }
    }

    [Fact]
    public async Task Config_StorageBlock_HasExpectedFields()
    {
        var json = await GetJsonAsync("/api/config");

        json.Should().ContainKey("storage");
        var storage = json["storage"];
        storage.ValueKind.Should().Be(JsonValueKind.Object);

        var fields = storage.EnumerateObject().Select(p => p.Name).ToHashSet();
        fields.Should().Contain("namingConvention");
        fields.Should().Contain("datePartition");
    }

    [Fact]
    public async Task Config_DataSource_IsValidString()
    {
        var json = await GetJsonAsync("/api/config");

        json.Should().ContainKey("dataSource");
        json["dataSource"].ValueKind.Should().Be(JsonValueKind.String);
        json["dataSource"].GetString().Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region /api/config/data-sources schema

    [Fact]
    public async Task DataSources_Response_ContainsSourcesArray()
    {
        var json = await GetJsonAsync("/api/config/data-sources");

        json.Should().ContainKey("sources");
        json["sources"].ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task DataSources_Response_ContainsFailoverSettings()
    {
        var json = await GetJsonAsync("/api/config/data-sources");

        json.Should().ContainKey("enableFailover");
        json["enableFailover"].ValueKind.Should().Match(k => k == JsonValueKind.True || k == JsonValueKind.False);
        json.Should().ContainKey("failoverTimeoutSeconds");
        json["failoverTimeoutSeconds"].ValueKind.Should().Be(JsonValueKind.Number);
    }

    #endregion

    #region /api/providers/comparison schema

    [Fact]
    public async Task ProviderComparison_Response_ContainsProviderCounts()
    {
        var json = await GetJsonAsync("/api/providers/comparison");

        json.Should().ContainKey("providers");
        json.Should().ContainKey("totalProviders");
        json.Should().ContainKey("healthyProviders");
        json["totalProviders"].ValueKind.Should().Be(JsonValueKind.Number);
    }

    #endregion

    #region /api/backpressure schema

    [Fact]
    public async Task Backpressure_Response_ContainsExpectedFields()
    {
        var json = await GetJsonAsync("/api/backpressure");

        // Backpressure response should have key fields
        var fields = json.Keys.ToHashSet();
        fields.Should().Contain("isActive");
        fields.Should().Contain("queueUtilization");
    }

    #endregion

    private async Task<Dictionary<string, JsonElement>> GetJsonAsync(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await DeserializeResponseAsync(response);
    }

    private static async Task<Dictionary<string, JsonElement>> DeserializeResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions)!;
    }
}
