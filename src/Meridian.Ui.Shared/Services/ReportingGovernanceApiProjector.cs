using Meridian.Contracts.Reporting;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects domain governance records into version-stable workstation API contracts.</summary>
public static class ReportingGovernanceApiProjector
{
    public static GovernedReportingRunDto Project(GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new GovernedReportingRunDto(
            run.RunId,
            run.SeriesId,
            run.Revision,
            run.TemplateId,
            run.TemplateVersion,
            new ReportingGovernanceOperationalScopeDto(
                run.Scope.TenantId,
                run.Scope.OrganizationId,
                run.Scope.CompanyId,
                run.Scope.FundId,
                run.Scope.BookId,
                run.Scope.PeriodId),
            new ReportingGovernanceAccessScopeDto(
                run.Access.PolicyId,
                run.Access.PolicyVersion,
                run.Access.Mode.ToString(),
                run.Access.OwnerPrincipalId,
                Safe(run.Access.PrincipalIds),
                run.Access.PolicyHash),
            new ReportingGovernanceCertifiedSnapshotDto(
                run.Snapshot.SnapshotId,
                run.Snapshot.SnapshotHash,
                run.Snapshot.ReconciliationCheckpointId,
                run.Snapshot.CapturedAtUtc),
            Project(run.CreationAuthority),
            run.CreatedAtUtc,
            run.RestatementOfRunId,
            run.ExecutionState.ToString(),
            run.GovernanceState.ToString(),
            run.Version,
            run.Readiness is null ? null : Project(run.Readiness),
            run.Approval is null
                ? null
                : new ReportingGovernanceApprovalDto(
                    Project(run.Approval.Authority),
                    run.Approval.ApprovedAtUtc,
                    run.Approval.DecisionNote),
            run.Release is null ? null : Project(run.Release),
            Safe(run.AuditTrail).Select(Project).ToArray());
    }

    public static ReportingGovernanceRestatementDto Project(ReportingRestatementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ReportingGovernanceRestatementDto(
            request.RequestId,
            request.PredecessorRunId,
            request.SeriesId,
            request.PredecessorRevision,
            request.PredecessorVersion,
            request.Reason,
            Safe(request.ChangedLines)
                .Select(static line => new ReportingGovernanceChangedLineDto(
                    line.LineKey,
                    line.PreviousValue,
                    line.CurrentValue,
                    Safe(line.EvidenceIds)))
                .ToArray(),
            Project(request.RequestedBy),
            request.RequestedAtUtc,
            request.State.ToString(),
            request.Version,
            request.ApprovedBy is null ? null : Project(request.ApprovedBy),
            request.ApprovedAtUtc,
            request.DraftRunId,
            Safe(request.AuditTrail).Select(Project).ToArray());
    }

    public static ReportingGovernanceRestatementApprovalDto Project(
        ReportingRestatementApprovalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReportingGovernanceRestatementApprovalDto(
            Project(result.Request),
            Project(result.DraftRun));
    }

    private static ReportingGovernanceAuthorityDto Project(ReportingAuthorityScope authority) =>
        new(
            authority.ActorId,
            authority.TenantId,
            authority.OrganizationId,
            authority.CompanyId,
            Safe(authority.Permissions).Select(static permission => permission.ToString()).ToArray(),
            authority.Origin.ToString(),
            authority.CorrelationId,
            Safe(authority.PrincipalIds));

    private static ReportingGovernanceReadinessDto Project(ReportingReadinessReceipt readiness) =>
        new(
            readiness.ReceiptId,
            readiness.ReceiptHash,
            readiness.EvaluatedAtUtc,
            readiness.IsReady,
            Safe(readiness.Checks)
                .Select(static check => new ReportingGovernanceReadinessCheckDto(
                    check.CheckId,
                    check.Passed,
                    Safe(check.EvidenceIds),
                    check.FailureReason))
                .ToArray());

    private static ReportingGovernanceReleaseDto Project(ReportingReleaseReceipt release) =>
        new(
            Project(release.Authority),
            release.ReleasedAtUtc,
            release.ManifestId,
            release.ManifestHash,
            Safe(release.Artifacts)
                .Select(static artifact => new ReportingGovernanceArtifactDto(
                    artifact.ArtifactId,
                    artifact.ArtifactHash,
                    artifact.ByteLength))
                .ToArray(),
            Safe(release.EvidenceIds));

    private static ReportingGovernanceAuditEntryDto Project(ReportingGovernanceAuditEntry entry) =>
        new(
            entry.EventId,
            entry.AggregateKind.ToString(),
            entry.AggregateId,
            entry.AggregateVersion,
            entry.OccurredAtUtc,
            entry.Action.ToString(),
            Project(entry.Authority),
            entry.PermissionUsed.ToString(),
            entry.FromExecutionState?.ToString(),
            entry.ToExecutionState?.ToString(),
            entry.FromGovernanceState?.ToString(),
            entry.ToGovernanceState?.ToString(),
            entry.FromRestatementState?.ToString(),
            entry.ToRestatementState?.ToString(),
            entry.Note,
            entry.PreviousHash,
            entry.Hash);

    private static T[] Safe<T>(System.Collections.Immutable.ImmutableArray<T> values) =>
        values.IsDefault ? [] : values.ToArray();
}
