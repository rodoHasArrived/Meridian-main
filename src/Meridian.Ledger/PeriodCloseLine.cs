namespace Meridian.Ledger;

/// <summary>
/// One temporary (revenue or expense) account being closed to retained earnings,
/// retaining the dimensional scope it was posted under.
/// </summary>
public sealed record PeriodCloseLine(
    LedgerAccount Account,
    decimal PeriodBalance,
    decimal ClosingDebit,
    decimal ClosingCredit,
    LedgerLineDimensionSet? Dimensions = null);
