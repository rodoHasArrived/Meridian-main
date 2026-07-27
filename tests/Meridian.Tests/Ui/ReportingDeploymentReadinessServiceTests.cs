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
    [InlineData("runs", "reporting_run_create_claims.tenant_id")]
    [InlineData("runs", "reporting_run_create_claims.claimed_at_utc")]
    [InlineData("runs", "reporting_run_create_claims.lease_version")]
    [InlineData("scheduling", "reporting_schedule_snapshots.due_at_utc")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_owner")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_expires_at_utc")]
    [InlineData("scheduling", "reporting_schedule_snapshots.lease_version")]
    public void HasRequiredSchema_MissingOperationalAuthorityColumn_ShouldFailClosed(
        string componentId,
        string missingColumn)
    {
        var probe = CompleteProbe() with
        {
            MissingColumns = [missingColumn]
        };

        probe.IsComplete.Should().BeFalse();
        ReportingDeploymentReadinessService.HasRequiredSchema(probe, componentId)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("governance", "reporting_governed_runs(tenant_id,run_id)")]
    [InlineData("artifacts", "reporting_artifact_blobs(tenant_id,content_hash_sha256)")]
    [InlineData("reconciliation-evidence", "reporting_reconciliation_evidence_v2(tenant_id,receipt_key_sha256)")]
    [InlineData("runs", "reporting_run_snapshots(tenant_id,run_id_key)")]
    [InlineData("runs", "reporting_run_create_claims(tenant_id,run_id_key)")]
    [InlineData("scheduling", "reporting_schedule_snapshots(tenant_id,company_id,schedule_id_key)")]
    [InlineData("delivery", "reporting_delivery_jobs(idempotency_key)")]
    [InlineData("delivery", "reporting_delivery_jobs(access_grant_id) where access_grant_id IS NOT NULL")]
    [InlineData("migrations", "reporting_schema_migrations(filename)")]
    public void HasRequiredSchema_MissingUniqueAuthorityKey_ShouldFailClosed(
        string componentId,
        string missingUniqueKey)
    {
        var probe = CompleteProbe() with
        {
            MissingUniqueKeys = [missingUniqueKey]
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
