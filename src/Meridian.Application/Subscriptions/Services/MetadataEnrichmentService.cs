using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for enriching symbols with metadata (industry, sector, market cap).
/// Provides filtering capabilities based on symbol metadata.
/// </summary>
public sealed class MetadataEnrichmentService
{
    private readonly string _cachePath;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, SymbolMetadata> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _cacheLoaded;

    // Built-in metadata for common symbols (fallback when external API not available)
    private static readonly Dictionary<string, SymbolMetadata> BuiltInMetadata = InitializeBuiltInMetadata();

    public MetadataEnrichmentService(string? cachePath = null, ILogger? log = null)
    {
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "metadata_cache.json");
        _log = log ?? LoggingSetup.ForContext<MetadataEnrichmentService>();
    }

    /// <summary>
    /// Get metadata for a specific symbol.
    /// </summary>
    public async Task<SymbolMetadata?> GetMetadataAsync(string symbol, CancellationToken ct = default)
    {
        await EnsureCacheLoadedAsync(ct);

        if (_cache.TryGetValue(symbol, out var cached))
            return cached;

        if (BuiltInMetadata.TryGetValue(symbol, out var builtIn))
        {
            _cache[symbol] = builtIn;
            return builtIn;
        }

        return null;
    }

    /// <summary>
    /// Get metadata for multiple symbols.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, SymbolMetadata>> GetMetadataBatchAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        await EnsureCacheLoadedAsync(ct);

        var result = new Dictionary<string, SymbolMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            var metadata = await GetMetadataAsync(symbol, ct);
            if (metadata is not null)
            {
                result[symbol] = metadata;
            }
        }
        return result;
    }

    /// <summary>
    /// Filter symbols based on metadata criteria.
    /// </summary>
    public async Task<MetadataFilterResult> FilterSymbolsAsync(
        SymbolMetadataFilter filter,
        CancellationToken ct = default)
    {
        await EnsureCacheLoadedAsync(ct);

        var allMetadata = _cache.Values.Concat(BuiltInMetadata.Values)
            .DistinctBy(m => m.Symbol)
            .ToList();

        var filtered = allMetadata.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.Sector))
        {
            filtered = filtered.Where(m =>
                m.Sector?.Equals(filter.Sector, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(filter.Industry))
        {
            filtered = filtered.Where(m =>
                m.Industry?.Equals(filter.Industry, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (filter.MinMarketCap.HasValue)
        {
            filtered = filtered.Where(m => m.MarketCap >= filter.MinMarketCap.Value);
        }

        if (filter.MaxMarketCap.HasValue)
        {
            filtered = filtered.Where(m => m.MarketCap <= filter.MaxMarketCap.Value);
        }

        if (filter.MarketCapCategory.HasValue)
        {
            filtered = filtered.Where(m => m.MarketCapCategory == filter.MarketCapCategory.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Exchange))
        {
            filtered = filtered.Where(m =>
                m.Exchange?.Equals(filter.Exchange, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(filter.Country))
        {
            filtered = filtered.Where(m =>
                m.Country?.Equals(filter.Country, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(filter.AssetType))
        {
            filtered = filtered.Where(m =>
                m.AssetType?.Equals(filter.AssetType, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(filter.IndexMembership))
        {
            filtered = filtered.Where(m =>
                m.IndexMemberships?.Contains(filter.IndexMembership, StringComparer.OrdinalIgnoreCase) == true);
        }

        if (filter.PaysDividend.HasValue)
        {
            filtered = filtered.Where(m => m.PaysDividend == filter.PaysDividend.Value);
        }

        var result = filtered.ToArray();
        return new MetadataFilterResult(result, result.Length, filter);
    }

    /// <summary>
    /// Get available sectors from cached metadata.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableSectorsAsync(CancellationToken ct = default)
    {
        await EnsureCacheLoadedAsync(ct);

        return _cache.Values.Concat(BuiltInMetadata.Values)
            .Select(m => m.Sector)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList()!;
    }

    /// <summary>
    /// Get available industries from cached metadata.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableIndustriesAsync(
        string? sector = null,
        CancellationToken ct = default)
    {
        await EnsureCacheLoadedAsync(ct);

        var metadata = _cache.Values.Concat(BuiltInMetadata.Values);

        if (!string.IsNullOrWhiteSpace(sector))
        {
            metadata = metadata.Where(m =>
                m.Sector?.Equals(sector, StringComparison.OrdinalIgnoreCase) == true);
        }

        return metadata
            .Select(m => m.Industry)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList()!;
    }

    /// <summary>
    /// Update metadata for a symbol.
    /// </summary>
    public async Task UpdateMetadataAsync(SymbolMetadata metadata, CancellationToken ct = default)
    {
        var updated = metadata with { LastUpdated = DateTimeOffset.UtcNow };
        _cache[metadata.Symbol] = updated;
        await SaveCacheAsync(ct);
    }

    /// <summary>
    /// Update metadata for multiple symbols.
    /// </summary>
    public async Task UpdateMetadataBatchAsync(
        IEnumerable<SymbolMetadata> metadataList,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var metadata in metadataList)
        {
            var updated = metadata with { LastUpdated = now };
            _cache[metadata.Symbol] = updated;
        }
        await SaveCacheAsync(ct);
    }

    /// <summary>
    /// Categorize market cap value.
    /// </summary>
    public static MarketCapCategory CategorizeMarketCap(decimal marketCap)
    {
        return marketCap switch
        {
            < 300_000_000m => MarketCapCategory.Nano,
            < 2_000_000_000m => MarketCapCategory.Micro,
            < 10_000_000_000m => MarketCapCategory.Small,
            < 200_000_000_000m => MarketCapCategory.Mid,
            < 1_000_000_000_000m => MarketCapCategory.Large,
            _ => MarketCapCategory.Mega
        };
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken ct)
    {
        if (_cacheLoaded)
            return;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_cacheLoaded)
                return;
            await LoadCacheAsync(ct);
            _cacheLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadCacheAsync(CancellationToken ct)
    {
        if (!File.Exists(_cachePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_cachePath, ct);
            var cached = JsonSerializer.Deserialize<List<SymbolMetadata>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            foreach (var metadata in cached ?? Enumerable.Empty<SymbolMetadata>())
            {
                _cache[metadata.Symbol] = metadata;
            }

            _log.Debug("Loaded {Count} metadata entries from cache", _cache.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load metadata cache from {Path}", _cachePath);
        }
    }

    private async Task SaveCacheAsync(CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_cache.Values.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await AtomicFileWriter.WriteAsync(_cachePath, json, ct);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save metadata cache to {Path}", _cachePath);
        }
    }

    private static Dictionary<string, SymbolMetadata> InitializeBuiltInMetadata()
    {
        var metadata = new Dictionary<string, SymbolMetadata>(StringComparer.OrdinalIgnoreCase);

        // Technology - Mega Cap
        AddMetadata(metadata, "AAPL", "Apple Inc.", "Technology", "Consumer Electronics", 3000000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX", "DJI" });
        AddMetadata(metadata, "MSFT", "Microsoft Corporation", "Technology", "Software - Infrastructure", 2800000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX", "DJI" });
        AddMetadata(metadata, "GOOGL", "Alphabet Inc.", "Technology", "Internet Content & Information", 1800000000000m, "NASDAQ", "US", false, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "AMZN", "Amazon.com Inc.", "Technology", "Internet Retail", 1600000000000m, "NASDAQ", "US", false, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "META", "Meta Platforms Inc.", "Technology", "Internet Content & Information", 900000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "NVDA", "NVIDIA Corporation", "Technology", "Semiconductors", 1200000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "TSLA", "Tesla Inc.", "Technology", "Auto Manufacturers", 800000000000m, "NASDAQ", "US", false, new[] { "SPX", "NDX" });

        // Technology - Semiconductors
        AddMetadata(metadata, "AMD", "Advanced Micro Devices", "Technology", "Semiconductors", 200000000000m, "NASDAQ", "US", false, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "INTC", "Intel Corporation", "Technology", "Semiconductors", 150000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX", "DJI" });
        AddMetadata(metadata, "AVGO", "Broadcom Inc.", "Technology", "Semiconductors", 400000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "QCOM", "Qualcomm Inc.", "Technology", "Semiconductors", 180000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });

        // Healthcare - Pharma
        AddMetadata(metadata, "JNJ", "Johnson & Johnson", "Healthcare", "Drug Manufacturers", 400000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "PFE", "Pfizer Inc.", "Healthcare", "Drug Manufacturers", 200000000000m, "NYSE", "US", true, new[] { "SPX" });
        AddMetadata(metadata, "MRK", "Merck & Co.", "Healthcare", "Drug Manufacturers", 280000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "ABBV", "AbbVie Inc.", "Healthcare", "Drug Manufacturers", 250000000000m, "NYSE", "US", true, new[] { "SPX" });
        AddMetadata(metadata, "LLY", "Eli Lilly and Company", "Healthcare", "Drug Manufacturers", 500000000000m, "NYSE", "US", true, new[] { "SPX" });

        // Financials - Banks
        AddMetadata(metadata, "JPM", "JPMorgan Chase & Co.", "Financials", "Banks - Diversified", 450000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "BAC", "Bank of America Corp", "Financials", "Banks - Diversified", 250000000000m, "NYSE", "US", true, new[] { "SPX" });
        AddMetadata(metadata, "WFC", "Wells Fargo & Co.", "Financials", "Banks - Diversified", 180000000000m, "NYSE", "US", true, new[] { "SPX" });
        AddMetadata(metadata, "GS", "Goldman Sachs Group", "Financials", "Capital Markets", 120000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "MS", "Morgan Stanley", "Financials", "Capital Markets", 150000000000m, "NYSE", "US", true, new[] { "SPX" });

        // Consumer Discretionary
        AddMetadata(metadata, "HD", "The Home Depot", "Consumer Discretionary", "Home Improvement Retail", 350000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "NKE", "Nike Inc.", "Consumer Discretionary", "Footwear & Accessories", 150000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "MCD", "McDonald's Corporation", "Consumer Discretionary", "Restaurants", 200000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });

        // Consumer Staples
        AddMetadata(metadata, "PG", "Procter & Gamble", "Consumer Staples", "Household Products", 350000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "KO", "The Coca-Cola Company", "Consumer Staples", "Beverages", 260000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "PEP", "PepsiCo Inc.", "Consumer Staples", "Beverages", 230000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });
        AddMetadata(metadata, "WMT", "Walmart Inc.", "Consumer Staples", "Discount Stores", 420000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "COST", "Costco Wholesale", "Consumer Staples", "Discount Stores", 250000000000m, "NASDAQ", "US", true, new[] { "SPX", "NDX" });

        // Energy
        AddMetadata(metadata, "XOM", "Exxon Mobil Corporation", "Energy", "Oil & Gas Integrated", 450000000000m, "NYSE", "US", true, new[] { "SPX" });
        AddMetadata(metadata, "CVX", "Chevron Corporation", "Energy", "Oil & Gas Integrated", 300000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });

        // Industrials
        AddMetadata(metadata, "BA", "Boeing Company", "Industrials", "Aerospace & Defense", 130000000000m, "NYSE", "US", false, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "CAT", "Caterpillar Inc.", "Industrials", "Farm & Heavy Construction", 150000000000m, "NYSE", "US", true, new[] { "SPX", "DJI" });
        AddMetadata(metadata, "HON", "Honeywell International", "Industrials", "Specialty Industrial Machinery", 130000000000m, "NASDAQ", "US", true, new[] { "SPX", "DJI" });

        // ETFs
        AddMetadata(metadata, "SPY", "SPDR S&P 500 ETF", "ETF", "Large Blend", 400000000000m, "NYSE", "US", true, null, "ETF");
        AddMetadata(metadata, "QQQ", "Invesco QQQ Trust", "ETF", "Large Growth", 200000000000m, "NASDAQ", "US", true, null, "ETF");
        AddMetadata(metadata, "IWM", "iShares Russell 2000", "ETF", "Small Blend", 60000000000m, "NYSE", "US", true, null, "ETF");
        AddMetadata(metadata, "DIA", "SPDR Dow Jones ETF", "ETF", "Large Value", 30000000000m, "NYSE", "US", true, null, "ETF");
        AddMetadata(metadata, "VTI", "Vanguard Total Stock Market", "ETF", "Large Blend", 300000000000m, "NYSE", "US", true, null, "ETF");

        return metadata;
    }

    private static void AddMetadata(
        Dictionary<string, SymbolMetadata> dict,
        string symbol,
        string name,
        string sector,
        string industry,
        decimal marketCap,
        string exchange,
        string country,
        bool paysDividend,
        string[]? indexMemberships,
        string assetType = "Stock")
    {
        dict[symbol] = new SymbolMetadata(
            Symbol: symbol,
            Name: name,
            Sector: sector,
            Industry: industry,
            MarketCap: marketCap,
            MarketCapCategory: CategorizeMarketCap(marketCap),
            Exchange: exchange,
            Country: country,
            AssetType: assetType,
            PaysDividend: paysDividend,
            IndexMemberships: indexMemberships,
            LastUpdated: DateTimeOffset.UtcNow
        );
    }
}
