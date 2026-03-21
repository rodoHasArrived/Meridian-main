using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Canonical symbol registry providing standardized symbol naming across the system.
/// Wraps the underlying <see cref="ISymbolRegistryService"/> to provide a unified
/// resolution interface that accepts any known identifier (canonical, alias, ISIN,
/// FIGI, SEDOL, CUSIP, provider-specific ticker) and resolves it to the canonical name.
/// </summary>
public sealed class CanonicalSymbolRegistry : ICanonicalSymbolRegistry
{
    private readonly ILogger _log = LoggingSetup.ForContext<CanonicalSymbolRegistry>();
    private readonly ISymbolRegistryService _registryService;

    /// <summary>
    /// Fast reverse-lookup from any known identifier string to canonical symbol.
    /// Populated during initialization and kept in sync on registration.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _resolverCache = new(StringComparer.OrdinalIgnoreCase);

    public CanonicalSymbolRegistry(ISymbolRegistryService registryService)
    {
        _registryService = registryService ?? throw new ArgumentNullException(nameof(registryService));
        RebuildResolverCache();
    }

    /// <inheritdoc />
    public int Count => _registryService.GetRegistry().Symbols.Count;

    /// <inheritdoc />
    public string? ResolveToCanonical(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim();

        // Fast path: check the unified resolver cache
        if (_resolverCache.TryGetValue(normalized, out var canonical))
            return canonical;

        // Fallback to underlying service lookup (includes fuzzy matching)
        var result = _registryService.LookupSymbol(normalized);
        if (result.Found && result.CanonicalSymbol is not null)
        {
            // Cache the successful resolution for future lookups
            _resolverCache[normalized] = result.CanonicalSymbol;
            return result.CanonicalSymbol;
        }

        return null;
    }

    /// <inheritdoc />
    public string? TryResolve(string symbol, string provider)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var normalized = symbol.Trim();

        // Fast path: check provider-specific mappings first
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var registry = _registryService.GetRegistry();
            var upperProvider = provider.ToUpperInvariant();
            if (registry.ProviderMappings.TryGetValue(upperProvider, out var mappings) &&
                mappings.TryGetValue(normalized, out var canonical))
            {
                return canonical;
            }
        }

        // Fall back to generic resolution
        return ResolveToCanonical(normalized);
    }

    /// <inheritdoc />
    public async Task RegisterAsync(CanonicalSymbolDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Canonical))
            throw new ArgumentException("Canonical symbol name is required.", nameof(definition));

        var entry = ToRegistryEntry(definition);
        await _registryService.RegisterSymbolAsync(entry, ct);

        // Update the resolver cache with all known identifiers for this symbol
        IndexDefinition(definition);

        _log.Information("Registered canonical symbol {Symbol} with {AliasCount} aliases",
            definition.Canonical, definition.Aliases.Count);
    }

    /// <inheritdoc />
    public async Task<int> RegisterBatchAsync(IEnumerable<CanonicalSymbolDefinition> definitions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var entries = definitions.Select(ToRegistryEntry).ToList();
        var count = await _registryService.ImportSymbolsAsync(entries, merge: true, ct);

        RebuildResolverCache();

        _log.Information("Batch registered {Count} canonical symbols", count);
        return count;
    }

    /// <inheritdoc />
    public CanonicalSymbolDefinition? GetDefinition(string symbolOrIdentifier)
    {
        if (string.IsNullOrWhiteSpace(symbolOrIdentifier))
            return null;

        var canonical = ResolveToCanonical(symbolOrIdentifier);
        if (canonical is null)
            return null;

        var registry = _registryService.GetRegistry();
        if (!registry.Symbols.TryGetValue(canonical, out var entry))
            return null;

        return FromRegistryEntry(entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<CanonicalSymbolDefinition> GetAll()
    {
        return _registryService.GetAllSymbols()
            .Select(FromRegistryEntry)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<CanonicalSymbolDefinition> GetByAssetClass(string assetClass)
    {
        return _registryService.GetSymbolsByAssetClass(assetClass)
            .Select(FromRegistryEntry)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<CanonicalSymbolDefinition> GetByExchange(string exchange)
    {
        if (string.IsNullOrWhiteSpace(exchange))
            return [];

        return _registryService.GetAllSymbols()
            .Where(e => string.Equals(e.Exchange, exchange, StringComparison.OrdinalIgnoreCase))
            .Select(FromRegistryEntry)
            .ToList();
    }

    /// <inheritdoc />
    public string? TryResolveWithProvider(string symbol, string provider)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(provider))
            return null;

        var normalized = symbol.Trim();

        // Check provider-specific mappings first (highest priority for disambiguation)
        var registry = _registryService.GetRegistry();
        if (registry.ProviderMappings.TryGetValue(provider, out var providerMap) &&
            providerMap.TryGetValue(normalized, out var canonical))
        {
            return canonical;
        }

        // Check ProviderSymbols on individual entries for this provider
        foreach (var (canonicalName, entry) in registry.Symbols)
        {
            if (entry.ProviderSymbols.TryGetValue(provider, out var providerSymbol) &&
                string.Equals(providerSymbol, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return canonicalName;
            }
        }

        // Fall back to general resolution (cache, alias index, fuzzy match)
        return ResolveToCanonical(normalized);
    }

    /// <inheritdoc />
    public bool IsKnown(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        return _resolverCache.ContainsKey(identifier.Trim());
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string canonical, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            return false;

        var registry = _registryService.GetRegistry();
        if (!registry.Symbols.TryGetValue(canonical, out var entry))
            return false;

        // Remove from registry
        registry.Symbols.Remove(canonical);

        // Remove alias index entries
        foreach (var alias in entry.Aliases)
        {
            registry.AliasIndex.Remove(alias.Alias);
        }

        // Remove identifier index entries
        RemoveIdentifierIndexEntries(registry, entry);

        // Remove provider mapping entries
        foreach (var (provider, mappings) in registry.ProviderMappings)
        {
            var keysToRemove = mappings
                .Where(kv => kv.Value.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                mappings.Remove(key);
            }
        }

        await _registryService.SaveRegistryAsync(ct);
        RebuildResolverCache();

        _log.Information("Removed canonical symbol {Symbol}", canonical);
        return true;
    }

    /// <summary>
    /// Rebuilds the unified resolver cache from the current registry state.
    /// </summary>
    private void RebuildResolverCache()
    {
        _resolverCache.Clear();

        var registry = _registryService.GetRegistry();

        foreach (var (canonical, entry) in registry.Symbols)
        {
            // Index the canonical name itself
            _resolverCache[canonical] = canonical;

            // Index all aliases
            foreach (var alias in entry.Aliases)
            {
                _resolverCache.TryAdd(alias.Alias, canonical);
            }

            // Index all industry identifiers
            IndexIdentifiers(entry.Identifiers, canonical);

            // Index all provider symbols
            foreach (var (_, providerSymbol) in entry.ProviderSymbols)
            {
                _resolverCache.TryAdd(providerSymbol, canonical);
            }
        }

        // Also include the top-level alias index
        foreach (var (alias, canonical) in registry.AliasIndex)
        {
            _resolverCache.TryAdd(alias, canonical);
        }

        // Include provider mapping entries
        foreach (var (_, mappings) in registry.ProviderMappings)
        {
            foreach (var (providerSymbol, canonical) in mappings)
            {
                _resolverCache.TryAdd(providerSymbol, canonical);
            }
        }

        // Include identifier index entries
        foreach (var (isin, canonical) in registry.IdentifierIndex.IsinToSymbol)
            _resolverCache.TryAdd(isin, canonical);
        foreach (var (figi, canonical) in registry.IdentifierIndex.FigiToSymbol)
            _resolverCache.TryAdd(figi, canonical);
        foreach (var (cusip, canonical) in registry.IdentifierIndex.CusipToSymbol)
            _resolverCache.TryAdd(cusip, canonical);
        foreach (var (sedol, canonical) in registry.IdentifierIndex.SedolToSymbol)
            _resolverCache.TryAdd(sedol, canonical);
    }

    /// <summary>
    /// Indexes all identifiers for a single definition into the resolver cache.
    /// </summary>
    private void IndexDefinition(CanonicalSymbolDefinition definition)
    {
        var canonical = definition.Canonical;

        _resolverCache[canonical] = canonical;

        foreach (var alias in definition.Aliases)
        {
            _resolverCache.TryAdd(alias, canonical);
        }

        if (!string.IsNullOrEmpty(definition.Isin))
            _resolverCache.TryAdd(definition.Isin, canonical);
        if (!string.IsNullOrEmpty(definition.Figi))
            _resolverCache.TryAdd(definition.Figi, canonical);
        if (!string.IsNullOrEmpty(definition.CompositeFigi))
            _resolverCache.TryAdd(definition.CompositeFigi, canonical);
        if (!string.IsNullOrEmpty(definition.Sedol))
            _resolverCache.TryAdd(definition.Sedol, canonical);
        if (!string.IsNullOrEmpty(definition.Cusip))
            _resolverCache.TryAdd(definition.Cusip, canonical);
    }

    private void IndexIdentifiers(SymbolIdentifiers identifiers, string canonical)
    {
        if (!string.IsNullOrEmpty(identifiers.Isin))
            _resolverCache.TryAdd(identifiers.Isin, canonical);
        if (!string.IsNullOrEmpty(identifiers.Figi))
            _resolverCache.TryAdd(identifiers.Figi, canonical);
        if (!string.IsNullOrEmpty(identifiers.CompositeFigi))
            _resolverCache.TryAdd(identifiers.CompositeFigi, canonical);
        if (!string.IsNullOrEmpty(identifiers.ShareClassFigi))
            _resolverCache.TryAdd(identifiers.ShareClassFigi, canonical);
        if (!string.IsNullOrEmpty(identifiers.Sedol))
            _resolverCache.TryAdd(identifiers.Sedol, canonical);
        if (!string.IsNullOrEmpty(identifiers.Cusip))
            _resolverCache.TryAdd(identifiers.Cusip, canonical);
        if (!string.IsNullOrEmpty(identifiers.BloombergId))
            _resolverCache.TryAdd(identifiers.BloombergId, canonical);
        if (!string.IsNullOrEmpty(identifiers.Ric))
            _resolverCache.TryAdd(identifiers.Ric, canonical);
    }

    private static void RemoveIdentifierIndexEntries(SymbolRegistry registry, SymbolRegistryEntry entry)
    {
        var ids = entry.Identifiers;

        if (!string.IsNullOrEmpty(ids.Isin))
            registry.IdentifierIndex.IsinToSymbol.Remove(ids.Isin);
        if (!string.IsNullOrEmpty(ids.Figi))
            registry.IdentifierIndex.FigiToSymbol.Remove(ids.Figi);
        if (!string.IsNullOrEmpty(ids.CompositeFigi))
            registry.IdentifierIndex.FigiToSymbol.Remove(ids.CompositeFigi);
        if (!string.IsNullOrEmpty(ids.Cusip))
            registry.IdentifierIndex.CusipToSymbol.Remove(ids.Cusip);
        if (!string.IsNullOrEmpty(ids.Sedol))
            registry.IdentifierIndex.SedolToSymbol.Remove(ids.Sedol);
    }

    /// <summary>
    /// Converts a <see cref="CanonicalSymbolDefinition"/> to a <see cref="SymbolRegistryEntry"/>.
    /// </summary>
    private static SymbolRegistryEntry ToRegistryEntry(CanonicalSymbolDefinition definition)
    {
        var aliases = definition.Aliases
            .Select(a => new SymbolAlias
            {
                Alias = a,
                Source = ClassifyAliasSource(a),
                Type = ClassifyAliasType(a),
                IsActive = true
            })
            .ToList();

        return new SymbolRegistryEntry
        {
            Canonical = definition.Canonical,
            DisplayName = definition.DisplayName,
            AssetClass = definition.AssetClass,
            Exchange = definition.Exchange,
            Currency = definition.Currency,
            Country = definition.Country,
            Aliases = aliases,
            Identifiers = new SymbolIdentifiers
            {
                Isin = definition.Isin,
                Figi = definition.Figi,
                CompositeFigi = definition.CompositeFigi,
                Sedol = definition.Sedol,
                Cusip = definition.Cusip
            },
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Converts a <see cref="SymbolRegistryEntry"/> to a <see cref="CanonicalSymbolDefinition"/>.
    /// </summary>
    private static CanonicalSymbolDefinition FromRegistryEntry(SymbolRegistryEntry entry)
    {
        var aliases = entry.Aliases
            .Where(a => a.IsActive)
            .Select(a => a.Alias)
            .ToList();

        return new CanonicalSymbolDefinition
        {
            Canonical = entry.Canonical,
            DisplayName = entry.DisplayName,
            Aliases = aliases,
            AssetClass = entry.AssetClass,
            Exchange = entry.Exchange,
            Currency = entry.Currency,
            Country = entry.Country,
            Sedol = entry.Identifiers.Sedol,
            Isin = entry.Identifiers.Isin,
            Figi = entry.Identifiers.Figi,
            CompositeFigi = entry.Identifiers.CompositeFigi,
            Cusip = entry.Identifiers.Cusip
        };
    }

    /// <summary>
    /// Classifies the source of an alias based on its format.
    /// </summary>
    private static string ClassifyAliasSource(string alias)
    {
        if (alias.EndsWith(".US", StringComparison.OrdinalIgnoreCase) ||
            alias.EndsWith(".UK", StringComparison.OrdinalIgnoreCase))
            return "exchange-suffix";

        if (alias.Contains('.') && alias.Length <= 10)
            return "reuters";

        if (alias.Length == 12 && alias.StartsWith("US", StringComparison.OrdinalIgnoreCase) &&
            alias.Skip(2).All(char.IsDigit))
            return "isin";

        if (alias.StartsWith("BBG", StringComparison.OrdinalIgnoreCase))
            return "figi";

        return "manual";
    }

    /// <summary>
    /// Classifies the type of an alias based on its format.
    /// </summary>
    private static string ClassifyAliasType(string alias)
    {
        if (alias.Length == 12 && alias[..2].All(char.IsLetter) && alias[2..].All(char.IsDigit))
            return "isin";

        if (alias.StartsWith("BBG", StringComparison.OrdinalIgnoreCase) && alias.Length == 12)
            return "figi";

        if (alias.Length >= 6 && alias.Length <= 7 && alias.All(char.IsLetterOrDigit))
            return "sedol";

        if (alias.Contains('.') || alias.Contains(' '))
            return "exchange-ticker";

        return "ticker";
    }
}
