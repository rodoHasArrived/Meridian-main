namespace Meridian.Ledger;

/// <summary>
/// Multi-currency journal projection ready for base-currency ledger posting.
/// </summary>
public sealed record MultiCurrencyJournalProjection(
    MultiCurrencyJournalInput Input,
    IReadOnlyList<MultiCurrencyJournalLineProjection> Lines)
{
    public decimal TotalBaseDebits => Lines.Sum(static line => line.BaseDebit);

    public decimal TotalBaseCredits => Lines.Sum(static line => line.BaseCredit);

    public bool IsBaseBalanced => TotalBaseDebits == TotalBaseCredits;

    public IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> ToLedgerLines()
        => Lines
            .Select(static line => (line.Account, line.BaseDebit, line.BaseCredit))
            .ToList();

    /// <summary>
    /// Projects to base-currency posting lines that preserve each leg's original transaction
    /// currency, transaction amounts, and FX rate for currency-aware posting.
    /// </summary>
    public IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions, LedgerEntryCurrency? currency)> ToCurrencyAwareLedgerLines()
        => Lines
            .Select(static line => (
                line.Account,
                line.BaseDebit,
                line.BaseCredit,
                (LedgerLineDimensionSet?)null,
                (LedgerEntryCurrency?)new LedgerEntryCurrency(
                    line.LocalCurrency,
                    line.BaseCurrency,
                    line.LocalDebit,
                    line.LocalCredit,
                    line.FxRateToBase)))
            .ToList();
}
