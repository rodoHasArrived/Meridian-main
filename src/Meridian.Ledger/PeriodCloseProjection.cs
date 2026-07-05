namespace Meridian.Ledger;

/// <summary>
/// Balanced period-close closing entries: every temporary account zeroed and the
/// net income rolled into retained earnings, scoped per financial account.
/// </summary>
public sealed record PeriodCloseProjection(
    PeriodCloseInput Input,
    IReadOnlyList<PeriodCloseLine> Lines,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> JournalLines,
    IReadOnlyDictionary<string, decimal> NetIncomeByScope)
{
    /// <summary>Scope key used for accounts without a financial-account identifier.</summary>
    public const string DefaultScope = "";

    /// <summary>Total net income rolled to retained earnings across all scopes.</summary>
    public decimal NetIncome => NetIncomeByScope.Values.Sum();

    /// <summary>True when there were temporary-account balances to close.</summary>
    public bool HasClosingEntries => JournalLines.Count > 0;

    public decimal TotalDebits => JournalLines.Sum(static line => line.debit);

    public decimal TotalCredits => JournalLines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;
}
