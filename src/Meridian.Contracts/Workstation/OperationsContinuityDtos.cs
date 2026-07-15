using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

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

[JsonConverter(typeof(JsonStringEnumConverter<OperationsReconciliationLaneStatusDto>))]
public enum OperationsReconciliationLaneStatusDto : byte
{
    Missing = 0,
    Ready = 1,
    ReviewRequired = 2,
    Blocked = 3
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

    public static IReadOnlyList<OperationsReconciliationLaneStatusDto> ReconciliationLaneStatuses { get; } =
    [
        OperationsReconciliationLaneStatusDto.Missing,
        OperationsReconciliationLaneStatusDto.Ready,
        OperationsReconciliationLaneStatusDto.ReviewRequired,
        OperationsReconciliationLaneStatusDto.Blocked
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
        "APPROVAL_REVIEWER_MISMATCH",
        "APPROVAL_SUBMISSION_METADATA_REQUIRED",
        "APPROVAL_SUBMISSION_REQUIRED",
        "AUDIT_CHAIN_INVALID",
        "AUDIT_CHAIN_MISSING",
        "BROKER_CASH_COVERAGE_INCOMPLETE",
        "BROKER_IMPORT_ALREADY_RECORDED",
        "BROKER_IMPORT_GATE_NOT_READY",
        "BROKER_IMPORT_REQUIRED",
        "BROKER_INTAKE_REQUIRED",
        "BROKER_NORMALIZATION_REQUIRED",
        "BROKER_DUPLICATE_TRANSACTION",
        "BROKER_OUT_OF_PERIOD_ROWS",
        "BROKER_PARSE_FAILED",
        "BROKER_PROVIDER_ACCOUNT_UNLINKED",
        "BROKER_PROVIDER_CAPABILITY_DEGRADED",
        "BROKER_PROVIDER_REQUIRED_CAPABILITY_UNROUTABLE",
        "BROKER_SCHEMA_INCOMPATIBLE",
        "BROKER_SECURITY_UNRESOLVED",
        "BROKER_STATEMENT_MISSING",
        "BROKER_SYNC_STALE",
        "BROKER_TRANSACTION_TYPE_UNKNOWN",
        "CLOSE_CHECKLIST_CONTROL_APPROVALS_INCOMPLETE",
        "CLOSE_CHECKLIST_CONTROL_APPROVALS_REQUIRED",
        "FUND_ACCOUNT_REQUIRED",
        "LEDGER_ACCOUNT_MAPPING_MISSING",
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
        "LEDGER_JOURNAL_PERIOD_ID_MISMATCH",
        "LEDGER_JOURNAL_AGGREGATE_ID_MISMATCH",
        "LEDGER_JOURNAL_STORE_UNAVAILABLE",
        "LEDGER_JOURNAL_TIMESTAMP_REQUIRED",
        "LEDGER_PERIOD_CLOSED",
        "LEDGER_POSTING_KIND_REQUIRED",
        "LEDGER_POSTING_REQUIRED",
        "LEDGER_PREVIEW_ID_REQUIRED",
        "LEDGER_SECURITY_MASTER_APPROVAL_MISSING",
        "LEDGER_SECURITY_MASTER_MAPPING_MISSING",
        "LEDGER_SECURITY_MASTER_ACCOUNTING_RULE_MISSING",
        "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_JOURNAL_PROVENANCE_MISSING",
        "LEDGER_JOURNAL_SECURITY_MASTER_PROVENANCE_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED",
        "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED",
        "LEDGER_LINE_SECURITY_MASTER_ID_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_ID_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_SOURCE_ACTIVITY_DUPLICATE",
        "LEDGER_VALIDATED_JOURNAL_REQUIRED",
        "LEDGER_VALIDATION_REQUIRED",
        "OPERATIONS_CONTINUITY_WORKFLOW_ALREADY_EXISTS",
        "OPERATIONS_GATES_NOT_PASSED",
        "OPERATIONS_PREREQUISITE_GATES_NOT_PASSED",
        "PERIOD_REQUIRED",
        "POSITION_COVERAGE_INCOMPLETE",
        "PRICING_COVERAGE_INCOMPLETE",
        "RECONCILIATION_ACTUAL_FEED_ACTIVITY_MISSING_EXPECTED_EVENT",
        "RECONCILIATION_BREAK_ALREADY_CLOSED",
        "RECONCILIATION_BREAK_ASSIGNMENT_RATIONALE_REQUIRED",
        "RECONCILIATION_BREAK_NOT_FOUND",
        "RECONCILIATION_BREAK_OWNER_REQUIRED",
        "RECONCILIATION_BREAK_RATIONALE_REQUIRED",
        "RECONCILIATION_CRITICAL_BREAKS_OPEN",
        "RECONCILIATION_EVIDENCE_MISSING",
        "RECONCILIATION_EXPECTED_ACCRUAL_MISSING_ACTUAL",
        "RECONCILIATION_EXTERNAL_EVIDENCE_MISSING_LEDGER_POSTING",
        "RECONCILIATION_FACTOR_PAYDOWN_MISMATCH",
        "RECONCILIATION_LEDGER_POSTING_MISSING_EXTERNAL_EVIDENCE",
        "RECONCILIATION_PRINCIPAL_INCOME_CLASSIFICATION_MISMATCH",
        "RECONCILIATION_RUN_NOT_FOUND",
        "RECONCILIATION_RUN_REQUIRED",
        "RECONCILIATION_UNASSIGNED_OWNER",
        "REJECTION_METADATA_REQUIRED",
        "REOPEN_GOVERNANCE_METADATA_REQUIRED",
        "REPORT_PACK_ID_MISMATCH",
        "REPORT_PACK_NOT_READY",
        "REPORT_PACK_REQUIRED",
        "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED",
        "SECURITY_MASTER_RESOLUTION_REQUIRED",
        "ACCRUAL_ACTUAL_EVENT_MISSING",
        "ACCRUAL_AMOUNT_MISMATCH",
        "ACCRUAL_CLASSIFICATION_MISMATCH",
        "ACCRUAL_DAY_COUNT_MISSING",
        "ACCRUAL_DUPLICATE_RECOGNITION",
        "ACCRUAL_EXPECTED_EVENT_MISSING",
        "ACCRUAL_EXTERNAL_EVIDENCE_MISSING",
        "ACCRUAL_FACTOR_PAYDOWN_MISMATCH",
        "ACCRUAL_FACTOR_STALE",
        "ACCRUAL_LEDGER_POSTING_MISSING",
        "ACCRUAL_RATE_RESET_MISSING",
        "ACCRUAL_TIMING_MISMATCH",
        "FACTOR_PAYDOWN_AMOUNT_MISMATCH",
        "FACTOR_PAYDOWN_CLASSIFICATION_MISMATCH",
        "FACTOR_PAYDOWN_EXTERNAL_CASH_MISSING",
        "FACTOR_PAYDOWN_LEDGER_MISSING",
        "FACTOR_REDUCTION_UNRECONCILED",
        "FACTOR_SCHEDULE_MISSING",
        "FACTOR_STALE",
        "SECURITY_ACCOUNTING_RULE_MISSING",
        "SECURITY_SCHEDULE_MISSING",
        "SM_ACCOUNTING_CLASSIFICATION_MISSING",
        "SM_ACCOUNTING_TERMS_INCOMPLETE",
        "SM_ACCRUAL_CASH_FLOW_TERMS_MISSING",
        "SM_COUPON_TERMS_MISSING",
        "SM_DAY_COUNT_MISSING",
        "SM_DIVIDEND_TERMS_MISSING",
        "SM_FACTOR_SCHEDULE_MISSING",
        "SM_IDENTIFIER_CONFLICT",
        "SM_PROVENANCE_INCOMPLETE",
        "SM_PAYMENT_FREQUENCY_MISSING",
        "SM_RATE_RESET_TERMS_MISSING",
        "SM_RATE_RESET_SCHEDULE_MISSING",
        "SM_INSTRUMENT_UNRESOLVED",
        "SM_OVERRIDE_APPROVAL_EXPIRED",
        "SM_OVERRIDE_APPROVAL_METADATA_REQUIRED",
        "SM_OVERRIDE_APPROVAL_REQUIRED",
        "SM_OVERRIDE_ID_MISMATCH",
        "SM_OVERRIDE_REQUEST_REQUIRED",
        "SM_RECON_SECURITY_UNRESOLVED",
        "SM_UNAPPROVED_OVERRIDE",
        "SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT",
        "SM_VALUATION_SOURCE_MISSING",
        "WORKFLOW_CLOSED",
        "WORKFLOW_ID_REQUIRED",
        "WORKFLOW_NOT_CLOSED",
        "WORKFLOW_VERSION_MISMATCH"
    };

    public static IReadOnlySet<string> IssueCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "BROKER_PROVIDER_ACCOUNT_UNLINKED",
        "BROKER_PROVIDER_CAPABILITY_DEGRADED",
        "BROKER_PROVIDER_REQUIRED_CAPABILITY_UNROUTABLE",
        "BROKER_SECURITY_UNRESOLVED",
        "BROKER_STATEMENT_MISSING",
        "BROKER_SYNC_STALE",
        "BROKER_TRANSACTION_TYPE_UNKNOWN",
        "SM_INSTRUMENT_UNRESOLVED",
        "SM_ACCOUNTING_TERMS_INCOMPLETE",
        "SM_ACCOUNTING_CLASSIFICATION_MISSING",
        "SM_ACCRUAL_CASH_FLOW_TERMS_MISSING",
        "SM_COUPON_TERMS_MISSING",
        "SM_DAY_COUNT_MISSING",
        "SM_DIVIDEND_TERMS_MISSING",
        "SM_FACTOR_SCHEDULE_MISSING",
        "SM_IDENTIFIER_CONFLICT",
        "SM_PROVENANCE_INCOMPLETE",
        "SM_PAYMENT_FREQUENCY_MISSING",
        "SM_RATE_RESET_TERMS_MISSING",
        "SM_RATE_RESET_SCHEDULE_MISSING",
        "SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT",
        "SM_VALUATION_SOURCE_MISSING",
        "SM_RECON_SECURITY_UNRESOLVED",
        "SECURITY_ACCOUNTING_RULE_MISSING",
        "SECURITY_SCHEDULE_MISSING",
        "ACCRUAL_ACTUAL_EVENT_MISSING",
        "ACCRUAL_AMOUNT_MISMATCH",
        "ACCRUAL_CLASSIFICATION_MISMATCH",
        "ACCRUAL_DAY_COUNT_MISSING",
        "ACCRUAL_DUPLICATE_RECOGNITION",
        "ACCRUAL_EXPECTED_EVENT_MISSING",
        "ACCRUAL_EXTERNAL_EVIDENCE_MISSING",
        "ACCRUAL_FACTOR_PAYDOWN_MISMATCH",
        "ACCRUAL_FACTOR_STALE",
        "ACCRUAL_LEDGER_POSTING_MISSING",
        "ACCRUAL_RATE_RESET_MISSING",
        "ACCRUAL_TIMING_MISMATCH",
        "FACTOR_PAYDOWN_AMOUNT_MISMATCH",
        "FACTOR_PAYDOWN_CLASSIFICATION_MISMATCH",
        "FACTOR_PAYDOWN_EXTERNAL_CASH_MISSING",
        "FACTOR_PAYDOWN_LEDGER_MISSING",
        "FACTOR_REDUCTION_UNRECONCILED",
        "FACTOR_SCHEDULE_MISSING",
        "FACTOR_STALE",
        "LEDGER_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_SECURITY_MASTER_APPROVAL_MISSING",
        "LEDGER_SECURITY_MASTER_MAPPING_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_EVIDENCE_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_APPROVAL_REQUIRED",
        "LEDGER_LINE_SECURITY_MASTER_ACTIVE_STATUS_REQUIRED",
        "LEDGER_LINE_SECURITY_MASTER_ID_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_ID_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_SYMBOL_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_MAPPING_MISSING",
        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISMATCH",
        "LEDGER_LINE_SECURITY_MASTER_PROVENANCE_MISSING",
        "LEDGER_JOURNAL_SECURITY_MASTER_PROVENANCE_MISMATCH",
        "LEDGER_SECURITY_MASTER_ACCOUNTING_RULE_MISSING",
        "LEDGER_DRAFT_IMBALANCED",
        "LEDGER_PERIOD_CLOSED",
        "LEDGER_DUPLICATE_POSTING_CANDIDATE",
        "RECONCILIATION_CRITICAL_BREAKS_OPEN",
        "REPORT_PACK_NOT_READY",
        "APPROVAL_REQUIRED"
    };

    public static IReadOnlySet<string> AuditEventTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "workflow-started",
        "broker-imported",
        "broker-transactions-normalized",
        "gate-posture-refreshed",
        "security-master-resolved",
        "security-master-override-approved",
        "security-master-field-edit-approved",
        "ledger-draft-built",
        "ledger-draft-validated",
        "ledger-posted",
        "ledger-posting-blocked",
        "reconciliation-run",
        "reconciliation-break-assigned",
        "reconciliation-break-escalated",
        "reconciliation-break-resolved",
        "approval-submitted",
        "approval-approved",
        "approval-rejected",
        "workflow-closed",
        "workflow-reopened"
    };
}

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

[JsonConverter(typeof(JsonStringEnumConverter<OperationsActionOriginDto>))]
public enum OperationsActionOriginDto : byte
{
    HumanOperator = 0,
    AutomationSuggestion = 1,
    AssistantDraft = 2,
    AutomationAssistant = 3
}

public sealed record OperationsStartWorkflowRequestDto(
    Guid FundAccountId,
    string PeriodId,
    Guid? SecurityMasterSnapshotId,
    string? BrokerSource,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    Guid? LedgerBookId = null);

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
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

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
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? ProviderRequiredCapabilityGaps = null,
    IReadOnlyList<string>? ProviderDegradedCapabilityGaps = null);

public sealed record OperationsSecurityMasterResolveRequestDto(
    long ExpectedVersion,
    string Actor,
    string? Rationale = null,
    string? CorrelationId = null,
    int UnresolvedInstrumentCount = 0,
    int OverrideRequestCount = 0,
    bool OverridesApproved = false,
    int MissingAccountingTermCount = 0,
    IReadOnlyList<Guid>? OverrideSecurityIds = null,
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
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    bool HasSecurityMasterApproval = true,
    bool HasLedgerMappings = true);

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
    OperationsLedgerJournalCandidateDto? JournalCandidate = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

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
    OperationsJournalEntryMetadataDto? Metadata = null,
    string? IdempotencyKey = null,
    string? SecurityMasterProvenance = null);

public sealed record OperationsLedgerJournalLineDto(
    Guid? EntryId,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    string? Symbol = null,
    string? FinancialAccountId = null,
    Guid? SecurityId = null,
    bool SecurityMasterApproved = false,
    string? SecurityMasterProvenance = null,
    string? LedgerMappingReference = null,
    string? SecurityMasterApprovalReference = null,
    SecurityStatusDto? SecurityMasterStatus = null,
    LedgerDimensionSetDto? Dimensions = null);

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
    int? ExpectedJournalPreviewCount = null,
    string? SourceRunId = null,
    string? ReconciliationRunId = null,
    Guid? BankEntityId = null,
    decimal? AmountTolerance = null,
    int? MaxAsOfDriftMinutes = null,
    IReadOnlyList<OperationsReconciliationLaneSummaryDto>? ReconciliationLanes = null);

public sealed record OperationsResolveBreakCaseRequestDto(
    long ExpectedVersion,
    string Actor,
    string ResolutionStatus,
    string Rationale,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsAssignBreakCaseRequestDto(
    long ExpectedVersion,
    string Actor,
    string Owner,
    string Rationale,
    string? EscalationLevel = null,
    string? EscalationReason = null,
    DateOnly? DueDate = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsSubmitApprovalRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<OperationsChecklistControlApprovalDto>? ChecklistControlApprovals = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsApprovalDecisionRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReportPackId,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<OperationsChecklistControlApprovalDto>? ChecklistControlApprovals = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsRejectWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Reviewer,
    string Rationale,
    string ReasonCode,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsCloseWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string ReportPackId,
    IReadOnlyList<OperationsChecklistControlApprovalDto>? ChecklistControlApprovals = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    string? ClosePackageId = null,
    string? ClosePackageManifestId = null,
    string? ClosePackageEvidenceHash = null,
    string? ClosePackageRetainedManifestRoute = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    IReadOnlyList<EvidenceDocumentDto>? DocumentSnapshots = null,
    EvidenceManifestDto? ManifestSnapshot = null)
{
    public IReadOnlyList<EvidenceDocumentDto> DocumentSnapshots { get; init; } =
        DocumentSnapshots ?? [];
}

public sealed record OperationsReopenWorkflowRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string IncidentId,
    bool IsGovernedAdmin,
    string? Justification = null,
    string? ApprovalReference = null,
    string? ImpactSummary = null,
    string? CorrelationId = null,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record OperationsChecklistControlApprovalDto(
    string TaskId,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

public sealed record OperationsApprovalPolicyMatrixDto(
    string PolicyId,
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<OperationsApprovalPolicyMatrixRowDto> Rows);

public sealed record OperationsApprovalPolicyMatrixRowDto(
    string PolicyKey,
    string WorkflowArea,
    string Action,
    OperationsGateKeyDto Gate,
    string Trigger,
    string RequiredPermission,
    string SubmitterRole,
    string ReviewerRole,
    int RequiredDistinctApprovals,
    bool RequiresIndependentReviewer,
    bool RequiresReportPack,
    bool RequiresChecklistControlApprovals,
    string EvidenceRequirement,
    string AuditEventType,
    string Route,
    string Severity);

public sealed record OperationsApprovalPolicyRuleUpsertRequestDto(
    string PolicyKey,
    string WorkflowArea,
    string Action,
    OperationsGateKeyDto Gate,
    string Trigger,
    string RequiredPermission,
    string SubmitterRole,
    string ReviewerRole,
    int RequiredDistinctApprovals,
    bool RequiresIndependentReviewer,
    bool RequiresReportPack,
    bool RequiresChecklistControlApprovals,
    string EvidenceRequirement,
    string AuditEventType,
    string Route,
    string Severity,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record OperationsApprovalPolicyRuleUpsertResultDto(
    OperationsApprovalPolicyMatrixRowDto Rule,
    OperationsApprovalPolicyMatrixDto Matrix,
    OperationsApprovalPolicyRuleAuditEventDto AuditEvent);

public sealed record OperationsApprovalPolicyRuleAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Rationale,
    string CorrelationId,
    string PolicyKey,
    string Action,
    OperationsGateKeyDto Gate,
    int RequiredDistinctApprovals,
    bool RequiresIndependentReviewer,
    bool RequiresReportPack,
    bool RequiresChecklistControlApprovals);

public sealed record OperationsCloseCalendarDto(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<OperationsCloseCalendarItemDto> Items);

public sealed record OperationsCloseCalendarItemDto(
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    OperationsWorkflowStatusDto Status,
    long Version,
    DateOnly? NextDueDate,
    string? NextDueTaskId,
    string? NextDueLabel,
    string? NextDueOwner,
    string? ReadinessSeverity,
    int? ReadinessScore,
    bool IsReadyToClose,
    int BlockerCount,
    int OpenChecklistCount,
    int RequiredApprovalCount,
    int CompletedApprovalCount,
    string Route,
    IReadOnlyList<OperationsCloseReadinessComponentDto>? ReadinessComponents = null,
    IReadOnlyList<OperationsCloseReadinessBlockerDto>? ReadinessBlockers = null,
    IReadOnlyList<OperationsNextActionDto>? ReadinessNextActions = null);

public sealed record OperationsCloseCalendarItemUpsertRequestDto(
    Guid WorkflowId,
    string TaskId,
    DateOnly DueDate,
    string Owner,
    string RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record OperationsCloseCalendarItemUpsertResultDto(
    OperationsCloseCalendarItemDto Item,
    OperationsCloseCalendarDto Calendar,
    OperationsCloseCalendarItemAuditEventDto AuditEvent);

public sealed record OperationsCloseCalendarItemAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Rationale,
    string CorrelationId,
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    string TaskId,
    DateOnly DueDate,
    string Owner);

public sealed record OperationsTransitionResultDto(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    OperationsContinuityWorkflowDto? Workflow,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    long? NewVersion = null,
    OperationsCloseReadinessDto? CloseReadiness = null);

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
    IReadOnlyList<OperationsNextActionDto> NextActions,
    Guid? LedgerBookId = null);

public sealed record OperationsReviewedAutomationSummaryDto(
    string SummaryId,
    string Stage,
    EvidenceStatusDto Status,
    bool RequiresHumanReview,
    string Summary,
    IReadOnlyList<string> AllowedUseCases,
    IReadOnlyList<string> ProhibitedActions,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null,
    IReadOnlyList<OperationsReviewedAutomationArtifactDto>? Artifacts = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];

    public IReadOnlyList<OperationsReviewedAutomationArtifactDto> Artifacts { get; init; } =
        Artifacts ?? [];
}

public sealed record OperationsReviewedAutomationArtifactDto(
    string ArtifactId,
    string ArtifactKind,
    string Title,
    EvidenceStatusDto Status,
    bool RequiresHumanReview,
    decimal? ConfidencePercent,
    string SourceSummary,
    string SuggestedOperatorAction,
    string BlockedMaterialAction,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? ReviewChecklist = null)
{
    public IReadOnlyList<string> ReviewChecklist { get; init; } =
        ReviewChecklist ?? [];
}

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
    IReadOnlyList<OperationsCloseChecklistTaskDto> CloseChecklist,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<OperationsWorkflowBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    OperationsCloseReadinessDto? CloseReadiness = null,
    OperationsClosePackagePublicationDto? ClosePackage = null,
    OperationsAccountingRecordSummaryDto? AccountingRecordSummary = null,
    IReadOnlyList<OperationsReconciliationLaneSummaryDto>? ReconciliationLanes = null,
    OperationsDashboardSummaryDto? DashboardSummary = null,
    IReadOnlyList<OperationsEvidencePackageSummaryDto>? EvidencePackages = null,
    OperationsReviewedAutomationSummaryDto? ReviewedAutomation = null,
    Guid? LedgerBookId = null)
{
    public IReadOnlyList<OperationsEvidencePackageSummaryDto> EvidencePackages { get; init; } =
        EvidencePackages ?? [];
}

public sealed record OperationsDashboardSummaryDto(
    string DashboardId,
    string Stage,
    EvidenceStatusDto Status,
    bool IsReady,
    int ReadyMetricCount,
    int TotalMetricCount,
    string Summary,
    IReadOnlyList<OperationsDashboardMetricDto> Metrics,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record OperationsDashboardMetricDto(
    string MetricId,
    string Label,
    string Value,
    EvidenceStatusDto Status,
    string Detail,
    string? RouteHint,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record OperationsEvidencePackageSummaryDto(
    string PackageId,
    string Label,
    EvidenceStatusDto Status,
    bool IsReady,
    string Summary,
    string? RouteHint,
    int CompleteCategoryCount,
    int RequiredCategoryCount,
    int EvidenceLinkCount,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record OperationsReconciliationLaneSummaryDto(
    string LaneId,
    string Label,
    OperationsReconciliationLaneStatusDto Status,
    bool IsReady,
    int BreakCount,
    string Summary,
    string? RouteHint,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null);

public sealed record OperationsAccountingRecordSummaryDto(
    string RecordId,
    bool IsAuditReady,
    int CompleteCategoryCount,
    int RequiredCategoryCount,
    string Summary,
    IReadOnlyList<OperationsAccountingRecordEvidenceCategoryDto> EvidenceCategories,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    FundAuditPackReadinessDto? AuditPackReadiness = null);

public sealed record OperationsAccountingRecordEvidenceCategoryDto(
    string Key,
    string Label,
    bool IsComplete,
    string Status,
    string? RouteHint,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredEvidence = null);

public sealed record OperationsCloseChecklistTaskDto(
    string TaskId,
    OperationsGateKeyDto Gate,
    string Label,
    string Owner,
    string RequiredEvidence,
    int RequiredApprovalCount,
    DateOnly? ExpiresOn,
    DateOnly? DueDate,
    string Status,
    string? BlockingReason,
    string? EvidencePointer,
    string? RemediationRoute,
    bool CanAcknowledge,
    DateTimeOffset? AcknowledgedAtUtc,
    string? AcknowledgedBy);

public sealed record OperationsClosePackagePublicationDto(
    string ClosePackageId,
    string ReportPackId,
    string RetainedManifestId,
    string RetainedManifestRoute,
    string EvidenceHash,
    DateTimeOffset PublishedAtUtc,
    string PublishedBy,
    string SignOffRationale,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<OperationsChecklistControlApprovalDto> ChecklistControlApprovals,
    IReadOnlyList<EvidenceDocumentDto>? DocumentSnapshots = null,
    EvidenceManifestDto? ManifestSnapshot = null)
{
    public IReadOnlyList<EvidenceDocumentDto> DocumentSnapshots { get; init; } =
        DocumentSnapshots ?? [];
}

public sealed record OperationsChecklistAcknowledgeRequestDto(
    long ExpectedVersion,
    string Actor,
    string Rationale,
    string? CorrelationId = null);

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
    OperationsContinuityCorrelationKeysDto? CorrelationKeys,
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
    OperationsContinuityCorrelationKeysDto? CorrelationKeys,
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
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    OperationsContinuityCorrelationKeysDto? CorrelationKeys = null,
    string? EscalationLevel = null,
    string? EscalationReason = null,
    DateTimeOffset? EscalatedAtUtc = null,
    string? SlaState = null,
    DateTimeOffset? SlaDueAtUtc = null,
    decimal? Materiality = null,
    string? RootCauseCode = null,
    string? ApprovalState = null,
    IReadOnlyList<string>? BlockedOutputs = null);

public sealed record OperationsContinuityCorrelationKeysDto(
    string? RunId = null,
    Guid? FundAccountId = null,
    string? PortfolioSnapshotId = null,
    string? LedgerBatchId = null,
    string? LedgerPostingGroupId = null,
    string? ReconciliationCaseId = null);

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


public sealed record OperationsCloseReadinessDto(
    bool IsReadyToClose,
    string Severity,
    int Score,
    IReadOnlyList<OperationsCloseReadinessComponentDto> Components,
    IReadOnlyList<OperationsCloseReadinessBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions);

public sealed record OperationsCloseReadinessComponentDto(
    string Key,
    string Label,
    int Score,
    int Weight,
    bool IsReady,
    string Severity,
    string? BlockingReason,
    OperationsGateKeyDto? Gate,
    string? RouteHint);

public sealed record OperationsCloseReadinessBlockerDto(
    string Code,
    string Category,
    string Severity,
    string Message,
    OperationsGateKeyDto? Gate,
    string? RouteHint);

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
    OperationsGateKeyDto? Gate)
{
    public string? RouteHint { get; init; } = Route;
}

public sealed record OperationsEvidenceLinkDto(
    string EvidenceId,
    string Label,
    string? Route,
    string? Source,
    DateTimeOffset? CapturedAtUtc);

public sealed record PrivateCapitalCloseCockpitWorkflowDto(
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    OperationsWorkflowStatusDto Status,
    int CloseReadinessScore,
    bool IsReadyToClose,
    string WorkflowRoute,
    string? ClosePackageId,
    string? ClosePackageRoute,
    int BlockerCount,
    int OpenChecklistCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record PrivateCapitalCloseCockpitLaneDto(
    string LaneId,
    string Label,
    EvidenceStatusDto Status,
    bool IsReady,
    string Summary,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalCloseCockpitApprovalDto(
    string ApprovalId,
    Guid WorkflowId,
    Guid FundAccountId,
    string PeriodId,
    OperationsApprovalStateDto Status,
    string? Operator,
    string? Reviewer,
    string? Rationale,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string WorkflowRoute,
    int EvidenceLinkCount,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

public sealed record PrivateCapitalNavSupportComponentDto(
    string ComponentId,
    string Label,
    EvidenceStatusDto Status,
    bool IsReady,
    string Summary,
    string? Route,
    int Score);

public sealed record PrivateCapitalShadowNavTieOutDto(
    string TieOutId,
    EvidenceStatusDto Status,
    bool IsReady,
    decimal? MeridianShadowNav,
    decimal? AdministratorNav,
    decimal? Variance,
    decimal Tolerance,
    string? Currency,
    string Summary,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalNavSupportPackageDto(
    string PackageId,
    string Label,
    EvidenceStatusDto Status,
    bool IsReady,
    string Summary,
    string? Route,
    decimal? ShadowNav,
    string? Currency,
    int EvidenceLinkCount,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<PrivateCapitalNavSupportComponentDto> Components,
    IReadOnlyList<string>? RequiredActions = null,
    PrivateCapitalShadowNavTieOutDto? ShadowNavTieOut = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalCloseCockpitDto(
    string? FundProfileId,
    Guid? LedgerBookId,
    Guid? FundAccountId,
    string? PeriodId,
    string? EntityId,
    DateTimeOffset ProjectedAtUtc,
    string CockpitRoute,
    EvidenceStatusDto OverallStatus,
    bool IsReadyToClose,
    int ReadinessScore,
    int WorkflowCount,
    int FundEventCount,
    int CapitalAccountCount,
    int ReportOutputCount,
    int DeliveredReportOutputCount,
    int ReadyLaneCount,
    int BlockedLaneCount,
    IReadOnlyList<PrivateCapitalCloseCockpitLaneDto> Lanes,
    IReadOnlyList<PrivateCapitalCloseCockpitWorkflowDto> Workflows,
    IReadOnlyList<OperationsCloseReadinessBlockerDto> Blockers,
    IReadOnlyList<OperationsNextActionDto> NextActions,
    IReadOnlyList<string> LiveCapabilities,
    IReadOnlyList<string> PlannedCapabilities,
    IReadOnlyList<PrivateCapitalCloseCockpitApprovalDto>? ApprovalHistory = null,
    IReadOnlyList<PrivateCapitalNavSupportPackageDto>? NavSupportPackages = null,
    IReadOnlyList<OperationsEvidencePackageSummaryDto>? EvidencePackages = null)
{
    public IReadOnlyList<PrivateCapitalCloseCockpitApprovalDto> ApprovalHistory { get; init; } =
        ApprovalHistory ?? [];

    public IReadOnlyList<PrivateCapitalNavSupportPackageDto> NavSupportPackages { get; init; } =
        NavSupportPackages ?? [];

    public IReadOnlyList<OperationsEvidencePackageSummaryDto> EvidencePackages { get; init; } =
        EvidencePackages ?? [];
}

public interface IPrivateCapitalCloseCockpitService
{
    Task<PrivateCapitalCloseCockpitDto> GetCockpitAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        Guid? fundAccountId = null,
        string? periodId = null,
        string? entityId = null,
        CancellationToken ct = default);
}
