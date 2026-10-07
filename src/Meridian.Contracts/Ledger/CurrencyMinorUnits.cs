namespace Meridian.Contracts.Ledger;

/// <summary>Shared minor-unit precision for currency-specific monetary allocations.</summary>
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
}
