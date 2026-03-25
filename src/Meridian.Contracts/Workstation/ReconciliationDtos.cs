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
    IReadOnlyList<BankTransactionDto>? BankTransactions = null);
