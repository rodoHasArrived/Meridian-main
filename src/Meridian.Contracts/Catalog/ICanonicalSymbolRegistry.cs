using System.Text.Json.Serialization;

namespace Meridian.Contracts.Catalog;

/// <summary>
/// Service for canonical symbol naming standardization.
/// Provides a unified interface for resolving symbols across providers
/// using canonical names, aliases, and industry identifiers (ISIN, FIGI, SEDOL, CUSIP).
/// </summary>
public interface ICanonicalSymbolRegistry
{
    /// <summary>
    /// Resolves any symbol input (alias, ISIN, FIGI, SEDOL, provider-specific format)
    /// to its canonical representation. Returns null if no match is found.
    /// </summary>
    string? ResolveToCanonical(string input);

    /// <summary>
    /// Resolves a provider-specific symbol to canonical form.
    /// Checks provider-specific mappings first, then falls back to generic resolution.
    /// </summary>
    /// <param name="symbol">Raw symbol from the provider.</param>
    /// <param name="provider">Provider name (e.g., "ALPACA", "POLYGON", "IB").</param>
    /// <returns>Canonical symbol or null if unresolved.</returns>
    string? TryResolve(string symbol, string provider);

    /// <summary>
    /// Registers a symbol with its canonical entry, updating aliases and identifier indexes.
    /// </summary>
    Task RegisterAsync(CanonicalSymbolDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Registers multiple symbols in batch.
    /// </summary>
    Task<int> RegisterBatchAsync(IEnumerable<CanonicalSymbolDefinition> definitions, CancellationToken ct = default);

    /// <summary>
    /// Gets the canonical definition for a symbol. Accepts any known identifier.
    /// </summary>
    CanonicalSymbolDefinition? GetDefinition(string symbolOrIdentifier);

    /// <summary>
    /// Gets all registered canonical definitions.
    /// </summary>
    IReadOnlyList<CanonicalSymbolDefinition> GetAll();

    /// <summary>
    /// Gets canonical definitions filtered by asset class.
    /// </summary>
    IReadOnlyList<CanonicalSymbolDefinition> GetByAssetClass(string assetClass);

    /// <summary>
    /// Gets canonical definitions filtered by exchange.
    /// </summary>
    IReadOnlyList<CanonicalSymbolDefinition> GetByExchange(string exchange);

    /// <summary>
    /// Resolves a symbol to its canonical form using a provider hint for disambiguation.
    /// Checks provider-specific mappings first, then falls back to general resolution.
    /// Returns null if no match is found.
    /// </summary>
    string? TryResolveWithProvider(string symbol, string provider);

    /// <summary>
    /// Checks if a given identifier (canonical, alias, ISIN, FIGI, etc.) is known.
    /// </summary>
    bool IsKnown(string identifier);

    /// <summary>
    /// Removes a symbol from the registry by its canonical name.
    /// </summary>
    Task<bool> RemoveAsync(string canonical, CancellationToken ct = default);

    /// <summary>
    /// Gets the total count of registered canonical symbols.
    /// </summary>
    int Count { get; }
}

/// <summary>
/// Defines a canonical symbol with all known identifiers, aliases, and metadata.
/// This is the standardized representation used for symbol naming consistency.
/// </summary>
public sealed class CanonicalSymbolDefinition
{
    /// <summary>
    /// Internal canonical symbol name (e.g., "AAPL").
    /// This is the primary key for the symbol across the system.
    /// </summary>
    [JsonPropertyName("canonical")]
    public required string Canonical { get; init; }

    /// <summary>
    /// Human-readable display name (e.g., "Apple Inc.").
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    /// <summary>
    /// Known aliases for this symbol (e.g., "AAPL.US", "AAPL.O", "US0378331005").
    /// Includes provider-specific tickers, exchange suffixes, and identifiers.
    /// </summary>
    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Asset class: equity, etf, option, future, forex, crypto, index.
    /// </summary>
    [JsonPropertyName("asset_class")]
    public string AssetClass { get; init; } = "equity";

    /// <summary>
    /// Primary exchange (e.g., "NASDAQ", "NYSE").
    /// </summary>
    [JsonPropertyName("exchange")]
    public string? Exchange { get; init; }

    /// <summary>
    /// Trading currency (e.g., "USD").
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// SEDOL identifier (Stock Exchange Daily Official List).
    /// </summary>
    [JsonPropertyName("sedol")]
    public string? Sedol { get; init; }

    /// <summary>
    /// ISIN identifier (International Securities Identification Number).
    /// </summary>
    [JsonPropertyName("isin")]
    public string? Isin { get; init; }

    /// <summary>
    /// FIGI identifier (Financial Instrument Global Identifier).
    /// </summary>
    [JsonPropertyName("figi")]
    public string? Figi { get; init; }

    /// <summary>
    /// Composite FIGI (aggregate-level identifier).
    /// </summary>
    [JsonPropertyName("compositeFigi")]
    public string? CompositeFigi { get; init; }

    /// <summary>
    /// CUSIP identifier (Committee on Uniform Securities Identification Procedures).
    /// </summary>
    [JsonPropertyName("cusip")]
    public string? Cusip { get; init; }

    /// <summary>
    /// Country of listing (e.g., "US").
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; init; }
}
