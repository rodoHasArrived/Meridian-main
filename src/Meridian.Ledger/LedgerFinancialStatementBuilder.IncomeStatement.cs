namespace Meridian.Ledger;

public static partial class LedgerFinancialStatementBuilder
{
    /// <summary>
    /// Builds income-statement rows from activity in (periodStart, asOf]. Opening balances and
    /// pure retained-earnings closing transfers do not create income or expense for the period.
    /// Ordinary reversals and corrections retain their signed effect on the period's result.
    /// </summary>
    public static LedgerIncomeStatement BuildIncomeStatementForPeriod(
        IReadOnlyLedger ledger,
        DateTimeOffset periodStart,
        DateTimeOffset asOf,
        ChartOfAccounts? chart = null,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (asOf < periodStart)
            throw new ArgumentException("As-of timestamp cannot precede the period start.", nameof(asOf));

        var activity = new Dictionary<LedgerAccount, decimal>();
        foreach (var entry in PeriodEntries(ledger, periodStart, asOf))
        {
            if (IsClosingTransfer(entry, financialAccountId, lineDimensions))
                continue;

            foreach (var line in entry.Lines)
            {
                if (!MatchesScope(line, financialAccountId, lineDimensions)
                    || line.Account.AccountType is not (LedgerAccountType.Revenue or LedgerAccountType.Expense))
                    continue;

                var amount = line.Account.AccountType == LedgerAccountType.Revenue
                    ? line.Credit - line.Debit
                    : line.Debit - line.Credit;
                activity[line.Account] = activity.GetValueOrDefault(line.Account) + amount;
            }
        }

        var statements = Build(activity, asOf, chart, financialAccountId);
        return new LedgerIncomeStatement(periodStart, asOf, statements.IncomeStatementRows,
            statements.TotalRevenue, statements.TotalExpenses, statements.NetIncome);
    }
}
