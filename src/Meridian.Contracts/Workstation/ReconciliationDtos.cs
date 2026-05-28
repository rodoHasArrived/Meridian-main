using System.Text.Json.Serialization;
using Meridian.Contracts.Banking;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Source type participating in a reconciliation comparison.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationSourceKind>))]
public enum ReconciliationSourceKind : byte
{
    Unknown = 0,
    Portfolio = 1,
    Ledger = 2,
    Bank = 3,
    ExternalStatement = Bank,
    Cash = 4
}

/// <summary>
/// Current workflow state for a reconciliation break.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakStatus>))]
public enum ReconciliationBreakStatus : byte
{
    Open = 0,
    Matched = 1,
    Investigating = 2,
    Resolved = 3,
    PartialMatch = 4
}

/// <summary>
/// Materiality level for a reconciliation break.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakSeverity>))]
public enum ReconciliationBreakSeverity : byte
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Canonical classification for reconciliation outcomes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBreakCategory>))]
public enum ReconciliationBreakCategory : byte
{
    AmountMismatch = 0,
    MissingLedgerCoverage = 1,
    MissingPortfolioCoverage = 2,
    ClassificationGap = 3,
    TimingMismatch = 4,
    MissingBankCoverage = 5,
    CashMismatch = 6,
    MissingCashCoverage = 7,
    MissingExternalStatementCoverage = 8,
    ExternalStatementMismatch = 9,
    PartialMatch = 10
}

/// <summary>
/// Request to create a reconciliation run for a recorded strategy run.
/// </summary>
public sealed record ReconciliationRunRequest(
    string RunId,
    decimal AmountTolerance = 0.01m,
    int MaxAsOfDriftMinutes = 5,
    /// <summary>
    /// Optional banking entity identifier.  When provided, bank transactions for this
    /// entity are fetched and included as additional reconciliation checks alongside
    /// the portfolio/ledger comparison.
    /// </summary>
    Guid? BankEntityId = null);

/// <summary>
/// Summary of a completed reconciliation run.
/// </summary>
public sealed record ReconciliationRunSummary(
    string ReconciliationRunId,
    string RunId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PortfolioAsOf,
    DateTimeOffset? LedgerAsOf,
    int MatchCount,
    int BreakCount,
    int OpenBreakCount,
    bool HasTimingDrift,
    decimal AmountTolerance,
    int MaxAsOfDriftMinutes,
    int SecurityIssueCount = 0,
    bool HasSecurityCoverageIssues = false,
    int BankTransactionCount = 0,
    int BankBreakCount = 0,
    int ExpectedAccountingEventCount = 0,
    int ExpectedJournalPreviewCount = 0,
    int SecurityMasterAccountingIssueCount = 0,
    bool HasSecurityMasterAccountingIssues = false);

/// <summary>
/// Successful comparison row emitted by the reconciliation engine.
/// </summary>
public sealed record ReconciliationMatchDto(
    string CheckId,
    string Label,
    string ExpectedSource,
    string ActualSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
    DateTimeOffset? ExpectedAsOf,
    DateTimeOffset? ActualAsOf);

/// <summary>
/// Break row emitted by the reconciliation engine.
/// </summary>
public sealed record ReconciliationBreakDto(
    string CheckId,
    string Label,
    ReconciliationBreakCategory Category,
    ReconciliationBreakStatus Status,
    string MissingSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
    ReconciliationBreakSeverity Severity,
    string Reason,
    DateTimeOffset? ExpectedAsOf,
    DateTimeOffset? ActualAsOf,
    OperationsContinuityCorrelationKeysDto? CorrelationKeys = null);

/// <summary>
/// Security Master coverage issue attached to a reconciliation run.
/// </summary>
public sealed record ReconciliationSecurityCoverageIssueDto(
    string Source,
    string Symbol,
    string? AccountName,
    string Reason,
    string? Code = null,
    ReconciliationBreakSeverity Severity = ReconciliationBreakSeverity.Medium,
    string? EvidenceLink = null);

/// <summary>
/// Security Master accounting event type generated from instrument terms and schedules.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExpectedAccountingEventKindDto>))]
public enum ExpectedAccountingEventKindDto : byte
{
    AccrueInterestIncome = 0,
    ReversePriorAccrual = 1,
    RecognizeCouponIncome = 2,
    ReceiveCashInterest = 3,
    AmortizePremium = 4,
    AccreteDiscount = 5,
    RecognizePrincipalPaydown = 6,
    ReduceCostBasisForFactorPaydown = 7,
    RecognizeRealizedGainLoss = 8,
    RecordMaturityProceeds = 9,
    RecordCallProceeds = 10,
    RecordDividendReceivable = 11,
    RecordDividendIncome = 12,
    RecordFxRemeasurement = 13
}

/// <summary>
/// Deterministic input snapshot used to generate an expected accounting event.
/// </summary>
public sealed record AccrualInputSnapshotDto(
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal ParAmount,
    decimal? CouponRate,
    string? CouponType,
    string? DayCountConvention,
    int? PaymentFrequencyPerYear,
    decimal? PriorFactor,
    decimal? CurrentFactor,
    string SourceHash);

/// <summary>
/// Result of a Security Master accrual calculation.
/// </summary>
public sealed record AccrualCalculationResultDto(
    string EventId,
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly AccrualStartDate,
    DateOnly AccrualEndDate,
    int AccrualDays,
    decimal DayCountFraction,
    decimal AccruedAmount,
    string Currency,
    AccrualInputSnapshotDto InputSnapshot);

/// <summary>
/// Expected accounting event generated from Security Master accounting rules.
/// </summary>
public sealed record ExpectedAccountingEventDto(
    string EventId,
    ExpectedAccountingEventKindDto EventKind,
    Guid SecurityId,
    string Symbol,
    string AccountId,
    DateOnly EventDate,
    decimal ExpectedAmount,
    decimal PrincipalAmount,
    decimal IncomeAmount,
    string Currency,
    string IdempotencyKey,
    string Provenance,
    AccrualInputSnapshotDto InputSnapshot);

/// <summary>
/// Preview line for a balanced journal candidate generated from an expected event.
/// </summary>
public sealed record ExpectedJournalPreviewLineDto(
    string AccountName,
    string AccountType,
    string? Symbol,
    decimal Debit,
    decimal Credit);

/// <summary>
/// Balanced journal preview candidate. Posting remains a separate approval-gated workflow.
/// </summary>
public sealed record ExpectedJournalPreviewDto(
    string JournalPreviewId,
    string ExpectedEventId,
    string Description,
    DateOnly EventDate,
    bool IsBalanced,
    bool RequiresOperatorApproval,
    string IdempotencyKey,
    IReadOnlyList<ExpectedJournalPreviewLineDto> Lines);

/// <summary>
/// Structured posture or reconciliation issue from Security Master-driven accounting.
/// </summary>
public sealed record SecurityMasterAccountingIssueDto(
    string Code,
    string Source,
    string Symbol,
    string? AccountId,
    string Reason,
    ReconciliationBreakSeverity Severity,
    string? EvidenceLink = null,
    decimal? ExpectedAmount = null,
    decimal? ActualAmount = null);

/// <summary>
/// Full detail payload for a single reconciliation run.
/// </summary>
public sealed record ReconciliationRunDetail(
    ReconciliationRunSummary Summary,
    IReadOnlyList<ReconciliationMatchDto> Matches,
    IReadOnlyList<ReconciliationBreakDto> Breaks,
    IReadOnlyList<ReconciliationSecurityCoverageIssueDto>? SecurityCoverageIssues = null,
    /// <summary>
    /// Security Master classification keyed by ticker symbol, populated for every
    /// symbol resolved at reconciliation time from the shared workstation instrument layer.
    /// Suitable for governance and audit reporting.
    /// </summary>
    IReadOnlyDictionary<string, SecurityClassificationSummaryDto>? SecurityClassifications = null,
    IReadOnlyList<ExpectedAccountingEventDto>? ExpectedAccountingEvents = null,
    IReadOnlyList<AccrualCalculationResultDto>? AccrualCalculations = null,
    IReadOnlyList<ExpectedJournalPreviewDto>? ExpectedJournalPreviews = null,
    IReadOnlyList<SecurityMasterAccountingIssueDto>? SecurityMasterAccountingIssues = null);

/// <summary>
/// Operator queue state for a reconciliation break.
/// </summary>
public enum ReconciliationBreakQueueStatus : byte
{
    Open = 0,
    InReview = 1,
    Resolved = 2,
    Dismissed = 3,
    Investigating = 4,
    AwaitingEvidence = 5,
    SignedOff = 6,
    Reopened = 7,
    LegacyTerminal = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseLifecycleState>))]
public enum ReconciliationCaseLifecycleState : byte
{
    Open = 0,
    Investigating = 1,
    InReview = 1,
    AwaitingEvidence = 2,
    Resolved = 3,
    SignedOff = 4,
    Reopened = 5,
    LegacyTerminal = 6,
    AwaitingApproval = 7,
    Approved = 8,
    Posted = 9,
    Superseded = 10
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCasePriority>))]
public enum ReconciliationCasePriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationSlaState>))]
public enum ReconciliationSlaState : byte
{
    NotStarted = 0,
    Running = 1,
    Warning = 2,
    Breached = 3,
    Paused = 4,
    Stopped = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseAgeBand>))]
public enum ReconciliationCaseAgeBand : byte
{
    SameDay = 0,
    OneToTwoBusinessDays = 1,
    ThreeToFiveBusinessDays = 2,
    OlderThanFiveBusinessDays = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCommentVisibility>))]
public enum ReconciliationCommentVisibility : byte
{
    Internal = 0,
    CloseEvidence = 1,
    ExternalSummary = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationBulkActionType>))]
public enum ReconciliationBulkActionType : byte
{
    Assign = 0,
    ChangePriority = 1,
    AddComment = 2,
    TransitionStatus = 3,
    SetRootCause = 4,
    SetResolution = 5,
    Resolve = 6,
    SignOff = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationSlaStopPolicy>))]
public enum ReconciliationSlaStopPolicy : byte
{
    StopOnResolved = 0,
    StopOnSignedOff = 1
}

/// <summary>
/// Aggregate readiness state for reconciliation tolerance calibration and governance sign-off.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCalibrationStatusDto>))]
public enum ReconciliationCalibrationStatusDto : byte
{
    Ready = 0,
    ReviewRequired = 1,
    Blocked = 2
}

/// <summary>
/// Work item shown in the reconciliation break queue.
/// </summary>
public sealed record ReconciliationBreakQueueItem(
    string BreakId,
    string RunId,
    string StrategyName,
    ReconciliationBreakCategory Category,
    ReconciliationBreakQueueStatus Status,
    decimal Variance,
    string Reason,
    string? AssignedTo,
    DateTimeOffset DetectedAt,
    DateTimeOffset LastUpdatedAt,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAt = null,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAt = null,
    string? ResolutionNote = null,
    ReconciliationBreakSeverity Severity = ReconciliationBreakSeverity.Medium,
    string? ExceptionRoute = null,
    string? ToleranceProfileId = null,
    decimal? ToleranceBand = null,
    string? RequiredSignoffRole = null,
    string? SignoffStatus = null,
    string? FundAccountId = null,
    string? ExplainabilitySummary = null,
    string? RoutingTarget = null,
    string? RoutingDetail = null,
    string? RecommendedAction = null,
    ReconciliationCaseLifecycleState LifecycleState = ReconciliationCaseLifecycleState.Open,
    string? LifecycleRationale = null,
    string? ExternalAccountId = null,
    string? CustodianId = null,
    string? UpstreamSyncCursor = null,
    DateTimeOffset? LastUpstreamSyncAt = null,
    IReadOnlyList<ReconciliationCaseSignoffRecord>? SignoffHistory = null,
    IReadOnlyList<ReconciliationCaseStateTransition>? StateTransitions = null,
    string? Team = null,
    string? Counterparty = null,
    ReconciliationBreakScore? Score = null,
    DateTimeOffset? SlaDueAt = null,
    bool SlaBreached = false,
    string? AssigneeId = null,
    string? AssigneeDisplayName = null,
    string? AssignedBy = null,
    DateTimeOffset? AssignedAt = null,
    ReconciliationCasePriority Priority = ReconciliationCasePriority.Normal,
    string? SlaPolicyId = null,
    DateTimeOffset? SlaWarningAt = null,
    DateTimeOffset? SlaBreachedAt = null,
    ReconciliationSlaState SlaState = ReconciliationSlaState.NotStarted,
    ReconciliationCaseAgeBand AgeBand = ReconciliationCaseAgeBand.SameDay,
    double BusinessAgeHours = 0,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    string? SignOffBy = null,
    DateTimeOffset? SignOffAt = null,
    string? SignOffNote = null,
    string? ReopenedBy = null,
    DateTimeOffset? ReopenedAt = null,
    string? ReopenReason = null,
    int Version = 0,
    int CommentCount = 0,
    int EvidenceCount = 0,
    DateTimeOffset? LastActivityAt = null,
    IReadOnlyList<ReconciliationCaseComment>? Comments = null,
    IReadOnlyList<string>? EvidenceLinks = null);


public sealed record ReconciliationCaseComment(
    string CommentId,
    string BreakId,
    string? ParentCommentId,
    string AuthorId,
    string? AuthorDisplayName,
    string Body,
    ReconciliationCommentVisibility Visibility,
    IReadOnlyList<string>? EvidenceLinks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt = null,
    string? EditedBy = null,
    DateTimeOffset? DeletedAt = null,
    string? DeletedBy = null,
    string? DeleteReason = null,
    int Version = 0);

public sealed record ReconciliationSlaPolicy(
    string PolicyId,
    string? FundAccountId,
    string? BreakType,
    ReconciliationBreakSeverity? Severity,
    ReconciliationCasePriority? Priority,
    string TimeZoneId,
    int DueBusinessHours,
    int WarningBusinessHoursBeforeDue,
    bool PauseAwaitingEvidenceWithRequest,
    ReconciliationSlaStopPolicy StopPolicy,
    IReadOnlyList<DayOfWeek>? BusinessDays = null,
    TimeOnly? BusinessDayStart = null,
    TimeOnly? BusinessDayEnd = null);

public sealed record ReconciliationCaseworkReadinessDto(
    int UnresolvedCriticalCaseCount,
    int BreachedCaseCount,
    int AwaitingEvidenceCaseCount,
    int SignedOffCaseEvidenceCount,
    IReadOnlyList<string> ReportPackExceptionSummary);

public sealed record ReconciliationCaseSignoffRecord(
    string Actor,
    string Role,
    string Decision,
    string? Note,
    DateTimeOffset SignedAt,
    string? InvalidatedBySyncCursor = null,
    DateTimeOffset? InvalidatedAt = null);



public sealed record ReconciliationBreakScore(
    int SeverityScore,
    int PriorityScore,
    decimal MaterialityComponent,
    double AgeHours,
    int CounterpartyCriticalityComponent,
    int RecurringPatternComponent,
    bool IsHighPriority,
    DateTimeOffset? SlaDueAt = null,
    DateTimeOffset? SlaBreachAt = null);

public sealed record ReconciliationCaseStateTransition(
    string TransitionId,
    ReconciliationCaseLifecycleState From,
    ReconciliationCaseLifecycleState To,
    string Actor,
    string? Rationale,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string>? EvidenceReferences = null,
    string? PreviousHash = null,
    string? EntryHash = null);

public enum ReconciliationCaseTransitionAction : byte
{
    StartReview = 0,
    RequestApproval = 1,
    Approve = 2,
    Post = 3,
    Reopen = 4,
    Supersede = 5,
    Assign = 6,
    ChangePriority = 7,
    TransitionStatus = 8,
    AwaitEvidence = 9,
    Resolve = 10,
    SignOff = 11,
    SetRootCause = 12,
    SetResolution = 13,
    AddComment = 14,
    EditComment = 15,
    DeleteComment = 16,
    LinkEvidence = 17
}

public sealed record ReconciliationCaseTransitionCommand(
    string BreakId,
    ReconciliationCaseTransitionAction Action,
    string Actor,
    string Reason,
    IReadOnlyList<string> EvidenceReferences,
    string? Role = null,
    string? SupersedingBreakId = null);

/// <summary>
/// Per-profile rollup for reconciliation tolerance calibration and exception routing.
/// </summary>
public sealed record ReconciliationCalibrationProfileSummaryDto(
    string ToleranceProfileId,
    string ExceptionRoute,
    ReconciliationBreakSeverity HighestSeverity,
    decimal? MaxToleranceBand,
    int TotalBreakCount,
    int OpenBreakCount,
    int InReviewBreakCount,
    int ResolvedBreakCount,
    int DismissedBreakCount,
    int PendingSignoffCount,
    int SignedOffCount,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// Operator-facing calibration summary for the reconciliation break queue.
/// </summary>
public sealed record ReconciliationCalibrationSummaryDto(
    DateTimeOffset AsOf,
    ReconciliationCalibrationStatusDto Status,
    string Summary,
    int TotalBreakCount,
    int ActiveBreakCount,
    int OpenBreakCount,
    int InReviewBreakCount,
    int ResolvedBreakCount,
    int DismissedBreakCount,
    int CriticalOpenBreakCount,
    int PendingSignoffCount,
    int SignedOffCount,
    int MissingCalibrationMetadataCount,
    IReadOnlyList<ReconciliationCalibrationProfileSummaryDto> Profiles);

/// <summary>
/// Request to move a break into active review and assign an operator.
/// </summary>
public sealed record ReviewReconciliationBreakRequest(
    string BreakId,
    string AssignedTo,
    string ReviewedBy,
    string? ReviewNote = null,
    string? Team = null);

/// <summary>
/// Request to resolve or dismiss a break with audit metadata.
/// </summary>
public sealed record ResolveReconciliationBreakRequest(
    string BreakId,
    ReconciliationBreakQueueStatus Status,
    string ResolvedBy,
    string ResolutionNote,
    string OperatorRationale,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    int? ExpectedVersion = null);

/// <summary>
/// Canonical schema version metadata for reconciliation ingress/egress payloads.
/// </summary>
public sealed record ReconciliationSchemaVersion(
    string ContractName,
    int Major,
    int Minor,
    int Patch,
    string ContentType = "application/vnd.meridian.reconciliation+json");

/// <summary>
/// Cross-workflow correlation metadata stamped on every reconciliation payload.
/// </summary>
public sealed record ReconciliationCorrelationContext(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string WorkflowId,
    string? JobId = null);

/// <summary>
/// Canonical envelope for inbound/outbound reconciliation payloads.
/// </summary>
public sealed record ReconciliationPayloadEnvelope<TPayload>(
    string PayloadId,
    ReconciliationSchemaVersion Schema,
    ReconciliationCorrelationContext Correlation,
    DateTimeOffset CreatedAt,
    string Producer,
    string Direction,
    string? IdempotencyKey,
    TPayload Payload);

/// <summary>
/// Queue orchestration controls and replay metadata for resilient reconciliation jobs.
/// </summary>
public sealed record ReconciliationJobControl(
    string JobId,
    string IdempotencyKey,
    int Attempt,
    int MaxAttempts,
    bool DeadLettered,
    string? DeadLetterReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    string BackpressureBucket);

/// <summary>
/// Processing telemetry summary for SLA and throughput observability.
/// </summary>
public sealed record ReconciliationProcessingTelemetry(
    double MatchRate,
    double BreakRate,
    int SlaMissCount,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs);

/// <summary>
/// Scoped rollout flags for phased reconciliation releases.
/// </summary>
public sealed record ReconciliationRolloutFlags(
    bool Enabled,
    IReadOnlyList<string> ClientIds,
    IReadOnlyList<string> TeamIds,
    IReadOnlyList<string> CounterpartyIds,
    bool AllowReplay,
    bool AllowBackfill);


public sealed record ReconciliationAssignRequest(
    string BreakId,
    string Assignee,
    string? AssigneeDisplayName,
    string Reason,
    int? ExpectedVersion = null);

public sealed record ReconciliationPriorityRequest(
    string BreakId,
    ReconciliationCasePriority Priority,
    string Reason,
    int? ExpectedVersion = null);

public sealed record ReconciliationStatusTransitionRequest(
    string BreakId,
    ReconciliationCaseLifecycleState Status,
    string Reason,
    IReadOnlyList<string>? EvidenceLinks = null,
    int? ExpectedVersion = null);

public sealed record ReconciliationCommentMutationRequest(
    string BreakId,
    string? CommentId,
    string? ParentCommentId,
    string Body,
    ReconciliationCommentVisibility Visibility,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? Reason = null,
    int? ExpectedVersion = null);

public sealed record ReconciliationTaxonomyRequest(
    string BreakId,
    string Code,
    string? Note,
    string Reason,
    int? ExpectedVersion = null);

public sealed record ReconciliationSignOffRequest(
    string BreakId,
    string Note,
    IReadOnlyList<string>? EvidenceLinks = null,
    int? ExpectedVersion = null);

public sealed record ReconciliationReopenRequest(
    string BreakId,
    string Reason,
    bool Privileged = false,
    int? ExpectedVersion = null);

public sealed record ReconciliationBulkActionRequest(
    IReadOnlyList<string> BreakIds,
    ReconciliationBulkActionType Action,
    string IdempotencyKey,
    bool DryRun,
    bool AllowPartialSuccess,
    int MaxCaseCount = 100,
    string? Assignee = null,
    ReconciliationCasePriority? Priority = null,
    ReconciliationCaseLifecycleState? Status = null,
    string? Comment = null,
    string? RootCauseCode = null,
    string? ResolutionCode = null,
    string? ResolutionNote = null,
    string? Reason = null,
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record ReconciliationBulkCaseResult(
    string BreakId,
    bool Success,
    string? Error,
    ReconciliationBreakQueueItem? Item = null);

public sealed record ReconciliationBulkActionResult(
    string BulkActionId,
    string IdempotencyKey,
    bool DryRun,
    bool Accepted,
    int RequestedCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<ReconciliationBulkCaseResult> Cases);
