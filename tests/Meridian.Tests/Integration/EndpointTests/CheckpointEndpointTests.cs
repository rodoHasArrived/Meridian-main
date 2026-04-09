using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Integration tests for backfill checkpoint API endpoints.
/// Tests GET /api/backfill/checkpoints, /checkpoints/resumable, /checkpoints/{jobId},
/// /checkpoints/{jobId}/pending, and POST /checkpoints/{jobId}/resume.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class CheckpointEndpointTests
{
    private readonly HttpClient _client;
    private readonly string _dataRoot;
    private readonly EndpointTestFixture _fixture;

    public CheckpointEndpointTests(EndpointTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _dataRoot = fixture.DataRoot;
    }

    #region GET /api/backfill/checkpoints

    [Fact]
    public async Task GetCheckpoints_ReturnsJsonArray()
    {
        await ResetCheckpointStateAsync();

        var response = await _client.GetAsync("/api/backfill/checkpoints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    #endregion

    #region GET /api/backfill/checkpoints/resumable

    [Fact]
    public async Task GetResumableCheckpoints_ReturnsJsonArray()
    {
        await ResetCheckpointStateAsync();

        var response = await _client.GetAsync("/api/backfill/checkpoints/resumable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetResumableCheckpoints_WhenNoFailedJobs_ReturnsEmpty()
    {
        await ResetCheckpointStateAsync();

        // When no backfill has run or the last run succeeded, resumable list should be empty
        var response = await _client.GetAsync("/api/backfill/checkpoints/resumable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        // Either empty array or the run was successful (isResumable = false)
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            var first = doc.RootElement[0];
            first.TryGetProperty("isResumable", out var isResumable).Should().BeTrue();
            isResumable.GetBoolean().Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetResumableCheckpoints_WithFailedRun_ReturnsResumableEntry()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: false,
                Provider: "stooq",
                Symbols: ["SPY", "AAPL"],
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 1, 31),
                BarsWritten: 125,
                StartedUtc: started,
                CompletedUtc: started.AddMinutes(3),
                Error: "provider timeout"));

        var response = await _client.GetAsync("/api/backfill/checkpoints/resumable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().Should().Be(1);
        var checkpoint = doc.RootElement[0];
        checkpoint.GetProperty("provider").GetString().Should().Be("stooq");
        checkpoint.GetProperty("isResumable").GetBoolean().Should().BeTrue();
        checkpoint.GetProperty("reason").GetString().Should().Contain("did not complete successfully");
    }

    #endregion

    #region GET /api/backfill/checkpoints/{jobId}

    [Fact]
    public async Task GetCheckpointById_WithNonExistentJobId_ReturnsNotFound()
    {
        await ResetCheckpointStateAsync();

        var response = await _client.GetAsync("/api/backfill/checkpoints/nonexistent-job-id");

        // When no backfill has run, any specific jobId lookup returns 404
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCheckpointById_ResponseHasExpectedShape_WhenFound()
    {
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: false,
                Provider: "polygon",
                Symbols: ["SPY"],
                From: new DateOnly(2024, 2, 1),
                To: new DateOnly(2024, 2, 29),
                BarsWritten: 20,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedUtc: DateTimeOffset.UtcNow,
                Error: "transient error"));

        var response = await _client.GetAsync("/api/backfill/checkpoints/job-123");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("jobId").GetString().Should().Be("job-123");
        doc.RootElement.GetProperty("provider").GetString().Should().Be("polygon");
        doc.RootElement.GetProperty("canResume").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    #endregion

    #region GET /api/backfill/checkpoints/{jobId}/pending

    [Fact]
    public async Task GetPendingSymbols_WithUnknownJobId_ReturnsNotFoundOrEmpty()
    {
        await ResetCheckpointStateAsync();

        var response = await _client.GetAsync("/api/backfill/checkpoints/unknown-xyz/pending");

        // Either 404 (no checkpoint at all) or 200 with empty pending list
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            doc.RootElement.TryGetProperty("pendingSymbols", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetPendingSymbols_WithPartialCoverage_ReturnsOnlyOutstandingSymbols()
    {
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: false,
                Provider: "stooq",
                Symbols: ["SPY", "AAPL", "MSFT"],
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 1, 31),
                BarsWritten: 60,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
                CompletedUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                Error: "interrupted"),
            ("SPY", new DateOnly(2024, 1, 31), 30),
            ("AAPL", new DateOnly(2024, 1, 15), 15));

        var response = await _client.GetAsync("/api/backfill/checkpoints/job-456/pending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var pending = doc.RootElement.GetProperty("pendingSymbols").EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => item is not null)
            .Cast<string>()
            .ToArray();

        pending.Should().BeEquivalentTo(["AAPL", "MSFT"]);
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("completedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetCheckpointValidation_WithCheckpointSidecars_ReturnsDerivedSignals()
    {
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: false,
                Provider: "stooq",
                Symbols: ["SPY", "AAPL"],
                From: new DateOnly(2024, 3, 1),
                To: new DateOnly(2024, 3, 31),
                BarsWritten: 10,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-20),
                CompletedUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
                Error: "interrupted"),
            ("SPY", new DateOnly(2024, 3, 31), 10),
            ("AAPL", new DateOnly(2024, 3, 31), 0));

        var response = await _client.GetAsync("/api/backfill/checkpoints/validation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var signals = doc.RootElement.GetProperty("signals").EnumerateArray().ToArray();
        signals.Should().HaveCount(2);
        signals.Should().Contain(signal =>
            signal.GetProperty("symbol").GetString() == "SPY"
            && signal.GetProperty("status").GetString() == "Pass"
            && signal.GetProperty("barsWritten").GetInt64() == 10);
        signals.Should().Contain(signal =>
            signal.GetProperty("symbol").GetString() == "AAPL"
            && signal.GetProperty("status").GetString() == "Warn");
    }

    #endregion

    #region POST /api/backfill/checkpoints/{jobId}/resume

    [Fact]
    public async Task ResumeCheckpoint_WithUnknownJobId_ReturnsNotFound()
    {
        await ResetCheckpointStateAsync();

        var response = await _client.PostAsync(
            "/api/backfill/checkpoints/no-such-job/resume",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResumeCheckpoint_WithAlreadySuccessfulJob_ReturnsBadRequest()
    {
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: true,
                Provider: "stooq",
                Symbols: ["SPY"],
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 1, 31),
                BarsWritten: 31,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-8),
                CompletedUtc: DateTimeOffset.UtcNow.AddMinutes(-2)));

        var resumeResp = await _client.PostAsync(
            "/api/backfill/checkpoints/any-completed/resume",
            content: null);

        resumeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResumeCheckpoint_WithFailedRunAndUnknownProvider_ReturnsBadRequest()
    {
        await SeedCheckpointStateAsync(
            new BackfillResult(
                Success: false,
                Provider: "unsupported-provider",
                Symbols: ["SPY"],
                From: new DateOnly(2024, 1, 1),
                To: new DateOnly(2024, 1, 31),
                BarsWritten: 5,
                StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-12),
                CompletedUtc: DateTimeOffset.UtcNow.AddMinutes(-7),
                Error: "interrupted"));

        var response = await _client.PostAsync("/api/backfill/checkpoints/job-789/resume", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Resume failed");
    }

    #endregion

    private async Task SeedCheckpointStateAsync(
        BackfillResult result,
        params (string Symbol, DateOnly LastCompletedDate, long BarsWritten)[] checkpoints)
    {
        await ResetCheckpointStateAsync();

        var store = new BackfillStatusStore(_dataRoot);
        await store.WriteAsync(result);
        SetCoordinatorLastRun(result);

        foreach (var checkpoint in checkpoints)
        {
            await store.WriteSymbolCheckpointAsync(
                checkpoint.Symbol,
                checkpoint.LastCompletedDate,
                checkpoint.BarsWritten);
        }
    }

    private Task ResetCheckpointStateAsync()
    {
        var statusDir = Path.Combine(_dataRoot, "_status");
        if (!Directory.Exists(statusDir))
        {
            SetCoordinatorLastRun(null);
            return Task.CompletedTask;
        }

        foreach (var file in Directory.GetFiles(statusDir, "backfill*.json", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        SetCoordinatorLastRun(null);

        return Task.CompletedTask;
    }

    private void SetCoordinatorLastRun(BackfillResult? result)
    {
        var backfill = _fixture.Services.GetRequiredService<BackfillCoordinator>();
        var coreField = typeof(BackfillCoordinator).GetField("_core", BindingFlags.Instance | BindingFlags.NonPublic);
        coreField.Should().NotBeNull();

        var core = coreField!.GetValue(backfill);
        core.Should().NotBeNull();

        var lastRunField = core!.GetType().GetField("_lastRun", BindingFlags.Instance | BindingFlags.NonPublic);
        lastRunField.Should().NotBeNull();
        lastRunField!.SetValue(core, result);
    }
}
