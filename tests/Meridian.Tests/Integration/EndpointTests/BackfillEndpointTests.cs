using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for backfill API endpoints.
/// Tests provider listing, status, and backfill execution with validation.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class BackfillEndpointTests
{
    private readonly HttpClient _client;
    private readonly EndpointTestFixture _fixture;

    public BackfillEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
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
        json.Should().ContainKey("message");
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
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("symbol");
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
            AutoRemediationLastOutcome = "Completed"
        });

        var response = await _client.GetAsync("/api/backfill/executions?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var auto = doc.RootElement.GetProperty("autoRemediation");
        auto.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        auto.GetProperty("withReason").GetInt32().Should().BeGreaterThan(0);

        var executions = doc.RootElement.GetProperty("executions");
        executions[0].TryGetProperty("autoRemediationTriggerReason", out _).Should().BeTrue();
        executions[0].TryGetProperty("autoRemediationAttemptCount", out _).Should().BeTrue();
        executions[0].TryGetProperty("autoRemediationLastOutcome", out _).Should().BeTrue();
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
}
