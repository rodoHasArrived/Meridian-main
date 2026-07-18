using System.Collections.Concurrent;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Interfaces;

namespace Meridian.Storage.Services;

/// <summary>
/// Canonical symbol registry providing standardized symbol naming across the system.
/// Wraps the underlying <see cref="ISymbolRegistryService"/> to provide a unified
/// resolution interface that accepts any known identifier (canonical, alias, ISIN,
/// FIGI, SEDOL, CUSIP, provider-specific ticker) and resolves it to the canonical name.
/// </summary>
public sealed class CanonicalSymbolRegistry : ICanonicalSymbolRegistry
{
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

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var registry = _registryService.GetRegistry();
            var resolution = ResolveProviderScoped(
                registry,
                normalized,
                NormalizeProvider(provider),
                out var canonical);
            if (resolution == ProviderScopedResolution.Resolved)
                return canonical;
            if (resolution == ProviderScopedResolution.Ambiguous)
                return null;
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

        // Registration is merge-safe; rebuild from the persisted merged state so learned
        // information cannot evict Security Master identifiers or provider overrides.
        RebuildResolverCache();

    }

    /// <inheritdoc />
    public async Task<int> RegisterBatchAsync(IEnumerable<CanonicalSymbolDefinition> definitions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var entries = definitions.Select(ToRegistryEntry).ToList();
        var count = await _registryService.ImportSymbolsAsync(entries, merge: true, ct);

        RebuildResolverCache();

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

        var registry = _registryService.GetRegistry();
        var providerKey = NormalizeProvider(provider);
        var resolution = ResolveProviderScoped(registry, normalized, providerKey, out var canonical);
        if (resolution == ProviderScopedResolution.Resolved)
            return canonical;
        if (resolution == ProviderScopedResolution.Ambiguous)
            return null;

        // Fall back to general resolution (cache, alias index, fuzzy match)
        return ResolveToCanonical(normalized);
    }

    /// <inheritdoc />
    public string? GetProviderSymbol(string symbolOrIdentifier, string provider)
    {
        if (string.IsNullOrWhiteSpace(symbolOrIdentifier) || string.IsNullOrWhiteSpace(provider))
            return null;

        var normalizedProvider = NormalizeProvider(provider);
        var normalizedSymbol = symbolOrIdentifier.Trim();
        var resolution = ResolveProviderScoped(
            _registryService.GetRegistry(),
            normalizedSymbol,
            normalizedProvider,
            out var canonical);
        if (resolution == ProviderScopedResolution.Ambiguous)
            return null;
        canonical ??= ResolveToCanonical(normalizedSymbol);
        return canonical is null
            ? null
            : _registryService.GetProviderSymbol(canonical, normalizedProvider);
    }

    /// <inheritdoc />
    public async Task SetProviderSymbolAsync(
        string canonical,
        string provider,
        string providerSymbol,
        string source = SymbolMappingSources.Operator,
        bool isOverride = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSymbol);

        var resolvedCanonical = ResolveToCanonical(canonical) ?? canonical.Trim().ToUpperInvariant();
        await _registryService.AddProviderMappingAsync(
            resolvedCanonical,
            NormalizeProvider(provider),
            providerSymbol.Trim(),
            source,
            isOverride,
            ct).ConfigureAwait(false);
        RebuildResolverCache();
    }

    /// <inheritdoc />
    public async Task<bool> RemoveProviderSymbolAsync(
        string canonical,
        string provider,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(provider))
            return false;

        var resolvedCanonical = ResolveToCanonical(canonical) ?? canonical.Trim().ToUpperInvariant();
        var removed = await _registryService.RemoveProviderMappingAsync(
            resolvedCanonical,
            NormalizeProvider(provider),
            ct).ConfigureAwait(false);
        if (removed)
            RebuildResolverCache();
        return removed;
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

        return true;
    }

    /// <summary>
    /// Rebuilds the unified resolver cache from the current registry state.
    /// </summary>
    private void RebuildResolverCache()
    {
        _resolverCache.Clear();

        var registry = _registryService.GetRegistry();
        var aliasCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, entry) in registry.Symbols)
        {
            // Index the canonical name itself
            _resolverCache[canonical] = canonical;

            // Index all aliases
            foreach (var alias in entry.Aliases.Where(IsGenericResolvableAlias))
            {
                AddResolverCandidate(aliasCandidates, alias.Alias, canonical);
            }

            // Index all industry identifiers
            IndexIdentifiers(entry.Identifiers, canonical);

        }

        foreach (var (alias, canonicals) in aliasCandidates)
        {
            if (canonicals.Count == 1)
                _resolverCache.TryAdd(alias, canonicals.Single());
        }

        // Also include the top-level alias index
        foreach (var (alias, canonical) in registry.AliasIndex)
        {
            _resolverCache.TryAdd(alias, canonical);
        }

        // Provider-aware lookup remains scoped. A provider symbol is safe in the generic
        // cache only when every provider that uses that spelling points to one security.
        // This prevents dictionary iteration order from choosing a security when two
        // providers reuse the same ticker for different instruments.
        var providerSymbolCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in registry.Symbols.Values)
        {
            foreach (var providerSymbol in entry.ProviderSymbols.Values)
                AddResolverCandidate(providerSymbolCandidates, providerSymbol, entry.Canonical);
        }
        foreach (var mappings in registry.ProviderMappings.Values)
        {
            foreach (var (providerSymbol, canonical) in mappings)
                AddResolverCandidate(providerSymbolCandidates, providerSymbol, canonical);
        }
        foreach (var (providerSymbol, canonicals) in providerSymbolCandidates)
        {
            if (canonicals.Count == 1)
                _resolverCache.TryAdd(providerSymbol, canonicals.Single());
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

    private static void AddResolverCandidate(
        Dictionary<string, HashSet<string>> candidates,
        string symbol,
        string canonical)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(canonical))
            return;

        if (!candidates.TryGetValue(symbol.Trim(), out var canonicals))
        {
            canonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            candidates[symbol.Trim()] = canonicals;
        }

        canonicals.Add(canonical.Trim());
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

        foreach (var alias in definition.AliasDefinitions)
        {
            var provider = string.IsNullOrWhiteSpace(alias.Provider)
                ? null
                : NormalizeProvider(alias.Provider);
            if (aliases.Any(existing =>
                    existing.Alias.Equals(alias.Alias, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.Provider, provider, StringComparison.OrdinalIgnoreCase)))
                continue;

            aliases.Add(new SymbolAlias
            {
                Alias = alias.Alias,
                Source = alias.Source,
                Provider = provider,
                Type = "ticker",
                IsActive = alias.IsActive,
                ValidFrom = alias.ValidFrom?.UtcDateTime,
                ValidTo = alias.ValidTo?.UtcDateTime
            });
        }

        return new SymbolRegistryEntry
        {
            Canonical = definition.Canonical,
            SecurityId = definition.SecurityId,
            DisplayName = definition.DisplayName,
            AssetClass = definition.AssetClass,
            Exchange = definition.Exchange,
            Currency = definition.Currency,
            Country = definition.Country,
            Aliases = aliases,
            ProviderSymbols = definition.ProviderSymbols.ToDictionary(
                static pair => NormalizeProvider(pair.Key),
                static pair => pair.Value.Symbol,
                StringComparer.OrdinalIgnoreCase),
            ProviderSymbolMetadata = definition.ProviderSymbols.ToDictionary(
                static pair => NormalizeProvider(pair.Key),
                static pair => new ProviderSymbolMetadata
                {
                    Source = pair.Value.Source,
                    IsOverride = pair.Value.IsOverride,
                    UpdatedAt = pair.Value.UpdatedAt ?? DateTimeOffset.UtcNow
                },
                StringComparer.OrdinalIgnoreCase),
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
            SecurityId = entry.SecurityId,
            DisplayName = entry.DisplayName,
            Aliases = aliases,
            AliasDefinitions = entry.Aliases.Select(alias => new CanonicalSymbolAliasDefinition(
                alias.Alias,
                Source: alias.Source,
                Provider: alias.Provider,
                ValidFrom: alias.ValidFrom is null ? null : new DateTimeOffset(DateTime.SpecifyKind(alias.ValidFrom.Value, DateTimeKind.Utc)),
                ValidTo: alias.ValidTo is null ? null : new DateTimeOffset(DateTime.SpecifyKind(alias.ValidTo.Value, DateTimeKind.Utc)),
                IsActive: alias.IsActive)).ToArray(),
            ProviderSymbols = entry.ProviderSymbols.ToDictionary(
                static pair => NormalizeProvider(pair.Key),
                pair =>
                {
                    var metadata = entry.ProviderSymbolMetadata.GetValueOrDefault(pair.Key);
                    return new ProviderSymbolDefinition(
                        pair.Value,
                        metadata?.Source ?? SymbolMappingSources.Registry,
                        metadata?.IsOverride ?? false,
                        metadata?.UpdatedAt);
                },
                StringComparer.OrdinalIgnoreCase),
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

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant();

    private static ProviderScopedResolution ResolveProviderScoped(
        SymbolRegistry registry,
        string providerSymbol,
        string provider,
        out string? canonical)
    {
        if (registry.ProviderMappings.TryGetValue(provider, out var providerMap) &&
            providerMap.TryGetValue(providerSymbol, out canonical))
        {
            return ProviderScopedResolution.Resolved;
        }

        canonical = null;
        foreach (var (candidateCanonical, entry) in registry.Symbols)
        {
            var matchesCurrentSymbol = entry.ProviderSymbols.TryGetValue(provider, out var currentSymbol)
                && string.Equals(currentSymbol, providerSymbol, StringComparison.OrdinalIgnoreCase);
            var matchesScopedAlias = entry.Aliases.Any(alias =>
                alias.IsActive
                && string.Equals(alias.Provider, provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(alias.Alias, providerSymbol, StringComparison.OrdinalIgnoreCase));
            if (!matchesCurrentSymbol && !matchesScopedAlias)
                continue;

            if (canonical is not null &&
                !canonical.Equals(candidateCanonical, StringComparison.OrdinalIgnoreCase))
            {
                canonical = null;
                return ProviderScopedResolution.Ambiguous;
            }

            canonical = candidateCanonical;
        }

        return canonical is null
            ? ProviderScopedResolution.NotFound
            : ProviderScopedResolution.Resolved;
    }

    private enum ProviderScopedResolution : byte
    {
        NotFound,
        Resolved,
        Ambiguous
    }

    private static bool IsGenericResolvableAlias(SymbolAlias alias)
        => alias.IsActive && string.IsNullOrWhiteSpace(alias.Provider);
}
