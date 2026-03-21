namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Base record for all typed cash-flow entries that form the ledger used to compute XIRR
/// and distinguish time-weighted from cash-weighted returns.
/// </summary>
public abstract record CashFlowEntry(DateTimeOffset Timestamp, decimal Amount, string? AccountId = null);

/// <summary>Cash flow arising from executing a trade (buy or sell).</summary>
public sealed record TradeCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    string Symbol,
    long Quantity,
    decimal Price,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Daily margin interest charge on a debit balance (negative amount = cash outflow).</summary>
/// <param name="Timestamp">Date and time the interest was charged.</param>
/// <param name="Amount">Net cash-flow amount (negative = outflow).</param>
/// <param name="MarginBalance">Balance on which interest was charged.</param>
/// <param name="AnnualRate">Annual rate (e.g. 0.05 for 5%).</param>
/// <param name="AccountId">Optional account identifier this cash flow belongs to.</param>
public sealed record MarginInterestCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    decimal MarginBalance,
    double AnnualRate,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Short-sale rebate received from the broker (positive amount = cash inflow).</summary>
public sealed record ShortRebateCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    string Symbol,
    long ShortShares,
    double AnnualRebateRate,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Brokerage commission paid on an order (negative amount).</summary>
public sealed record CommissionCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    string Symbol,
    Guid OrderId,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Dividend received on a long position (positive amount).</summary>
public sealed record DividendCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    string Symbol,
    long Shares,
    decimal DividendPerShare,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Interest credited to an idle cash balance, usually in a bank or sweep account.</summary>
public sealed record CashInterestCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    double AnnualRate,
    string? AccountId = null) : CashFlowEntry(Timestamp, Amount, AccountId);

/// <summary>Generic cash-flow caused by a scheduled asset-level event.</summary>
public sealed record AssetEventCashFlow(
    DateTimeOffset Timestamp,
    decimal Amount,
    string Symbol,
    AssetEventType EventType,
    long UnitsImpacted,
    decimal CashPerShare,
    string? RelatedSymbol = null,
    decimal PositionFactor = 1m,
    string? Description = null) : CashFlowEntry(Timestamp, Amount);
