using System.Collections.Frozen;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// ISO 4217 alphabetic currency codes recognised by Security Master validation (review Priority 1,
/// step 3). The list is maintained by hand from the published ISO 4217 table (current codes, including
/// funds and the precious-metal and SDR X-codes) and is checked case-sensitively: a code is three
/// uppercase letters exactly as listed, so <c>"usd"</c> or <c>" USD"</c> is not recognised.
/// <para>
/// <see cref="MarketExtensions"/> holds market codes that are not ISO 4217 but are in established use
/// for quoting: <c>CNH</c> (offshore renminbi) and <c>GBX</c> (pence sterling). <c>XTS</c> (testing)
/// and <c>XXX</c> (no currency) are ISO codes but are deliberately not recognised for securities.
/// Withdrawn codes (for example <c>HRK</c>) are not recognised; records that still carry them are
/// reported so they can be reviewed.
/// </para>
/// </summary>
public static class Iso4217Currencies
{
    /// <summary>Current ISO 4217 alphabetic codes accepted for a security.</summary>
    public static FrozenSet<string> Codes { get; } = new[]
    {
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BOV", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHE", "CHF", "CHW", "CLF", "CLP", "CNY", "COP", "COU", "CRC", "CUP", "CVE", "CZK",
        "DJF", "DKK", "DOP", "DZD",
        "EGP", "ERN", "ETB", "EUR",
        "FJD", "FKP",
        "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD",
        "HKD", "HNL", "HTG", "HUF",
        "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY",
        "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT",
        "LAK", "LBP", "LKR", "LRD", "LSL", "LYD",
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MXV", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG",
        "QAR",
        "RON", "RSD", "RUB", "RWF",
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL",
        "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS",
        "UAH", "UGX", "USD", "USN", "UYI", "UYU", "UYW", "UZS",
        "VED", "VES", "VND", "VUV",
        "WST",
        "XAF", "XAG", "XAU", "XBA", "XBB", "XBC", "XBD", "XCD", "XCG", "XDR", "XOF", "XPD", "XPF", "XPT", "XSU", "XUA",
        "YER",
        "ZAR", "ZMW", "ZWG"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Non-ISO market codes in established quoting use.</summary>
    public static FrozenSet<string> MarketExtensions { get; } =
        new[] { "CNH", "GBX" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="code"/> is a recognised ISO 4217 code or market extension, compared
    /// exactly (no trimming or case folding).
    /// </summary>
    public static bool IsRecognized(string? code)
        => code is { Length: 3 } && (Codes.Contains(code) || MarketExtensions.Contains(code));
}
