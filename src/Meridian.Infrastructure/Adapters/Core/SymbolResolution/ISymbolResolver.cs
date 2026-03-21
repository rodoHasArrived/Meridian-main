using System.Threading;

namespace Meridian.Infrastructure.Adapters.Core.SymbolResolution;

/// <summary>
/// Contract for resolving and normalizing symbols across different providers and exchanges.
/// </summary>
public interface ISymbolResolver
{
    /// <summary>
    /// Resolver name for identification.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Resolve a symbol to its canonical identifiers.
    /// </summary>
    Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default);

    /// <summary>
    /// Map a symbol from one provider format to another.
    /// </summary>
    Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default);

    /// <summary>
    /// Search for symbols matching a query.
    /// </summary>
    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);
}

/// <summary>
/// Canonical symbol resolution with identifiers across systems.
/// </summary>
public sealed record SymbolResolution(
    string Ticker,
    string? Figi = null,
    string? CompositeFigi = null,
    string? ShareClassFigi = null,
    string? Isin = null,
    string? Cusip = null,
    string? Sedol = null,
    string? Name = null,
    string? Exchange = null,
    string? ExchangeCode = null,
    string? MarketSector = null,
    string? SecurityType = null,
    string? Currency = null
)
{
    /// <summary>
    /// Provider-specific symbol mappings (e.g., "yahoo" -> "AAPL", "stooq" -> "aapl.us").
    /// </summary>
    public Dictionary<string, string> ProviderSymbols { get; init; } = new();
}

/// <summary>
/// Symbol search result.
/// </summary>
public sealed record SymbolSearchResult(
    string Ticker,
    string Name,
    string? Exchange = null,
    string? SecurityType = null,
    string? Figi = null,
    double? Score = null
);
