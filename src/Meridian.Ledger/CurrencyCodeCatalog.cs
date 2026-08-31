namespace Meridian.Ledger;

/// <summary>
/// Repository-owned catalog of currency codes Meridian accepts for accounting and payment
/// intent. It includes active and historically retained ISO-4217 codes, supranational units,
/// precious metals, and the operational offshore-renminbi code CNH.
/// </summary>
public static class CurrencyCodeCatalog
{
    // Codes retained for historical ledgers but absent from SIX ISO-4217 List One as of
    // 2026-08-05. CNH remains an explicitly supported operational code.
    private static readonly HashSet<string> HistoricalCodeSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANG", "BGN", "BYR", "CUC", "HRK", "SLL", "ZWL",
    };

    private static readonly string[] CodeValues =
    [
        "USD","EUR","GBP","JPY","CHF","CAD","AUD","NZD","HKD","SGD","SEK","NOK","DKK","CNY","CNH",
        "BRL","MXN","INR","ZAR","KRW","TRY","PLN","CZK","HUF","ILS","SAR","AED","MYR","THB","IDR",
        "PHP","CLP","COP","PEN","ARS","VND","NGN","EGP","PKR","BDT","UAH","RON","BGN","HRK","RSD",
        "ISK","GEL","KZT","UZS","AZN","AMD","BYN","BYR","MDL","MKD","ALL","BAM","RUB","TND","MAD",
        "DZD","LYD","IQD","KWD","BHD","OMR","QAR","JOD","LBP","SYP","YER","AFN","IRR","XAU","XAG",
        "XPT","XPD","XDR","XOF","XAF","XCD","XCG","XPF","CLF","UYU","UYI","SOS","GHS","ETB","TZS",
        "UGX","MZN","AOA","KES","RWF","ZMW","BWP","MGA","ZWL","ZWG","NAD","SCR","MUR","MWK","SZL",
        "LSL","CVE","GMD","GNF","LRD","SLL","SLE","STN","XSU","XUA","NIO","GTQ","HNL","CRC","PAB",
        "DOP","JMD","TTD","BBD","BSD","HTG","CUP","CUC","AWG","ANG","SRD","GYD","BMD","KYD","BZD",
        "FJD","PGK","SBD","VUV","WST","TOP","MOP","BND","MMK","KHR","LAK","MNT","NPR","LKR","MVR",
        "BTN","TJS","TMT","KGS","KPW","TWD","BOB","PYG","VES","VED","SVC","SDG","SSP","ERN","DJF",
        "BIF","KMF","CDF","MRU","FKP","GIP","SHP",
    ];

    private static readonly HashSet<string> CodeSet = new(CodeValues, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<string> CodeView = Array.AsReadOnly(CodeValues);
    private static readonly string[] CurrentTransactionCodeValues = CodeValues
        .Where(static code => !HistoricalCodeSet.Contains(code))
        .ToArray();
    private static readonly HashSet<string> CurrentTransactionCodeSet =
        new(CurrentTransactionCodeValues, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<string> CurrentTransactionCodeView =
        Array.AsReadOnly(CurrentTransactionCodeValues);

    /// <summary>All recognized codes in stable catalog order.</summary>
    public static IReadOnlyList<string> RecognizedCodes => CodeView;

    /// <summary>
    /// Codes accepted for a new payment or a pending-payment currency repair. Historical codes
    /// remain queryable through <see cref="RecognizedCodes"/> but cannot initiate new movement.
    /// </summary>
    public static IReadOnlyList<string> CurrentTransactionCodes => CurrentTransactionCodeView;

    /// <summary>Returns whether <paramref name="candidate"/> exactly names a recognized code.</summary>
    public static bool IsRecognized(string? candidate)
        => candidate is { Length: 3 } && CodeSet.Contains(candidate);

    /// <summary>Returns whether a code is permitted for a new transaction.</summary>
    public static bool IsCurrentForTransactions(string? candidate)
        => candidate is { Length: 3 } && CurrentTransactionCodeSet.Contains(candidate);

    /// <summary>Trims, uppercases, and validates a caller-supplied currency code.</summary>
    public static bool TryNormalizeRecognized(string? candidate, out string normalized)
    {
        normalized = candidate?.Trim().ToUpperInvariant() ?? string.Empty;
        return IsRecognized(normalized);
    }

    /// <summary>Trims, uppercases, and validates a currency for a new transaction.</summary>
    public static bool TryNormalizeCurrent(string? candidate, out string normalized)
    {
        normalized = candidate?.Trim().ToUpperInvariant() ?? string.Empty;
        return IsCurrentForTransactions(normalized);
    }
}
