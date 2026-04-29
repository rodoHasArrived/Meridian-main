using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.ProviderSdk;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class KernelObservabilityServiceTests
{
    [Fact]
    public void RecordResult_TracksCandidateReasonCoverage_AndDeterminismMismatches()
    {
        var observability = new KernelObservabilityService();
        var context = new ProviderRouteContext(
            ProviderCapabilityKind.HistoricalBars,
            Workspace: "diagnostics",
            Symbol: "SPY");

        RecordObservation(
            observability,
            context,
            BuildSuccessResult(
                context,
                "route-a",
                selectedReasonCodes: [],
                candidateReasonCodes: ["binding-explicit"]),
            score: 94);

        RecordObservation(
            observability,
            context,
            BuildSuccessResult(
                context,
                "route-b",
                selectedReasonCodes: [],
                candidateReasonCodes: []),
            score: 91);

        var snapshot = observability.GetSnapshot();
        snapshot.DeterminismChecksEnabled.Should().BeTrue();
        snapshot.ActiveAlertCount.Should().Be(0);
        snapshot.AlertCount.Should().Be(0);
        snapshot.Domains.Should().ContainSingle();

        var domain = snapshot.Domains[0];
        domain.Domain.Should().Be(nameof(ProviderCapabilityKind.HistoricalBars));
        domain.Evaluations.Should().Be(2);
        domain.ThroughputPerMinute.Should().Be(2);
        domain.ReasonCodeCoveragePercent.Should().BeApproximately(50.0, 0.001);
        domain.DeterminismMismatches.Should().Be(1);
        domain.Latency.P95Ms.Should().BeGreaterThanOrEqualTo(domain.Latency.P50Ms);
        domain.Latency.P99Ms.Should().BeGreaterThanOrEqualTo(domain.Latency.P95Ms);
    }

    [Fact]
    public void RecordResult_RaisesSingleCriticalJumpAlert_AndReportsDistributionShift()
    {
        var observability = new KernelObservabilityService();

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "governance",
                Symbol: $"baseline-{index}");
            RecordObservation(
                observability,
                context,
                BuildSuccessResult(context, "route-stable", selectedReasonCodes: ["healthy-route"]),
                score: 95);
        }

        for (var index = 0; index < 30; index++)
        {
            var context = new ProviderRouteContext(
                ProviderCapabilityKind.HistoricalBars,
                Workspace: "governance",
                Symbol: $"critical-{index}");
            RecordObservation(
                observability,
                context,
                BuildCriticalResult(context, "route-manual", reasonCodes: ["manual-review"]),
                score: 10);
        }

        var snapshot = observability.GetSnapshot();
        snapshot.AlertCount.Should().Be(1);
        snapshot.ActiveAlertCount.Should().Be(1);

        var domain = snapshot.Domains.Single();
        domain.CriticalJumpAlertCount.Should().Be(1);
        domain.CriticalJumpActive.Should().BeTrue();
        domain.CriticalRateShortWindow.Should().BeApproximately(1.0, 0.001);
        domain.CriticalRateLongWindow.Should().BeApproximately(0.5, 0.001);
        domain.CriticalRateShortWindowSamples.Should().Be(30);
        domain.CriticalRateLongWindowSamples.Should().Be(60);
        domain.ScoreDrift.Should().BeGreaterThan(0.4);
        domain.SeverityDrift.Should().BeGreaterThan(0.4);
        domain.CriticalJumpThresholds.MinimumSampleCount.Should().Be(20);
        domain.CriticalJumpThresholds.MinimumShortRate.Should().Be(0.25);
        domain.CriticalJumpThresholds.ZeroBaselineShortRate.Should().Be(0.35);
        domain.CriticalJumpThresholds.RelativeMultiplier.Should().Be(2.0);
        domain.CriticalJumpThresholds.AbsoluteIncrease.Should().Be(0.15);
    }

    private static void RecordObservation(
        KernelObservabilityService observability,
        ProviderRouteContext context,
        ProviderRouteResult result,
        double score)
    {
        var scope = observability.BeginExecution(context);
        var healthByConnection = result.SelectedDecision is null
            ? new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [result.SelectedDecision.ConnectionId] = new(
                    result.SelectedDecision.ConnectionId,
                    result.SelectedDecision.ProviderFamilyId,
                    result.SelectedDecision.IsHealthy,
                    result.SelectedDecision.IsHealthy ? "healthy" : "degraded",
                    score,
                    DateTimeOffset.UtcNow)
            };

        observability.RecordResult(context, result, healthByConnection, scope);
    }

    private static ProviderRouteResult BuildSuccessResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? selectedReasonCodes = null,
        IReadOnlyList<string>? candidateReasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "alpha",
            context.Capability,
            ProviderSafetyMode.HealthAwareFailover,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: selectedReasonCodes ?? [],
            FallbackConnectionIds: []);

        var candidates = candidateReasonCodes is { Count: > 0 }
            ? new[]
            {
                selected,
                selected with
                {
                    ConnectionId = $"{connectionId}-candidate",
                    ProviderFamilyId = "beta",
                    ReasonCodes = candidateReasonCodes
                }
            }
            : new[] { selected };

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: candidates,
            SkippedCandidates: []);
    }

    private static ProviderRouteResult BuildCriticalResult(
        ProviderRouteContext context,
        string connectionId,
        IReadOnlyList<string>? reasonCodes = null)
    {
        var selected = new ProviderRouteDecision(
            connectionId,
            "beta",
            context.Capability,
            ProviderSafetyMode.ManualApprovalRequired,
            ScopeRank: 0,
            Priority: 0,
            IsHealthy: true,
            ReasonCodes: reasonCodes ?? [],
            FallbackConnectionIds: []);

        return new ProviderRouteResult(
            context,
            selected,
            Candidates: [selected],
            SkippedCandidates: [],
            RequiresManualApproval: true);
    }
}
