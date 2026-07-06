namespace Meridian.Ledger;

/// <summary>
/// One temporary (revenue or expense) account being closed to retained earnings.
/// </summary>
public sealed record PeriodCloseLine(
    LedgerAccount Account,
    decimal PeriodBalance,
    decimal ClosingDebit,
    decimal ClosingCredit);
