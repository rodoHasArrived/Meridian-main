using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Failover;
using Microsoft.Extensions.DependencyInjection;
using UiConfigStore = Meridian.Ui.Shared.Services.ConfigStore;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for failover API endpoints.
/// Tests failover configuration, rules CRUD, health status, and force failover.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class FailoverEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly EndpointTestFixture _fixture;
    private readonly HttpClient _client;

    public FailoverEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreatePermittedClient(
            UserPermission.ViewDiagnostics,
            UserPermission.ManageProviders);
    }

    public void Dispose() => _client.Dispose();

    #region GET /api/failover/config

    [Fact]
    public async Task GetFailoverConfig_ReturnsJsonWithExpectedFields()
    {
        var response = await GetWithLiveRuntimeAsync("/api/failover/config");

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
        var response = await GetWithLiveRuntimeAsync("/api/failover/config");
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
        var response = await GetWithLiveRuntimeAsync("/api/failover/rules");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetFailoverRules_ContainsExpectedFields()
    {
        var response = await GetWithLiveRuntimeAsync("/api/failover/rules");
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

    [Theory]
    [InlineData("/api/failover/config")]
    [InlineData("/api/failover/rules")]
    public async Task FailoverStateRead_WhenRuntimeIsMissing_ReturnsServiceUnavailableProblem(
        string route)
    {
        var response = await _client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/service-unavailable",
            "Service Unavailable");
        problem.GetProperty("service").GetString()
            .Should().Be("streaming failover runtime");
    }

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
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/validation",
            "Validation Failed");
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
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/not-found",
            "Resource Not Found");
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
    public async Task GetFailoverHealth_WhenRuntimeIsMissing_ReturnsServiceUnavailableProblem()
    {
        var response = await _client.GetAsync("/api/failover/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/service-unavailable",
            "Service Unavailable");
        problem.GetProperty("service").GetString().Should().Be("streaming failover runtime");
    }

    [Fact]
    public async Task GetFailoverHealth_WhenScorerIsMissing_ReturnsServiceUnavailableInsteadOfZeroScore()
    {
        var registry = _fixture.Services.GetRequiredService<StreamingFailoverRegistry>();
        using var healthMonitor = new ConnectionHealthMonitor();
        using var service = CreateFailoverService(healthMonitor);
        registry.Service = service;

        try
        {
            var response = await _client.GetAsync("/api/failover/health");

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var problem = await AssertProblemDetailsAsync(
                response,
                "https://meridian.io/errors/service-unavailable",
                "Service Unavailable");
            problem.GetProperty("service").GetString()
                .Should().Be("provider degradation scorer");
        }
        finally
        {
            registry.Service = null;
        }
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
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/not-found",
            "Resource Not Found");
    }

    [Fact]
    public async Task ForceFailover_WithMissingTargetProvider_ReturnsBadRequest()
    {
        var payload = new { TargetProviderId = "" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/validation",
            "Validation Failed");
    }

    [Fact]
    public async Task ForceFailover_WithTargetOutsideRule_ReturnsValidationProblem()
    {
        var payload = new { TargetProviderId = "not-in-rule" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/validation",
            "Validation Failed");
        problem.GetProperty("errors").GetProperty("targetProviderId")[0].GetString()
            .Should().Contain("primary provider");
    }

    [Fact]
    public async Task ForceFailover_WithValidRuleButNoRuntime_ReturnsServiceUnavailableProblem()
    {
        var payload = new { TargetProviderId = "test-backup" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/service-unavailable",
            "Service Unavailable");
    }

    [Fact]
    public async Task ForceFailover_WithCoordinatorButNoLiveTransitionHandler_ReturnsServiceUnavailableProblem()
    {
        var registry = _fixture.Services.GetRequiredService<StreamingFailoverRegistry>();
        using var healthMonitor = new ConnectionHealthMonitor();
        using var service = CreateFailoverService(healthMonitor);
        registry.Service = service;

        try
        {
            var payload = new { TargetProviderId = "test-backup" };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            await AssertProblemDetailsAsync(
                response,
                "https://meridian.io/errors/service-unavailable",
                "Service Unavailable");
            service.GetActiveProviderId("test-rule-1").Should().Be("test-alpaca");
        }
        finally
        {
            registry.Service = null;
        }
    }

    [Fact]
    public async Task ForceFailover_WhenLiveRuntimeRejectsTransition_ReturnsConflictProblem()
    {
        var registry = _fixture.Services.GetRequiredService<StreamingFailoverRegistry>();
        using var healthMonitor = new ConnectionHealthMonitor();
        using var service = CreateFailoverService(healthMonitor);
        using var registration = service.RegisterTransitionHandler(
            "test-rule-1",
            static transition => transition.TryReject("Injected runtime hand-off failure."));
        registry.Service = service;

        try
        {
            var payload = new { TargetProviderId = "test-backup" };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _client.PostAsync("/api/failover/force/test-rule-1", content);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await AssertProblemDetailsAsync(
                response,
                "https://meridian.io/errors/conflict",
                "State Conflict");
            service.GetActiveProviderId("test-rule-1").Should().Be("test-alpaca");
        }
        finally
        {
            registry.Service = null;
        }
    }

    #endregion

    [Fact]
    public async Task ConfigStoreSave_PreCanceledRequest_DoesNotPersistConfiguration()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "meridian-ui-config-store-tests",
            Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(tempDirectory, "appsettings.json");

        try
        {
            var store = new UiConfigStore(configPath);
            var config = store.Load();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => store.SaveAsync(config, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(configPath).Should().BeFalse(
                "a canceled failover configuration write must not become durable");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static async Task<Dictionary<string, JsonElement>> DeserializeAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task<JsonElement> AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedType,
        string expectedTitle)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement.Clone();
        problem.GetProperty("type").GetString().Should().Be(expectedType);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
        problem.GetProperty("status").GetInt32().Should().Be((int)response.StatusCode);
        problem.GetProperty("instance").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("timestamp").GetDateTimeOffset().Should().BeCloseTo(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));
        return problem;
    }

    private async Task<HttpResponseMessage> GetWithLiveRuntimeAsync(string route)
    {
        var registry = _fixture.Services.GetRequiredService<StreamingFailoverRegistry>();
        using var healthMonitor = new ConnectionHealthMonitor();
        using var service = CreateFailoverService(healthMonitor);
        registry.Service = service;

        try
        {
            return await _client.GetAsync(route);
        }
        finally
        {
            registry.Service = null;
        }
    }

    private StreamingFailoverService CreateFailoverService(
        ConnectionHealthMonitor healthMonitor)
    {
        var configured = _fixture.Services
            .GetRequiredService<UiConfigStore>()
            .Load()
            .DataSources ?? new DataSourcesConfig();
        var rules = configured.FailoverRules ?? Array.Empty<FailoverRuleConfig>();
        var service = new StreamingFailoverService(healthMonitor);
        foreach (var providerId in rules
                     .SelectMany(rule => new[] { rule.PrimaryProviderId }.Concat(rule.BackupProviderIds))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            service.RegisterProvider(providerId);
        }

        service.Start(configured with
        {
            EnableFailover = true,
            HealthCheckIntervalSeconds = 3600,
            FailoverRules = rules
        });
        return service;
    }
}
