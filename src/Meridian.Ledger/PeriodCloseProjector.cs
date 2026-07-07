using System.Globalization;
using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// Projects period-close closing entries: zeroes every revenue and expense balance and
/// rolls the resulting net income into retained earnings, scoped per financial account and
/// per dimensional accounting scope (entity, sleeve, ...). A close without these entries is
/// status-only; this projector makes the roll real without collapsing dimension-split balances.
/// </summary>
public static class PeriodCloseProjector
{
    public static PeriodCloseProjection Project(PeriodCloseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var lines = new List<PeriodCloseLine>();
        var journalLines = new List<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)>();
        var netIncomeByScope = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        // Retained-earnings roll is grouped by (financial-account scope + dimensional scope) so
        // the same revenue/expense account across two entities closes to two dimension-scoped
        // retained-earnings lines rather than one aggregate.
        var rollByScope = new Dictionary<string, PeriodCloseRollScope>(StringComparer.Ordinal);

        var temporaryAccounts = input.TrialBalance
            .Where(static row =>
                row.Account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense &&
                row.Balance != 0m)
            .OrderBy(static row => row.Account.AccountType)
            .ThenBy(static row => row.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Account.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Account.FinancialAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => DimensionKey(row.Dimensions), StringComparer.Ordinal);

        foreach (var row in temporaryAccounts)
        {
            var account = row.Account;
            var balance = row.Balance;

            // Normal-balance semantics: revenue balances are credits - debits, expense
            // balances are debits - credits. Closing flips each account to zero.
            var closesWithDebit = account.AccountType == LedgerAccountType.Revenue
                ? balance > 0m
                : balance < 0m;
            var amount = Math.Abs(balance);
            var debit = closesWithDebit ? amount : 0m;
            var credit = closesWithDebit ? 0m : amount;

            lines.Add(new PeriodCloseLine(account, balance, debit, credit, row.Dimensions));
            journalLines.Add((account, debit, credit, row.Dimensions));

            var financialScope = account.FinancialAccountId ?? PeriodCloseProjection.DefaultScope;
            var signedContribution = account.AccountType == LedgerAccountType.Revenue ? balance : -balance;
            netIncomeByScope[financialScope] = netIncomeByScope.GetValueOrDefault(financialScope) + signedContribution;

            var rollKey = FormattableString.Invariant($"{financialScope}|{DimensionKey(row.Dimensions)}");
            if (!rollByScope.TryGetValue(rollKey, out var roll))
            {
                roll = new PeriodCloseRollScope(financialScope, row.Dimensions, 0m);
                rollByScope[rollKey] = roll;
            }

            rollByScope[rollKey] = roll with { NetIncome = roll.NetIncome + signedContribution };
        }

        // Roll each (financial-account, dimensional) scope's net income into retained earnings so the
        // balancing side of the close lands on the same financial account and dimensions as the
        // temporary activity it closes.
        foreach (var roll in rollByScope.Values.OrderBy(static scope => scope.FinancialScope, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static scope => DimensionKey(scope.Dimensions), StringComparer.Ordinal))
        {
            if (roll.NetIncome == 0m)
                continue;

            var retainedEarnings = roll.FinancialScope.Length == 0
                ? LedgerAccounts.RetainedEarnings
                : LedgerAccounts.RetainedEarningsFor(roll.FinancialScope);
            journalLines.Add(roll.NetIncome > 0m
                ? (retainedEarnings, 0m, roll.NetIncome, roll.Dimensions)
                : (retainedEarnings, -roll.NetIncome, 0m, roll.Dimensions));
        }

        return new PeriodCloseProjection(input, lines, journalLines, netIncomeByScope);
    }

    /// <summary>
    /// Convenience overload that snapshots the ledger's trial balance as of the close time.
    /// The in-memory trial balance is dimension-flat, so the projected closing entries carry
    /// no dimensional scope.
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

    private sealed record PeriodCloseRollScope(
        string FinancialScope,
        LedgerLineDimensionSet? Dimensions,
        decimal NetIncome);

    /// <summary>
    /// Deterministic key for a dimensional scope, used for grouping the retained-earnings roll
    /// and ordering journal lines. Record value equality is unreliable for the external-GL
    /// dictionary, so this canonicalizes every field into a stable string.
    /// </summary>
    private static string DimensionKey(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append(dimensions.FundId).Append('|')
            .Append(dimensions.EntityId).Append('|')
            .Append(dimensions.SleeveId).Append('|')
            .Append(dimensions.StrategyId).Append('|')
            .Append(dimensions.InvestorId).Append('|')
            .Append(dimensions.CapitalAccountId).Append('|')
            .Append(dimensions.InstrumentId?.ToString("D", CultureInfo.InvariantCulture)).Append('|')
            .Append(dimensions.TaxLotId).Append('|')
            .Append(dimensions.CostCenterId).Append('|')
            .Append(dimensions.CounterpartyId).Append('|')
            .Append(dimensions.OrganizationId).Append('|')
            .Append(dimensions.PortfolioId).Append('|')
            .Append(dimensions.BookId).Append('|')
            .Append(dimensions.AccountId).Append('|')
            .Append(dimensions.CustomerId).Append('|')
            .Append(dimensions.VendorId).Append('|')
            .Append(dimensions.ProjectId).Append('|');

        foreach (var pair in dimensions.ExternalGlDimensions.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
        }

        return builder.ToString();
    }
}
