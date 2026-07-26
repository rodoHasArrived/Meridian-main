using FluentAssertions;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportingDeploymentReadinessServiceTests
{
    [Theory]
    [InlineData("governance", "reporting_restatement_requests")]
    [InlineData("artifacts", "reporting_artifact_packages")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2")]
    [InlineData("runs", "reporting_run_snapshots")]
    [InlineData("scheduling", "reporting_schedule_snapshots")]
    [InlineData("delivery", "reporting_delivery_receipts")]
    [InlineData("migrations", "reporting_schema_migrations")]
    public void HasRequiredSchema_MissingComponentTable_ShouldFailClosed(
        string componentId,
        string missingTable)
    {
        var probe = CompleteProbe() with
        {
            MissingTables = [missingTable]
        };

        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("governance", "trg_reporting_governance_audit_immutable")]
    [InlineData("artifacts", "trg_reporting_artifact_audit_append_guard")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2_append")]
    [InlineData("delivery", "trg_reporting_delivery_receipts_immutable")]
    public void HasRequiredSchema_MissingImmutableControlTrigger_ShouldFailClosed(
        string componentId,
        string missingTrigger)
    {
        var probe = CompleteProbe() with
        {
            MissingTriggers = [missingTrigger]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("governance")]
    [InlineData("artifacts")]
    [InlineData("reconciliation-evidence")]
    [InlineData("runs")]
    [InlineData("scheduling")]
    [InlineData("delivery")]
    [InlineData("migrations")]
    public void HasRequiredSchema_CompleteAuthority_ShouldPassComponent(string componentId)
    {
        ReportingDeploymentReadinessService.HasRequiredSchema(CompleteProbe(), componentId)
            .Should().BeTrue();
    }

    private static ReportingDeploymentProbeResult CompleteProbe() => new(
        IsReachable: true,
        MissingTables: [],
        MissingTriggers: [],
        FailureCode: null);
}
