using System.Collections.Immutable;

namespace Meridian.Reporting;

/// <summary>
/// Execution is deliberately independent from report governance. A successfully rendered report is
/// still a draft until it passes the governed validation, review, approval, and release lifecycle.
/// </summary>
public enum GovernedReportingExecutionState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum GovernedReportingState
{
    Draft,
    Validated,
    InReview,
    Approved,
    Released
}

/// <summary>
/// Explicit capabilities consumed by the Reporting domain. Identity adapters resolve these from
/// the authenticated session; the domain never authorizes a transition from caller-supplied roles.
/// </summary>
public enum ReportingGovernancePermission
{
    CreateRun,
    ExecuteRun,
    ValidateRun,
    SubmitRun,
    ApproveRun,
    ReleaseRun,
    RequestRestatement,
    ApproveRestatement
}

public enum ReportingCommandOrigin
{
    HumanOperator,
    ServicePrincipal,
    ReviewedAutomation
}

public enum ReportingGovernanceAccessMode
{
    Private,
    Restricted,
    CompanyWide
}

public enum ReportingRestatementRequestState
{
    PendingApproval,
    Approved
}

public enum ReportingGovernanceAuditAggregateKind
{
    Run,
    RestatementRequest
}

public enum ReportingGovernanceAuditAction
{
    RunCreated,
    ExecutionStarted,
    ExecutionSucceeded,
    ExecutionFailed,
    ExecutionCancelled,
    RunValidated,
    RunSubmitted,
    RunApproved,
    RunReleased,
    RestatementRequested,
    RestatementApproved,
    RestatementDraftCreated
}

/// <summary>Customer-neutral operational scope retained immutably on every run revision.</summary>
public sealed record ReportingOperationalScope(
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    string? FundId,
    string BookId,
    string PeriodId);

/// <summary>The exact access policy version captured when the run revision was created.</summary>
public sealed record ReportingAccessScope(
    string PolicyId,
    string PolicyVersion,
    ReportingGovernanceAccessMode Mode,
    string? OwnerPrincipalId,
    ImmutableArray<string> PrincipalIds,
    string PolicyHash);

/// <summary>
/// Certified input snapshot. Scope identity is repeated intentionally so a snapshot from another
/// tenant, organization, fund, book, or period cannot be attached to a governed run accidentally.
/// </summary>
public sealed record ReportingCertifiedSnapshotScope(
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

/// <summary>Server-resolved authority snapshot used for one governed command.</summary>
public sealed record ReportingAuthorityScope(
    string ActorId,
    string TenantId,
    string OrganizationId,
    string? CompanyId,
    ImmutableArray<ReportingGovernancePermission> Permissions,
    ReportingCommandOrigin Origin,
    string CorrelationId,
    ImmutableArray<string> PrincipalIds = default)
{
    public bool HasPermission(ReportingGovernancePermission permission) =>
        !Permissions.IsDefaultOrEmpty && Permissions.Contains(permission);

    public bool HasPrincipal(string principalId) =>
        StringComparer.Ordinal.Equals(ActorId, principalId)
        || (!PrincipalIds.IsDefaultOrEmpty && PrincipalIds.Contains(principalId, StringComparer.Ordinal));
}

public sealed record ReportingReadinessCheck(
    string CheckId,
    bool Passed,
    ImmutableArray<string> EvidenceIds,
    string? FailureReason = null);

public sealed record ReportingReadinessReceipt(
    string ReceiptId,
    string ReceiptHash,
    string RunId,
    string TenantId,
    string SnapshotId,
    string SnapshotHash,
    DateTimeOffset EvaluatedAtUtc,
    ImmutableArray<ReportingReadinessCheck> Checks)
{
    public bool IsReady =>
        !Checks.IsDefaultOrEmpty &&
        Checks.All(static check => check.Passed && !check.EvidenceIds.IsDefaultOrEmpty);
}

public sealed record ReportingApprovalReceipt(
    ReportingAuthorityScope Authority,
    DateTimeOffset ApprovedAtUtc,
    string DecisionNote);

public sealed record ReportingArtifactReference(
    string ArtifactId,
    string ArtifactHash,
    long ByteLength);

public sealed record ReportingReleaseEvidence(
    string ManifestId,
    string ManifestHash,
    ImmutableArray<ReportingArtifactReference> Artifacts,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingReleaseReceipt(
    ReportingAuthorityScope Authority,
    DateTimeOffset ReleasedAtUtc,
    string ManifestId,
    string ManifestHash,
    ImmutableArray<ReportingArtifactReference> Artifacts,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingGovernanceAuditEntry(
    string EventId,
    ReportingGovernanceAuditAggregateKind AggregateKind,
    string AggregateId,
    long AggregateVersion,
    DateTimeOffset OccurredAtUtc,
    ReportingGovernanceAuditAction Action,
    ReportingAuthorityScope Authority,
    ReportingGovernancePermission PermissionUsed,
    GovernedReportingExecutionState? FromExecutionState,
    GovernedReportingExecutionState? ToExecutionState,
    GovernedReportingState? FromGovernanceState,
    GovernedReportingState? ToGovernanceState,
    ReportingRestatementRequestState? FromRestatementState,
    ReportingRestatementRequestState? ToRestatementState,
    string? Note,
    string? PreviousHash,
    string Hash);

public sealed record GovernedReportingRun(
    string RunId,
    string SeriesId,
    int Revision,
    string TemplateId,
    string TemplateVersion,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    ReportingAuthorityScope CreationAuthority,
    DateTimeOffset CreatedAtUtc,
    string? RestatementOfRunId,
    GovernedReportingExecutionState ExecutionState,
    GovernedReportingState GovernanceState,
    long Version,
    ReportingReadinessReceipt? Readiness,
    ReportingApprovalReceipt? Approval,
    ReportingReleaseReceipt? Release,
    ImmutableArray<ReportingGovernanceAuditEntry> AuditTrail);

public sealed record ReportingRestatementChangedLine(
    string LineKey,
    string PreviousValue,
    string CurrentValue,
    ImmutableArray<string> EvidenceIds);

public sealed record ReportingRestatementRequest(
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

/// <summary>
/// Ordinary creation intentionally has no restatement switch or predecessor field. Once a series
/// has a Released revision, only the independently approved restatement workflow can add a revision.
/// </summary>
public sealed record ReportingRunCreationRequest(
    string RunId,
    string SeriesId,
    string TemplateId,
    string TemplateVersion,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot);

public sealed record ReportingRestatementRequestCommand(
    string PredecessorRunId,
    long ExpectedPredecessorVersion,
    string Reason,
    ImmutableArray<ReportingRestatementChangedLine> ChangedLines);

public sealed record ReportingRestatementApprovalCommand(
    string RequestId,
    long ExpectedRequestVersion,
    ReportingCertifiedSnapshotScope ReplacementSnapshot);

public sealed record ReportingRestatementApprovalResult(
    ReportingRestatementRequest Request,
    GovernedReportingRun DraftRun);

/// <summary>
/// Repository boundary for a storage implementation that can atomically compare versions, append
/// lifecycle state, and create a restatement revision in one transaction.
/// </summary>
public interface IReportingGovernanceRepository
{
    ValueTask<TResult> ExecuteTransactionAsync<TResult>(
        Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);
}

public interface IReportingGovernanceTransaction
{
    ValueTask<GovernedReportingRun?> GetRunAsync(
        string tenantId,
        string runId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<GovernedReportingRun>> ListRunsBySeriesAsync(
        string tenantId,
        string seriesId,
        CancellationToken cancellationToken = default);

    ValueTask AddRunAsync(
        GovernedReportingRun run,
        CancellationToken cancellationToken = default);

    ValueTask ReplaceRunAsync(
        GovernedReportingRun run,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    ValueTask<ReportingRestatementRequest?> GetRestatementRequestAsync(
        string tenantId,
        string requestId,
        CancellationToken cancellationToken = default);

    ValueTask AddRestatementRequestAsync(
        string tenantId,
        ReportingRestatementRequest request,
        CancellationToken cancellationToken = default);

    ValueTask ReplaceRestatementRequestAsync(
        string tenantId,
        ReportingRestatementRequest request,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}

public class ReportingGovernanceException : InvalidOperationException
{
    public ReportingGovernanceException(string message) : base(message)
    {
    }
}

public sealed class ReportingGovernanceAuthorizationException : ReportingGovernanceException
{
    public ReportingGovernanceAuthorizationException(string message) : base(message)
    {
    }
}

public sealed class ReportingGovernanceConcurrencyException : ReportingGovernanceException
{
    public ReportingGovernanceConcurrencyException(string message) : base(message)
    {
    }
}

public sealed class ReportingGovernanceNotFoundException : ReportingGovernanceException
{
    public ReportingGovernanceNotFoundException(string message) : base(message)
    {
    }
}
