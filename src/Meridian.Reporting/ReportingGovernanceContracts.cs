using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    ApproveRestatement,
    ExportPersistenceEvidence
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

[JsonConverter(typeof(ReportingAccessPrincipalKindJsonConverter))]
public enum ReportingAccessPrincipalKind
{
    User,
    Group,
    Company
}

/// <summary>
/// Strict wire converter for the immutable access-principal namespace. Numeric enum payloads are
/// rejected so browser, desktop, audit JSON, and persisted policy snapshots share one unambiguous
/// User/Group/Company representation.
/// </summary>
public sealed class ReportingAccessPrincipalKindJsonConverter
    : JsonConverter<ReportingAccessPrincipalKind>
{
    public override ReportingAccessPrincipalKind Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !Enum.TryParse<ReportingAccessPrincipalKind>(
                reader.GetString(),
                ignoreCase: true,
                out var kind)
            || !Enum.IsDefined(kind))
        {
            throw new JsonException(
                "Reporting access principal kind must be User, Group, or Company.");
        }

        return kind;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ReportingAccessPrincipalKind value,
        JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException("Reporting access principal kind is invalid.");
        }

        writer.WriteStringValue(value.ToString());
    }
}

public sealed record ReportingAccessPrincipalScope(
    ReportingAccessPrincipalKind Kind,
    string PrincipalId);

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
    bool AllowOwnerAccess,
    ImmutableArray<ReportingAccessPrincipalScope> Principals,
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
    DateTimeOffset CapturedAtUtc,
    string? SourceCheckpointId = null,
    string? SourceCheckpointHash = null,
    string? ReconciliationCheckpointHash = null,
    string? ParametersCanonicalJson = null,
    string? ParametersHash = null)
{
    /// <summary>
    /// True when final client artifacts must be rendered from the checkpoint-bound canonical
    /// ledger presentation. Init-only preserves the established constructor and deconstruction ABI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresCertifiedLedgerPresentation { get; init; }
}

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
        StringComparer.OrdinalIgnoreCase.Equals(ActorId, principalId)
        || (!PrincipalIds.IsDefaultOrEmpty && PrincipalIds.Contains(principalId, StringComparer.OrdinalIgnoreCase));

    public bool Matches(ReportingAccessPrincipalScope principal) =>
        principal.Kind switch
        {
            ReportingAccessPrincipalKind.User =>
                StringComparer.OrdinalIgnoreCase.Equals(ActorId, principal.PrincipalId),
            ReportingAccessPrincipalKind.Group =>
                !PrincipalIds.IsDefaultOrEmpty
                && PrincipalIds.Contains(principal.PrincipalId, StringComparer.OrdinalIgnoreCase),
            ReportingAccessPrincipalKind.Company =>
                StringComparer.OrdinalIgnoreCase.Equals(CompanyId, principal.PrincipalId),
            _ => false
        };
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
    string OrganizationId,
    string? CompanyId,
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
    ImmutableArray<ReportingGovernanceAuditEntry> AuditTrail,
    string? RestatementRequestId = null);

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
    ImmutableArray<ReportingGovernanceAuditEntry> AuditTrail,
    ImmutableArray<ReportingRestatementChangedLine> RequestedChangedLines = default);

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
    ReportingCertifiedSnapshotScope ReplacementSnapshot,
    string ReplacementRunId,
    ImmutableArray<ReportingRestatementChangedLine> ChangedLines);

public sealed record ReportingRestatementApprovalResult(
    ReportingRestatementRequest Request,
    GovernedReportingRun DraftRun);

/// <summary>
/// Durable governance formats are explicit so already-retained evidence is never interpreted with
/// a newer contract or hash algorithm by accident.
/// </summary>
public enum ReportingGovernancePersistenceFormat : short
{
    LegacyV1 = 1,
    CanonicalV2 = 2
}

public enum ReportingGovernancePersistenceDisposition
{
    Current,
    LegacyReadOnlyRecertificationRequired,
    IntegrityFailure
}

/// <summary>
/// Tenant-scoped storage status. Legacy records expose only indexed identity and verified storage
/// facts; their untyped access and incomplete certification claims are deliberately not promoted
/// into the current governed-run model.
/// </summary>
public sealed record ReportingGovernancePersistenceStatus(
    string TenantId,
    ReportingGovernanceAuditAggregateKind AggregateKind,
    string AggregateId,
    long AggregateVersion,
    ReportingGovernancePersistenceFormat StateFormat,
    string StatePayloadHash,
    bool StateChecksumVerified,
    int AuditEventCount,
    ImmutableArray<ReportingGovernancePersistenceFormat> AuditHashFormats,
    bool AuditChainVerified,
    ReportingGovernancePersistenceDisposition Disposition,
    string Reason);

public sealed record ReportingGovernanceRawAuditEnvelope(
    long AggregateVersion,
    string EventId,
    string? PreviousHash,
    string EventHash,
    ReportingGovernancePersistenceFormat HashFormat,
    string EventPayload,
    string PayloadHash);

/// <summary>
/// Exact retained bytes for operator-controlled archival or remediation. Implementations return an
/// export only after the state checksum, indexed identity, audit payload checksums, and audit chain
/// have all been verified with their declared historical formats.
/// </summary>
public sealed record ReportingGovernancePersistenceExport(
    ReportingGovernancePersistenceStatus Status,
    string StatePayload,
    ImmutableArray<ReportingGovernanceRawAuditEnvelope> AuditEvents);

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
    ValueTask<IReadOnlyList<ReportingGovernancePersistenceStatus>> ListPersistenceStatusAsync(
        ReportingAuthorityScope authority,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ReportingGovernancePersistenceStatus>>([]);

    ValueTask<ReportingGovernancePersistenceExport?> ExportPersistenceRecordAsync(
        ReportingAuthorityScope authority,
        ReportingGovernanceAuditAggregateKind aggregateKind,
        string aggregateId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ReportingGovernancePersistenceExport?>(null);

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

    ValueTask<IReadOnlyList<ReportingRestatementRequest>> ListRestatementRequestsBySeriesAsync(
        string tenantId,
        string seriesId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ReportingRestatementRequest>>([]);

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

    public ReportingGovernanceException(string message, Exception innerException)
        : base(message, innerException)
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
