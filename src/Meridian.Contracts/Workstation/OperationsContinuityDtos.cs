using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

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

/// <summary>
/// Single source-of-truth contract matrix for Operations Continuity workflow status/state/code surfaces.
/// </summary>
public static class OperationsWorkflowContractMatrix
{
    public static IReadOnlyList<OperationsWorkflowStatusDto> OverallStatuses { get; } =
    [
        OperationsWorkflowStatusDto.NotStarted,
        OperationsWorkflowStatusDto.CollectingBrokerData,
        OperationsWorkflowStatusDto.SecurityMasterValidation,
        OperationsWorkflowStatusDto.LedgerPostingDraft,
        OperationsWorkflowStatusDto.ReconciliationActive,
        OperationsWorkflowStatusDto.ApprovalPending,
        OperationsWorkflowStatusDto.ReadyForClose,
        OperationsWorkflowStatusDto.Closed,
        OperationsWorkflowStatusDto.Blocked
    ];

    public static IReadOnlyList<OperationsGateStatusDto> GateStatuses { get; } =
    [
        OperationsGateStatusDto.NotStarted,
        OperationsGateStatusDto.InProgress,
        OperationsGateStatusDto.Passed,
        OperationsGateStatusDto.ReviewRequired,
        OperationsGateStatusDto.Blocked
    ];

    public static IReadOnlyList<OperationsBrokerIntakeStateDto> BrokerSubStates { get; } =
    [
        OperationsBrokerIntakeStateDto.Pending,
        OperationsBrokerIntakeStateDto.Imported,
        OperationsBrokerIntakeStateDto.Normalized,
        OperationsBrokerIntakeStateDto.MatchedToInternalRun,
        OperationsBrokerIntakeStateDto.Complete
    ];

    public static IReadOnlyList<OperationsSecurityMasterStateDto> SecurityMasterSubStates { get; } =
    [
        OperationsSecurityMasterStateDto.Pending,
        OperationsSecurityMasterStateDto.ResolvedAllInstruments,
        OperationsSecurityMasterStateDto.OverridesRequested,
        OperationsSecurityMasterStateDto.OverridesApproved,
        OperationsSecurityMasterStateDto.Complete
    ];

    public static IReadOnlyList<OperationsLedgerPostingStateDto> LedgerSubStates { get; } =
    [
        OperationsLedgerPostingStateDto.Pending,
        OperationsLedgerPostingStateDto.Drafted,
        OperationsLedgerPostingStateDto.Validated,
        OperationsLedgerPostingStateDto.Posted,
        OperationsLedgerPostingStateDto.Complete
    ];

    public static IReadOnlyList<OperationsReconciliationStateDto> ReconciliationSubStates { get; } =
    [
        OperationsReconciliationStateDto.Pending,
        OperationsReconciliationStateDto.AutoMatched,
        OperationsReconciliationStateDto.ExceptionsOpen,
        OperationsReconciliationStateDto.InReview,
        OperationsReconciliationStateDto.Cleared,
        OperationsReconciliationStateDto.Complete
    ];

    public static IReadOnlyList<OperationsApprovalStateDto> ApprovalSubStates { get; } =
    [
        OperationsApprovalStateDto.Pending,
        OperationsApprovalStateDto.Submitted,
        OperationsApprovalStateDto.ReviewerAssigned,
        OperationsApprovalStateDto.Approved,
        OperationsApprovalStateDto.Rejected
    ];

    public static IReadOnlySet<string> BlockerCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "ACTOR_REQUIRED",
        "APPROVAL_DECISION_REQUIRED",
        "APPROVAL_METADATA_REQUIRED",
        "APPROVAL_REQUIRED",
        "APPROVAL_SUBMISSION_METADATA_REQUIRED",
        "APPROVAL_SUBMISSION_REQUIRED",
        "BROKER_IMPORT_ALREADY_RECORDED",
        "BROKER_IMPORT_GATE_NOT_READY",
        "BROKER_IMPORT_REQUIRED",
        "BROKER_INTAKE_REQUIRED",
        "BROKER_NORMALIZATION_REQUIRED",
        "BROKER_PROVIDER_ACCOUNT_UNLINKED",
        "BROKER_SYNC_STALE",
        "FUND_ACCOUNT_REQUIRED",
        "LEDGER_BATCH_ID_REQUIRED",
        "LEDGER_DRAFT_IMBALANCED",
        "LEDGER_DRAFT_REQUIRED",
        "LEDGER_DUPLICATE_POSTING_CANDIDATE",
        "LEDGER_IDEMPOTENCY_KEY_MISSING",
        "LEDGER_JOURNAL_ACCOUNT_NAME_REQUIRED",
        "LEDGER_JOURNAL_ACCOUNT_TYPE_INVALID",
        "LEDGER_JOURNAL_AGGREGATE_ID_REQUIRED",
        "LEDGER_JOURNAL_APPEND_REJECTED",
        "LEDGER_JOURNAL_CANDIDATE_INVALID",
        "LEDGER_JOURNAL_CANDIDATE_REQUIRED",
        "LEDGER_JOURNAL_DESCRIPTION_REQUIRED",
        "LEDGER_JOURNAL_LINES_REQUIRED",
        "LEDGER_JOURNAL_PERIOD_ID_REQUIRED",
        "LEDGER_JOURNAL_STORE_UNAVAILABLE",
        "LEDGER_JOURNAL_TIMESTAMP_REQUIRED",
        "LEDGER_PERIOD_CLOSED",
        "LEDGER_POSTING_KIND_REQUIRED",
        "LEDGER_POSTING_REQUIRED",
        "LEDGER_PREVIEW_ID_REQUIRED",
        "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_VALIDATED_JOURNAL_REQUIRED",
        "LEDGER_VALIDATION_REQUIRED",
        "OPERATIONS_CONTINUITY_WORKFLOW_ALREADY_EXISTS",
        "OPERATIONS_GATES_NOT_PASSED",
        "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED",
        "PERIOD_REQUIRED",
        "RECONCILIATION_BREAK_NOT_FOUND",
        "RECONCILIATION_BREAK_RATIONALE_REQUIRED",
        "RECONCILIATION_CRITICAL_BREAKS_OPEN",
        "RECONCILIATION_RUN_REQUIRED",
        "REJECTION_METADATA_REQUIRED",
        "REOPEN_GOVERNANCE_METADATA_REQUIRED",
        "REPORT_PACK_NOT_READY",
        "REPORT_PACK_REQUIRED",
        "SECURITY_MASTER_RESOLUTION_REQUIRED",
        "SM_ACCOUNTING_TERMS_INCOMPLETE",
        "SM_INSTRUMENT_UNRESOLVED",
        "SM_OVERRIDE_APPROVAL_METADATA_REQUIRED",
        "SM_OVERRIDE_APPROVAL_REQUIRED",
        "SM_OVERRIDE_ID_MISMATCH",
        "SM_OVERRIDE_REQUEST_REQUIRED",
        "SM_RECON_SECURITY_UNRESOLVED",
        "WORKFLOW_CLOSED",
        "WORKFLOW_ID_REQUIRED",
        "WORKFLOW_NOT_CLOSED",
        "WORKFLOW_VERSION_MISMATCH"
    };

    public static IReadOnlySet<string> IssueCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "BROKER_PROVIDER_ACCOUNT_UNLINKED",
        "BROKER_SYNC_STALE",
        "SM_INSTRUMENT_UNRESOLVED",
        "SM_ACCOUNTING_TERMS_INCOMPLETE",
        "SM_RECON_SECURITY_UNRESOLVED",
        "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_DRAFT_IMBALANCED",
        "LEDGER_PERIOD_CLOSED",
        "LEDGER_DUPLICATE_POSTING_CANDIDATE",
        "RECONCILIATION_CRITICAL_BREAKS_OPEN",
        "REPORT_PACK_NOT_READY",
        "APPROVAL_REQUIRED"
    };

[JsonConverter(typeof(JsonStringEnumConverter<OperationsIssueCodeDto>))]
public enum OperationsIssueCodeDto : byte
{
    Unknown = 0,
    WorkflowAlreadyExists = 1,
    VersionMismatch = 2,
    BrokerSourceMissing = 3,
    BrokerImportFailed = 4,
    SecurityCoverageMissing = 5,
    SecurityAccountingClassificationMissing = 6,
    LedgerPreviewUnavailable = 7,
    LedgerDraftUnbalanced = 8,
    LedgerPostingValidationFailed = 9,
    ReconciliationBreaksOpen = 10,
    ReconciliationCriticalBreaksOpen = 11,
    ApprovalReviewerMissing = 12,
    ApprovalRejected = 13,
    ReportPackMissing = 14,
    ReportPackNotReady = 15,
    GovernanceApprovalRequired = 16
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
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? EvidenceReferenceIds = null);

public sealed record OperationsSecurityMasterOverrideApprovalRequestDto(
    long ExpectedVersion,
    string Actor,
    string OverrideId,
    string Rationale,
    string PolicyReference,
    DateOnly? ExpiresOn,
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

public sealed record OperationsLedgerPostRequestDto(
    long ExpectedVersion,
    string Actor,
    string LedgerBatchId,
    string PostingKind,
    bool PeriodOpen,
    bool HasValidatedJournal = true,
    bool HasDuplicatePostingCandidate = false,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsLedgerJournalCandidateDto? JournalCandidate = null);

public sealed record OperationsLedgerJournalCandidateDto(
    Guid? JournalEntryId,
    Guid AggregateId,
    Guid PeriodId,
    DateTimeOffset Timestamp,
    string Description,
    IReadOnlyList<OperationsLedgerJournalLineDto> Lines,
    Guid? CommandId = null,
    Guid? CorrelationId = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    OperationsJournalEntryMetadataDto? Metadata = null);

public sealed record OperationsLedgerJournalLineDto(
    Guid? EntryId,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    string? Symbol = null,
    string? FinancialAccountId = null);

public sealed record OperationsJournalEntryMetadataDto(
    string? ActivityType = null,
    string? Symbol = null,
    Guid? SecurityId = null,
    Guid? OrderId = null,
    Guid? FillId = null,
    string? ProjectId = null,
    string? LedgerBook = null,
    string? ScenarioId = null,
    string? StrategyId = null,
    string? FinancialAccountId = null,
    string? CounterpartyAccountId = null,
    string? Institution = null,
    IReadOnlyDictionary<string, string>? Tags = null);

public sealed record OperationsReconciliationRunRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsBreakCaseDto>? BreakCases = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    int? SecurityCoverageIssueCount = null,
    int? SecurityAccountingIssueCount = null,
    int? ExpectedAccountingEventCount = null,
    int? ExpectedJournalPreviewCount = null);

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

public sealed record OperationsRejectWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReasonCode,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsCloseWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsReopenWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string IncidentId,
    bool IsGovernedAdmin,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null);

public sealed record OperationsTransitionResultDto(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    OperationsContinuityWorkflowDto? Workflow,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    long? NewVersion = null);

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
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    OperationsIssueCodeDto? IssueCode = null);

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
