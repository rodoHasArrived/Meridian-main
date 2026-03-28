using System.Collections.Concurrent;
using System.Diagnostics;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Contracts.Domain;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Polygon;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for searching and autocompleting symbols across multiple providers.
/// Aggregates results from Finnhub, Polygon, Alpaca, and OpenFIGI.
/// </summary>
public sealed class SymbolSearchService : IDisposable
{
    private readonly List<ISymbolSearchProvider> _providers = new();
    private readonly OpenFigiClient? _figiClient;
    private readonly MetadataEnrichmentService _metadataService;
    private readonly ConcurrentDictionary<string, CachedSearchResult> _searchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedDetails> _detailsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _log;
    private readonly TimeSpan _searchCacheDuration;
    private readonly TimeSpan _detailsCacheDuration;
    private bool _disposed;

    /// <summary>
    /// Creates a new symbol search service with default provider configuration.
    /// </summary>
    public SymbolSearchService(
        MetadataEnrichmentService? metadataService = null,
        TimeSpan? searchCacheDuration = null,
        TimeSpan? detailsCacheDuration = null,
        ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<SymbolSearchService>();
        _metadataService = metadataService ?? new MetadataEnrichmentService();
        _searchCacheDuration = searchCacheDuration ?? TimeSpan.FromMinutes(5);
        _detailsCacheDuration = detailsCacheDuration ?? TimeSpan.FromMinutes(30);

        // Initialize default providers
        InitializeDefaultProviders();
    }

    /// <summary>
    /// Creates a symbol search service with custom providers.
    /// </summary>
    public SymbolSearchService(
        IEnumerable<ISymbolSearchProvider> providers,
        OpenFigiClient? figiClient,
        MetadataEnrichmentService metadataService,
        TimeSpan? searchCacheDuration = null,
        TimeSpan? detailsCacheDuration = null,
        ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<SymbolSearchService>();
        _providers.AddRange(providers.OrderBy(p => p.Priority));
        _figiClient = figiClient;
        _metadataService = metadataService;
        _searchCacheDuration = searchCacheDuration ?? TimeSpan.FromMinutes(5);
        _detailsCacheDuration = detailsCacheDuration ?? TimeSpan.FromMinutes(30);
    }

    private void InitializeDefaultProviders()
    {
        try
        {
            // Initialize providers in priority order
            _providers.Add(new AlpacaSymbolSearchProviderRefactored());
            _providers.Add(new FinnhubSymbolSearchProviderRefactored());
            _providers.Add(new PolygonSymbolSearchProvider());
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to initialize one or more symbol search providers");
        }
    }

    /// <summary>
    /// Get available providers and their status.
    /// </summary>
    public async Task<IReadOnlyList<ProviderStatus>> GetProvidersAsync(CancellationToken ct = default)
    {
        var tasks = _providers.Select(async p =>
        {
            bool available;
            try
            {
                available = await p.IsAvailableAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                available = false;
            }

            return new ProviderStatus(p.Name, p.DisplayName, p.Priority, available);
        });

        var statuses = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Add OpenFIGI status
        var figiAvailable = false;
        if (_figiClient is not null)
        {
            try
            {
                figiAvailable = await _figiClient.IsAvailableAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                figiAvailable = false;
            }
        }

        var result = statuses.ToList();
        result.Add(new ProviderStatus("openfigi", "OpenFIGI", 100, figiAvailable));

        return result;
    }

    /// <summary>
    /// Search for symbols with autocomplete support.
    /// </summary>
    /// <param name="request">Search request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated search results from all available providers.</returns>
    public async Task<SymbolSearchResponse> SearchAsync(SymbolSearchRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new SymbolSearchResponse(
                Results: Array.Empty<SymbolSearchResult>(),
                TotalCount: 0,
                Sources: Array.Empty<string>(),
                ElapsedMs: 0,
                Query: request.Query ?? ""
            );
        }

        // Check cache
        var cacheKey = BuildCacheKey(request);
        if (_searchCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _log.Debug("Returning cached search results for {Query}", request.Query);
            return cached.Response;
        }

        var sw = Stopwatch.StartNew();
        var allResults = new List<SymbolSearchResult>();
        var sources = new List<string>();

        // Get providers to query
        var providers = string.IsNullOrEmpty(request.Provider)
            ? _providers.Where(p => true)
            : _providers.Where(p => p.Name.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));

        // Query all providers in parallel
        var tasks = providers.Select(async p =>
        {
            try
            {
                var available = await p.IsAvailableAsync(ct).ConfigureAwait(false);
                if (!available)
                    return (Provider: p.Name, Results: Array.Empty<SymbolSearchResult>());

                IReadOnlyList<SymbolSearchResult> results;

                if (p is IFilterableSymbolSearchProvider filterable &&
                    (!string.IsNullOrEmpty(request.AssetType) || !string.IsNullOrEmpty(request.Exchange)))
                {
                    results = await filterable.SearchAsync(
                        request.Query,
                        request.Limit * 2, // Get more results for deduplication
                        request.AssetType,
                        request.Exchange,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    results = await p.SearchAsync(request.Query, request.Limit * 2, ct).ConfigureAwait(false);
                }

                return (Provider: p.Name, Results: results);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Symbol search failed for provider {Provider}", p.Name);
                return (Provider: p.Name, Results: Array.Empty<SymbolSearchResult>());
            }
        }).ToList();

        var providerResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Aggregate results
        foreach (var (provider, results) in providerResults)
        {
            if (results.Count > 0)
            {
                sources.Add(provider);
                allResults.AddRange(results);
            }
        }

        // Deduplicate by symbol, keeping highest score
        var deduped = allResults
            .GroupBy(r => r.Symbol.ToUpperInvariant())
            .Select(g => g.OrderByDescending(r => r.MatchScore).First())
            .OrderByDescending(r => r.MatchScore)
            .Take(request.Limit)
            .ToList();

        // Enrich with local metadata
        deduped = await EnrichWithMetadataAsync(deduped, ct).ConfigureAwait(false);

        // Optionally enrich with FIGI
        if (request.IncludeFigi && _figiClient is not null && deduped.Count > 0)
        {
            try
            {
                deduped = (await _figiClient.EnrichWithFigiAsync(deduped, ct).ConfigureAwait(false)).ToList();
                if (!sources.Contains("openfigi"))
                    sources.Add("openfigi");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to enrich search results with FIGI");
            }
        }

        sw.Stop();

        var response = new SymbolSearchResponse(
            Results: deduped,
            TotalCount: deduped.Count,
            Sources: sources,
            ElapsedMs: sw.ElapsedMilliseconds,
            Query: request.Query
        );

        // Cache the response
        _searchCache[cacheKey] = new CachedSearchResult(response, _searchCacheDuration);

        _log.Information("Symbol search for {Query} returned {Count} results from {Sources} in {ElapsedMs}ms",
            request.Query, deduped.Count, string.Join(", ", sources), sw.ElapsedMilliseconds);

        return response;
    }

    /// <summary>
    /// Get detailed information about a specific symbol.
    /// </summary>
    /// <param name="symbol">Symbol ticker.</param>
    /// <param name="provider">Optional specific provider to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Symbol details or null if not found.</returns>
    public async Task<SymbolDetails?> GetDetailsAsync(
        string symbol,
        string? provider = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var normalizedSymbol = symbol.ToUpperInvariant();

        // Check cache
        var cacheKey = $"{normalizedSymbol}:{provider ?? "any"}";
        if (_detailsCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _log.Debug("Returning cached details for {Symbol}", symbol);
            return cached.Details;
        }

        // Get providers to query
        var providers = string.IsNullOrEmpty(provider)
            ? _providers.Where(p => true)
            : _providers.Where(p => p.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));

        // Query providers in priority order until we get a result
        SymbolDetails? details = null;

        foreach (var p in providers.OrderBy(p => p.Priority))
        {
            try
            {
                var available = await p.IsAvailableAsync(ct).ConfigureAwait(false);
                if (!available)
                    continue;

                details = await p.GetDetailsAsync(new SymbolId(normalizedSymbol), ct).ConfigureAwait(false);
                if (details is not null)
                {
                    _log.Debug("Got details for {Symbol} from {Provider}", symbol, p.Name);
                    break;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to get details for {Symbol} from {Provider}", symbol, p.Name);
            }
        }

        // Enrich with local metadata
        if (details is not null)
        {
            details = await EnrichDetailsWithMetadataAsync(details, ct).ConfigureAwait(false);
        }

        // Enrich with FIGI if available
        if (details is not null && _figiClient is not null && string.IsNullOrEmpty(details.Figi))
        {
            try
            {
                var figiResults = await _figiClient.LookupByTickerAsync(normalizedSymbol, ct: ct).ConfigureAwait(false);
                if (figiResults.Count > 0)
                {
                    var figi = figiResults.First();
                    details = details with
                    {
                        Figi = figi.Figi,
                        CompositeFigi = figi.CompositeFigi
                    };
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to enrich details with FIGI for {Symbol}", symbol);
            }
        }

        // Cache the result
        if (details is not null)
        {
            _detailsCache[cacheKey] = new CachedDetails(details, _detailsCacheDuration);
        }

        return details;
    }

    /// <summary>
    /// Lookup FIGI identifiers for symbols.
    /// </summary>
    /// <param name="symbols">List of symbols to lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of symbol to FIGI mappings.</returns>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FigiMapping>>> LookupFigiAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_figiClient is null)
        {
            return new Dictionary<string, IReadOnlyList<FigiMapping>>();
        }

        return await _figiClient.BulkLookupByTickersAsync(symbols, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Search OpenFIGI directly.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum results.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<FigiMapping>> SearchFigiAsync(
        string query,
        int limit = 20,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_figiClient is null)
        {
            return Array.Empty<FigiMapping>();
        }

        return await _figiClient.SearchAsync(query, limit: limit, ct: ct).ConfigureAwait(false);
    }

    private async Task<List<SymbolSearchResult>> EnrichWithMetadataAsync(
        List<SymbolSearchResult> results,
        CancellationToken ct)
    {
        var enriched = new List<SymbolSearchResult>();

        foreach (var result in results)
        {
            var metadata = await _metadataService.GetMetadataAsync(result.Symbol, ct).ConfigureAwait(false);
            if (metadata is not null)
            {
                enriched.Add(result with
                {
                    Name = string.IsNullOrEmpty(result.Name) || result.Name == result.Symbol
                        ? metadata.Name
                        : result.Name,
                    Exchange = result.Exchange ?? metadata.Exchange,
                    AssetType = result.AssetType ?? metadata.AssetType,
                    Country = result.Country ?? metadata.Country
                });
            }
            else
            {
                enriched.Add(result);
            }
        }

        return enriched;
    }

    private async Task<SymbolDetails> EnrichDetailsWithMetadataAsync(SymbolDetails details, CancellationToken ct)
    {
        var metadata = await _metadataService.GetMetadataAsync(details.Symbol, ct).ConfigureAwait(false);
        if (metadata is null)
            return details;

        return details with
        {
            Name = string.IsNullOrEmpty(details.Name) || details.Name == details.Symbol
                ? metadata.Name
                : details.Name,
            Sector = details.Sector ?? metadata.Sector,
            Industry = details.Industry ?? metadata.Industry,
            Exchange = details.Exchange ?? metadata.Exchange,
            Country = details.Country ?? metadata.Country,
            MarketCap = details.MarketCap ?? metadata.MarketCap,
            PaysDividend = details.PaysDividend ?? metadata.PaysDividend
        };
    }

    private static string BuildCacheKey(SymbolSearchRequest request)
    {
        return $"{request.Query}:{request.Limit}:{request.AssetType}:{request.Exchange}:{request.Provider}:{request.IncludeFigi}";
    }

    /// <summary>
    /// Clear the search and details cache.
    /// </summary>
    public void ClearCache()
    {
        _searchCache.Clear();
        _detailsCache.Clear();
        _log.Debug("Symbol search cache cleared");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var provider in _providers)
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _figiClient?.Dispose();
    }

    #region Cache Types

    private sealed record CachedSearchResult(SymbolSearchResponse Response, TimeSpan Duration)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Duration;
    }

    private sealed record CachedDetails(SymbolDetails Details, TimeSpan Duration)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Duration;
    }

    #endregion
}

/// <summary>
/// Provider availability status.
/// </summary>
public sealed record ProviderStatus(
    string Name,
    string DisplayName,
    int Priority,
    bool Available
);
