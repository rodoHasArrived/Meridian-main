using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterReconciliationSlaPolicyProviderTests
{
    [Theory]
    [InlineData(ReconciliationBreakSeverity.Critical, 8)]
    [InlineData(ReconciliationBreakSeverity.High, 24)]
    [InlineData(ReconciliationBreakSeverity.Medium, 40)]
    public void ResolvePolicy_QualityCases_UseSeverityBasedWindows(
        ReconciliationBreakSeverity severity, int expectedDueHours)
    {
        var provider = new SecurityMasterReconciliationSlaPolicyProvider();

        var policy = provider.ResolvePolicy(MakeQualityItem(severity, team: "Security Master"));

        policy.Should().NotBeNull();
        policy!.PolicyId.Should().Be("security-master-quality-sla");
        policy.DueBusinessHours.Should().Be(expectedDueHours);
    }

    [Fact]
    public void ResolvePolicy_QualityCaseOutsideSecurityMasterTeam_ReturnsNull()
    {
        var provider = new SecurityMasterReconciliationSlaPolicyProvider();

        var policy = provider.ResolvePolicy(MakeQualityItem(ReconciliationBreakSeverity.High, team: "Reconciliation"));

        policy.Should().BeNull();
    }

    [Fact]
    public void ResolvePolicy_ConflictCases_KeepConflictPolicyId()
    {
        var provider = new SecurityMasterReconciliationSlaPolicyProvider();
        var item = MakeQualityItem(ReconciliationBreakSeverity.High, team: "Security Master") with
        {
            RunId = "security-master-conflicts"
        };

        var policy = provider.ResolvePolicy(item);

        policy.Should().NotBeNull();
        policy!.PolicyId.Should().Be("security-master-conflict-sla");
    }

    private static ReconciliationBreakQueueItem MakeQualityItem(ReconciliationBreakSeverity severity, string team)
        => new(
            BreakId: "security-master:quality:ma001:test:record",
            RunId: "security-master-quality",
            StrategyName: "Security Master exception casework",
            Category: ReconciliationBreakCategory.ClassificationGap,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: "Data quality rule MA001 (Missing Maturity Date): test.",
            AssignedTo: "security-master-steward",
            DetectedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            Severity: severity,
            Team: team);
}
