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
    DateTimeOffset? ActualAsOf);

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
    ReceiveCashInterest = 1,
    RecognizePrincipalPaydown = 2
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
    IReadOnlyList<BankTransactionDto>? BankTransactions = null,
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
    Dismissed = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationCaseLifecycleState>))]
public enum ReconciliationCaseLifecycleState : byte
{
    Opened = 0,
    Triaged = 1,
    Calibrated = 2,
    Approved = 3,
    Escalated = 4,
    Closed = 5,
    Superseded = 6
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
    ReconciliationCaseLifecycleState LifecycleState = ReconciliationCaseLifecycleState.Opened,
    string? LifecycleRationale = null,
    string? ExternalAccountId = null,
    string? CustodianId = null,
    string? UpstreamSyncCursor = null,
    DateTimeOffset? LastUpstreamSyncAt = null,
    IReadOnlyList<ReconciliationCaseSignoffRecord>? SignoffHistory = null,
    IReadOnlyList<ReconciliationCaseStateTransition>? StateTransitions = null);

public sealed record ReconciliationCaseSignoffRecord(
    string Actor,
    string Role,
    string Decision,
    string? Note,
    DateTimeOffset SignedAt,
    string? InvalidatedBySyncCursor = null,
    DateTimeOffset? InvalidatedAt = null);

public sealed record ReconciliationCaseStateTransition(
    string TransitionId,
    ReconciliationCaseLifecycleState From,
    ReconciliationCaseLifecycleState To,
    string Actor,
    string? Rationale,
    DateTimeOffset OccurredAt);

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
    string? ReviewNote = null);

/// <summary>
/// Request to resolve or dismiss a break with audit metadata.
/// </summary>
public sealed record ResolveReconciliationBreakRequest(
    string BreakId,
    ReconciliationBreakQueueStatus Status,
    string ResolvedBy,
    string ResolutionNote,
    string OperatorRationale);
