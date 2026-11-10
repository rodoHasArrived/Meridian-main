using ContractsCurrencyMinorUnits = Meridian.Contracts.Ledger.CurrencyMinorUnits;

namespace Meridian.Core.Currency;

/// <summary>
/// ISO 4217 minor-unit precision for ledger-level monetary checks. The precision table is owned by
/// <see cref="ContractsCurrencyMinorUnits"/>; this type forwards to it and adds ledger-level helpers.
/// </summary>
public static class CurrencyMinorUnits
{
    /// <summary>Returns the currency's decimal precision, with two decimals for ordinary currencies.</summary>
    public static int GetPrecision(string currency)
        => ContractsCurrencyMinorUnits.GetPrecision(currency);

    /// <summary>One minor unit of <paramref name="currency"/> (1 for JPY, 0.01 for USD, 0.001 for KWD).</summary>
    public static decimal OneMinorUnit(string currency)
    {
        var precision = GetPrecision(currency);
        var unit = 1m;
        for (var i = 0; i < precision; i++)
            unit /= 10m;
        return unit;
    }
}
