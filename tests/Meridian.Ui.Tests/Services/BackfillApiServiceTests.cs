using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="BackfillApiService"/> — service construction,
/// API route constants, and related data model behavior.
/// </summary>
public sealed class BackfillApiServiceTests
{
    // ── Construction ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        var act = () => new BackfillApiService();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ShouldCreateDistinctInstances()
    {
        var a = new BackfillApiService();
        var b = new BackfillApiService();
        a.Should().NotBeSameAs(b);
    }

    // ── BackfillRequest Model ────────────────────────────────────────

    [Fact]
    public void BackfillRequest_ShouldHaveDefaults()
    {
        var request = new BackfillRequest();
        request.Provider.Should().BeNull();
        request.Symbols.Should().NotBeNull().And.BeEmpty();
        request.From.Should().BeNull();
        request.To.Should().BeNull();
        request.Granularity.Should().Be("Daily");
    }

    [Fact]
    public void BackfillRequest_ShouldAcceptValues()
    {
        var request = new BackfillRequest
        {
            Provider = "stooq",
            Symbols = new[] { "SPY", "AAPL" },
            From = "2024-01-01",
            To = "2024-12-31",
            Granularity = "Hourly"
        };

        request.Provider.Should().Be("stooq");
        request.Symbols.Should().HaveCount(2);
        request.From.Should().Be("2024-01-01");
        request.Granularity.Should().Be("Hourly");
    }

    // ── BackfillResultDto Model ──────────────────────────────────────

    [Fact]
    public void BackfillResultDto_ShouldHaveDefaults()
    {
        var result = new BackfillResultDto();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void BackfillResultDto_ShouldAcceptValues()
    {
        var result = new BackfillResultDto
        {
            Success = true,
            Error = null
        };

        result.Success.Should().BeTrue();
    }

    // ── BackfillProviderInfo Model ───────────────────────────────────

    [Fact]
    public void BackfillProviderInfo_ShouldHaveDefaults()
    {
        var info = new BackfillProviderInfo();
        info.Name.Should().BeNullOrEmpty();
    }

    // ── BackfillExecution Model ──────────────────────────────────────

    [Fact]
    public void BackfillExecution_ShouldHaveDefaults()
    {
        var execution = new BackfillExecution();
        execution.Id.Should().BeNullOrEmpty();
    }

    // ── BackfillPreset Model ─────────────────────────────────────────

    [Fact]
    public void BackfillPreset_ShouldHaveDefaults()
    {
        var preset = new BackfillPreset();
        preset.Name.Should().BeNullOrEmpty();
    }

    // ── BackfillStatistics Model ─────────────────────────────────────

    [Fact]
    public void BackfillStatistics_ShouldHaveDefaults()
    {
        var stats = new BackfillStatistics();
        stats.TotalExecutions.Should().Be(0);
    }

    // ── Route Constants ──────────────────────────────────────────────

    [Fact]
    public void UiApiRoutes_BackfillProviders_ShouldNotBeEmpty()
    {
        UiApiRoutes.BackfillProviders.Should().NotBeNullOrEmpty();
        UiApiRoutes.BackfillProviders.Should().StartWith("/api/");
    }

    [Fact]
    public void UiApiRoutes_BackfillStatus_ShouldNotBeEmpty()
    {
        UiApiRoutes.BackfillStatus.Should().NotBeNullOrEmpty();
        UiApiRoutes.BackfillStatus.Should().StartWith("/api/");
    }

    [Fact]
    public void UiApiRoutes_BackfillRun_ShouldNotBeEmpty()
    {
        UiApiRoutes.BackfillRun.Should().NotBeNullOrEmpty();
        UiApiRoutes.BackfillRun.Should().StartWith("/api/");
        UiApiRoutes.BackfillRunPreview.Should().Be($"{UiApiRoutes.BackfillRun}/preview");
        UiApiRoutes.BackfillProgress.Should().Be("/api/backfill/progress");
        UiApiRoutes.BackfillCheckpointsResumable.Should().Be($"{UiApiRoutes.BackfillCheckpoints}/resumable");
        UiApiRoutes.BackfillCheckpointsValidation.Should().Be($"{UiApiRoutes.BackfillCheckpoints}/validation");
        UiApiRoutes.BackfillCheckpointById.Should().Be($"{UiApiRoutes.BackfillCheckpoints}/{{jobId}}");
        UiApiRoutes.BackfillCheckpointPending.Should().Be($"{UiApiRoutes.BackfillCheckpoints}/{{jobId}}/pending");
        UiApiRoutes.BackfillCheckpointResume.Should().Be($"{UiApiRoutes.BackfillCheckpoints}/{{jobId}}/resume");
    }

    [Fact]
    public void UiApiRoutes_WithQuery_ShouldAppendQueryString()
    {
        var route = UiApiRoutes.WithQuery("/api/test", "limit=50");

        route.Should().Contain("?");
        route.Should().Contain("limit=50");
    }

    [Fact]
    public void UiApiRoutes_WithParam_ShouldEncodeSegmentsAndConstrainedPlaceholders()
    {
        UiApiRoutes.WithParam("/api/workstation/runs/{runId}/ledger", "runId", " run / 1 ")
            .Should()
            .Be("/api/workstation/runs/run%20%2F%201/ledger");

        UiApiRoutes.WithParam(UiApiRoutes.RunsEquityCurve, "runId", " run / 1 ")
            .Should()
            .Be("/api/workstation/runs/run%20%2F%201/equity-curve");

        UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationSecurityMasterById,
                "securityId",
                "11111111-1111-1111-1111-111111111111")
            .Should()
            .Be("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111");

        UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationSecurityMasterHistory,
                "securityId",
                "11111111-1111-1111-1111-111111111111")
            .Should()
            .Be("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/history");

        UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationSecurityMasterEconomicDefinition,
                "securityId",
                "11111111-1111-1111-1111-111111111111")
            .Should()
            .Be("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/economic-definition");

        Action blankSegment = () => UiApiRoutes.WithParam("/api/workstation/runs/{runId}", "runId", "   ");
        blankSegment.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UiApiRoutes_BackfillRoutes_ShouldBeDistinct()
    {
        var routes = new[]
        {
            UiApiRoutes.BackfillProviders,
            UiApiRoutes.BackfillStatus,
            UiApiRoutes.BackfillRun,
            UiApiRoutes.BackfillRunPreview,
            UiApiRoutes.BackfillProgress,
            UiApiRoutes.BackfillCheckpoints,
            UiApiRoutes.BackfillCheckpointsResumable,
            UiApiRoutes.BackfillCheckpointsValidation,
            UiApiRoutes.BackfillCheckpointById,
            UiApiRoutes.BackfillCheckpointPending,
            UiApiRoutes.BackfillCheckpointResume
        };

        routes.Distinct().Should().HaveCount(routes.Length);
    }
}
