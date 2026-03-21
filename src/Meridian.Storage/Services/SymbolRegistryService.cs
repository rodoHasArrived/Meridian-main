using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Service for managing the symbol registry with comprehensive alias resolution.
/// Stored at _catalog/symbols.json.
/// </summary>
public sealed class SymbolRegistryService : ISymbolRegistryService
{
    private const string CatalogDirectoryName = "_catalog";
    private const string SymbolsFileName = "symbols.json";

    private readonly ILogger _log = LoggingSetup.ForContext<SymbolRegistryService>();
    private readonly string _registryPath;
    private readonly SemaphoreSlim _registryLock = new(1, 1);

    private SymbolRegistry _registry;
    private readonly ConcurrentDictionary<string, string> _aliasCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolRegistryEntry> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

    public SymbolRegistryService(string storagePath)
    {
        var catalogPath = Path.Combine(storagePath, CatalogDirectoryName);
        _registryPath = Path.Combine(catalogPath, SymbolsFileName);
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
                        RebuildCaches();
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
        await _registryLock.WaitAsync(ct);
        try
        {
            // Update or add the symbol
            _registry.Symbols[entry.Canonical] = entry;
            _symbolCache[entry.Canonical] = entry;

            // Update alias index
            foreach (var alias in entry.Aliases)
            {
                _registry.AliasIndex[alias.Alias] = entry.Canonical;
                _aliasCache[alias.Alias] = entry.Canonical;
            }

            // Update provider mappings
            foreach (var (provider, providerSymbol) in entry.ProviderSymbols)
            {
                if (!_registry.ProviderMappings.ContainsKey(provider))
                {
                    _registry.ProviderMappings[provider] = new Dictionary<string, string>();
                }
                _registry.ProviderMappings[provider][providerSymbol] = entry.Canonical;
            }

            // Update identifier index
            UpdateIdentifierIndex(entry);

            entry.LastUpdatedAt = DateTime.UtcNow;
            _registry.LastUpdatedAt = DateTime.UtcNow;
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

        // 4. Try provider mapping lookup
        foreach (var (provider, mappings) in _registry.ProviderMappings)
        {
            if (mappings.TryGetValue(normalizedQuery, out canonical) &&
                _symbolCache.TryGetValue(canonical, out entry))
            {
                return new SymbolLookupResult
                {
                    Found = true,
                    Query = query,
                    MatchType = $"provider:{provider}",
                    CanonicalSymbol = canonical,
                    Entry = entry
                };
            }
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

        var normalized = alias.Trim().ToUpperInvariant();

        // Check if it's already canonical
        if (_symbolCache.ContainsKey(normalized))
            return normalized;

        // Check alias cache
        if (_aliasCache.TryGetValue(normalized, out var canonical))
            return canonical;

        // Check provider mappings
        foreach (var mappings in _registry.ProviderMappings.Values)
        {
            if (mappings.TryGetValue(normalized, out canonical))
                return canonical;
        }

        // Check identifier indexes
        if (_registry.IdentifierIndex.IsinToSymbol.TryGetValue(normalized, out canonical) ||
            _registry.IdentifierIndex.FigiToSymbol.TryGetValue(normalized, out canonical) ||
            _registry.IdentifierIndex.CusipToSymbol.TryGetValue(normalized, out canonical) ||
            _registry.IdentifierIndex.SedolToSymbol.TryGetValue(normalized, out canonical))
        {
            return canonical;
        }

        // Return original if no mapping found
        return alias;
    }

    public string? GetProviderSymbol(string canonical, string provider)
    {
        if (_symbolCache.TryGetValue(canonical, out var entry))
        {
            if (entry.ProviderSymbols.TryGetValue(provider, out var providerSymbol))
            {
                return providerSymbol;
            }
        }

        // Also check if there's a provider-specific alias
        if (_registry.ProviderMappings.TryGetValue(provider, out var mappings))
        {
            var reverseMapping = mappings.FirstOrDefault(kv =>
                kv.Value.Equals(canonical, StringComparison.OrdinalIgnoreCase));
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
            if (!entry.Aliases.Any(a => a.Alias.Equals(alias.Alias, StringComparison.OrdinalIgnoreCase)))
            {
                entry.Aliases.Add(alias);
                _registry.AliasIndex[alias.Alias] = canonical;
                _aliasCache[alias.Alias] = canonical;
                entry.LastUpdatedAt = DateTime.UtcNow;
                _registry.LastUpdatedAt = DateTime.UtcNow;
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

    public async Task AddProviderMappingAsync(string canonical, string provider, string providerSymbol, CancellationToken ct = default)
    {
        await _registryLock.WaitAsync(ct);
        try
        {
            if (!_registry.Symbols.TryGetValue(canonical, out var entry))
            {
                throw new InvalidOperationException($"Symbol {canonical} not found in registry");
            }

            entry.ProviderSymbols[provider] = providerSymbol;

            if (!_registry.ProviderMappings.ContainsKey(provider))
            {
                _registry.ProviderMappings[provider] = new Dictionary<string, string>();
            }
            _registry.ProviderMappings[provider][providerSymbol] = canonical;

            entry.LastUpdatedAt = DateTime.UtcNow;
            _registry.LastUpdatedAt = DateTime.UtcNow;
            await SaveRegistryAsync(ct);

            _log.Debug("Added provider mapping {Provider}:{ProviderSymbol} -> {Canonical}",
                provider, providerSymbol, canonical);
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

    public async Task SaveRegistryAsync(CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(_registry, MarketDataJsonContext.Default.SymbolRegistry);
        await AtomicFileWriter.WriteAsync(_registryPath, json, ct);
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
                if (merge && _registry.Symbols.TryGetValue(symbol.Canonical, out var existing))
                {
                    // Merge aliases
                    foreach (var alias in symbol.Aliases)
                    {
                        if (!existing.Aliases.Any(a => a.Alias.Equals(alias.Alias, StringComparison.OrdinalIgnoreCase)))
                        {
                            existing.Aliases.Add(alias);
                        }
                    }

                    // Merge provider symbols
                    foreach (var (provider, providerSymbol) in symbol.ProviderSymbols)
                    {
                        existing.ProviderSymbols[provider] = providerSymbol;
                    }

                    // Update identifiers if not set
                    if (string.IsNullOrEmpty(existing.Identifiers.Isin) && !string.IsNullOrEmpty(symbol.Identifiers.Isin))
                        existing.Identifiers.Isin = symbol.Identifiers.Isin;
                    if (string.IsNullOrEmpty(existing.Identifiers.Figi) && !string.IsNullOrEmpty(symbol.Identifiers.Figi))
                        existing.Identifiers.Figi = symbol.Identifiers.Figi;
                    if (string.IsNullOrEmpty(existing.Identifiers.Cusip) && !string.IsNullOrEmpty(symbol.Identifiers.Cusip))
                        existing.Identifiers.Cusip = symbol.Identifiers.Cusip;
                    if (string.IsNullOrEmpty(existing.Identifiers.Sedol) && !string.IsNullOrEmpty(symbol.Identifiers.Sedol))
                        existing.Identifiers.Sedol = symbol.Identifiers.Sedol;

                    existing.LastUpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _registry.Symbols[symbol.Canonical] = symbol;
                }

                imported++;
            }

            RebuildCaches();
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

    private void RebuildCaches()
    {
        _symbolCache.Clear();
        _aliasCache.Clear();

        foreach (var (canonical, entry) in _registry.Symbols)
        {
            _symbolCache[canonical] = entry;

            foreach (var alias in entry.Aliases)
            {
                _aliasCache[alias.Alias] = canonical;
            }
        }

        // Also add alias index entries to cache
        foreach (var (alias, canonical) in _registry.AliasIndex)
        {
            _aliasCache[alias] = canonical;
        }
    }

    private void UpdateIdentifierIndex(SymbolRegistryEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Identifiers.Isin))
            _registry.IdentifierIndex.IsinToSymbol[entry.Identifiers.Isin] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Figi))
            _registry.IdentifierIndex.FigiToSymbol[entry.Identifiers.Figi] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.CompositeFigi))
            _registry.IdentifierIndex.FigiToSymbol[entry.Identifiers.CompositeFigi] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Cusip))
            _registry.IdentifierIndex.CusipToSymbol[entry.Identifiers.Cusip] = entry.Canonical;

        if (!string.IsNullOrEmpty(entry.Identifiers.Sedol))
            _registry.IdentifierIndex.SedolToSymbol[entry.Identifiers.Sedol] = entry.Canonical;
    }

    private void UpdateStatistics()
    {
        _registry.Statistics = new SymbolRegistryStatistics
        {
            TotalSymbols = _registry.Symbols.Count,
            ActiveSymbols = _registry.Symbols.Values.Count(s => s.IsActive),
            DelistedSymbols = _registry.Symbols.Values.Count(s => !s.IsActive),
            TotalAliases = _registry.Symbols.Values.Sum(s => s.Aliases.Count),
            ProviderCount = _registry.ProviderMappings.Count
        };

        // Breakdown by asset class
        _registry.Statistics.ByAssetClass = _registry.Symbols.Values
            .GroupBy(s => s.AssetClass)
            .ToDictionary(g => g.Key, g => g.Count());

        // Breakdown by exchange
        _registry.Statistics.ByExchange = _registry.Symbols.Values
            .Where(s => !string.IsNullOrEmpty(s.Exchange))
            .GroupBy(s => s.Exchange!)
            .ToDictionary(g => g.Key, g => g.Count());
    }

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
            foreach (var alias in entry.Aliases)
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
