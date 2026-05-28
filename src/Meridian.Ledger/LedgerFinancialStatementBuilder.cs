namespace Meridian.Ledger;

/// <summary>
/// Builds financial-statement read models from a ledger trial balance and optional chart hierarchy.
/// </summary>
public static class LedgerFinancialStatementBuilder
{
    /// <summary>
    /// Builds trial-balance, income-statement, and balance-sheet rows from the current ledger state.
    /// </summary>
    public static LedgerFinancialStatements Build(
        IReadOnlyLedger ledger,
        ChartOfAccounts? chart = null,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Build(
            ledger.TrialBalance(financialAccountId),
            asOf: null,
            chart,
            financialAccountId);
    }

    /// <summary>
    /// Builds trial-balance, income-statement, and balance-sheet rows as of a point in time.
    /// </summary>
    public static LedgerFinancialStatements BuildAsOf(
        IReadOnlyLedger ledger,
        DateTimeOffset asOf,
        ChartOfAccounts? chart = null,
        string? financialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Build(
            ledger.TrialBalanceAsOf(asOf, financialAccountId),
            asOf,
            chart,
            financialAccountId);
    }

    private static LedgerFinancialStatements Build(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        DateTimeOffset? asOf,
        ChartOfAccounts? chart,
        string? financialAccountId)
    {
        var statementChart = BuildStatementChart(trialBalance, chart, financialAccountId);
        var rows = statementChart.AggregateBalances(trialBalance);

        var incomeRows = rows
            .Where(row => row.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense)
            .ToList();
        var balanceSheetRows = rows
            .Where(row => row.Account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability or LedgerAccountType.Equity)
            .ToList();

        var totalAssets = SumTrialBalance(trialBalance, LedgerAccountType.Asset);
        var totalLiabilities = SumTrialBalance(trialBalance, LedgerAccountType.Liability);
        var totalEquity = SumTrialBalance(trialBalance, LedgerAccountType.Equity);
        var totalRevenue = SumTrialBalance(trialBalance, LedgerAccountType.Revenue);
        var totalExpenses = SumTrialBalance(trialBalance, LedgerAccountType.Expense);
        var netIncome = totalRevenue - totalExpenses;

        return new LedgerFinancialStatements(
            asOf,
            rows,
            incomeRows,
            balanceSheetRows,
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalRevenue,
            totalExpenses,
            netIncome);
    }

    private static ChartOfAccounts BuildStatementChart(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        ChartOfAccounts? chart,
        string? financialAccountId)
    {
        var result = new ChartOfAccounts();

        if (chart is not null)
        {
            foreach (var node in chart.Accounts)
            {
                result.Register(
                    node.Path,
                    node.Account.AccountType,
                    node.Account.Symbol,
                    node.Account.FinancialAccountId);
            }
        }

        foreach (var account in trialBalance.Keys)
        {
            if (!MatchesFinancialAccount(account, financialAccountId))
                continue;

            result.Register(account.Name, account.AccountType, account.Symbol, account.FinancialAccountId);
        }

        return result;
    }

    private static decimal SumTrialBalance(
        IReadOnlyDictionary<LedgerAccount, decimal> trialBalance,
        LedgerAccountType accountType)
    {
        var total = 0m;
        foreach (var (account, balance) in trialBalance)
        {
            if (account.AccountType == accountType)
                total += balance;
        }

        return total;
    }

    private static bool MatchesFinancialAccount(LedgerAccount account, string? financialAccountId)
        => string.IsNullOrWhiteSpace(financialAccountId)
           || string.Equals(account.FinancialAccountId, financialAccountId.Trim(), StringComparison.OrdinalIgnoreCase);
}

