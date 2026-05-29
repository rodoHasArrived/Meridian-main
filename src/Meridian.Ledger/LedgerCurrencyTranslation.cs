namespace Meridian.Ledger;

/// <summary>
/// Translated ledger balances and FX revaluation evidence for one base currency.
/// </summary>
public sealed record LedgerCurrencyTranslation(
    string BaseCurrency,
    IReadOnlyList<LedgerCurrencyExposure> Exposures)
{
    /// <summary>Translated trial balance keyed by original ledger account.</summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> TranslatedTrialBalance =>
        Exposures.ToDictionary(exposure => exposure.Account, exposure => exposure.TranslatedBaseBalance);

    /// <summary>Total translated balance for the supplied account type.</summary>
    public decimal Total(LedgerAccountType accountType)
        => Exposures
            .Where(exposure => exposure.Account.AccountType == accountType)
            .Sum(exposure => exposure.TranslatedBaseBalance);
}

