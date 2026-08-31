namespace Meridian.Infrastructure.Utilities;

/// <summary>
/// Centralized symbol normalization utilities to eliminate duplicate implementations
/// across providers. Each provider was implementing NormalizeSymbol() separately.
/// </summary>
public static class SymbolNormalization
{
    /// <summary>
    /// Standard symbol normalization: uppercase and trim whitespace.
    /// Used by: Alpaca, Polygon, Finnhub, Alpha Vantage, Yahoo Finance, Stooq, Nasdaq Data Link
    /// </summary>
    public static string Normalize(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol, nameof(symbol));
        return symbol.ToUpperInvariant().Trim();
    }

    /// <summary>
    /// Tiingo-specific normalization: replaces dots with dashes.
    /// Tiingo uses dashes instead of dots for class shares (e.g., BRK-A instead of BRK.A)
    /// </summary>
    public static string NormalizeForTiingo(string symbol)
    {
        return Normalize(symbol).Replace(".", "-");
    }

    /// <summary>
    /// Yahoo Finance-specific normalization for international symbols.
    /// Adds exchange suffix if not present (e.g., .L for London, .T for Tokyo)
    /// </summary>
    public static string NormalizeForYahoo(string symbol, string? exchangeSuffix = null)
    {
        var normalized = Normalize(symbol);

        if (!string.IsNullOrEmpty(exchangeSuffix) && !normalized.Contains('.'))
        {
            return $"{normalized}.{exchangeSuffix.TrimStart('.')}";
        }

        return normalized;
    }

    /// <summary>
    /// Stooq-specific normalization for Polish market symbols.
    /// Stooq uses lowercase symbols, replaces dots with dashes, and specific market suffixes.
    /// </summary>
    public static string NormalizeForStooq(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol, nameof(symbol));
        var normalized = symbol.Trim().ToLowerInvariant();
        if (normalized.EndsWith(".us", StringComparison.OrdinalIgnoreCase))
        {
            var ticker = normalized[..^3].Replace(".", "-");
            return $"{ticker}.us";
        }

        return normalized.Replace(".", "-");
    }

    /// <summary>
    /// Formats a canonical ticker for a provider when no explicit registry mapping exists.
    /// This is a deterministic formatting fallback only; it does not establish identity.
    /// </summary>
    public static string NormalizeForProvider(string symbol, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol, nameof(symbol));
        ArgumentException.ThrowIfNullOrWhiteSpace(provider, nameof(provider));

        return provider.Trim().ToLowerInvariant() switch
        {
            "ib" or "interactivebrokers" => Normalize(symbol).Replace(".", " "),
            "yahoo" or "yahoofinance" or "tiingo" => Normalize(symbol).Replace(".", "-"),
            "stooq" => NormalizeForStooq(symbol) is var stooq && stooq.EndsWith(".us", StringComparison.OrdinalIgnoreCase)
                ? stooq
                : $"{stooq}.us",
            "quandl" or "nasdaq" or "nasdaqdatalink" => symbol.StartsWith("WIKI/", StringComparison.OrdinalIgnoreCase)
                ? symbol.ToUpperInvariant()
                : $"WIKI/{NormalizeForNasdaqDataLink(symbol)}",
            _ => Normalize(symbol)
        };
    }

    /// <summary>
    /// Nasdaq Data Link (Quandl) specific normalization.
    /// Nasdaq Data Link expects uppercase symbols with dots replaced by underscores.
    /// </summary>
    public static string NormalizeForNasdaqDataLink(string symbol)
    {
        return Normalize(symbol).Replace(".", "_");
    }

    /// <summary>
    /// OpenFIGI-specific normalization.
    /// OpenFIGI expects uppercase symbols without special characters.
    /// </summary>
    public static string NormalizeForOpenFigi(string symbol)
    {
        var normalized = Normalize(symbol);
        // Remove common suffixes that OpenFIGI doesn't recognize
        if (normalized.Contains('.'))
        {
            normalized = normalized.Split('.')[0];
        }
        return normalized;
    }

    /// <summary>
    /// Validates that a symbol meets basic requirements.
    /// </summary>
    public static bool IsValidSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        // Symbols should be alphanumeric with optional dots, dashes, or underscores
        return symbol.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ');
    }

    /// <summary>
    /// Batch normalize multiple symbols.
    /// </summary>
    public static IReadOnlyList<string> NormalizeMany(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols, nameof(symbols));
        return symbols.Where(s => !string.IsNullOrWhiteSpace(s))
                      .Select(Normalize)
                      .Distinct()
                      .ToList();
    }
}
