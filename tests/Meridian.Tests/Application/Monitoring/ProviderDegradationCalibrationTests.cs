using FluentAssertions;
using Meridian.Application.Monitoring;

namespace Meridian.Tests.Application.Monitoring;

public sealed class ProviderDegradationCalibrationTests
{
    [Fact]
    public void Run_ComputesPrecisionRecallAndAlertVolumeComparison()
    {
        var dataset = BuildDataset();
        var baseline = ProviderDegradationKernelProfile.Default("baseline");
        var candidate = baseline with
        {
            KernelVersion = "candidate-v2",
            Config = baseline.Config with
            {
                ConnectionWeight = 0.50,
                LatencyWeight = 0.20,
                ErrorRateWeight = 0.20,
                ReconnectWeight = 0.10,
                DegradationThreshold = 0.55
            }
        };

        var runner = new ProviderDegradationCalibrationRunner();
        var snapshot = runner.Run(dataset, baseline, candidate, "test");

        snapshot.CandidateKernelVersion.Should().Be("candidate-v2");
        snapshot.BaselineMetrics.Should().HaveCount(4);
        snapshot.CandidateMetrics.Should().HaveCount(4);
        snapshot.Comparison.ExpectedAlertCountCandidate.Should().BeGreaterThan(0);
        snapshot.Comparison.ExpectedAlertVolumeChangePercent.Should().NotBe(double.NaN);
    }

    [Fact]
    public void Policy_Evaluate_BlocksStaleSnapshot()
    {
        var snapshot = BuildSnapshot(createdAt: DateTimeOffset.UtcNow.AddDays(-30));
        var policy = new ProviderKernelCalibrationPolicy(
            TimeSpan.FromDays(14),
            MinPrecision: 0.6,
            MinRecall: 0.6,
            RequiredSeverity: IncidentSeverity.Critical);

        var decision = policy.Evaluate(snapshot, "candidate-v2");

        decision.Passed.Should().BeFalse();
        decision.FreshnessPassed.Should().BeFalse();
        decision.BlockingReasons.Should().Contain(reason => reason.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GovernanceWorkflow_RequiresExplicitCalibrationPass()
    {
        var workflow = new KernelWeightGovernanceWorkflowService(
            new ProviderKernelCalibrationPolicy(TimeSpan.FromDays(14), 0.8, 0.8, IncidentSeverity.Critical));

        var weakSnapshot = BuildSnapshot(criticalPrecision: 0.62, criticalRecall: 0.59);
        var decision = workflow.EvaluatePromotion("candidate-v2", weakSnapshot, "ops");

        decision.Approved.Should().BeFalse();
        decision.CalibrationPass.Should().BeFalse();
        decision.BlockingReasons.Should().Contain(reason => reason.Contains("precision", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("recall", StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderIncidentCalibrationDataset BuildDataset()
    {
        return new ProviderIncidentCalibrationDataset(
            DatasetId: "dataset-2026-04",
            GeneratedAt: DateTimeOffset.UtcNow,
            Source: "historical-provider-incidents",
            Windows:
            [
                new("A", DateTimeOffset.UtcNow.AddHours(-10), DateTimeOffset.UtcNow.AddHours(-9), IncidentSeverity.None, 0.1, 0.1, 0.0, 0.0, 120, 0),
                new("A", DateTimeOffset.UtcNow.AddHours(-9), DateTimeOffset.UtcNow.AddHours(-8), IncidentSeverity.Moderate, 0.4, 0.3, 0.3, 0.1, 100, 2),
                new("A", DateTimeOffset.UtcNow.AddHours(-8), DateTimeOffset.UtcNow.AddHours(-7), IncidentSeverity.Critical, 1.0, 0.8, 0.9, 0.7, 40, 6),
                new("B", DateTimeOffset.UtcNow.AddHours(-7), DateTimeOffset.UtcNow.AddHours(-6), IncidentSeverity.Major, 0.7, 0.6, 0.5, 0.2, 70, 3),
                new("B", DateTimeOffset.UtcNow.AddHours(-6), DateTimeOffset.UtcNow.AddHours(-5), IncidentSeverity.Minor, 0.3, 0.2, 0.1, 0.1, 90, 1)
            ]);
    }

    private static ProviderKernelCalibrationSnapshot BuildSnapshot(
        DateTimeOffset? createdAt = null,
        double criticalPrecision = 0.9,
        double criticalRecall = 0.9)
    {
        return new ProviderKernelCalibrationSnapshot(
            SnapshotId: Guid.NewGuid().ToString("N"),
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
            DatasetId: "dataset-2026-04",
            BaselineKernelVersion: "baseline",
            CandidateKernelVersion: "candidate-v2",
            BaselineMetrics:
            [
                new(IncidentSeverity.Critical, 0.60, 0.70, 0.70, 10, 3, 2, 12)
            ],
            CandidateMetrics:
            [
                new(IncidentSeverity.Critical, 0.55, criticalPrecision, criticalRecall, 11, 2, 1, 13)
            ],
            Comparison: new CalibrationComparisonSummary(
                ExpectedAlertVolumeChangePercent: 8.3,
                ExpectedAlertCountBaseline: 12,
                ExpectedAlertCountCandidate: 13,
                DecisionSeverity: IncidentSeverity.Critical,
                CandidatePrecision: criticalPrecision,
                CandidateRecall: criticalRecall,
                BaselinePrecision: 0.70,
                BaselineRecall: 0.70),
            RunBy: "ops",
            CalibrationPass: criticalPrecision >= 0.70 && criticalRecall >= 0.70);
    }
}
