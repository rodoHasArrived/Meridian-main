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
    /// Gets all symbols.
    /// </summary>
    IEnumerable<SymbolRegistryEntry> GetAllSymbols();

    /// <summary>
    /// Gets symbols by asset class.
    /// </summary>
    IEnumerable<SymbolRegistryEntry> GetSymbolsByAssetClass(string assetClass);

    /// <summary>
    /// Saves the registry to disk.
    /// </summary>
    Task SaveRegistryAsync(CancellationToken ct = default);

    /// <summary>
    /// Imports symbols from an external source.
    /// </summary>
    Task<int> ImportSymbolsAsync(IEnumerable<SymbolRegistryEntry> symbols, bool merge = true, CancellationToken ct = default);
}
