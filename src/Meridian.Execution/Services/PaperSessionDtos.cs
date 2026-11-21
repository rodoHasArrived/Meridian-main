using Meridian.Execution.Models;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>Request to create a new paper session.</summary>
public sealed record CreatePaperSessionDto(
    string StrategyId,
    string? StrategyName,
    decimal InitialCash = 100_000m,
    IReadOnlyList<string>? Symbols = null)
{
    /// <summary>Optional server-authorized financial account for a native simulation.</summary>
    public string? FinancialAccountId { get; init; }
}

/// <summary>Session summary DTO.</summary>
public sealed record PaperSessionSummaryDto(
    string SessionId,
    string StrategyId,
    string? StrategyName,
    decimal InitialCash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    bool IsActive,
    string? MatchingModelVersion = null,
    string? CostModelVersion = null)
{
    public Guid? FundAccountId { get; init; }
    public string? JournalCurrency { get; init; }
}

/// <summary>
/// Detailed session DTO. <see cref="TradingCosts"/> is the session's total explicit
/// transaction cost (commission + fees + modeled slippage) across all fills.
/// </summary>
public sealed record PaperSessionDetailDto(
    PaperSessionSummaryDto Summary,
    IReadOnlyList<string> Symbols,
    ExecutionPortfolioSnapshotDto? Portfolio,
    IReadOnlyList<OrderState>? OrderHistory,
    int FillCount,
    int LedgerEntryCount,
    DateTimeOffset? LastFillAt,
    DateTimeOffset? LastOrderUpdatedAt,
    IReadOnlyList<ExecutionReport>? FillHistory = null,
    decimal TradingCosts = 0m,
    string? AccountingUnavailableReason = null);

/// <summary>Portfolio snapshot DTO for session detail.</summary>
public sealed record ExecutionPortfolioSnapshotDto(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf)
{
    /// <summary>Alias for <see cref="Cash"/> using the naming used in operator-facing surfaces.</summary>
    public decimal CashBalance => Cash;
}

internal sealed record LedgerReconstructionResult(
    Meridian.Ledger.Ledger? Ledger,
    IReadOnlyList<string> CorruptEntryIds)
{
    public static LedgerReconstructionResult Empty { get; } = new(null, []);
    public int CorruptEntryCount => CorruptEntryIds.Count;
}

/// <summary>
/// Result of replaying a paper session and comparing the replayed state to the
/// currently tracked portfolio snapshot.
/// </summary>
public sealed record PaperSessionReplayVerificationDto(
    PaperSessionSummaryDto Summary,
    IReadOnlyList<string> Symbols,
    string ReplaySource,
    bool IsConsistent,
    IReadOnlyList<string> MismatchReasons,
    ExecutionPortfolioSnapshotDto? CurrentPortfolio,
    ExecutionPortfolioSnapshotDto ReplayPortfolio,
    DateTimeOffset VerifiedAt,
    int ComparedFillCount,
    int ComparedOrderCount,
    int ComparedLedgerEntryCount,
    int CorruptLedgerEntryCount,
    IReadOnlyList<string> CorruptLedgerEntryIds,
    DateTimeOffset? LastPersistedFillAt,
    DateTimeOffset? LastPersistedOrderUpdateAt,
    string? VerificationAuditId)
{
    /// <summary>Alias for <see cref="ComparedFillCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedFilledCount => ComparedFillCount;

    /// <summary>Alias for <see cref="ComparedOrderCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedOrderCount => ComparedOrderCount;

    /// <summary>Alias for <see cref="ComparedLedgerEntryCount"/> using the paper-cockpit acceptance naming.</summary>
    public int VerifiedLedgerEntriesCount => ComparedLedgerEntryCount;

    /// <summary>Alias for <see cref="VerifiedAt"/> using the paper-cockpit acceptance naming.</summary>
    public DateTimeOffset LastVerifiedAt => VerifiedAt;
}
