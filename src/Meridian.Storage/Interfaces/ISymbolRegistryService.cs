using Meridian.Contracts.Catalog;

namespace Meridian.Storage.Interfaces;

/// <summary>
/// Service for managing the symbol registry.
/// </summary>
public interface ISymbolRegistryService
{
    /// <summary>
    /// Gets the symbol registry.
    /// </summary>
    SymbolRegistry GetRegistry();

    /// <summary>
    /// Initializes the registry from storage.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers or updates a symbol entry.
    /// </summary>
    Task RegisterSymbolAsync(SymbolRegistryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Looks up a symbol by any identifier (canonical, alias, ISIN, FIGI, etc.).
    /// </summary>
    SymbolLookupResult LookupSymbol(string query);

    /// <summary>
    /// Resolves an alias to a canonical symbol.
    /// </summary>
    string ResolveAlias(string alias);

    /// <summary>
    /// Gets the provider-specific symbol for a canonical symbol.
    /// </summary>
    string? GetProviderSymbol(string canonical, string provider);

    /// <summary>
    /// Adds an alias for a symbol.
    /// </summary>
    Task AddAliasAsync(string canonical, SymbolAlias alias, CancellationToken ct = default);

    /// <summary>
    /// Adds a provider mapping.
    /// </summary>
    Task AddProviderMappingAsync(string canonical, string provider, string providerSymbol, CancellationToken ct = default);

    /// <summary>
    /// Adds a provider mapping with provenance and merge precedence.
    /// </summary>
    Task AddProviderMappingAsync(
        string canonical,
        string provider,
        string providerSymbol,
        string source,
        bool isOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Removes a provider mapping without removing the canonical security entry.
    /// </summary>
    Task<bool> RemoveProviderMappingAsync(string canonical, string provider, CancellationToken ct = default);

    /// <summary>
    /// Gets all symbols.
    /// </summary>
    IEnumerable<SymbolRegistryEntry> GetAllSymbols();

    /// <summary>
    /// Gets symbols by asset class.
    /// </summary>
    IEnumerable<SymbolRegistryEntry> GetSymbolsByAssetClass(string assetClass);

    /// <summary>
    /// Gets the retained fingerprint for a completed registry migration while holding the
    /// registry mutation gate.
    /// </summary>
    Task<string?> GetMigrationMarkerAsync(string migrationId, CancellationToken ct = default);

    /// <summary>
    /// Persists a registry migration fingerprint while holding the registry mutation gate.
    /// </summary>
    Task SetMigrationMarkerAsync(string migrationId, string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Saves the registry to disk.
    /// </summary>
    Task SaveRegistryAsync(CancellationToken ct = default);

    /// <summary>
    /// Imports symbols from an external source.
    /// </summary>
    Task<int> ImportSymbolsAsync(IEnumerable<SymbolRegistryEntry> symbols, bool merge = true, CancellationToken ct = default);
}
