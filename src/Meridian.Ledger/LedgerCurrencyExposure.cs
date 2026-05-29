namespace Meridian.Ledger;

/// <summary>
/// Base-currency translation evidence for one ledger account balance.
/// </summary>
public sealed record LedgerCurrencyExposure(
    LedgerAccount Account,
    string LocalCurrency,
    string BaseCurrency,
    decimal LocalBalance,
    decimal FxRateToBase,
    decimal TranslatedBaseBalance,
    decimal? CarryingBaseBalance)
{
    /// <summary>
    /// Difference between translated and carrying base-currency balances.
    /// Positive values mean the normal-balance amount increased in base currency.
    /// </summary>
    public decimal? BaseCurrencyVariance =>
        CarryingBaseBalance is null ? null : TranslatedBaseBalance - CarryingBaseBalance.Value;
}

