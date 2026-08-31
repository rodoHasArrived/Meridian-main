using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Core.Logging;
using Meridian.Core.Serialization;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing the symbol registry with comprehensive alias resolution.
/// Stored at _catalog/symbols.json.
/// </summary>
public sealed class SymbolRegistryService :
    ISymbolRegistryService,
    ISymbolRegistryMigrationWriter
{
    private const string CatalogDirectoryName = "_catalog";
    private const string SymbolsFileName = "symbols.json";

    private readonly ILogger _log = LoggingSetup.ForContext<SymbolRegistryService>();
    private readonly string _registryPath;
    private readonly SemaphoreSlim _registryLock = new(1, 1);
    private readonly Func<string, string, CancellationToken, Task> _atomicWriteAsync;

    private SymbolRegistry _registry;
    private ConcurrentDictionary<string, string> _aliasCache = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, SymbolRegistryEntry> _symbolCache = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<Guid, string> _securityIdCache = new();

    public SymbolRegistryService(string storagePath)
        : this(
            storagePath,
            static (path, contents, ct) => AtomicFileWriter.WriteAsync(path, contents, ct))
    {
    }

    internal SymbolRegistryService(
        string storagePath,
        Func<string, string, CancellationToken, Task> atomicWriteAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentNullException.ThrowIfNull(atomicWriteAsync);

        var catalogPath = Path.Combine(storagePath, CatalogDirectoryName);
        _registryPath = Path.Combine(catalogPath, SymbolsFileName);
        _atomicWriteAsync = atomicWriteAsync;
        _registry = new SymbolRegistry();

        Directory.CreateDirectory(catalogPath);
    }

    public SymbolRegistry GetRegistry() => _registry;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _registryLock.WaitAsync(ct);
        try
        {
            if (File.Exists(_registryPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_registryPath, ct);
                    var registry = JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.SymbolRegistry);
                    if (registry != null)
                    {
                        _registry = registry;
                        RebuildIndexesAndCaches();
                        _log.Information("Loaded symbol registry with {SymbolCount} symbols", _registry.Symbols.Count);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to load symbol registry, starting fresh");
                    _registry = new SymbolRegistry();
                }
            }
            else
            {
                _registry = new SymbolRegistry();
                InitializeDefaultSymbols();
                await SaveRegistryAsync(ct);
                _log.Information("Created new symbol registry at {Path}", _registryPath);
            }
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public async Task RegisterSymbolAsync(SymbolRegistryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Canonical))
            throw new ArgumentException("Canonical symbol is required.", nameof(entry));

        await _registryLock.WaitAsync(ct);
        try
        {
            entry.Canonical = entry.Canonical.Trim().ToUpperInvariant();
            NormalizeEntry(entry);

            entry = RegisterEntryLocked(entry, merge: true);

            entry.LastUpdatedAt = DateTime.UtcNow;
            _registry.LastUpdatedAt = DateTime.UtcNow;
            RebuildIndexesAndCaches();
            UpdateStatistics();

            await SaveRegistryAsync(ct);
            _log.Debug("Registered symbol {Symbol} with {AliasCount} aliases", entry.Canonical, entry.Aliases.Count);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public SymbolLookupResult LookupSymbol(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SymbolLookupResult { Found = false, Query = query };
        }

        var normalizedQuery = query.Trim().ToUpperInvariant();

        // 1. Try canonical match
        if (_symbolCache.TryGetValue(normalizedQuery, out var entry))
        {
            return new SymbolLookupResult
            {
                Found = true,
                Query = query,
                MatchType = "canonical",
                CanonicalSymbol = entry.Canonical,
                Entry = entry
            };
        }

        // 2. Try alias match
        if (_aliasCache.TryGetValue(normalizedQuery, out var canonical) &&
            _symbolCache.TryGetValue(canonical, out entry))
        {
            return new SymbolLookupResult
            {
                Found = true,
                Query = query,
                MatchType = "alias",
                CanonicalSymbol = canonical,
                Entry = entry
            };
        }

        // 3. Try identifier match (ISIN, FIGI, CUSIP, etc.)
        if (_registry.IdentifierIndex.IsinToSymbol.TryGetValue(normalizedQuery, out canonical) ||
            _registry.IdentifierIndex.FigiToSymbol.TryGetValue(normalizedQuery, out canonical) ||
            _registry.IdentifierIndex.CusipToSymbol.TryGetValue(normalizedQuery, out canonical) ||
            _registry.IdentifierIndex.SedolToSymbol.TryGetValue(normalizedQuery, out canonical))
        {
            if (_symbolCache.TryGetValue(canonical, out entry))
            {
                return new SymbolLookupResult
                {
                    Found = true,
                    Query = query,
                    MatchType = "identifier",
                    CanonicalSymbol = canonical,
                    Entry = entry
                };
            }
        }

        // 4. Try provider mapping lookup only when the spelling is globally
        // unambiguous. Provider-aware callers use the scoped provider map directly.
        var providerMatches = _registry.ProviderMappings
            .Where(pair => pair.Value.TryGetValue(normalizedQuery, out _))
            .Select(pair => new
            {
                Provider = pair.Key,
                Canonical = pair.Value[normalizedQuery]
            })
            .GroupBy(static match => match.Canonical, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(2)
            .ToArray();
        if (providerMatches.Length == 1 &&
            _symbolCache.TryGetValue(providerMatches[0].Canonical, out entry))
        {
            return new SymbolLookupResult
            {
                Found = true,
                Query = query,
                MatchType = $"provider:{providerMatches[0].Provider}",
                CanonicalSymbol = providerMatches[0].Canonical,
                Entry = entry
            };
        }

        // 5. Not found - suggest similar symbols
        var suggestions = _symbolCache.Keys
            .Where(s => s.Contains(normalizedQuery) ||
                        normalizedQuery.Contains(s) ||
                        LevenshteinDistance(s, normalizedQuery) <= 2)
            .Take(5)
            .ToArray();

        return new SymbolLookupResult
        {
            Found = false,
            Query = query,
            Suggestions = suggestions.Length > 0 ? suggestions : null
        };
    }

    public string ResolveAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return alias;

        return LookupSymbol(alias).CanonicalSymbol ?? alias;
    }

    public string? GetProviderSymbol(string canonical, string provider)
    {
        if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(provider))
            return null;

        var canonicalKey = canonical.Trim().ToUpperInvariant();
        var providerKey = NormalizeProvider(provider);
        if (_symbolCache.TryGetValue(canonicalKey, out var entry))
        {
            if (entry.ProviderSymbols.TryGetValue(providerKey, out var providerSymbol))
            {
                return providerSymbol;
            }
        }

        // Also check if there's a provider-specific alias
        if (_registry.ProviderMappings.TryGetValue(providerKey, out var mappings))
        {
            var reverseMapping = mappings.FirstOrDefault(kv =>
                kv.Value.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));
            if (reverseMapping.Key != null)
            {
                return reverseMapping.Key;
            }
        }

        return null;
    }

    public async Task AddAliasAsync(string canonical, SymbolAlias alias, CancellationToken ct = default)
    {
        await _registryLock.WaitAsync(ct);
        try
        {
            if (!_registry.Symbols.TryGetValue(canonical, out var entry))
            {
                throw new InvalidOperationException($"Symbol {canonical} not found in registry");
            }

            // Check if alias already exists
            if (!entry.Aliases.Any(existing => AliasIdentityMatches(existing, alias)))
            {
                entry.Aliases.Add(alias);
                entry.LastUpdatedAt = DateTime.UtcNow;
                _registry.LastUpdatedAt = DateTime.UtcNow;
                RebuildIndexesAndCaches();
                UpdateStatistics();
                await SaveRegistryAsync(ct);

                _log.Debug("Added alias {Alias} for symbol {Symbol}", alias.Alias, canonical);
            }
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public Task AddProviderMappingAsync(string canonical, string provider, string providerSymbol, CancellationToken ct = default)
        => AddProviderMappingAsync(
            canonical,
            provider,
            providerSymbol,
            SymbolMappingSources.Registry,
            isOverride: false,
            ct);

    public async Task AddProviderMappingAsync(
        string canonical,
        string provider,
        string providerSymbol,
        string source,
        bool isOverride,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSymbol);

        await _registryLock.WaitAsync(ct);
        try
        {
            var canonicalKey = canonical.Trim().ToUpperInvariant();
            var providerKey = NormalizeProvider(provider);
            if (!_registry.Symbols.TryGetValue(canonicalKey, out var entry))
            {
                throw new InvalidOperationException($"Symbol {canonical} not found in registry");
            }

            var incomingMetadata = new ProviderSymbolMetadata
            {
                Source = string.IsNullOrWhiteSpace(source) ? SymbolMappingSources.Registry : source,
                IsOverride = isOverride,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (!entry.ProviderSymbolMetadata.TryGetValue(providerKey, out var existingMetadata) ||
                ShouldReplaceProviderSymbol(existingMetadata, incomingMetadata))
            {
                entry.ProviderSymbols[providerKey] = providerSymbol.Trim();
                entry.ProviderSymbolMetadata[providerKey] = incomingMetadata;
            }

            entry.LastUpdatedAt = DateTime.UtcNow;
            _registry.LastUpdatedAt = DateTime.UtcNow;
            RebuildIndexesAndCaches();
            UpdateStatistics();
            await SaveRegistryAsync(ct);

            _log.Debug("Added provider mapping {Provider}:{ProviderSymbol} -> {Canonical}",
                provider, providerSymbol, canonical);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public async Task<bool> RemoveProviderMappingAsync(
        string canonical,
        string provider,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(canonical) || string.IsNullOrWhiteSpace(provider))
            return false;

        await _registryLock.WaitAsync(ct);
        try
        {
            var canonicalKey = canonical.Trim().ToUpperInvariant();
            var providerKey = NormalizeProvider(provider);
            if (!_registry.Symbols.TryGetValue(canonicalKey, out var entry))
                return false;

            var removed = entry.ProviderSymbols.Remove(providerKey);
            entry.ProviderSymbolMetadata.Remove(providerKey);
            if (!removed)
                return false;

            entry.LastUpdatedAt = DateTime.UtcNow;
            _registry.LastUpdatedAt = DateTime.UtcNow;
            RebuildIndexesAndCaches();
            UpdateStatistics();
            await SaveRegistryAsync(ct);
            return true;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public IEnumerable<SymbolRegistryEntry> GetAllSymbols()
    {
        return _registry.Symbols.Values.OrderBy(s => s.Canonical);
    }

    public IEnumerable<SymbolRegistryEntry> GetSymbolsByAssetClass(string assetClass)
    {
        return _registry.Symbols.Values
            .Where(s => s.AssetClass.Equals(assetClass, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Canonical);
    }

    public async Task<string?> GetMigrationMarkerAsync(string migrationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);

        await _registryLock.WaitAsync(ct);
        try
        {
            return _registry.MigrationMarkers.TryGetValue(migrationId.Trim(), out var fingerprint)
                ? fingerprint
                : null;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public async Task SetMigrationMarkerAsync(
        string migrationId,
        string fingerprint,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        await _registryLock.WaitAsync(ct);
        try
        {
            _registry.MigrationMarkers[migrationId.Trim()] = fingerprint.Trim();
            _registry.LastUpdatedAt = DateTime.UtcNow;
            await SaveRegistryAsync(ct);
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public async Task SaveRegistryAsync(CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry);
        await _atomicWriteAsync(_registryPath, json, ct).ConfigureAwait(false);
        _log.Debug("Saved symbol registry to {Path}", _registryPath);
    }

    public async Task<int> ImportSymbolsAsync(IEnumerable<SymbolRegistryEntry> symbols, bool merge = true, CancellationToken ct = default)
    {
        await _registryLock.WaitAsync(ct);
        try
        {
            var imported = 0;

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol.Canonical))
                    continue;

                symbol.Canonical = symbol.Canonical.Trim().ToUpperInvariant();
                NormalizeEntry(symbol);
                RegisterEntryLocked(symbol, merge);

                imported++;
            }

            RebuildIndexesAndCaches();
            UpdateStatistics();
            await SaveRegistryAsync(ct);

            _log.Information("Imported {Count} symbols into registry", imported);
            return imported;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    public async Task<int> ApplyMigrationAsync(
        string migrationId,
        string fingerprint,
        IEnumerable<SymbolRegistryEntry> symbols,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(symbols);

        await _registryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedMigrationId = migrationId.Trim();
            var normalizedFingerprint = fingerprint.Trim();
            if (_registry.MigrationMarkers.TryGetValue(normalizedMigrationId, out var completed) &&
                string.Equals(completed, normalizedFingerprint, StringComparison.Ordinal))
            {
                return 0;
            }

            var originalJson = JsonSerializer.Serialize(
                _registry,
                MarketDataJsonContext.Default.SymbolRegistry);
            var candidate = JsonSerializer.Deserialize(
                    originalJson,
                    MarketDataJsonContext.Default.SymbolRegistry)
                ?? throw new InvalidDataException("Unable to clone the retained symbol registry.");
            var imported = 0;
            var candidateIndexes = BuildIndexesAndCaches(candidate);
            foreach (var symbol in symbols)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(symbol.Canonical))
                    continue;

                symbol.Canonical = symbol.Canonical.Trim().ToUpperInvariant();
                NormalizeEntry(symbol);
                RegisterEntry(
                    candidate,
                    candidateIndexes.SecurityIdCache,
                    symbol,
                    merge: true,
                    _log);
                imported++;
            }

            candidate.MigrationMarkers = new Dictionary<string, string>(
                candidate.MigrationMarkers ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase)
            {
                [normalizedMigrationId] = normalizedFingerprint
            };
            candidate.LastUpdatedAt = DateTime.UtcNow;
            candidateIndexes = BuildIndexesAndCaches(candidate);
            UpdateStatistics(candidate);
            var candidateJson = JsonSerializer.Serialize(
                candidate,
                MarketDataJsonContext.Default.SymbolRegistry);

            await _atomicWriteAsync(_registryPath, candidateJson, ct).ConfigureAwait(false);

            // Publish only after the atomic writer reports durable success. All candidate
            // collections were built privately, so lock-free readers never observe a partial
            // migration or its completion marker.
            _registry = candidate;
            _aliasCache = candidateIndexes.AliasCache;
            _symbolCache = candidateIndexes.SymbolCache;
            _securityIdCache = candidateIndexes.SecurityIdCache;
            _log.Information(
                "Atomically imported {Count} symbols for migration {MigrationId}",
                imported,
                normalizedMigrationId);
            return imported;
        }
        finally
        {
            _registryLock.Release();
        }
    }

    private void RebuildIndexesAndCaches()
    {
        var indexes = BuildIndexesAndCaches(_registry);
        _aliasCache = indexes.AliasCache;
        _symbolCache = indexes.SymbolCache;
        _securityIdCache = indexes.SecurityIdCache;
    }

    private RegistryIndexes BuildIndexesAndCaches(SymbolRegistry registry)
    {
        var aliasCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var symbolCache = new ConcurrentDictionary<string, SymbolRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        var securityIdCache = new ConcurrentDictionary<Guid, string>();

        var symbols = new Dictionary<string, SymbolRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        var entriesBySecurityId = new Dictionary<Guid, SymbolRegistryEntry>();
        foreach (var entry in registry.Symbols.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.Canonical))
                continue;

            entry.Canonical = entry.Canonical.Trim().ToUpperInvariant();
            NormalizeEntry(entry);

            if (entry.SecurityId is Guid securityId &&
                entriesBySecurityId.TryGetValue(securityId, out var identityEntry))
            {
                AddCanonicalTickerAlias(identityEntry, entry.Canonical);
                MergeEntry(identityEntry, entry, _log);
                _log.Warning(
                    "Collapsed duplicate canonical symbol {DuplicateCanonical} into {RetainedCanonical} for SecurityId {SecurityId}",
                    entry.Canonical,
                    identityEntry.Canonical,
                    securityId);
                continue;
            }

            symbols[entry.Canonical] = entry;
            if (entry.SecurityId is Guid retainedSecurityId)
                entriesBySecurityId[retainedSecurityId] = entry;
        }
        registry.Symbols = symbols;
        registry.MigrationMarkers = new Dictionary<string, string>(
            registry.MigrationMarkers ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        registry.AliasIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        registry.ProviderMappings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        registry.IdentifierIndex = new IdentifierIndex
        {
            IsinToSymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            FigiToSymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CusipToSymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SedolToSymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        var providerMappingCandidates = new Dictionary<
            string,
            Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        var aliasCandidates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, entry) in registry.Symbols)
        {
            symbolCache[canonical] = entry;
            if (entry.SecurityId is Guid securityId)
                securityIdCache[securityId] = canonical;

            foreach (var alias in entry.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias.Alias))
                    continue;

                if (IsGenericResolvableAlias(alias))
                {
                    AddAliasCandidate(aliasCandidates, alias.Alias, canonical);
                }
                else if (alias.IsActive && !string.IsNullOrWhiteSpace(alias.Provider))
                {
                    AddProviderMappingCandidate(
                        providerMappingCandidates,
                        NormalizeProvider(alias.Provider),
                        alias.Alias,
                        canonical);
                }
            }

            foreach (var (provider, providerSymbol) in entry.ProviderSymbols)
            {
                AddProviderMappingCandidate(
                    providerMappingCandidates,
                    provider,
                    providerSymbol,
                    canonical);
            }

            UpdateIdentifierIndex(registry, entry);
        }

        foreach (var (alias, canonicals) in aliasCandidates)
        {
            if (canonicals.Count != 1)
                continue;

            var canonical = canonicals.Single();
            registry.AliasIndex[alias] = canonical;
            aliasCache[alias] = canonical;
        }

        foreach (var (provider, providerSymbols) in providerMappingCandidates)
        {
            var mappings = providerSymbols
                .Where(static candidate => candidate.Value.Count == 1)
                .ToDictionary(
                    static candidate => candidate.Key,
                    static candidate => candidate.Value.Single(),
                    StringComparer.OrdinalIgnoreCase);
            if (mappings.Count > 0)
                registry.ProviderMappings[provider] = mappings;
        }

        return new RegistryIndexes(aliasCache, symbolCache, securityIdCache);
    }

    private SymbolRegistryEntry RegisterEntryLocked(SymbolRegistryEntry incoming, bool merge)
    {
        return RegisterEntry(_registry, _securityIdCache, incoming, merge, _log);
    }

    private static SymbolRegistryEntry RegisterEntry(
        SymbolRegistry registry,
        IReadOnlyDictionary<Guid, string> securityIdCache,
        SymbolRegistryEntry incoming,
        bool merge,
        ILogger log)
    {
        registry.Symbols.TryGetValue(incoming.Canonical, out var canonicalEntry);

        if (canonicalEntry?.SecurityId is Guid retainedSecurityId && incoming.SecurityId is null)
            incoming.SecurityId = retainedSecurityId;

        if (canonicalEntry?.SecurityId is Guid existingSecurityId &&
            incoming.SecurityId is Guid incomingSecurityId &&
            existingSecurityId != incomingSecurityId)
        {
            throw new InvalidOperationException(
                $"Canonical symbol '{incoming.Canonical}' is already assigned to SecurityId " +
                $"'{existingSecurityId}' and cannot be reassigned to '{incomingSecurityId}'.");
        }

        var identityEntry = incoming.SecurityId is Guid securityId
            ? FindBySecurityId(registry, securityIdCache, securityId)
            : null;

        if (identityEntry is not null && !ReferenceEquals(identityEntry, canonicalEntry))
        {
            if (canonicalEntry is not null)
            {
                throw new InvalidOperationException(
                    $"Canonical symbol '{incoming.Canonical}' is already registered to a different instrument; " +
                    $"SecurityId '{incoming.SecurityId}' is retained under '{identityEntry.Canonical}'.");
            }

            AddCanonicalTickerAlias(identityEntry, incoming.Canonical);
            MergeEntry(identityEntry, incoming, log);
            return identityEntry;
        }

        if (canonicalEntry is not null)
        {
            if (merge)
            {
                MergeEntry(canonicalEntry, incoming, log);
                return canonicalEntry;
            }

            registry.Symbols[incoming.Canonical] = incoming;
            return incoming;
        }

        registry.Symbols[incoming.Canonical] = incoming;
        return incoming;
    }

    private static SymbolRegistryEntry? FindBySecurityId(
        SymbolRegistry registry,
        IReadOnlyDictionary<Guid, string> securityIdCache,
        Guid securityId)
    {
        if (securityIdCache.TryGetValue(securityId, out var canonical) &&
            registry.Symbols.TryGetValue(canonical, out var cachedEntry) &&
            cachedEntry.SecurityId == securityId)
        {
            return cachedEntry;
        }

        return registry.Symbols.Values.FirstOrDefault(entry => entry.SecurityId == securityId);
    }

    private static void AddCanonicalTickerAlias(SymbolRegistryEntry entry, string canonicalTicker)
    {
        if (string.IsNullOrWhiteSpace(canonicalTicker) ||
            canonicalTicker.Equals(entry.Canonical, StringComparison.OrdinalIgnoreCase) ||
            entry.Aliases.Any(alias =>
                string.IsNullOrWhiteSpace(alias.Provider) &&
                alias.Alias.Equals(canonicalTicker, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        entry.Aliases.Add(new SymbolAlias
        {
            Alias = canonicalTicker.Trim().ToUpperInvariant(),
            Source = SymbolMappingSources.SecurityMaster,
            Type = "ticker",
            IsActive = true
        });
    }

    private static void AddAliasCandidate(
        Dictionary<string, HashSet<string>> candidates,
        string alias,
        string canonical)
    {
        if (!candidates.TryGetValue(alias, out var canonicals))
        {
            canonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            candidates[alias] = canonicals;
        }

        canonicals.Add(canonical);
    }

    private static void AddProviderMappingCandidate(
        Dictionary<string, Dictionary<string, HashSet<string>>> candidates,
        string provider,
        string providerSymbol,
        string canonical)
    {
        if (!candidates.TryGetValue(provider, out var symbols))
        {
            symbols = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            candidates[provider] = symbols;
        }

        if (!symbols.TryGetValue(providerSymbol, out var canonicals))
        {
            canonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            symbols[providerSymbol] = canonicals;
        }

        canonicals.Add(canonical);
    }

    private static void NormalizeEntry(SymbolRegistryEntry entry)
    {
        entry.Aliases ??= [];
        entry.Identifiers ??= new SymbolIdentifiers();
        entry.ProviderSymbols ??= new Dictionary<string, string>();
        entry.ProviderSymbolMetadata ??= new Dictionary<string, ProviderSymbolMetadata>();

        foreach (var alias in entry.Aliases)
        {
            alias.Alias = alias.Alias?.Trim() ?? string.Empty;
            alias.Provider = string.IsNullOrWhiteSpace(alias.Provider)
                ? null
                : NormalizeProvider(alias.Provider);
        }
        entry.Aliases = entry.Aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias.Alias))
            .GroupBy(
                static alias => $"{alias.Provider}\u001f{alias.Alias}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        var providerSymbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (provider, providerSymbol) in entry.ProviderSymbols)
        {
            if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(providerSymbol))
                providerSymbols[NormalizeProvider(provider)] = providerSymbol.Trim();
        }
        entry.ProviderSymbols = providerSymbols;

        var metadata = new Dictionary<string, ProviderSymbolMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var (provider, value) in entry.ProviderSymbolMetadata)
        {
            if (!string.IsNullOrWhiteSpace(provider) && value is not null)
                metadata[NormalizeProvider(provider)] = value;
        }

        foreach (var provider in entry.ProviderSymbols.Keys)
        {
            metadata.TryAdd(provider, new ProviderSymbolMetadata());
        }
        entry.ProviderSymbolMetadata = metadata;
    }

    private static void MergeEntry(
        SymbolRegistryEntry existing,
        SymbolRegistryEntry incoming,
        ILogger log)
    {
        NormalizeEntry(existing);
        NormalizeEntry(incoming);

        if (existing.SecurityId is null)
            existing.SecurityId = incoming.SecurityId;
        else if (incoming.SecurityId is not null && existing.SecurityId != incoming.SecurityId)
            log.Warning(
                "Ignored conflicting SecurityId {IncomingSecurityId} for canonical symbol {Canonical}; retaining {ExistingSecurityId}",
                incoming.SecurityId,
                existing.Canonical,
                existing.SecurityId);

        existing.DisplayName ??= incoming.DisplayName;
        existing.Exchange ??= incoming.Exchange;
        existing.Currency ??= incoming.Currency;
        existing.Country ??= incoming.Country;
        if (string.IsNullOrWhiteSpace(existing.AssetClass))
            existing.AssetClass = incoming.AssetClass;

        foreach (var alias in incoming.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias.Provider))
            {
                // The first provider-aware registry projection stored provider scope in Source.
                // Upgrade that wire-compatible legacy shape when the same scoped alias is seeded
                // again, without guessing that every historical Source value is a provider.
                var legacyScopedAlias = existing.Aliases.FirstOrDefault(existingAlias =>
                    existingAlias.Alias.Equals(alias.Alias, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(existingAlias.Provider)
                    && string.Equals(existingAlias.Source, alias.Provider, StringComparison.OrdinalIgnoreCase));
                if (legacyScopedAlias is not null)
                {
                    legacyScopedAlias.Provider = alias.Provider;
                    legacyScopedAlias.Source = alias.Source;
                }
            }

            if (!existing.Aliases.Any(existingAlias => AliasIdentityMatches(existingAlias, alias)))
            {
                existing.Aliases.Add(alias);
            }
        }

        MergeIdentifiers(existing.Identifiers, incoming.Identifiers);

        foreach (var (provider, providerSymbol) in incoming.ProviderSymbols)
        {
            var incomingMetadata = incoming.ProviderSymbolMetadata.GetValueOrDefault(provider)
                ?? new ProviderSymbolMetadata();
            if (!existing.ProviderSymbols.ContainsKey(provider) ||
                !existing.ProviderSymbolMetadata.TryGetValue(provider, out var existingMetadata) ||
                ShouldReplaceProviderSymbol(existingMetadata, incomingMetadata))
            {
                existing.ProviderSymbols[provider] = providerSymbol;
                existing.ProviderSymbolMetadata[provider] = incomingMetadata;
            }
        }

        existing.Classification ??= incoming.Classification;
        existing.Metadata ??= incoming.Metadata;
        existing.CreatedAt = existing.CreatedAt <= incoming.CreatedAt ? existing.CreatedAt : incoming.CreatedAt;
        existing.LastUpdatedAt = DateTime.UtcNow;
    }

    private static void MergeIdentifiers(SymbolIdentifiers existing, SymbolIdentifiers incoming)
    {
        existing.Isin ??= incoming.Isin;
        existing.Figi ??= incoming.Figi;
        existing.CompositeFigi ??= incoming.CompositeFigi;
        existing.ShareClassFigi ??= incoming.ShareClassFigi;
        existing.Sedol ??= incoming.Sedol;
        existing.Cusip ??= incoming.Cusip;
        existing.Cik ??= incoming.Cik;
        existing.Lei ??= incoming.Lei;
        existing.BloombergId ??= incoming.BloombergId;
        existing.Ric ??= incoming.Ric;
    }

    private static bool ShouldReplaceProviderSymbol(
        ProviderSymbolMetadata existing,
        ProviderSymbolMetadata incoming)
    {
        var existingPrecedence = GetSourcePrecedence(existing.Source, existing.IsOverride);
        var incomingPrecedence = GetSourcePrecedence(incoming.Source, incoming.IsOverride);
        return incomingPrecedence > existingPrecedence ||
               (incomingPrecedence == existingPrecedence && incoming.UpdatedAt >= existing.UpdatedAt);
    }

    private static int GetSourcePrecedence(string? source, bool isOverride)
    {
        if (isOverride || string.Equals(source, SymbolMappingSources.Operator, StringComparison.OrdinalIgnoreCase))
            return 500;
        if (string.Equals(source, SymbolMappingSources.SecurityMaster, StringComparison.OrdinalIgnoreCase))
            return 400;
        if (string.Equals(source, SymbolMappingSources.LegacyConfig, StringComparison.OrdinalIgnoreCase))
            return 300;
        if (string.Equals(source, SymbolMappingSources.OpenFigi, StringComparison.OrdinalIgnoreCase))
            return 200;
        if (string.Equals(source, SymbolMappingSources.FormattingFallback, StringComparison.OrdinalIgnoreCase))
            return 100;
        return 150;
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant();

    private static bool IsGenericResolvableAlias(SymbolAlias alias)
        => alias.IsActive && string.IsNullOrWhiteSpace(alias.Provider);

    private static bool AliasIdentityMatches(SymbolAlias left, SymbolAlias right)
        => left.Alias.Equals(right.Alias, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Provider, right.Provider, StringComparison.OrdinalIgnoreCase);

    private void UpdateIdentifierIndex(SymbolRegistryEntry entry)
        => UpdateIdentifierIndex(_registry, entry);

    private static void UpdateIdentifierIndex(SymbolRegistry registry, SymbolRegistryEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Identifiers.Isin))
            registry.IdentifierIndex.IsinToSymbol[entry.Identifiers.Isin] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Figi))
            registry.IdentifierIndex.FigiToSymbol[entry.Identifiers.Figi] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.CompositeFigi))
            registry.IdentifierIndex.FigiToSymbol[entry.Identifiers.CompositeFigi] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Cusip))
            registry.IdentifierIndex.CusipToSymbol[entry.Identifiers.Cusip] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Sedol))
            registry.IdentifierIndex.SedolToSymbol[entry.Identifiers.Sedol] = entry.Canonical;
    }

    private void UpdateStatistics()
        => UpdateStatistics(_registry);

    private static void UpdateStatistics(SymbolRegistry registry)
    {
        registry.Statistics = new SymbolRegistryStatistics
        {
            TotalSymbols = registry.Symbols.Count,
            ActiveSymbols = registry.Symbols.Values.Count(s => s.IsActive),
            DelistedSymbols = registry.Symbols.Values.Count(s => !s.IsActive),
            TotalAliases = registry.Symbols.Values.Sum(s => s.Aliases.Count),
            ProviderCount = registry.ProviderMappings.Count
        };

        // Breakdown by asset class
        registry.Statistics.ByAssetClass = registry.Symbols.Values
            .GroupBy(s => s.AssetClass)
            .ToDictionary(g => g.Key, g => g.Count());

        // Breakdown by exchange
        registry.Statistics.ByExchange = registry.Symbols.Values
            .Where(s => !string.IsNullOrEmpty(s.Exchange))
            .GroupBy(s => s.Exchange!)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private sealed record RegistryIndexes(
        ConcurrentDictionary<string, string> AliasCache,
        ConcurrentDictionary<string, SymbolRegistryEntry> SymbolCache,
        ConcurrentDictionary<Guid, string> SecurityIdCache);

    private void InitializeDefaultSymbols()
    {
        var defaultSymbols = BuildDefaultSymbolEntries();

        foreach (var entry in defaultSymbols)
        {
            _registry.Symbols[entry.Canonical] = entry;
            _symbolCache[entry.Canonical] = entry;

            // Index all identifiers
            UpdateIdentifierIndex(entry);

            // Index aliases
            foreach (var alias in entry.Aliases.Where(IsGenericResolvableAlias))
            {
                _registry.AliasIndex[alias.Alias] = entry.Canonical;
                _aliasCache[alias.Alias] = entry.Canonical;
            }
        }

        UpdateStatistics();
        _log.Information("Initialized default symbol registry with {Count} symbols", defaultSymbols.Count);
    }

    /// <summary>
    /// Builds the default symbol entries with canonical identifiers (ISIN, FIGI, SEDOL, CUSIP)
    /// and standard provider aliases.
    /// </summary>
    private static List<SymbolRegistryEntry> BuildDefaultSymbolEntries()
    {
        return
        [
            CreateDefaultEntry("AAPL", "Apple Inc.", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US0378331005",
                Figi = "BBG000B9XRY4",
                CompositeFigi = "BBG000B9XRY4",
                Sedol = "2046251",
                Cusip = "037833100"
            },
            ["AAPL.US", "AAPL.O", "AAPL.NASDAQ"]),

            CreateDefaultEntry("MSFT", "Microsoft Corporation", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US5949181045",
                Figi = "BBG000BPH459",
                CompositeFigi = "BBG000BPH459",
                Sedol = "2588173",
                Cusip = "594918104"
            },
            ["MSFT.US", "MSFT.O", "MSFT.NASDAQ"]),

            CreateDefaultEntry("GOOGL", "Alphabet Inc.", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US02079K3059",
                Figi = "BBG009S39JX6",
                CompositeFigi = "BBG009S39JX6",
                Sedol = "BYVY8G0",
                Cusip = "02079K305"
            },
            ["GOOGL.US", "GOOGL.O", "GOOGL.NASDAQ"]),

            CreateDefaultEntry("AMZN", "Amazon.com Inc.", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US0231351067",
                Figi = "BBG000BVPV84",
                CompositeFigi = "BBG000BVPV84",
                Sedol = "2000019",
                Cusip = "023135106"
            },
            ["AMZN.US", "AMZN.O", "AMZN.NASDAQ"]),

            CreateDefaultEntry("TSLA", "Tesla Inc.", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US88160R1014",
                Figi = "BBG000N9MNX3",
                CompositeFigi = "BBG000N9MNX3",
                Sedol = "B616C79",
                Cusip = "88160R101"
            },
            ["TSLA.US", "TSLA.O", "TSLA.NASDAQ"]),

            CreateDefaultEntry("META", "Meta Platforms Inc.", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US30303M1027",
                Figi = "BBG000MM2P62",
                CompositeFigi = "BBG000MM2P62",
                Sedol = "B7TL820",
                Cusip = "30303M102"
            },
            ["META.US", "META.O", "META.NASDAQ", "FB"]),

            CreateDefaultEntry("NVDA", "NVIDIA Corporation", "equity", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US67066G1040",
                Figi = "BBG000BBJQV0",
                CompositeFigi = "BBG000BBJQV0",
                Sedol = "2379504",
                Cusip = "67066G104"
            },
            ["NVDA.US", "NVDA.O", "NVDA.NASDAQ"]),

            CreateDefaultEntry("SPY", "SPDR S&P 500 ETF Trust", "etf", "NYSE", new SymbolIdentifiers
            {
                Isin = "US78462F1030",
                Figi = "BBG000BDTBL9",
                CompositeFigi = "BBG000BDTBL9",
                Sedol = "2840215",
                Cusip = "78462F103"
            },
            ["SPY.US", "SPY.P", "SPY.NYSE"]),

            CreateDefaultEntry("QQQ", "Invesco QQQ Trust", "etf", "NASDAQ", new SymbolIdentifiers
            {
                Isin = "US46090E1038",
                Figi = "BBG000BSWKH7",
                CompositeFigi = "BBG000BSWKH7",
                Sedol = "2591786",
                Cusip = "46090E103"
            },
            ["QQQ.US", "QQQ.O", "QQQ.NASDAQ"]),

            CreateDefaultEntry("IWM", "iShares Russell 2000 ETF", "etf", "NYSE", new SymbolIdentifiers
            {
                Isin = "US4642876555",
                Figi = "BBG000CGC9C3",
                CompositeFigi = "BBG000CGC9C3",
                Sedol = "2763479",
                Cusip = "464287655"
            },
            ["IWM.US", "IWM.P", "IWM.NYSE"])
        ];
    }

    private static SymbolRegistryEntry CreateDefaultEntry(
        string symbol,
        string displayName,
        string assetClass,
        string exchange,
        SymbolIdentifiers identifiers,
        string[] aliases)
    {
        var now = DateTime.UtcNow;
        return new SymbolRegistryEntry
        {
            Canonical = symbol,
            DisplayName = displayName,
            AssetClass = assetClass,
            Exchange = exchange,
            Currency = "USD",
            Country = "US",
            Identifiers = identifiers,
            Aliases = aliases.Select(a => new SymbolAlias
            {
                Alias = a,
                Source = a.Contains('.') ? "exchange-suffix" : "historical",
                Type = "ticker",
                IsActive = true
            }).ToList(),
            IsActive = true,
            CreatedAt = now,
            LastUpdatedAt = now
        };
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0)
            return m;
        if (m == 0)
            return n;

        for (var i = 0; i <= n; i++)
            d[i, 0] = i;
        for (var j = 0; j <= m; j++)
            d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
