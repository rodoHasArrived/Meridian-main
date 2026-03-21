using System.Text.RegularExpressions;

namespace Meridian.Ui.Shared;

/// <summary>
/// Maps between Meridian symbol format and QuantConnect Lean's SecurityIdentifier format.
/// Meridian symbols are plain tickers like "SPY" or "AAPL"; Lean uses qualified identifiers that
/// encode market, security type, and an exchange-specific sid component.
/// </summary>
/// <remarks>
/// Lean equity data paths follow the pattern:
///   data/{securityType}/{market}/{resolution}/{ticker}/{yyyyMMdd}_{eventType}.zip
/// This class converts Meridian tickers to the path components Lean expects and vice-versa.
/// </remarks>
public static class LeanSymbolMapper
{
    /// <summary>Lean market code for US equities.</summary>
    public const string UsaMarket = "usa";

    /// <summary>Lean security type folder for equities.</summary>
    public const string EquitySecurityType = "equity";

    /// <summary>Lean security type folder for crypto.</summary>
    public const string CryptoSecurityType = "crypto";

    /// <summary>Lean security type folder for forex.</summary>
    public const string ForexSecurityType = "forex";

    private static readonly Regex CryptoPattern = new(@"(BTC|ETH|LTC|XRP|ADA|SOL|DOT|AVAX|MATIC|LINK)USD?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForexPattern = new(@"^[A-Z]{3}[A-Z]{3}$", RegexOptions.Compiled);

    /// <summary>
    /// Converts an Meridian ticker symbol to a lowercase Lean ticker (used in data paths).
    /// Lean expects lower-case tickers in file/directory names.
    /// </summary>
    /// <param name="mdcSymbol">Meridian symbol such as "SPY" or "AAPL".</param>
    /// <returns>Lean-compatible lower-case ticker, e.g. "spy".</returns>
    public static string ToLeanTicker(string mdcSymbol) =>
        (mdcSymbol ?? throw new ArgumentNullException(nameof(mdcSymbol))).Trim().ToLowerInvariant();

    /// <summary>
    /// Converts a Lean lower-case ticker back to an Meridian upper-case symbol.
    /// </summary>
    /// <param name="leanTicker">Lean ticker such as "spy".</param>
    /// <returns>Meridian symbol such as "SPY".</returns>
    public static string FromLeanTicker(string leanTicker) =>
        (leanTicker ?? throw new ArgumentNullException(nameof(leanTicker))).Trim().ToUpperInvariant();

    /// <summary>
    /// Detects the Lean security type folder name for the given Meridian symbol.
    /// </summary>
    public static string DetectSecurityType(string mdcSymbol)
    {
        ArgumentNullException.ThrowIfNull(mdcSymbol);

        if (CryptoPattern.IsMatch(mdcSymbol))
            return CryptoSecurityType;

        if (ForexPattern.IsMatch(mdcSymbol) && !IsKnownEquity(mdcSymbol))
            return ForexSecurityType;

        return EquitySecurityType;
    }

    /// <summary>
    /// Detects the Lean market for the given symbol.
    /// </summary>
    public static string DetectMarket(string mdcSymbol)
    {
        ArgumentNullException.ThrowIfNull(mdcSymbol);

        var securityType = DetectSecurityType(mdcSymbol);
        return securityType switch
        {
            CryptoSecurityType => "coinbase",
            ForexSecurityType => "oanda",
            _ => UsaMarket
        };
    }

    /// <summary>
    /// Builds the Lean data directory path for a symbol, resolution, and data type.
    /// Path pattern: {baseDir}/{securityType}/{market}/{resolution}/{ticker}
    /// </summary>
    public static string BuildLeanDataDirectory(
        string baseDir,
        string mdcSymbol,
        string resolution = "tick",
        string? securityType = null,
        string? market = null)
    {
        ArgumentNullException.ThrowIfNull(baseDir);
        ArgumentNullException.ThrowIfNull(mdcSymbol);

        var sec = securityType ?? DetectSecurityType(mdcSymbol);
        var mkt = market ?? DetectMarket(mdcSymbol);
        var ticker = ToLeanTicker(mdcSymbol);

        return Path.Combine(baseDir, sec, mkt, resolution.ToLowerInvariant(), ticker);
    }

    /// <summary>
    /// Builds the Lean zip file name for a given date and event type.
    /// Lean names trade files "{yyyyMMdd}_trade.zip" and quote files "{yyyyMMdd}_quote.zip".
    /// </summary>
    public static string BuildLeanFileName(DateTime date, string mdcEventType) =>
        $"{date:yyyyMMdd}_{MapEventTypeToLean(mdcEventType)}.zip";

    /// <summary>
    /// Maps an Meridian event type name to the Lean data type name used in file names.
    /// </summary>
    public static string MapEventTypeToLean(string mdcEventType)
    {
        ArgumentNullException.ThrowIfNull(mdcEventType);
        return mdcEventType.ToLowerInvariant() switch
        {
            "trade" => "trade",
            "bboquote" or "quote" => "quote",
            "depth" or "marketdepth" => "depth",
            "bar" or "historicalbar" or "ohlcv" => "trade", // bars → trade zip in Lean
            _ => mdcEventType.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Maps a Lean resolution string to an Meridian-friendly description.
    /// </summary>
    public static string MapResolutionToMdc(string leanResolution) =>
        leanResolution.ToLowerInvariant() switch
        {
            "tick" => "Tick",
            "second" => "Second",
            "minute" => "Minute",
            "hour" => "Hour",
            "daily" or "day" => "Daily",
            _ => leanResolution
        };

    // ---------- helpers ----------

    private static bool IsKnownEquity(string symbol)
    {
        // Short list of 6-letter symbols that look like forex pairs but are equities.
        var knownEquities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GOOGL", "NVDIA", "BRKB" // add more as needed
        };
        return knownEquities.Contains(symbol);
    }
}
