namespace Meridian.Ledger;

/// <summary>
/// Income earned and expenses incurred over (PeriodStart, AsOf], excluding transfers of
/// accumulated revenue and expense balances into retained earnings.
/// </summary>
public sealed record LedgerIncomeStatement(
    DateTimeOffset PeriodStart,
    DateTimeOffset AsOf,
    IReadOnlyList<LedgerChartBalance> Rows,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome);
