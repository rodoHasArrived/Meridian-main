using Meridian.Contracts.Ledger;

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

    /// <summary>
    /// Builds the point-in-time statements as of <paramref name="asOf"/> and augments them with a
    /// statement of cash flows and a statement of changes in partners' capital derived from the
    /// journal activity over (<paramref name="periodStart"/>, <paramref name="asOf"/>].
    /// </summary>
    public static LedgerFinancialStatements BuildForPeriod(
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

        var statements = BuildAsOf(ledger, asOf, chart, financialAccountId, lineDimensions);
        var cashFlow = BuildCashFlowStatement(ledger, periodStart, asOf, financialAccountId, lineDimensions);
        var partnersCapital = BuildPartnersCapitalStatement(
            ledger, periodStart, asOf, financialAccountId, lineDimensions, statements.NetIncome);

        return statements with { CashFlow = cashFlow, PartnersCapital = partnersCapital };
    }

    private static LedgerCashFlowStatement BuildCashFlowStatement(
        IReadOnlyLedger ledger,
        DateTimeOffset periodStart,
        DateTimeOffset asOf,
        string? financialAccountId,
        LedgerLineDimensionSet? lineDimensions)
    {
        var beginningCash = SumCashBalances(ledger.TrialBalanceAsOf(periodStart, financialAccountId, lineDimensions));
        var endingCash = SumCashBalances(ledger.TrialBalanceAsOf(asOf, financialAccountId, lineDimensions));

        var operating = 0m;
        var investing = 0m;
        var financing = 0m;
        var lineTotals = new Dictionary<(LedgerCashFlowCategory Category, string Description, LedgerAccountType CounterpartyType), decimal>();

        foreach (var entry in PeriodEntries(ledger, periodStart, asOf))
        {
            var cashDelta = entry.Lines
                .Where(line => IsCashAccount(line.Account) && MatchesScope(line, financialAccountId, lineDimensions))
                .Sum(static line => line.Debit - line.Credit);
            if (cashDelta == 0m)
                continue;

            var counterpartLines = entry.Lines
                .Where(line => !IsCashAccount(line.Account) && MatchesScope(line, financialAccountId, lineDimensions))
                .Select(line => (line.Account, Weight: line.Debit + line.Credit))
                .Where(static line => line.Weight > 0m)
                .ToArray();
            var weightTotal = counterpartLines.Sum(static line => line.Weight);
            if (weightTotal <= 0m)
                continue;

            var placed = 0m;
            for (var index = 0; index < counterpartLines.Length; index++)
            {
                var (account, weight) = counterpartLines[index];
                var share = index == counterpartLines.Length - 1
                    ? cashDelta - placed
                    : LedgerCurrencyRounding.RoundCurrency(cashDelta * weight / weightTotal);
                placed += share;

                var category = CategorizeCashFlow(account.AccountType);
                switch (category)
                {
                    case LedgerCashFlowCategory.Operating:
                        operating += share;
                        break;
                    case LedgerCashFlowCategory.Investing:
                        investing += share;
                        break;
                    default:
                        financing += share;
                        break;
                }

                var key = (category, account.Name, account.AccountType);
                lineTotals[key] = lineTotals.TryGetValue(key, out var existing) ? existing + share : share;
            }
        }

        var lines = lineTotals
            .Where(static pair => pair.Value != 0m)
            .OrderBy(static pair => pair.Key.Category)
            .ThenBy(static pair => pair.Key.Description, StringComparer.Ordinal)
            .Select(static pair => new LedgerCashFlowLine(pair.Key.Category, pair.Key.Description, pair.Key.CounterpartyType, pair.Value))
            .ToArray();

        return new LedgerCashFlowStatement(
            periodStart,
            asOf,
            beginningCash,
            endingCash,
            operating,
            investing,
            financing,
            lines);
    }

    private static LedgerPartnersCapitalStatement BuildPartnersCapitalStatement(
        IReadOnlyLedger ledger,
        DateTimeOffset periodStart,
        DateTimeOffset asOf,
        string? financialAccountId,
        LedgerLineDimensionSet? lineDimensions,
        decimal netIncome)
    {
        // Opening and closing capital come from dimension-scoped trial balances so a scoped report
        // never discloses or reconciles against balances outside its scope.
        var beginningBalances = ledger.TrialBalanceAsOf(periodStart, financialAccountId, lineDimensions);
        var endingBalances = ledger.TrialBalanceAsOf(asOf, financialAccountId, lineDimensions);
        var equityAccounts = beginningBalances.Keys
            .Concat(endingBalances.Keys)
            .Where(static account => account.AccountType == LedgerAccountType.Equity)
            .Distinct()
            .ToArray();

        var movements = equityAccounts.ToDictionary(
            static account => account,
            static _ => (Contributions: 0m, Distributions: 0m, Allocated: 0m, Other: 0m));

        foreach (var entry in PeriodEntries(ledger, periodStart, asOf))
        {
            var equityLines = entry.Lines
                .Where(line => line.Account.AccountType == LedgerAccountType.Equity
                    && MatchesScope(line, financialAccountId, lineDimensions)
                    && movements.ContainsKey(line.Account))
                .ToArray();
            if (equityLines.Length == 0)
                continue;

            // Only counterpart legs within the report scope may classify the movement, so a batched
            // journal that also touches other funds/dimensions cannot misclassify this scope's
            // contribution as an allocation (or vice versa).
            var hasIncomeCounterpart = entry.Lines.Any(line =>
                MatchesScope(line, financialAccountId, lineDimensions)
                && (line.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense
                    || (line.Account.AccountType == LedgerAccountType.Equity
                        && line.Account.Name.Equals("Retained Earnings", StringComparison.Ordinal))));
            // Contributions and distributions settle through cash, receivables, or payables
            // (subscription-receivable and distribution-payable clearing), so treat asset and
            // liability counterparts alike as capital movement rather than "other".
            var hasSettlementCounterpart = entry.Lines.Any(line =>
                MatchesScope(line, financialAccountId, lineDimensions)
                && line.Account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability);

            foreach (var line in equityLines)
            {
                var signed = line.Credit - line.Debit;
                var current = movements[line.Account];
                var isRetainedEarnings = line.Account.Name.Equals("Retained Earnings", StringComparison.Ordinal);

                if (hasIncomeCounterpart && !isRetainedEarnings)
                {
                    current.Allocated += signed;
                }
                else if (hasSettlementCounterpart)
                {
                    if (signed >= 0m)
                        current.Contributions += signed;
                    else
                        current.Distributions += -signed;
                }
                else
                {
                    current.Other += signed;
                }

                movements[line.Account] = current;
            }
        }

        var rollForwards = equityAccounts
            .Select(account =>
            {
                var beginning = beginningBalances.TryGetValue(account, out var openingBalance) ? openingBalance : 0m;
                var ending = endingBalances.TryGetValue(account, out var closingBalance) ? closingBalance : 0m;
                var movement = movements[account];
                return new LedgerPartnersCapitalRollForward(
                    account,
                    account.Name,
                    account.FinancialAccountId,
                    beginning,
                    movement.Contributions,
                    movement.Distributions,
                    movement.Allocated,
                    movement.Other,
                    ending);
            })
            .Where(static rollForward =>
                rollForward.BeginningCapital != 0m
                || rollForward.EndingCapital != 0m
                || rollForward.Contributions != 0m
                || rollForward.Distributions != 0m
                || rollForward.AllocatedResult != 0m
                || rollForward.OtherMovements != 0m)
            .ToList();

        // Net income not yet closed to equity still belongs to the partners; carry it as an
        // undistributed line so ending capital ties to balance-sheet ending equity
        // (TotalEquity + NetIncome). The passed net income is cumulative as of the report date, so the
        // opening balance holds prior-period undistributed P&L and only the current period's activity
        // is the allocated movement — otherwise an interim period that opens with unclosed prior P&L
        // would double-count it and fail to reconcile.
        var netIncomeAtStart = NetIncomeOf(beginningBalances);
        var periodNetIncome = netIncome - netIncomeAtStart;
        if (netIncome != 0m || netIncomeAtStart != 0m)
        {
            rollForwards.Add(new LedgerPartnersCapitalRollForward(
                new LedgerAccount("Undistributed Net Income", LedgerAccountType.Equity),
                "Undistributed Net Income",
                InvestorId: null,
                BeginningCapital: netIncomeAtStart,
                Contributions: 0m,
                Distributions: 0m,
                AllocatedResult: periodNetIncome,
                OtherMovements: 0m,
                EndingCapital: netIncome));
        }

        var orderedRollForwards = rollForwards
            .OrderBy(static rollForward => rollForward.AccountName, StringComparer.Ordinal)
            .ThenBy(static rollForward => rollForward.InvestorId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        return new LedgerPartnersCapitalStatement(
            periodStart,
            asOf,
            orderedRollForwards.Sum(static rollForward => rollForward.BeginningCapital),
            orderedRollForwards.Sum(static rollForward => rollForward.Contributions),
            orderedRollForwards.Sum(static rollForward => rollForward.Distributions),
            orderedRollForwards.Sum(static rollForward => rollForward.AllocatedResult),
            orderedRollForwards.Sum(static rollForward => rollForward.OtherMovements),
            orderedRollForwards.Sum(static rollForward => rollForward.EndingCapital),
            orderedRollForwards);
    }

    private static IEnumerable<JournalEntry> PeriodEntries(
        IReadOnlyLedger ledger,
        DateTimeOffset periodStart,
        DateTimeOffset asOf)
        => ledger.Journal.Where(entry => entry.Timestamp > periodStart && entry.Timestamp <= asOf);

    private static decimal SumCashBalances(IReadOnlyDictionary<LedgerAccount, decimal> scopedTrialBalance)
        => scopedTrialBalance
            .Where(pair => IsCashAccount(pair.Key))
            .Sum(static pair => pair.Value);

    private static decimal NetIncomeOf(IReadOnlyDictionary<LedgerAccount, decimal> trialBalance)
    {
        var revenue = 0m;
        var expense = 0m;
        foreach (var (account, balance) in trialBalance)
        {
            if (account.AccountType == LedgerAccountType.Revenue)
                revenue += balance;
            else if (account.AccountType == LedgerAccountType.Expense)
                expense += balance;
        }

        return revenue - expense;
    }

    private static bool IsCashAccount(LedgerAccount account)
        => account.AccountType == LedgerAccountType.Asset
           && account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);

    private static LedgerCashFlowCategory CategorizeCashFlow(LedgerAccountType counterpartyType)
        => counterpartyType switch
        {
            LedgerAccountType.Revenue or LedgerAccountType.Expense => LedgerCashFlowCategory.Operating,
            LedgerAccountType.Asset => LedgerCashFlowCategory.Investing,
            _ => LedgerCashFlowCategory.Financing,
        };

    private static bool MatchesScope(
        LedgerEntry line,
        string? financialAccountId,
        LedgerLineDimensionSet? lineDimensions)
        => MatchesFinancialAccount(line.Account, financialAccountId)
           && LedgerLineDimensionSetNormalizer.Matches(line.Dimensions, lineDimensions);

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
