namespace Meridian.Ledger;

/// <summary>
/// Trial-balance-derived financial statements for a ledger at one point in time.
/// </summary>
public sealed record LedgerFinancialStatements(
    DateTimeOffset? AsOf,
    IReadOnlyList<LedgerChartBalance> TrialBalanceRows,
    IReadOnlyList<LedgerChartBalance> IncomeStatementRows,
    IReadOnlyList<LedgerChartBalance> BalanceSheetRows,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome)
{
    /// <summary>Equity after closing current-period net income to retained earnings.</summary>
    public decimal EndingEquity => TotalEquity + NetIncome;

    /// <summary>
    /// Difference between assets and liabilities plus ending equity.
    /// A balanced statement has a zero variance.
    /// </summary>
    public decimal AccountingEquationVariance => TotalAssets - TotalLiabilities - EndingEquity;
}

