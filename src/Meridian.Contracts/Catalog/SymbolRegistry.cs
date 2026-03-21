using System.Text.Json.Serialization;

namespace Meridian.Contracts.Catalog;

/// <summary>
/// Symbol registry for alias resolution across providers.
/// Stored at _catalog/symbols.json.
/// </summary>
public sealed class SymbolRegistry
{
    /// <summary>
    /// Registry format version.
    /// </summary>
    [JsonPropertyName("registryVersion")]
    public string RegistryVersion { get; set; } = "1.0.0";

    /// <summary>
    /// When the registry was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the registry was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Symbol entries keyed by canonical symbol name.
    /// </summary>
    [JsonPropertyName("symbols")]
    public Dictionary<string, SymbolRegistryEntry> Symbols { get; set; } = new();

    /// <summary>
    /// Alias lookup table mapping aliases to canonical symbols.
    /// </summary>
    [JsonPropertyName("aliasIndex")]
    public Dictionary<string, string> AliasIndex { get; set; } = new();

    /// <summary>
    /// Provider-specific symbol mappings.
    /// Key: provider name, Value: provider symbol to canonical symbol mapping.
    /// </summary>
    [JsonPropertyName("providerMappings")]
    public Dictionary<string, Dictionary<string, string>> ProviderMappings { get; set; } = new();

    /// <summary>
    /// Industry identifiers index (ISIN, FIGI, SEDOL to canonical symbol).
    /// </summary>
    [JsonPropertyName("identifierIndex")]
    public IdentifierIndex IdentifierIndex { get; set; } = new();

    /// <summary>
    /// Statistics about the registry.
    /// </summary>
    [JsonPropertyName("statistics")]
    public SymbolRegistryStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Individual symbol entry in the registry.
/// </summary>
public sealed class SymbolRegistryEntry
{
    /// <summary>
    /// Canonical symbol name used internally.
    /// </summary>
    [JsonPropertyName("canonical")]
    public string Canonical { get; set; } = string.Empty;

    /// <summary>
    /// Display name (e.g., "Apple Inc.").
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Asset class (equity, option, future, forex, crypto, index).
    /// </summary>
    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = "equity";

    /// <summary>
    /// Primary exchange.
    /// </summary>
    [JsonPropertyName("exchange")]
    public string? Exchange { get; set; }

    /// <summary>
    /// Base currency.
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Country of listing.
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>
    /// All known aliases for this symbol.
    /// </summary>
    [JsonPropertyName("aliases")]
    public List<SymbolAlias> Aliases { get; set; } = new();

    /// <summary>
    /// Industry identifiers.
    /// </summary>
    [JsonPropertyName("identifiers")]
    public SymbolIdentifiers Identifiers { get; set; } = new();

    /// <summary>
    /// Provider-specific symbols.
    /// </summary>
    [JsonPropertyName("providerSymbols")]
    public Dictionary<string, string> ProviderSymbols { get; set; } = new();

    /// <summary>
    /// Classification information.
    /// </summary>
    [JsonPropertyName("classification")]
    public SymbolClassification? Classification { get; set; }

    /// <summary>
    /// Corporate action history affecting symbol.
    /// </summary>
    [JsonPropertyName("corporateActions")]
    public List<CorporateActionRef>? CorporateActions { get; set; }

    /// <summary>
    /// When this entry was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this entry was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this symbol is currently active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Date when symbol was delisted (if applicable).
    /// </summary>
    [JsonPropertyName("delistedAt")]
    public DateTime? DelistedAt { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Symbol alias with source information.
/// </summary>
public sealed class SymbolAlias
{
    /// <summary>
    /// The alias string.
    /// </summary>
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    /// <summary>
    /// Source of this alias (provider name, exchange, etc.).
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Type of alias (ticker, ric, bloomberg, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Whether this alias is still valid.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this alias was valid from.
    /// </summary>
    [JsonPropertyName("validFrom")]
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// When this alias stopped being valid.
    /// </summary>
    [JsonPropertyName("validTo")]
    public DateTime? ValidTo { get; set; }
}

/// <summary>
/// Industry standard identifiers for a symbol.
/// </summary>
public sealed class SymbolIdentifiers
{
    /// <summary>
    /// ISIN (International Securities Identification Number).
    /// </summary>
    [JsonPropertyName("isin")]
    public string? Isin { get; set; }

    /// <summary>
    /// FIGI (Financial Instrument Global Identifier).
    /// </summary>
    [JsonPropertyName("figi")]
    public string? Figi { get; set; }

    /// <summary>
    /// Composite FIGI.
    /// </summary>
    [JsonPropertyName("compositeFigi")]
    public string? CompositeFigi { get; set; }

    /// <summary>
    /// Share class FIGI.
    /// </summary>
    [JsonPropertyName("shareClassFigi")]
    public string? ShareClassFigi { get; set; }

    /// <summary>
    /// SEDOL (Stock Exchange Daily Official List).
    /// </summary>
    [JsonPropertyName("sedol")]
    public string? Sedol { get; set; }

    /// <summary>
    /// CUSIP (Committee on Uniform Securities Identification Procedures).
    /// </summary>
    [JsonPropertyName("cusip")]
    public string? Cusip { get; set; }

    /// <summary>
    /// CIK (SEC Central Index Key).
    /// </summary>
    [JsonPropertyName("cik")]
    public string? Cik { get; set; }

    /// <summary>
    /// LEI (Legal Entity Identifier).
    /// </summary>
    [JsonPropertyName("lei")]
    public string? Lei { get; set; }

    /// <summary>
    /// Bloomberg ID.
    /// </summary>
    [JsonPropertyName("bloombergId")]
    public string? BloombergId { get; set; }

    /// <summary>
    /// Reuters RIC.
    /// </summary>
    [JsonPropertyName("ric")]
    public string? Ric { get; set; }
}

/// <summary>
/// Symbol classification information.
/// </summary>
public sealed class SymbolClassification
{
    /// <summary>
    /// GICS Sector.
    /// </summary>
    [JsonPropertyName("sector")]
    public string? Sector { get; set; }

    /// <summary>
    /// GICS Industry Group.
    /// </summary>
    [JsonPropertyName("industryGroup")]
    public string? IndustryGroup { get; set; }

    /// <summary>
    /// GICS Industry.
    /// </summary>
    [JsonPropertyName("industry")]
    public string? Industry { get; set; }

    /// <summary>
    /// GICS Sub-Industry.
    /// </summary>
    [JsonPropertyName("subIndustry")]
    public string? SubIndustry { get; set; }

    /// <summary>
    /// Market cap category (mega, large, mid, small, micro).
    /// </summary>
    [JsonPropertyName("marketCapCategory")]
    public string? MarketCapCategory { get; set; }

    /// <summary>
    /// Index memberships.
    /// </summary>
    [JsonPropertyName("indexMemberships")]
    public string[]? IndexMemberships { get; set; }
}

/// <summary>
/// Reference to a corporate action affecting the symbol.
/// </summary>
public sealed class CorporateActionRef
{
    /// <summary>
    /// Type of corporate action (split, merger, spinoff, rename).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Effective date of the action.
    /// </summary>
    [JsonPropertyName("effectiveDate")]
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Description of the action.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Previous symbol (for renames).
    /// </summary>
    [JsonPropertyName("previousSymbol")]
    public string? PreviousSymbol { get; set; }

    /// <summary>
    /// Split ratio (e.g., "4:1" for 4-for-1 split).
    /// </summary>
    [JsonPropertyName("splitRatio")]
    public string? SplitRatio { get; set; }
}

/// <summary>
/// Index of industry identifiers to canonical symbols.
/// </summary>
public sealed class IdentifierIndex
{
    /// <summary>
    /// ISIN to canonical symbol.
    /// </summary>
    [JsonPropertyName("isinToSymbol")]
    public Dictionary<string, string> IsinToSymbol { get; set; } = new();

    /// <summary>
    /// FIGI to canonical symbol.
    /// </summary>
    [JsonPropertyName("figiToSymbol")]
    public Dictionary<string, string> FigiToSymbol { get; set; } = new();

    /// <summary>
    /// CUSIP to canonical symbol.
    /// </summary>
    [JsonPropertyName("cusipToSymbol")]
    public Dictionary<string, string> CusipToSymbol { get; set; } = new();

    /// <summary>
    /// SEDOL to canonical symbol.
    /// </summary>
    [JsonPropertyName("sedolToSymbol")]
    public Dictionary<string, string> SedolToSymbol { get; set; } = new();
}

/// <summary>
/// Statistics about the symbol registry.
/// </summary>
public sealed class SymbolRegistryStatistics
{
    /// <summary>
    /// Total number of symbols.
    /// </summary>
    [JsonPropertyName("totalSymbols")]
    public int TotalSymbols { get; set; }

    /// <summary>
    /// Number of active symbols.
    /// </summary>
    [JsonPropertyName("activeSymbols")]
    public int ActiveSymbols { get; set; }

    /// <summary>
    /// Number of delisted symbols.
    /// </summary>
    [JsonPropertyName("delistedSymbols")]
    public int DelistedSymbols { get; set; }

    /// <summary>
    /// Total number of aliases.
    /// </summary>
    [JsonPropertyName("totalAliases")]
    public int TotalAliases { get; set; }

    /// <summary>
    /// Number of providers with mappings.
    /// </summary>
    [JsonPropertyName("providerCount")]
    public int ProviderCount { get; set; }

    /// <summary>
    /// Breakdown by asset class.
    /// </summary>
    [JsonPropertyName("byAssetClass")]
    public Dictionary<string, int> ByAssetClass { get; set; } = new();

    /// <summary>
    /// Breakdown by exchange.
    /// </summary>
    [JsonPropertyName("byExchange")]
    public Dictionary<string, int> ByExchange { get; set; } = new();
}

/// <summary>
/// Result of a symbol lookup operation.
/// </summary>
public sealed class SymbolLookupResult
{
    /// <summary>
    /// Whether a match was found.
    /// </summary>
    [JsonPropertyName("found")]
    public bool Found { get; set; }

    /// <summary>
    /// The query that was searched.
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// How the match was made (canonical, alias, identifier, provider).
    /// </summary>
    [JsonPropertyName("matchType")]
    public string? MatchType { get; set; }

    /// <summary>
    /// The canonical symbol if found.
    /// </summary>
    [JsonPropertyName("canonicalSymbol")]
    public string? CanonicalSymbol { get; set; }

    /// <summary>
    /// The full symbol entry if found.
    /// </summary>
    [JsonPropertyName("entry")]
    public SymbolRegistryEntry? Entry { get; set; }

    /// <summary>
    /// Similar symbols if exact match not found.
    /// </summary>
    [JsonPropertyName("suggestions")]
    public string[]? Suggestions { get; set; }
}
