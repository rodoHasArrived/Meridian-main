namespace Meridian.Contracts.Reporting;

/// <summary>
/// Optimistic concurrency token for a governed reporting transition. Actor, tenant, company,
/// permissions, command origin, readiness, artifact hashes, and evidence are intentionally absent;
/// the server resolves those values from the authenticated request and retained state.
/// </summary>
public sealed record ReportingGovernanceVersionRequestDto(long ExpectedVersion);

/// <summary>Human approval rationale plus the required optimistic concurrency token.</summary>
public sealed record ReportingGovernanceApprovalRequestDto(
    long ExpectedVersion,
    string DecisionNote);

/// <summary>
/// Human rationale for opening a governed restatement. Changed-line evidence and replacement
/// certification are computed from server-owned records rather than accepted from the caller.
/// </summary>
public sealed record ReportingGovernanceRestatementRequestDto(
    long ExpectedVersion,
    string Reason);

/// <summary>
/// Optimistic concurrency token for approval of a retained restatement request. The replacement
/// certified snapshot is resolved and verified by the server.
/// </summary>
public sealed record ReportingGovernanceRestatementApprovalRequestDto(long ExpectedVersion);

public sealed record ReportingGovernanceOperationalScopeDto(
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string? FundId,
    string BookId,
    string PeriodId);

public sealed record ReportingGovernanceAccessScopeDto(
    string PolicyId,
    string PolicyVersion,
    string Mode,
    string? OwnerPrincipalId,
    IReadOnlyList<string> PrincipalIds,
    string PolicyHash);

public sealed record ReportingGovernanceCertifiedSnapshotDto(
    string SnapshotId,
    string SnapshotHash,
    string ReconciliationCheckpointId,
    DateTimeOffset CapturedAtUtc);

public sealed record ReportingGovernanceAuthorityDto(
    string ActorId,
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    IReadOnlyList<string> Permissions,
    string Origin,
    string CorrelationId,
    IReadOnlyList<string> PrincipalIds);

public sealed record ReportingGovernanceReadinessCheckDto(
    string CheckId,
    bool Passed,
    IReadOnlyList<string> EvidenceIds,
    string? FailureReason);

public sealed record ReportingGovernanceReadinessDto(
    string ReceiptId,
    string ReceiptHash,
    DateTimeOffset EvaluatedAtUtc,
    bool IsReady,
    IReadOnlyList<ReportingGovernanceReadinessCheckDto> Checks);

public sealed record ReportingGovernanceApprovalDto(
    ReportingGovernanceAuthorityDto Authority,
    DateTimeOffset ApprovedAtUtc,
    string DecisionNote);

public sealed record ReportingGovernanceArtifactDto(
    string ArtifactId,
    string ArtifactHash,
    long ByteLength);

public sealed record ReportingGovernanceReleaseDto(
    ReportingGovernanceAuthorityDto Authority,
    DateTimeOffset ReleasedAtUtc,
    string ManifestId,
    string ManifestHash,
    IReadOnlyList<ReportingGovernanceArtifactDto> Artifacts,
    IReadOnlyList<string> EvidenceIds);

public sealed record ReportingGovernanceAuditEntryDto(
    string EventId,
    string AggregateKind,
    string AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAtUtc,
    string Action,
    ReportingGovernanceAuthorityDto Authority,
    string PermissionUsed,
    string? FromExecutionState,
    string? ToExecutionState,
    string? FromGovernanceState,
    string? ToGovernanceState,
    string? FromRestatementState,
    string? ToRestatementState,
    string? Note,
    string? PreviousHash,
    string Hash);

/// <summary>Immutable governed reporting run projection returned by canonical lifecycle routes.</summary>
public sealed record GovernedReportingRunDto(
    string RunId,
    string SeriesId,
    int Revision,
    string TemplateId,
    string TemplateVersion,
    ReportingGovernanceOperationalScopeDto Scope,
    ReportingGovernanceAccessScopeDto Access,
    ReportingGovernanceCertifiedSnapshotDto Snapshot,
    ReportingGovernanceAuthorityDto CreationAuthority,
    DateTimeOffset CreatedAtUtc,
    string? RestatementOfRunId,
    string ExecutionState,
    string GovernanceState,
    long Version,
    ReportingGovernanceReadinessDto? Readiness,
    ReportingGovernanceApprovalDto? Approval,
    ReportingGovernanceReleaseDto? Release,
    IReadOnlyList<ReportingGovernanceAuditEntryDto> AuditTrail);

public sealed record ReportingGovernanceChangedLineDto(
    string LineKey,
    string PreviousValue,
    string CurrentValue,
    IReadOnlyList<string> EvidenceIds);

public sealed record ReportingGovernanceRestatementDto(
    string RequestId,
    string PredecessorRunId,
    string SeriesId,
    int PredecessorRevision,
    long PredecessorVersion,
    string Reason,
    IReadOnlyList<ReportingGovernanceChangedLineDto> ChangedLines,
    ReportingGovernanceAuthorityDto RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string State,
    long Version,
    ReportingGovernanceAuthorityDto? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? DraftRunId,
    IReadOnlyList<ReportingGovernanceAuditEntryDto> AuditTrail);

public sealed record ReportingGovernanceRestatementApprovalDto(
    ReportingGovernanceRestatementDto Request,
    GovernedReportingRunDto DraftRun);
