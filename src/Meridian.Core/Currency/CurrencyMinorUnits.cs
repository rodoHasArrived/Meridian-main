namespace Meridian.Core.Currency;

/// <summary>
/// ISO 4217 minor-unit precision for ledger-level monetary checks. Meridian.Contracts carries the same
/// table for contract-level rounding (it cannot reference Core); a Contracts unit test keeps them equal.
/// </summary>
public static class CurrencyMinorUnits
{
    /// <summary>Returns the currency's decimal precision, with two decimals for ordinary currencies.</summary>
    public static int GetPrecision(string currency)
        => currency switch
        {
            "CLF" or "UYW" => 4,
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or
                "RWF" or "UGX" or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            _ => 2
        };

    /// <summary>One minor unit of <paramref name="currency"/> (1 for JPY, 0.01 for USD, 0.001 for KWD).</summary>
    public static decimal OneMinorUnit(string currency)
    {
        var precision = GetPrecision(currency);
        var unit = 1m;
        for (var i = 0; i < precision; i++) unit /= 10m;
        return unit;
    }
}
