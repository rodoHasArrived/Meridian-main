using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Contracts.Operations;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for backfill API endpoints.
/// Tests provider listing, status, and backfill execution with validation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BackfillEndpointTests : IDisposable, IClassFixture<EndpointTestFixture>
{
    private readonly HttpClient _client;
    private readonly EndpointTestFixture _fixture;

    public BackfillEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreatePermittedClient(
            UserPermission.ViewHistoricalData,
            UserPermission.TriggerBackfill);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ReceiptReadsAndCloneReviewEnforceTenantScope()
    {
        const string tenantId = "endpoint-test-tenant";
        var coordinator = _fixture.Services.GetRequiredService<Meridian.Ui.Shared.Services.BackfillCoordinator>();
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        var receipt = new ProviderProvenanceReceipt(1, Guid.NewGuid(), "HistoricalBars", "Explicit provider",
            [new("stooq", null, "Public", "Default historical feed", "Public feed", "Historical bars")], [], false,
            new("historical-backfill/v1"), new(date, date, ["SPY"], "Daily"), now, now, "Historical bars",
            "The original execution state was unavailable.");
        var foreign = receipt with { ReceiptId = Guid.NewGuid() };
        var store = coordinator.GetReceiptStore();
        await store.WriteRetainedResultAsync(new(receipt, true, 1, ProviderProvenanceAccess.Public.ForRequestTenant(tenantId)));
        await store.WriteRetainedResultAsync(new(foreign, true, 1, ProviderProvenanceAccess.Public.ForRequestTenant("other-tenant")));

        var response = await _client.GetAsync($"/api/backfill/receipts/{receipt.ReceiptId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        content.Should().NotContain(tenantId).And.NotContain("runtimeRevisions");
        (await _client.GetAsync($"/api/backfill/receipts/{foreign.ReceiptId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/backfill/receipts/{foreign.ReceiptId}/clone")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var clone = await _client.GetAsync($"/api/backfill/receipts/{receipt.ReceiptId}/clone");
        clone.StatusCode.Should().Be(HttpStatusCode.OK);
        using var plan = JsonDocument.Parse(await clone.Content.ReadAsStringAsync());
        plan.RootElement.GetProperty("status").GetString().Should().Be("Unavailable");
    }

    #region GET /api/backfill/providers

    [Fact]
    public async Task GetProviders_ReturnsJsonArray()
    {
        var response = await _client.GetAsync("/api/backfill/providers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region GET /api/backfill/status

    [Fact]
    public async Task GetStatus_ReturnsNotFoundWhenNoBackfillRan()
    {
        var response = await _client.GetAsync("/api/backfill/status");

        // 404 is expected when no backfill has run yet
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/not-found",
            "Resource Not Found");
    }

    #endregion

    #region GET /api/backfill/progress

    [Fact]
    public async Task GetProgress_ReturnsJsonWhenNoActiveBackfill()
    {
        var response = await _client.GetAsync("/api/backfill/progress");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content)!;
        json.Should().ContainKey("lastRun");
        json["lastRun"].ValueKind.Should().Be(JsonValueKind.Null);
        json.Should().ContainKey("isActive");
        json["isActive"].GetBoolean().Should().BeFalse();
        json.Should().ContainKey("providerProgress");
        json["providerProgress"].GetProperty("symbols").ValueKind.Should().Be(JsonValueKind.Object);
        json["providerProgress"].GetProperty("recentProviderAttempts").ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region POST /api/backfill/run - Validation

    [Fact]
    public async Task RunBackfill_WithNoSymbols_ReturnsBadRequest()
    {
        var payload = new { Symbols = Array.Empty<string>(), Provider = "stooq" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/validation",
            "Validation Failed");
        problem.GetProperty("errors").GetProperty("symbols")[0].GetString()
            .Should().Contain("symbol");
    }

    [Fact]
    public async Task RunBackfill_WithTooManySymbols_ReturnsBadRequest()
    {
        var symbols = Enumerable.Range(1, 101).Select(i => $"SYM{i}").ToArray();
        var payload = new { Symbols = symbols, Provider = "stooq" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("100");
    }

    [Fact]
    public async Task RunBackfill_WithInvalidSymbolFormat_ReturnsBadRequest()
    {
        var payload = new { Symbols = new[] { "INVALID SYMBOL!!!" }, Provider = "stooq" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid symbol");
    }

    [Fact]
    public async Task RunBackfill_WithFromAfterTo_ReturnsBadRequest()
    {
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "stooq",
            From = "2024-12-31",
            To = "2024-01-01"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("date");
    }

    [Fact]
    public async Task RunBackfill_WithFutureToDate_ReturnsBadRequest()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)).ToString("yyyy-MM-dd");
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "stooq",
            To = futureDate
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("future");
    }

    [Fact]
    public async Task RunBackfill_WithVeryOldFromDate_ReturnsBadRequest()
    {
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "stooq",
            From = "1960-01-01"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("1970");
    }

    [Fact]
    public async Task RunBackfill_WithUnsupportedIntradayProvider_ReturnsBadRequest()
    {
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "stooq",
            Granularity = "1Min"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("does not support");
        body.Should().Contain("1 Minute");
    }

    [Fact]
    public async Task RunBackfill_WithIntradayTooManySymbols_ReturnsBadRequest()
    {
        var symbols = Enumerable.Range(1, 11).Select(i => $"SYM{i}").ToArray();
        var payload = new
        {
            Symbols = symbols,
            Provider = "yahoo",
            Granularity = "1Min",
            From = "2026-01-01",
            To = "2026-01-02"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Intraday backfill supports at most 10 symbols");
    }

    [Fact]
    public async Task RunBackfill_WithIntradayRangeExceedingLimit_ReturnsBadRequest()
    {
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "yahoo",
            Granularity = "1Min",
            From = "2026-01-01",
            To = "2026-02-15"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Intraday backfill date range cannot exceed 31 days");
    }

    #endregion

    #region POST /api/backfill/run/preview - Validation

    [Fact]
    public async Task PreviewBackfill_WithNoSymbols_ReturnsBadRequest()
    {
        var payload = new { Symbols = Array.Empty<string>(), Provider = "stooq" };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run/preview", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PreviewBackfill_WithIntradayRangeExceedingLimit_ReturnsBadRequest()
    {
        var payload = new
        {
            Symbols = new[] { "SPY" },
            Provider = "yahoo",
            Granularity = "5Min",
            From = "2026-01-01",
            To = "2026-02-15"
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/backfill/run/preview", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Intraday backfill date range cannot exceed 31 days");
    }

    #endregion

    #region Provider planning configuration

    [Fact]
    public async Task WorkstationBackfill_PreviewWithoutQuotaCanBeScheduledAndReadBack()
    {
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(2);
        var symbols = new[] { "QUOTATEST" };
        using var preview = await _client.PostAsJsonAsync("/api/backfill/run/preview", new { provider = "stooq", symbols });
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = await preview.Content.ReadFromJsonAsync<JsonElement>();
        plan.GetProperty("quota").GetProperty("requestsPerWindow").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("estimatedDurationSeconds").ValueKind.Should().Be(JsonValueKind.Null);

        var submissionId = Guid.NewGuid();
        using var submitted = await _client.PostAsJsonAsync("/api/backfill/run", new
        {
            provider = "stooq",
            symbols,
            executionPolicy = "scheduled",
            scheduledAt,
            submissionId
        });
        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var receipt = await submitted.Content.ReadFromJsonAsync<JsonElement>();
        receipt.GetProperty("executionId").GetString().Should().Be(submissionId.ToString("N"));
        receipt.GetProperty("status").GetString().Should().Be("Scheduled");

        var history = await _client.GetFromJsonAsync<JsonElement>("/api/backfill/executions");
        var row = history.GetProperty("executions").EnumerateArray()
            .Single(item => item.GetProperty("executionId").GetString() == submissionId.ToString("N"));
        row.GetProperty("status").GetString().Should().Be("Scheduled");
        row.GetProperty("requestedProvider").GetString().Should().Be("stooq");
        row.GetProperty("quotaReceipt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ProviderPlanning_AbsentAlphaVantageMapping_RemainsDisabled()
    {
        var options = BackfillEndpoints.GetProviderOptionsFromConfig(
            new BackfillProvidersConfig(),
            "alphavantage");

        options.Enabled.Should().BeFalse("an absent runtime configuration must not opt a credentialed provider in");
        options.Priority.Should().Be(new AlphaVantageConfig().Priority);
        options.RateLimitPerMinute.Should().Be(new AlphaVantageConfig().RateLimitPerMinute);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProviderPlanning_ExplicitAlphaVantageOptionsRemainAuthoritative(bool enabled)
    {
        var configured = new AlphaVantageConfig(Enabled: enabled, Priority: 4, RateLimitPerMinute: 2);
        var options = BackfillEndpoints.GetProviderOptionsFromConfig(
            new BackfillProvidersConfig(AlphaVantage: configured), "alphavantage");

        options.Enabled.Should().Be(enabled);
        options.Priority.Should().Be(configured.Priority);
        options.RateLimitPerMinute.Should().Be(configured.RateLimitPerMinute);
    }

    [Fact]
    public void ProviderPlanning_AlphaVantagePresetDoesNotOptAnAbsentRuntimeProviderIn()
    {
        var credentials = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var config = new AppConfig(Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
            Alpaca: new(Enabled: false), Yahoo: new(Enabled: false), Nasdaq: new(Enabled: false),
            Stooq: new(Enabled: false), Tiingo: new(Enabled: false), Polygon: new(Enabled: false),
            Finnhub: new(Enabled: false), TwelveData: new(Enabled: false))));
        var factory = new ProviderFactory(config, credentials.Object);

        var providers = factory.CreateBackfillProviders();

        providers.Should().BeEmpty();
        credentials.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProviderMetadata_WhenProviderConfigurationIsMissing_StillExposesPresets()
    {
        var response = await _client.GetAsync("/api/backfill/providers/metadata");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var preset = document.RootElement.EnumerateArray()
            .Single(provider => provider.GetProperty("providerId").GetString() == "alphavantage");
        preset.GetProperty("defaultRateLimitPerMinute").GetInt32()
            .Should().Be(new AlphaVantageConfig().RateLimitPerMinute);
    }

    [Fact]
    public async Task ProviderStatuses_WhenConfigurationIsMissingButRuntimeIsInitialized_ReturnsSafeDefaults()
    {
        (_fixture.Services.GetRequiredService<Meridian.Ui.Shared.Services.ConfigStore>().Load().Backfill?.Providers)
            .Should().BeNull();
        _fixture.Services.GetRequiredService<BackfillRoutingRuntime>().Current.Should().NotBeNull();

        using var response = await _client.GetAsync("/api/backfill/providers/statuses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var alphaVantage = document.RootElement.EnumerateArray()
            .Single(provider => provider.GetProperty("metadata").GetProperty("providerId").GetString() == "alphavantage");
        alphaVantage.GetProperty("options").GetProperty("enabled").GetBoolean().Should().BeFalse();
        alphaVantage.GetProperty("effectiveConfigSource").GetString().Should().Be("default");
    }

    [Fact]
    public async Task ProviderFallbackChain_WhenProviderConfigurationIsMissing_ReturnsServiceUnavailable()
    {
        var response = await _client.GetAsync("/api/backfill/providers/fallback-chain");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/service-unavailable",
            "Service Unavailable");
        problem.GetProperty("service").GetString()
            .Should().Be("backfill provider configuration");
    }

    [Fact]
    public async Task ProviderDryRun_WhenProviderConfigurationIsMissing_ReturnsServiceUnavailable()
    {
        var payload = new StringContent(
            JsonSerializer.Serialize(new { symbols = new[] { "SPY" } }),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync(
            "/api/backfill/providers/dry-run-plan",
            payload);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await AssertProblemDetailsAsync(
            response,
            "https://meridian.io/errors/service-unavailable",
            "Service Unavailable");
        problem.GetProperty("service").GetString()
            .Should().Be("backfill provider configuration");
    }

    #endregion

    #region Scheduled execution

    [Fact]
    public async Task RunScheduleNow_WhenScheduledRuntimeIsMissing_DoesNotCreateLedgerOnlyExecution()
    {
        var manager = _fixture.Services.GetRequiredService<BackfillScheduleManager>();
        var schedule = await manager.CreateScheduleAsync(new BackfillSchedule
        {
            Name = "manual-runtime-required",
            Symbols = { "SPY" }
        });

        try
        {
            var historyBefore = manager.ExecutionHistory.GetExecutionsForSchedule(schedule.ScheduleId).Count;

            var response = await _client.PostAsync(
                $"/api/backfill/schedules/{schedule.ScheduleId}/run",
                content: null);

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var problem = await AssertProblemDetailsAsync(
                response,
                "https://meridian.io/errors/service-unavailable",
                "Service Unavailable");
            problem.GetProperty("service").GetString()
                .Should().Be("scheduled backfill runtime");
            manager.ExecutionHistory.GetExecutionsForSchedule(schedule.ScheduleId)
                .Should().HaveCount(historyBefore,
                    "a missing execution runtime must not create a misleading execution ledger row");
        }
        finally
        {
            await manager.DeleteScheduleAsync(schedule.ScheduleId);
        }
    }

    #endregion

    #region Auto remediation observability

    [Fact]
    public async Task BackfillExecutions_IncludesAutoRemediationFields()
    {
        var history = _fixture.Services.GetRequiredService<BackfillExecutionHistory>();
        history.AddExecution(new BackfillExecutionLog
        {
            ExecutionId = "autoexec123",
            ScheduleId = "auto-gap-remediation",
            ScheduleName = "Auto Gap Remediation",
            Trigger = ExecutionTrigger.AutoRemediation,
            Status = ExecutionStatus.Completed,
            FromDate = new DateOnly(2026, 03, 20),
            ToDate = new DateOnly(2026, 03, 20),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            AutoRemediationTriggerReason = "gap:Significant:00:10:00",
            AutoRemediationAttemptCount = 2,
            AutoRemediationLastOutcome = "Completed",
            AutoRemediationSla = new BackfillRemediationSlaMetadata(
                BackfillRemediationSlaTier.SameBusinessDay,
                DateTimeOffset.UtcNow.AddHours(4),
                RequiresOwnerAssignment: true,
                DownstreamWorkflow: "accounting",
                ReasonCode: "CriticalWorkflow",
                Provider: "polygon",
                TriggerSource: AutoRemediationTriggerSource.QualityAlert)
        });

        var response = await _client.GetAsync("/api/backfill/executions?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var auto = doc.RootElement.GetProperty("autoRemediation");
        auto.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        auto.GetProperty("withReason").GetInt32().Should().BeGreaterThan(0);
        auto.GetProperty("defaultProvider").GetString().Should().NotBeNullOrWhiteSpace();

        var executions = doc.RootElement.GetProperty("executions");
        executions[0].GetProperty("executionId").GetString().Should().Be("autoexec123");
        executions[0].TryGetProperty("autoRemediationTriggerReason", out _).Should().BeTrue();
        executions[0].TryGetProperty("autoRemediationAttemptCount", out _).Should().BeTrue();
        executions[0].TryGetProperty("autoRemediationLastOutcome", out _).Should().BeTrue();
        var sla = executions[0].GetProperty("autoRemediationSla");
        sla.GetProperty("tier").GetString().Should().Be("SameBusinessDay");
        sla.GetProperty("status").GetString().Should().Be("Completed");
        sla.GetProperty("provider").GetString().Should().Be("polygon");
        sla.GetProperty("isCompatibilityDerived").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BackfillStatistics_IncludesAutoRemediationSummary()
    {
        var response = await _client.GetAsync("/api/backfill/statistics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("autoRemediation", out var auto).Should().BeTrue();
        auto.TryGetProperty("latestTriggerReason", out _).Should().BeTrue();
        auto.TryGetProperty("latestAttemptCount", out _).Should().BeTrue();
        auto.TryGetProperty("latestOutcome", out _).Should().BeTrue();
    }

    #endregion

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
}
