using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services;

/// <summary>
/// Curated offline fallback membership for well-known indices, used by
/// <see cref="PortfolioImportService"/> when the service API cannot return live
/// constituents.
/// </summary>
/// <remarks>
/// These lists are intentionally <b>partial, weight-ranked samples</b> — not full
/// index membership — so quick imports still work offline. Extend or replace the
/// per-index symbol lists (or add new indices and aliases) here without touching the
/// import logic. Live constituents from the API always take precedence over this
/// fallback; this catalog only fills in when that call fails.
/// </remarks>
internal static class IndexConstituentCatalog
{
    /// <summary>A curated fallback index definition: its display name and sample symbols.</summary>
    internal sealed record IndexConstituentDefinition(string DisplayName, IReadOnlyList<string> Symbols);

    // Canonical index id -> definition. Add new indices here.
    private static readonly IReadOnlyDictionary<string, IndexConstituentDefinition> Definitions =
        new Dictionary<string, IndexConstituentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["SP500"] = new("S&P 500", new[]
            {
                "AAPL", "MSFT", "AMZN", "NVDA", "GOOGL", "META", "TSLA", "BRK.B", "UNH", "XOM",
                "JNJ", "JPM", "V", "PG", "MA", "HD", "CVX", "MRK", "ABBV", "LLY",
                "PEP", "KO", "COST", "AVGO", "WMT", "MCD", "CSCO", "TMO", "ABT", "ACN"
                // Top 30 by weight - full list would be 500+
            }),
            ["NDX"] = new("Nasdaq-100", new[]
            {
                "AAPL", "MSFT", "AMZN", "NVDA", "META", "GOOGL", "GOOG", "TSLA", "AVGO", "COST",
                "PEP", "CSCO", "NFLX", "AMD", "ADBE", "INTC", "CMCSA", "TMUS", "TXN", "QCOM"
            }),
            ["DOW"] = new("Dow Jones 30", new[]
            {
                "AAPL", "MSFT", "UNH", "GS", "HD", "MCD", "V", "CAT", "AMGN", "CRM",
                "BA", "HON", "TRV", "AXP", "JPM", "IBM", "JNJ", "PG", "CVX", "MRK",
                "DIS", "NKE", "KO", "MMM", "WBA", "VZ", "INTC", "CSCO", "DOW", "WMT"
            }),
            ["RUSSELL2000"] = new("Russell 2000 (Sample)", new[]
            {
                "AMC", "SFIX", "PLUG", "RKT", "SPCE", "CLOV", "WISH", "SOFI", "HOOD", "RIVN"
            })
        };

    // Ticker/alias -> canonical index id. Add new aliases here.
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SP500"] = "SP500",
            ["SPX"] = "SP500",
            ["S&P500"] = "SP500",
            ["QQQ"] = "NDX",
            ["NDX"] = "NDX",
            ["NASDAQ100"] = "NDX",
            ["DIA"] = "DOW",
            ["DJIA"] = "DOW",
            ["DOW30"] = "DOW",
            ["IWM"] = "RUSSELL2000",
            ["RTY"] = "RUSSELL2000",
            ["RUSSELL2000"] = "RUSSELL2000"
        };

    /// <summary>
    /// Resolves a fallback definition for the given index name or ticker alias
    /// (case-insensitive). Returns <c>false</c> when the index is not in the catalog.
    /// </summary>
    public static bool TryGet(string? indexName, out IndexConstituentDefinition definition)
    {
        if (indexName is not null
            && Aliases.TryGetValue(indexName, out var canonical)
            && Definitions.TryGetValue(canonical, out var resolved))
        {
            definition = resolved;
            return true;
        }

        definition = null!;
        return false;
    }
}
