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
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Build(
            ledger.TrialBalance(financialAccountId, lineDimensions),
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
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return Build(
            ledger.TrialBalanceAsOf(asOf, financialAccountId, lineDimensions),
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

        var accountsByPath = trialBalance.Keys
            .Where(account => MatchesFinancialAccount(account, financialAccountId))
            .GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var accountGroup in accountsByPath)
        {
            var accounts = accountGroup.ToList();
            var account = accounts[0];
            var existing = result.Find(account.Name);
            if (existing is not null)
            {
                if (accounts.Any(candidate => candidate.AccountType != existing.Account.AccountType))
                {
                    throw new ArgumentException(
                        $"Chart account '{account.Name}' is registered with a different account type.",
                        nameof(chart));
                }

                continue;
            }

            var symbols = accounts
                .Select(candidate => candidate.Symbol)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var financialAccountIds = accounts
                .Select(candidate => candidate.FinancialAccountId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.Register(
                account.Name,
                account.AccountType,
                symbols.Count == 1 ? symbols[0] : null,
                financialAccountIds.Count == 1 ? financialAccountIds[0] : null);
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
