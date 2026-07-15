using System.Collections.Immutable;
using Meridian.Reporting;

namespace Meridian.Storage.Reporting;

// These DTOs freeze the exact committed reporting-governance v1 JSON shape. They are storage-only:
// no v1 access principal or incomplete certification receipt is converted into a current domain
// claim because the missing principal kinds and source hashes cannot be inferred safely.
internal sealed record ReportingAccessScopeV1(
    string PolicyId,
    string PolicyVersion,
    ReportingGovernanceAccessMode Mode,
    string? OwnerPrincipalId,
    ImmutableArray<string> PrincipalIds,
    string PolicyHash);

internal sealed record ReportingCertifiedSnapshotScopeV1(
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string? FundId,
    string BookId,
    string PeriodId,
    string SnapshotId,
    string SnapshotHash,
    string ReconciliationCheckpointId,
    DateTimeOffset CapturedAtUtc);

internal sealed record ReportingReadinessReceiptV1(
    string ReceiptId,
    string ReceiptHash,
    string RunId,
    string TenantId,
    string SnapshotId,
    string SnapshotHash,
    DateTimeOffset EvaluatedAtUtc,
    ImmutableArray<ReportingReadinessCheck> Checks);

internal sealed record GovernedReportingRunV1(
    string RunId,
    string SeriesId,
    int Revision,
    string TemplateId,
    string TemplateVersion,
    ReportingOperationalScope Scope,
    ReportingAccessScopeV1 Access,
    ReportingCertifiedSnapshotScopeV1 Snapshot,
    ReportingAuthorityScope CreationAuthority,
    DateTimeOffset CreatedAtUtc,
    string? RestatementOfRunId,
    GovernedReportingExecutionState ExecutionState,
    GovernedReportingState GovernanceState,
    long Version,
    ReportingReadinessReceiptV1? Readiness,
    ReportingApprovalReceipt? Approval,
    ReportingReleaseReceipt? Release,
    ImmutableArray<ReportingGovernanceAuditEntry> AuditTrail);

internal sealed record ReportingRestatementRequestV1(
    string RequestId,
    string PredecessorRunId,
    string SeriesId,
    int PredecessorRevision,
    long PredecessorVersion,
    string Reason,
    ImmutableArray<ReportingRestatementChangedLine> ChangedLines,
    ReportingAuthorityScope RequestedBy,
    DateTimeOffset RequestedAtUtc,
    ReportingRestatementRequestState State,
    long Version,
    ReportingAuthorityScope? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? DraftRunId,
    ImmutableArray<ReportingGovernanceAuditEntry> AuditTrail);
