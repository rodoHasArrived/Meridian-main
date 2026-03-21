using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for failover API endpoints.
/// Tests failover configuration, rules CRUD, health status, and force failover.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class FailoverEndpointTests
{
    private readonly HttpClient _client;

    public FailoverEndpointTests(EndpointTestFixture fixture)
    {
        _client = fixture.Client;
    }

    #region GET /api/failover/config

    [Fact]
    public async Task GetFailoverConfig_ReturnsJsonWithExpectedFields()
    {
        var response = await _client.GetAsync("/api/failover/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await DeserializeAsync(response);
        json.Should().ContainKey("enableFailover");
        json.Should().ContainKey("failoverTimeoutSeconds");
        json.Should().ContainKey("rules");
    }

    [Fact]
    public async Task GetFailoverConfig_IncludesConfiguredRules()
    {
        var response = await _client.GetAsync("/api/failover/config");
        var json = await DeserializeAsync(response);

        json.Should().ContainKey("rules");
        var rules = json["rules"];
        rules.ValueKind.Should().Be(JsonValueKind.Array);
        rules.GetArrayLength().Should().BeGreaterThan(0,
            "Test config includes a failover rule");
    }

    #endregion

    #region POST /api/failover/config

    [Fact]
    public async Task UpdateFailoverConfig_WithValidData_ReturnsOk()
    {
        var payload = new
        {
            EnableFailover = false,
            HealthCheckIntervalSeconds = 15,
            AutoRecover = false,
            FailoverTimeoutSeconds = 60
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/config", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region GET /api/failover/rules

    [Fact]
    public async Task GetFailoverRules_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/failover/rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetFailoverRules_ContainsExpectedFields()
    {
        var response = await _client.GetAsync("/api/failover/rules");
        var content = await response.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement[]>(content)!;

        if (rules.Length > 0)
        {
            var firstRule = rules[0];
            firstRule.TryGetProperty("id", out _).Should().BeTrue();
            firstRule.TryGetProperty("primaryProviderId", out _).Should().BeTrue();
            firstRule.TryGetProperty("backupProviderIds", out _).Should().BeTrue();
        }
    }

    #endregion

    #region POST /api/failover/rules

    [Fact]
    public async Task CreateFailoverRule_WithValidData_ReturnsOk()
    {
        var payload = new
        {
            PrimaryProviderId = "test-provider-1",
            BackupProviderIds = new[] { "test-provider-2" },
            FailoverThreshold = 5,
            RecoveryThreshold = 10,
            DataQualityThreshold = 80,
            MaxLatencyMs = 200
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/rules", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("id");
    }

    [Fact]
    public async Task CreateFailoverRule_WithMissingPrimaryProvider_ReturnsBadRequest()
    {
        var payload = new
        {
            PrimaryProviderId = "",
            BackupProviderIds = new[] { "backup-1" }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/rules", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFailoverRule_WithNoBackupProviders_ReturnsBadRequest()
    {
        var payload = new
        {
            PrimaryProviderId = "primary-1",
            BackupProviderIds = Array.Empty<string>()
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/rules", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region DELETE /api/failover/rules/{id}

    [Fact]
    public async Task DeleteFailoverRule_WithNonexistentId_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync("/api/failover/rules/nonexistent-rule-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFailoverRule_WithExistingRule_ReturnsOk()
    {
        // First create a rule
        var payload = new
        {
            PrimaryProviderId = "del-primary",
            BackupProviderIds = new[] { "del-backup" },
            FailoverThreshold = 3
        };
        var createResponse = await _client.PostAsync("/api/failover/rules",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        var createJson = await DeserializeAsync(createResponse);
        var ruleId = createJson["id"].GetString();

        // Delete it
        var deleteResponse = await _client.DeleteAsync($"/api/failover/rules/{ruleId}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region GET /api/failover/health

    [Fact]
    public async Task GetFailoverHealth_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/failover/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region POST /api/failover/force/{ruleId}

    [Fact]
    public async Task ForceFailover_WithNonexistentRule_ReturnsNotFound()
    {
        var payload = new { TargetProviderId = "some-provider" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/nonexistent-rule", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ForceFailover_WithMissingTargetProvider_ReturnsBadRequest()
    {
        var payload = new { TargetProviderId = "" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForceFailover_WithValidRule_ReturnsJsonWithStatus()
    {
        var payload = new { TargetProviderId = "test-backup" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await DeserializeAsync(response);
        json.Should().ContainKey("ruleId");
        json.Should().ContainKey("targetProviderId");
    }

    #endregion

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
