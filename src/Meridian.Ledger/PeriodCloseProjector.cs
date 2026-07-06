namespace Meridian.Ledger;

/// <summary>
/// Projects period-close closing entries: zeroes every revenue and expense balance and
/// rolls the resulting net income into retained earnings, scoped per financial account.
/// A close without these entries is status-only; this projector makes the roll real.
/// </summary>
public static class PeriodCloseProjector
{
    public static PeriodCloseProjection Project(PeriodCloseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new List<PeriodCloseLine>();
        var journalLines = new List<(LedgerAccount account, decimal debit, decimal credit)>();
        var netIncomeByScope = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var temporaryAccounts = input.TrialBalance
            .Where(static pair =>
                pair.Key.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense &&
                pair.Value != 0m)
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key.FinancialAccountId, StringComparer.OrdinalIgnoreCase);

        foreach (var (account, balance) in temporaryAccounts)
        {
            // Normal-balance semantics: revenue balances are credits - debits, expense
            // balances are debits - credits. Closing flips each account to zero.
            var closesWithDebit = account.AccountType == LedgerAccountType.Revenue
                ? balance > 0m
                : balance < 0m;
            var amount = Math.Abs(balance);
            var debit = closesWithDebit ? amount : 0m;
            var credit = closesWithDebit ? 0m : amount;

            lines.Add(new PeriodCloseLine(account, balance, debit, credit));
            journalLines.Add((account, debit, credit));

            var scope = account.FinancialAccountId ?? PeriodCloseProjection.DefaultScope;
            var signedContribution = account.AccountType == LedgerAccountType.Revenue ? balance : -balance;
            netIncomeByScope[scope] = netIncomeByScope.GetValueOrDefault(scope) + signedContribution;
        }

        // Roll each scope's net income into retained earnings so the balancing side of the
        // close lands on the same financial account as the temporary activity it closes.
        foreach (var (scope, netIncome) in netIncomeByScope.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (netIncome == 0m)
                continue;

            var retainedEarnings = scope.Length == 0
                ? LedgerAccounts.RetainedEarnings
                : LedgerAccounts.RetainedEarningsFor(scope);
            journalLines.Add(netIncome > 0m
                ? (retainedEarnings, 0m, netIncome)
                : (retainedEarnings, -netIncome, 0m));
        }

        return new PeriodCloseProjection(input, lines, journalLines, netIncomeByScope);
    }

    /// <summary>
    /// Convenience overload that snapshots the ledger's trial balance as of the close time.
    /// </summary>
    public static PeriodCloseProjection ProjectFrom(
        Ledger ledger,
        string periodId,
        DateTimeOffset closedAtUtc,
        string closedBy)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return Project(new PeriodCloseInput(
            periodId,
            closedAtUtc,
            ledger.TrialBalanceAsOf(closedAtUtc),
            closedBy));
    }
}
