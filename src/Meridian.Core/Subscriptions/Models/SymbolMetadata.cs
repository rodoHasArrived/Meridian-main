namespace Meridian.Application.Subscriptions.Models;

/// <summary>
/// Enriched metadata for a symbol including industry, sector, and market data.
/// </summary>
public sealed record SymbolMetadata(
    string Symbol,

    string Name,

    string? Sector = null,

    string? Industry = null,

    decimal? MarketCap = null,

    MarketCapCategory? MarketCapCategory = null,

    string? Exchange = null,

    string? Country = null,

    string? AssetType = null,

    bool? PaysDividend = null,

    string[]? IndexMemberships = null,

    DateTimeOffset? LastUpdated = null
);

/// <summary>
/// Market capitalization categories.
/// </summary>
public enum MarketCapCategory : byte
{
    Nano,

    Micro,

    Small,

    Mid,

    Large,

    Mega
}

/// <summary>
/// Filter criteria for symbol metadata queries.
/// </summary>
public sealed record SymbolMetadataFilter(
    string? Sector = null,

    string? Industry = null,

    decimal? MinMarketCap = null,

    decimal? MaxMarketCap = null,

    MarketCapCategory? MarketCapCategory = null,

    string? Exchange = null,

    string? Country = null,

    string? AssetType = null,

    string? IndexMembership = null,

    bool? PaysDividend = null
);

/// <summary>
/// Result of a metadata filter operation.
/// </summary>
public sealed record MetadataFilterResult(
    SymbolMetadata[] Symbols,
    int TotalCount,
    SymbolMetadataFilter AppliedFilter
);
