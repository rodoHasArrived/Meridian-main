namespace Meridian.Ledger;

/// <summary>
/// Posting rules bound to a <see cref="Ledger"/> at construction. A bound ledger accepts only
/// accounts registered in <see cref="Chart"/> and only lines that carry explicit currency detail in
/// <see cref="FunctionalCurrency"/>. Unknown accounts and missing or mismatched currencies are hard
/// errors; nothing is inferred from an account name, number or symbol.
/// </summary>
public sealed record LedgerPolicy
{
    public LedgerPolicy(IChartOfAccounts chart, string functionalCurrency)
    {
        Chart = chart ?? throw new ArgumentNullException(nameof(chart));
        if (string.IsNullOrWhiteSpace(functionalCurrency))
            throw new LedgerValidationException("A bound ledger requires a functional currency.");

        var normalized = functionalCurrency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(static c => c is >= 'A' and <= 'Z'))
            throw new LedgerValidationException(
                $"Functional currency '{functionalCurrency}' must be a three-letter ISO 4217 code.");

        FunctionalCurrency = normalized;
    }

    /// <summary>The only registry of accounts this ledger may post to.</summary>
    public IChartOfAccounts Chart { get; }

    /// <summary>The ISO 4217 currency every line's functional amounts are stated in.</summary>
    public string FunctionalCurrency { get; }

    /// <summary>
    /// The policy used by the trading paths (backtest, paper, execution posting): the standard
    /// posting chart in the given functional currency.
    /// </summary>
    public static LedgerPolicy Trading(string functionalCurrency = "USD", IChartOfAccounts? chart = null)
        => new(chart ?? StandardChartOfAccounts.Instance, functionalCurrency);

    /// <summary>Currency detail for an amount already stated in the functional currency.</summary>
    public LedgerEntryCurrency FunctionalAmount(decimal debit, decimal credit)
        => new(FunctionalCurrency, FunctionalCurrency, debit, credit, 1m);
}
