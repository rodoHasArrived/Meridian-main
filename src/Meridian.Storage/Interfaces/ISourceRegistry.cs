namespace Meridian.Storage.Interfaces;

/// <summary>
/// Interface for managing data source and symbol registry information.
/// </summary>
public interface ISourceRegistry
{
    /// <summary>
    /// Gets information about a registered data source.
    /// </summary>
    SourceInfo? GetSourceInfo(string sourceId);

    /// <summary>
    /// Gets information about a symbol.
    /// </summary>
    SymbolInfo? GetSymbolInfo(string symbol);

    /// <summary>
    /// Gets all registered sources.
    /// </summary>
    IReadOnlyList<SourceInfo> GetAllSources();

    /// <summary>
    /// Gets all registered symbols.
    /// </summary>
    IReadOnlyList<SymbolInfo> GetAllSymbols();

    /// <summary>
    /// Registers or updates a data source.
    /// </summary>
    void RegisterSource(SourceInfo source);

    /// <summary>
    /// Registers or updates a symbol.
    /// </summary>
    void RegisterSymbol(SymbolInfo symbol);

    /// <summary>
    /// Resolves a symbol alias to its canonical name.
    /// </summary>
    string ResolveSymbolAlias(string alias);

    /// <summary>
    /// Gets the priority order for conflict resolution.
    /// </summary>
    string[] GetSourcePriorityOrder();
}

/// <summary>
/// Information about a data source.
/// </summary>
public sealed record SourceInfo(
    string Id,
    string Name,
    SourceType Type,
    int Priority = 1,
    string[]? AssetClasses = null,
    string[]? DataTypes = null,
    double? LatencyMs = null,
    double? Reliability = null,
    decimal? CostPerEvent = null,
    bool Enabled = true
);

/// <summary>
/// Type of data source.
/// </summary>
public enum SourceType : byte
{
    /// <summary>Live real-time data source.</summary>
    Live,
    /// <summary>Historical backfill data source.</summary>
    Historical,
    /// <summary>Consolidated/derived data source.</summary>
    Consolidated
}

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed record SymbolInfo(
    string Symbol,
    string Canonical,
    string[]? Aliases = null,
    string? AssetClass = null,
    string? Exchange = null,
    string? Currency = null,
    string? Sedol = null,
    string? Isin = null,
    string? Figi = null,
    string? Sector = null,
    string? Industry = null,
    Dictionary<string, string>? Metadata = null
);
