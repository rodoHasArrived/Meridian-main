using Meridian.Contracts.Banking;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Source type participating in a reconciliation comparison.
/// </summary>
public enum ReconciliationSourceKind : byte
{
    Unknown = 0,
    Portfolio = 1,
    Ledger = 2,
    Bank = 3
}

/// <summary>
/// Current workflow state for a reconciliation break.
/// </summary>
public enum ReconciliationBreakStatus : byte
{
    Open = 0,
    Matched = 1,
    Investigating = 2,
    Resolved = 3
}

/// <summary>
/// Canonical classification for reconciliation outcomes.
/// </summary>
public enum ReconciliationBreakCategory : byte
{
    AmountMismatch = 0,
    MissingLedgerCoverage = 1,
    MissingPortfolioCoverage = 2,
    ClassificationGap = 3,
    TimingMismatch = 4,
    MissingBankCoverage = 5
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
    int BankBreakCount = 0);

/// <summary>
/// Successful comparison row emitted by the reconciliation engine.
/// </summary>
public sealed record ReconciliationMatchDto(
    string CheckId,
    string Label,
    ReconciliationSourceKind ExpectedSource,
    ReconciliationSourceKind ActualSource,
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
    ReconciliationSourceKind MissingSource,
    decimal? ExpectedAmount,
    decimal? ActualAmount,
    decimal Variance,
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
    string Reason);

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
    /// Security Master classification keyed by ticker symbol, populated for every symbol whose
    /// Security Master entry was resolved at reconciliation time. Suitable for audit reporting.
    /// </summary>
    IReadOnlyDictionary<string, SecurityClassificationSummaryDto>? SecurityClassifications = null);

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
    string? ResolutionNote = null);

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
    string ResolutionNote);
