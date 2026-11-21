namespace Meridian.Contracts.Ledger;

/// <summary>
/// Shared currency rounding policy for ledger and accounting amounts. Consolidates the per-file
/// <c>RoundCurrency</c> helpers that were previously duplicated across the ledger projectors,
/// report-pack builders, and UI event producers, so a rounding-policy change is a single edit
/// instead of one coordinated edit per copy.
/// </summary>
public static class LedgerCurrencyRounding
{
    /// <summary>
    /// Rounds a monetary amount to 2 decimal places using away-from-zero midpoint rounding,
    /// the platform-wide currency rounding policy for posted ledger amounts.
    /// </summary>
    public static decimal RoundCurrency(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Rounds a monetary amount to the minor-unit precision of <paramref name="currency"/>
    /// (0 for JPY/KRW, 3 for KWD/BHD, 2 for ordinary currencies) using the same away-from-zero
    /// midpoint policy as <see cref="RoundCurrency(decimal)"/>.
    /// </summary>
    public static decimal RoundCurrency(decimal amount, string currency)
        => decimal.Round(amount, CurrencyMinorUnits.GetPrecision(currency), MidpointRounding.AwayFromZero);
}
