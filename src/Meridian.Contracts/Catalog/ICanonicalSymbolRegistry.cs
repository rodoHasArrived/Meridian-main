using System.Text.Json.Serialization;
using Meridian.Contracts.Domain;

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
    /// Typed overload: resolves a raw <see cref="SymbolId"/> to its
    /// <see cref="CanonicalSymbol"/>. Returns null if no match is found.
    /// </summary>
    CanonicalSymbol? ResolveToCanonical(SymbolId input)
        => ResolveToCanonical(input.Value) is { } s ? new CanonicalSymbol(s) : null;

    /// <summary>
    /// Resolves a provider-specific symbol to canonical form.
    /// Checks provider-specific mappings first, then falls back to generic resolution.
    /// </summary>
    /// <param name="symbol">Raw symbol from the provider.</param>
    /// <param name="provider">Provider name (e.g., "ALPACA", "POLYGON", "IB").</param>
    /// <returns>Canonical symbol or null if unresolved.</returns>
    string? TryResolve(string symbol, string provider);

    /// <summary>
    /// Typed overload: resolves a <see cref="ProviderSymbol"/> to its
    /// <see cref="CanonicalSymbol"/>. Checks provider-specific mappings first,
    /// then falls back to generic resolution. Returns null if unresolved.
    /// </summary>
    CanonicalSymbol? TryResolve(ProviderSymbol providerSymbol)
        => TryResolve(providerSymbol.Symbol.Value, providerSymbol.Provider.Value) is { } s
            ? new CanonicalSymbol(s)
            : null;

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
    /// Gets the outbound provider symbol for a canonical symbol or any known identifier.
    /// Returns <see langword="null"/> when the security is unknown or no explicit provider
    /// mapping has been registered.
    /// </summary>
    string? GetProviderSymbol(string symbolOrIdentifier, string provider);

    /// <summary>
    /// Adds or replaces one provider-scoped symbol mapping without replacing the security
    /// definition or mappings owned by other providers.
    /// </summary>
    Task SetProviderSymbolAsync(
        string canonical,
        string provider,
        string providerSymbol,
        string source = SymbolMappingSources.Operator,
        bool isOverride = true,
        CancellationToken ct = default);

    /// <summary>
    /// Removes one provider-scoped mapping while retaining the canonical security definition.
    /// </summary>
    Task<bool> RemoveProviderSymbolAsync(string canonical, string provider, CancellationToken ct = default);

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
/// Optional atomic-write capability used by startup migrations without expanding the stable
/// symbol-resolution contract implemented by external registry adapters.
/// </summary>
public interface ICanonicalSymbolRegistryMigrationWriter
{
    /// <summary>
    /// Atomically merges one legacy migration batch and its completion fingerprint. A failed or
    /// cancelled write leaves both the retained registry and the in-memory resolver unchanged.
    /// </summary>
    Task<int> ApplyMigrationAsync(
        string migrationId,
        string fingerprint,
        IEnumerable<CanonicalSymbolDefinition> definitions,
        CancellationToken ct = default);
}

/// <summary>
/// Defines a canonical symbol with all known identifiers, aliases, and metadata.
/// This is the standardized representation used for symbol naming consistency.
/// </summary>
public sealed class CanonicalSymbolDefinition
{
    /// <summary>
    /// Current canonical ticker used for lookup and display (e.g., "AAPL").
    /// The durable domain identity is <see cref="SecurityId"/>; tickers may change.
    /// </summary>
    [JsonPropertyName("canonical")]
    public required string Canonical { get; init; }

    /// <summary>
    /// Durable Security Master identity when the symbol has been matched to a security.
    /// </summary>
    [JsonPropertyName("securityId")]
    public Guid? SecurityId { get; init; }

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
    /// Rich alias history when source, provider, or validity windows are known.
    /// </summary>
    [JsonPropertyName("aliasDefinitions")]
    public IReadOnlyList<CanonicalSymbolAliasDefinition> AliasDefinitions { get; init; } = [];

    /// <summary>
    /// Provider-scoped outbound symbols keyed by normalized provider identifier.
    /// </summary>
    [JsonPropertyName("providerSymbols")]
    public IReadOnlyDictionary<string, ProviderSymbolDefinition> ProviderSymbols { get; init; }
        = new Dictionary<string, ProviderSymbolDefinition>(StringComparer.OrdinalIgnoreCase);

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

/// <summary>
/// A provider-scoped symbol together with provenance used to merge learned and curated values safely.
/// </summary>
public sealed record ProviderSymbolDefinition(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("source")] string Source = SymbolMappingSources.Registry,
    [property: JsonPropertyName("isOverride")] bool IsOverride = false,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt = null);

/// <summary>
/// Provider-aware, validity-dated alias retained from Security Master.
/// </summary>
public sealed record CanonicalSymbolAliasDefinition(
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("provider")] string? Provider = null,
    [property: JsonPropertyName("validFrom")] DateTimeOffset? ValidFrom = null,
    [property: JsonPropertyName("validTo")] DateTimeOffset? ValidTo = null,
    [property: JsonPropertyName("isActive")] bool IsActive = true);

/// <summary>
/// Stable provenance values and precedence used by the canonical symbol registry.
/// </summary>
public static class SymbolMappingSources
{
    public const string Operator = "operator";
    public const string SecurityMaster = "security-master";
    public const string LegacyConfig = "legacy-config";
    public const string OpenFigi = "openfigi";
    public const string Registry = "registry";
    public const string FormattingFallback = "formatting-fallback";
}

/// <summary>
/// Controls migration from legacy symbol translation to the canonical registry.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SymbolResolutionMode>))]
public enum SymbolResolutionMode
{
    /// <summary>Return the legacy resolver result.</summary>
    Legacy,
    /// <summary>Return the legacy result and report disagreements with the registry.</summary>
    Compare,
    /// <summary>Return the canonical-registry result, falling back only when unknown.</summary>
    Canonical
}

/// <summary>
/// Structured evidence emitted when comparison mode observes different legacy and canonical results.
/// </summary>
public sealed record SymbolResolutionMismatch(
    string Input,
    string FromProvider,
    string ToProvider,
    string? LegacyResult,
    string? CanonicalResult,
    Guid? SecurityId,
    DateTimeOffset ObservedAt);

/// <summary>
/// Read-only comparison evidence emitted while symbol resolution runs in
/// <see cref="SymbolResolutionMode.Compare"/> mode.
/// </summary>
public interface ISymbolResolutionMismatchReader
{
    /// <summary>Total mismatches observed since this process started.</summary>
    long TotalCount { get; }

    /// <summary>The most recent bounded set of mismatches, newest first.</summary>
    IReadOnlyList<SymbolResolutionMismatch> GetRecent();
}
