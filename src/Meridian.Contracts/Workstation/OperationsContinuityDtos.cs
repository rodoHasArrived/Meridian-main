using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Server-derived lifecycle status for the fund-account and period operations continuity workflow.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationsWorkflowStatusDto>))]
public enum OperationsWorkflowStatusDto : byte
{
    NotStarted = 0,
    CollectingBrokerData = 1,
    SecurityMasterValidation = 2,
    LedgerPostingDraft = 3,
    ReconciliationActive = 4,
    ApprovalPending = 5,
    ReadyForClose = 6,
    Closed = 7,
    Blocked = 8
}

/// <summary>
/// Server-derived status for a required operations continuity gate.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationsGateStatusDto>))]
public enum OperationsGateStatusDto : byte
{
    NotStarted = 0,
    InProgress = 1,
    Passed = 2,
    ReviewRequired = 3,
    Blocked = 4
}

/// <summary>
/// Stable gate keys in the operations continuity workflow.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationsGateKeyDto>))]
public enum OperationsGateKeyDto : byte
{
    BrokerIngest = 0,
    SecurityMaster = 1,
    LedgerPosting = 2,
    Reconciliation = 3,
    Approval = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationsBrokerIntakeStateDto>))]
public enum OperationsBrokerIntakeStateDto : byte
{
    Pending = 0,
    Imported = 1,
    Normalized = 2,
    MatchedToInternalRun = 3,
    Complete = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationsSecurityMasterStateDto>))]
public enum OperationsSecurityMasterStateDto : byte
{
    Pending = 0,
    ResolvedAllInstruments = 1,
    OverridesRequested = 2,
    OverridesApproved = 3,
    Complete = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationsLedgerPostingStateDto>))]
public enum OperationsLedgerPostingStateDto : byte
{
    Pending = 0,
    Drafted = 1,
    Validated = 2,
    Posted = 3,
    Complete = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationsReconciliationStateDto>))]
public enum OperationsReconciliationStateDto : byte
{
    Pending = 0,
    AutoMatched = 1,
    ExceptionsOpen = 2,
    InReview = 3,
    Cleared = 4,
    Complete = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationsApprovalStateDto>))]
public enum OperationsApprovalStateDto : byte
{
    Pending = 0,
    Submitted = 1,
    ReviewerAssigned = 2,
    Approved = 3,
    Rejected = 4
}

public sealed record OperationsStartWorkflowRequestDto(
    Guid FundAccountId,
    string PeriodId,
    Guid? SecurityMasterSnapshotId,
    string? BrokerSource,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsTransitionRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsGatePostureRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    bool? ProviderAccountLinked = null,
    bool? ProviderSyncStale = null,
    int? SecurityCoverageIssueCount = null,
    int? SecurityAccountingIssueCount = null,
    bool? LedgerPreviewAvailable = null,
    bool? LedgerDraftBalanced = null,
    bool? LedgerPostingValidated = null,
    int? OpenCriticalBreakCount = null,
    int? OpenNonCriticalBreakCount = null,
    bool? ReportPackReady = null,
    string? ReportPackId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsSecurityMasterResolveRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    int UnresolvedInstrumentCount = 0,
    int OverrideRequestCount = 0,
    bool OverridesApproved = false,
    int MissingAccountingTermCount = 0,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsLedgerDraftRequestDto(
    long ExpectedVersion,
    string Actor,
    string PreviewId,
    bool IsBalanced,
    string? Rationale = null,
    string? CorrelationId = null,
    bool HasSecurityMasterProvenance = true,
    bool HasIdempotencyKey = true,
    string? LedgerBatchId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsLedgerValidationRequestDto(
    long ExpectedVersion,
    string Actor,
    bool IsBalanced,
    bool PeriodOpen,
    bool HasDuplicatePostingCandidate = false,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsReconciliationRunRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsBreakCaseDto>? BreakCases = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsResolveBreakCaseRequestDto(
    long ExpectedVersion,
    string Actor,
    string ResolutionStatus,
    string Rationale,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsSubmitApprovalRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsApprovalDecisionRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsCloseWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsTransitionResultDto(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    OperationsContinuityWorkflowDto? Workflow,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions);

public sealed record OperationsContinuityWorkflowSummaryDto(
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    Guid? SecurityMasterSnapshotId,
    string BrokerSource,
    OperationsWorkflowStatusDto Status,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<OperationsGateDto> Gates,
    IReadOnlyList<OperationsNextActionDto> NextActions);

public sealed record OperationsContinuityWorkflowDto(
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    Guid? SecurityMasterSnapshotId,
    string BrokerSource,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version,
    OperationsWorkflowStatusDto Status,
    OperationsBrokerIntakeStateDto BrokerIntakeState,
    OperationsSecurityMasterStateDto SecurityMasterState,
    OperationsLedgerPostingStateDto LedgerPostingState,
    OperationsReconciliationStateDto ReconciliationState,
    OperationsApprovalStateDto ApprovalState,
    IReadOnlyList<OperationsGateDto> Gates,
    IReadOnlyList<OperationsTimelineEntryDto> Timeline,
    IReadOnlyList<OperationsBreakCaseDto> BreakCases,
    OperationsLedgerPreviewDto? LedgerPreview,
    IReadOnlyList<OperationsApprovalDto> Approvals,
    OperationsReportPackReadinessDto ReportPackReadiness,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions);

public sealed record OperationsGateDto(
    OperationsGateKeyDto GateKey,
    string DisplayName,
    OperationsGateStatusDto Status,
    bool IsRequired,
    string Description,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    DateTimeOffset? CompletedAtUtc,
    string? CompletedBy);

public sealed record OperationsTimelineEntryDto(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    string EventType,
    OperationsWorkflowStatusDto FromState,
    OperationsWorkflowStatusDto ToState,
    OperationsGateKeyDto? Gate,
    OperationsGateStatusDto? FromGateStatus,
    OperationsGateStatusDto? ToGateStatus,
    string Actor,
    string? Rationale,
    string? CorrelationId,
    IReadOnlyList<OperationsEvidenceLinkDto> References,
    string? PreviousHash,
    string CurrentHash);

public sealed record OperationsWorkflowAuditDto(
    Guid AuditId,
    DateTimeOffset OccurredAtUtc,
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    string EventType,
    OperationsWorkflowStatusDto FromState,
    OperationsWorkflowStatusDto ToState,
    OperationsGateKeyDto? Gate,
    OperationsGateStatusDto? FromGateStatus,
    OperationsGateStatusDto? ToGateStatus,
    string Actor,
    string? Rationale,
    string? CorrelationId,
    IReadOnlyList<OperationsEvidenceLinkDto> References,
    string? PreviousHash,
    string CurrentHash);

public sealed record OperationsBreakCaseDto(
    string BreakId,
    string CheckId,
    string Category,
    string Severity,
    string Status,
    string? Owner,
    DateOnly? DueDate,
    string? ExpectedSource,
    string? ActualSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal? Variance,
    string? SecurityId,
    string? Symbol,
    string? SuggestedAction,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record OperationsApprovalDto(
    string ApprovalId,
    OperationsApprovalStateDto Status,
    string? Operator,
    string? Reviewer,
    string? Rationale,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record OperationsLedgerPreviewDto(
    string PreviewId,
    string Status,
    string? LedgerBatchId,
    DateTimeOffset? GeneratedAtUtc,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record OperationsReportPackReadinessDto(
    bool IsReady,
    string? ReportPackId,
    string? BlockingReason,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record OperationsWorkflowBlockerDto(
    string Code,
    string Message,
    OperationsGateKeyDto? Gate,
    string Severity,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record OperationsNextActionDto(
    string Code,
    string Label,
    string? Route,
    OperationsGateKeyDto? Gate);

public sealed record OperationsEvidenceLinkDto(
    string EvidenceId,
    string Label,
    string? Route,
    string? Source,
    DateTimeOffset? CapturedAtUtc);
