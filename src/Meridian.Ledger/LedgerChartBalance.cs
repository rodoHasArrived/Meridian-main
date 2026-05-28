namespace Meridian.Ledger;

/// <summary>
/// Direct and rolled-up balances for a chart-of-accounts node.
/// </summary>
public sealed record LedgerChartBalance(
    LedgerAccount Account,
    string Path,
    string? ParentPath,
    decimal DirectBalance,
    decimal AggregateBalance);

