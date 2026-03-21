using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for negative-path and edge-case behaviour across
/// health, status, config, backfill, and provider endpoints.
/// Part of B2 (tranche 1): increase endpoint integration coverage with
/// negative-path behaviour verification.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class NegativePathEndpointTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NegativePathEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // ================================================================
    // Health / Status negative paths
    // ================================================================

    #region Health & Status Endpoints

    [Fact]
    public async Task NonExistentRoute_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthDetailed_ReturnsExpectedStatusCodes()
    {
        var response = await _client.GetAsync("/api/health/detailed");
        // Should return 200, 503, or 501 depending on service availability
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task HealthSummary_ReturnsJsonWithRequiredFields()
    {
        var response = await _client.GetAsync("/api/health/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("status");
        json.Should().ContainKey("timestamp");
        json.Should().ContainKey("storageHealthy");
        json.Should().ContainKey("pipelineActive");
    }

    [Fact]
    public async Task HealthProviders_ReturnsJsonWithProvidersArray()
    {
        var response = await _client.GetAsync("/api/health/providers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
    }

    [Fact]
    public async Task HealthProviderDiagnostics_NonExistentProvider_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/health/providers/nonexistent-provider-xyz");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthStorage_ReturnsJsonWithExpectedFields()
    {
        var response = await _client.GetAsync("/api/health/storage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("rootPath");
        json.Should().ContainKey("exists");
        json.Should().ContainKey("totalBytes");
        json.Should().ContainKey("fileCount");
    }

    [Fact]
    public async Task HealthEvents_ReturnsJsonWithMetricsFlag()
    {
        var response = await _client.GetAsync("/api/health/events");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("metricsAvailable");
        json.Should().ContainKey("timestamp");
    }

    [Fact]
    public async Task Status_ReturnsJsonWithMetricsBlock()
    {
        var response = await _client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("uptime");
        json.Should().ContainKey("metrics");
    }

    [Fact]
    public async Task Errors_WithZeroCount_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/errors?count=0");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Errors_WithNegativeCount_ReturnsOk()
    {
        // Negative count parameter is tolerated; handler clamps to reasonable value
        var response = await _client.GetAsync("/api/errors?count=-1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Errors_WithNonNumericCount_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/errors?count=abc");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Connections_ReturnsJsonWithExpectedShape()
    {
        var response = await _client.GetAsync("/api/connections");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task ProviderLatency_ReturnsJsonResponse()
    {
        var response = await _client.GetAsync("/api/providers/latency");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    // ================================================================
    // Configuration negative paths
    // ================================================================

    #region Config Negative Paths

    [Fact]
    public async Task UpdateDataSource_WithEmptyBody_ReturnsBadRequest()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/datasource", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateDataSource_WithNullValue_ReturnsBadRequest()
    {
        var payload = new { DataSource = (string?)null };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/datasource", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddSymbol_WithNullBody_ReturnsBadRequest()
    {
        var content = new StringContent("null", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/symbols", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddSymbol_WithWhitespaceSymbol_ReturnsBadRequest()
    {
        var payload = new { Symbol = "   ", SubscribeTrades = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/symbols", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteSymbol_NonExistentSymbol_ReturnsOk()
    {
        // Deleting a non-existent symbol is idempotent — returns OK
        var response = await _client.DeleteAsync("/api/config/symbols/NONEXISTENT_SYMBOL_ZZZ");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetConfig_ReturnsConsistentDataAfterModification()
    {
        // Add a symbol, retrieve config, verify presence
        var payload = new { Symbol = "TEST_NEG_PATH", SubscribeTrades = false, SubscribeDepth = false, DepthLevels = 5, SecurityType = "STK", Exchange = "SMART", Currency = "USD" };
        var addContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        await _client.PostAsync("/api/config/symbols", addContent);

        var configResponse = await _client.GetAsync("/api/config");
        configResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(configResponse);
        var symbols = json["symbols"].EnumerateArray()
            .Select(s => s.GetProperty("symbol").GetString())
            .ToList();
        symbols.Should().Contain("TEST_NEG_PATH");

        // Clean up
        await _client.DeleteAsync("/api/config/symbols/TEST_NEG_PATH");
    }

    [Fact]
    public async Task UpdateStorage_WithPathTraversal_ReturnsBadRequest()
    {
        var payload = new { DataRoot = "../../../etc", NamingConvention = "BySymbol", DatePartition = "Daily", IncludeProvider = false, Compress = false };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/storage", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    // ================================================================
    // Backfill negative paths
    // ================================================================

    #region Backfill Negative Paths

    [Fact]
    public async Task RunBackfill_WithEmptySymbols_ReturnsBadRequest()
    {
        var payload = new { Provider = "stooq", Symbols = Array.Empty<string>() };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/run", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunBackfill_WithInvalidSymbolFormat_ReturnsBadRequest()
    {
        var payload = new { Provider = "stooq", Symbols = new[] { "INVALID SYMBOL WITH SPACES!!!" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/run", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunBackfill_WithReversedDateRange_ReturnsBadRequest()
    {
        var payload = new { Provider = "stooq", Symbols = new[] { "SPY" }, From = "2024-12-31", To = "2024-01-01" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/run", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunBackfill_WithTooManySymbols_ReturnsBadRequest()
    {
        var symbols = Enumerable.Range(1, 101).Select(i => $"SYM{i}").ToArray();
        var payload = new { Provider = "stooq", Symbols = symbols };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/run", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PreviewBackfill_WithEmptySymbols_ReturnsBadRequest()
    {
        var payload = new { Provider = "stooq", Symbols = Array.Empty<string>() };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/run/preview", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BackfillStatus_WhenNoBackfillRun_ReturnsNotFound()
    {
        // No backfill has been run in test fixture — expect 404
        var response = await _client.GetAsync("/api/backfill/status");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BackfillProviders_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/backfill/providers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task BackfillProgress_WhenNoActiveBackfill_ReturnsJsonWithMessage()
    {
        var response = await _client.GetAsync("/api/backfill/progress");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("message");
    }

    [Fact]
    public async Task BackfillGapFill_WithEmptySymbols_ReturnsBadRequest()
    {
        var payload = new { Symbols = Array.Empty<string>() };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/backfill/gap-fill", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BackfillScheduleById_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/backfill/schedules/nonexistent-schedule-id-xyz");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteBackfillSchedule_NonExistent_ReturnsNotFoundOr503()
    {
        var response = await _client.DeleteAsync("/api/backfill/schedules/nonexistent-schedule-id-xyz");
        // 404 if schedule manager available but ID not found; 503 if manager unavailable
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable);
    }

    #endregion

    // ================================================================
    // Provider negative paths
    // ================================================================

    #region Provider Negative Paths

    [Fact]
    public async Task GetProviderMetricsById_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/providers/metrics/nonexistent-provider-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProviderCatalogById_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/providers/catalog/nonexistent-provider-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ToggleDataSource_NonExistent_ReturnsNotFound()
    {
        var payload = new { Enabled = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/data-sources/nonexistent-id/toggle", content);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpsertDataSource_WithEmptyName_ReturnsBadRequest()
    {
        var payload = new { Name = "", Provider = "Alpaca", Enabled = true, Type = "RealTime", Priority = 10 };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/config/data-sources", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SwitchProvider_WithEmptyName_ReturnsBadRequest()
    {
        var payload = new { ProviderName = "", SaveAsDefault = false };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/providers/switch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SwitchProvider_WithInvalidName_ReturnsBadRequest()
    {
        var payload = new { ProviderName = "NotARealProvider", SaveAsDefault = false };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/providers/switch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetProviderByName_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/providers/nonexistent-provider-xyz");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProviderComparison_ReturnsJsonWithProviders()
    {
        var response = await _client.GetAsync("/api/providers/comparison");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("providers");
        json.Should().ContainKey("totalProviders");
    }

    [Fact]
    public async Task GetProviderStatus_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/providers/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GetProviderCatalog_WithTypeFilter_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/providers/catalog?type=streaming");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProviderCatalog_WithInvalidTypeFilter_ReturnsOk()
    {
        // Invalid type filter falls through to default (all providers)
        var response = await _client.GetAsync("/api/providers/catalog?type=invalidtype");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    // ================================================================
    // Cross-cutting negative paths
    // ================================================================

    #region Cross-Cutting

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/status")]
    [InlineData("/api/errors")]
    [InlineData("/api/backpressure")]
    [InlineData("/api/connections")]
    [InlineData("/api/providers/latency")]
    [InlineData("/api/providers/status")]
    [InlineData("/api/providers/comparison")]
    [InlineData("/api/health/summary")]
    [InlineData("/api/health/providers")]
    [InlineData("/api/health/storage")]
    [InlineData("/api/health/events")]
    public async Task GetEndpoint_ReturnsJsonContentType(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Theory]
    [InlineData("/api/config")]
    [InlineData("/api/config/derivatives")]
    [InlineData("/api/config/data-sources")]
    public async Task ConfigGetEndpoints_ReturnJson(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task PostToGetOnlyEndpoint_ReturnsMethodNotAllowed()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/status", content);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content, JsonOptions)!;
    }
}
