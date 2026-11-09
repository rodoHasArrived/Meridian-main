namespace Meridian.Core.Currency;

/// <summary>
/// ISO 4217 minor-unit precision for ledger-level monetary checks. The precision table itself lives
/// in <see cref="Meridian.Contracts.Ledger.CurrencyMinorUnits"/> (Contracts cannot reference Core), and
/// this type delegates to it so there is one table to maintain.
/// </summary>
public static class CurrencyMinorUnits
{
    /// <summary>Returns the currency's decimal precision, with two decimals for ordinary currencies.</summary>
    public static int GetPrecision(string currency)
        => Meridian.Contracts.Ledger.CurrencyMinorUnits.GetPrecision(currency);

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
