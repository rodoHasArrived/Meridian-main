using Meridian.Application.Logging;
using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Contracts;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using BackfillResult = Meridian.Application.Backfill.BackfillResult;
using CoreBackfillCoordinator = Meridian.Application.UI.BackfillCoordinator;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Result of a backfill preview operation.
/// </summary>
public sealed record BackfillPreviewResult(
    string Provider,
    string ProviderDisplayName,
    DateOnly From,
    DateOnly To,
    int TotalDays,
    int EstimatedTradingDays,
    SymbolPreview[] Symbols,
    int EstimatedDurationSeconds,
    string[] Notes
);

/// <summary>
/// Preview information for a single symbol.
/// </summary>
public sealed record SymbolPreview(
    string Symbol,
    string DateRange,
    int EstimatedBars,
    ExistingDataInfo ExistingData,
    bool WouldOverwrite
);

/// <summary>
/// Information about existing data for a symbol.
/// </summary>
public sealed record ExistingDataInfo(
    bool HasData,
    bool IsComplete,
    DateOnly? ExistingFrom,
    DateOnly? ExistingTo,
    int FileCount,
    long TotalSizeBytes
);

/// <summary>
/// Extends the core BackfillCoordinator with preview functionality for UI applications.
/// Wraps the core implementation and adds preview-specific methods.
/// </summary>
/// <remarks>
/// <para><b>Migration Note:</b> This class wraps the core implementation from
/// <see cref="Meridian.Application.UI.BackfillCoordinator"/> to add preview
/// functionality while delegating core operations to the wrapped instance.</para>
/// <para><b>Provider Discovery:</b> Uses <see cref="ProviderRegistry"/> for unified
/// provider discovery. If a registry is provided, backfill providers are discovered
/// via <see cref="ProviderRegistry.GetBackfillProviders()"/>.</para>
/// </remarks>
[ImplementsAdr("ADR-001", "Uses ProviderRegistry for unified provider discovery")]
public sealed class BackfillCoordinator : IDisposable
{
    private readonly CoreBackfillCoordinator _core;
    private readonly ConfigStore _store;
    private readonly ProviderRegistry? _registry;
    private readonly ProviderFactory? _factory;
    private readonly Serilog.ILogger _log = LoggingSetup.ForContext<BackfillCoordinator>();

    /// <summary>
    /// Creates a BackfillCoordinator with unified provider registry support.
    /// </summary>
    /// <param name="store">Configuration store.</param>
    /// <param name="registry">Optional provider registry for unified provider discovery.</param>
    /// <param name="factory">Optional provider factory for creating providers if registry is empty.</param>
    public BackfillCoordinator(ConfigStore store, ProviderRegistry? registry = null, ProviderFactory? factory = null)
    {
        _store = store;
        _registry = registry;
        _factory = factory;
        // Convert Ui.Shared.ConfigStore wrapper to the core ConfigStore for the core coordinator
        var coreStore = new Meridian.Application.UI.ConfigStore(store.ConfigPath);
        _core = new CoreBackfillCoordinator(coreStore, registry, factory);
    }

    /// <summary>
    /// Gets descriptions of all available backfill providers.
    /// </summary>
    public IEnumerable<object> DescribeProviders() => _core.DescribeProviders();

    /// <summary>
    /// Tries to read the last backfill result.
    /// </summary>
    public BackfillResult? TryReadLast() => _core.TryReadLast();

    /// <summary>
    /// Returns the per-symbol checkpoint map, or <c>null</c> when no checkpoints exist.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly>? TryReadSymbolCheckpoints() => _core.TryReadSymbolCheckpoints();

    /// <summary>
    /// Returns the per-symbol bar-count sidecar, or <c>null</c> when no bar-count data exists.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts() => _core.TryReadSymbolBarCounts();

    /// <summary>
    /// Runs a backfill operation for the specified request.
    /// </summary>
    public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
        => _core.RunAsync(request, ct);

    public void ValidateRequest(BackfillRequest request) => _core.ValidateRequest(request);

    /// <summary>
    /// Gets health status of all providers.
    /// </summary>
    public Task<IReadOnlyDictionary<string, Infrastructure.Adapters.Core.ProviderHealthStatus>> CheckProviderHealthAsync(CancellationToken ct = default)
        => _core.CheckProviderHealthAsync(ct);

    /// <summary>
    /// Resolve a symbol using OpenFIGI.
    /// </summary>
    public Task<Meridian.Infrastructure.Adapters.Core.SymbolResolution.SymbolResolution?> ResolveSymbolAsync(string symbol, CancellationToken ct = default)
        => _core.ResolveSymbolAsync(symbol, ct);

    /// <summary>
    /// Gets backfill progress snapshot from the core coordinator, if available.
    /// </summary>
    public object? GetProgress() => _core.GetProgress();

    public void Dispose() => _core.Dispose();

    /// <summary>
    /// Previews a backfill operation without actually fetching data.
    /// Returns information about what would be backfilled.
    /// </summary>
    public Task<BackfillPreviewResult> PreviewAsync(BackfillRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        var service = CreateService();
        var cfg = _store.Load();
        var dataRoot = _store.GetDataRoot(cfg);

        var symbolPreviews = new List<SymbolPreview>();
        var providerInfo = service.Providers
            .FirstOrDefault(p => p.Name.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));

        var from = request.From ?? DateOnly.FromDateTime(DateTime.Today.AddYears(-1));
        var to = request.To ?? DateOnly.FromDateTime(DateTime.Today);
        var totalDays = to.DayNumber - from.DayNumber + 1;
        var tradingDays = EstimateTradingDays(from, to);
        var estimatedBarsPerSymbol = EstimateBarsPerSymbol(request.Granularity, tradingDays);

        foreach (var symbol in request.Symbols)
        {
            // Check if data already exists for this symbol
            var existingDataInfo = GetExistingDataInfo(dataRoot, symbol, from, to, request.Granularity);

            symbolPreviews.Add(new SymbolPreview(
                Symbol: symbol.ToUpperInvariant(),
                DateRange: $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}",
                EstimatedBars: estimatedBarsPerSymbol,
                ExistingData: existingDataInfo,
                WouldOverwrite: existingDataInfo.HasData && !existingDataInfo.IsComplete
            ));
        }

        return Task.FromResult(new BackfillPreviewResult(
            Provider: providerInfo?.Name ?? request.Provider,
            ProviderDisplayName: providerInfo?.DisplayName ?? request.Provider,
            From: from,
            To: to,
            TotalDays: totalDays,
            EstimatedTradingDays: tradingDays,
            Symbols: symbolPreviews.ToArray(),
            EstimatedDurationSeconds: EstimateBackfillDuration(request.Symbols.Count, tradingDays, request.Granularity, providerInfo),
            Notes: GetProviderNotes(providerInfo)
        ));
    }

    private static int EstimateTradingDays(DateOnly from, DateOnly to)
    {
        // Rough estimate: ~252 trading days per year, exclude weekends
        var days = 0;
        var current = from;
        while (current <= to)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                days++;
            }
            current = current.AddDays(1);
        }
        return days;
    }

    private ExistingDataInfo GetExistingDataInfo(
        string dataRoot,
        string symbol,
        DateOnly from,
        DateOnly to,
        DataGranularity granularity)
    {
        // Check for existing data files
        var symbolDir = Path.Combine(dataRoot, "historical", symbol.ToUpperInvariant());
        if (!Directory.Exists(symbolDir))
        {
            return new ExistingDataInfo(
                HasData: false,
                IsComplete: false,
                ExistingFrom: null,
                ExistingTo: null,
                FileCount: 0,
                TotalSizeBytes: 0
            );
        }

        var files = Directory.GetFiles(symbolDir, "*.jsonl*", SearchOption.AllDirectories)
            .Where(file => FileMatchesGranularity(file, granularity))
            .ToArray();
        if (files.Length == 0)
        {
            return new ExistingDataInfo(
                HasData: false,
                IsComplete: false,
                ExistingFrom: null,
                ExistingTo: null,
                FileCount: 0,
                TotalSizeBytes: 0
            );
        }

        var totalSize = files.Sum(f => new FileInfo(f).Length);

        // Try to determine date range from file names
        DateOnly? existingFrom = null;
        DateOnly? existingTo = null;
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Try to extract date from filename (common patterns: YYYY-MM-DD, YYYYMMDD)
            if (TryExtractDateFromFileName(name, out var date))
            {
                if (existingFrom is null || date < existingFrom.Value)
                    existingFrom = date;
                if (existingTo is null || date > existingTo.Value)
                    existingTo = date;
            }
        }

        var isComplete = existingFrom <= from && existingTo >= to;

        return new ExistingDataInfo(
            HasData: true,
            IsComplete: isComplete,
            ExistingFrom: existingFrom,
            ExistingTo: existingTo,
            FileCount: files.Length,
            TotalSizeBytes: totalSize
        );
    }

    private static bool FileMatchesGranularity(string filePath, DataGranularity granularity)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (granularity.IsIntraday())
            return fileName.Contains(granularity.ToStorageFilePrefix(), StringComparison.OrdinalIgnoreCase);

        if (fileName.Contains(DataGranularity.Daily.ToStorageFilePrefix(), StringComparison.OrdinalIgnoreCase))
            return true;

        return !GetIntradayStoragePrefixes()
            .Any(prefix => fileName.Contains(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetIntradayStoragePrefixes() =>
    [
        DataGranularity.Minute1.ToStorageFilePrefix(),
        DataGranularity.Minute5.ToStorageFilePrefix(),
        DataGranularity.Minute15.ToStorageFilePrefix(),
        DataGranularity.Minute30.ToStorageFilePrefix(),
        DataGranularity.Hour1.ToStorageFilePrefix(),
        DataGranularity.Hour4.ToStorageFilePrefix()
    ];

    private static bool TryExtractDateFromFileName(string name, out DateOnly date)
    {
        date = default;

        // Try YYYY-MM-DD pattern
        if (name.Length >= 10)
        {
            for (var i = 0; i <= name.Length - 10; i++)
            {
                var segment = name.Substring(i, 10);
                if (DateOnly.TryParseExact(segment, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out date))
                    return true;
            }
        }

        // Try YYYYMMDD pattern
        if (name.Length >= 8)
        {
            for (var i = 0; i <= name.Length - 8; i++)
            {
                var segment = name.Substring(i, 8);
                if (DateOnly.TryParseExact(segment, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date))
                    return true;
            }
        }

        return false;
    }

    private static int EstimateBarsPerSymbol(DataGranularity granularity, int tradingDays)
    {
        if (tradingDays <= 0)
            return 0;

        var barsPerTradingDay = granularity switch
        {
            DataGranularity.Minute1 => 390,
            DataGranularity.Minute5 => 78,
            DataGranularity.Minute15 => 26,
            DataGranularity.Minute30 => 13,
            DataGranularity.Hour1 => 7,
            DataGranularity.Hour4 => 2,
            _ => 1
        };

        return tradingDays * barsPerTradingDay;
    }

    private static int EstimateBackfillDuration(
        int symbolCount,
        int tradingDays,
        DataGranularity granularity,
        IHistoricalDataProvider? provider)
    {
        var requestsPerSymbol = granularity switch
        {
            DataGranularity.Minute1 => Math.Max(1, (int)Math.Ceiling(tradingDays / 8d)),
            DataGranularity.Minute5 or DataGranularity.Minute15 or DataGranularity.Minute30 => Math.Max(1, (int)Math.Ceiling(tradingDays / 60d)),
            DataGranularity.Hour1 or DataGranularity.Hour4 => Math.Max(1, (int)Math.Ceiling(tradingDays / 730d)),
            _ => 1
        };

        var estimatedRequests = Math.Max(1, symbolCount * requestsPerSymbol);
        var requestsPerSecond = provider?.RateLimitDelay > TimeSpan.Zero
            ? 1d / provider.RateLimitDelay.TotalSeconds
            : 0.5d;

        return (int)Math.Ceiling(estimatedRequests / Math.Max(requestsPerSecond, 0.1d)) + symbolCount;
    }

    /// <summary>
    /// Gets provider notes from the centralized ProviderCatalog.
    /// Eliminates per-provider conditionals in favor of standardized catalog lookup.
    /// </summary>
    private static string[] GetProviderNotes(IHistoricalDataProvider? provider)
    {
        if (provider is null)
        {
            return new[] { "Provider not found. Backfill may fail." };
        }

        // Use centralized ProviderCatalog instead of hardcoded per-provider conditionals
        var catalogNotes = ProviderCatalog.GetProviderNotes(provider.Name);
        if (catalogNotes.Length > 0)
        {
            return catalogNotes;
        }

        // Fallback: generate notes from provider's own metadata
        var notes = new List<string>();

        if (!string.IsNullOrEmpty(provider.Description))
        {
            notes.Add(provider.Description);
        }

        if (provider.MaxRequestsPerWindow < int.MaxValue)
        {
            var window = provider.RateLimitWindow.TotalMinutes >= 1
                ? $"{provider.RateLimitWindow.TotalMinutes:F0} minute(s)"
                : $"{provider.RateLimitWindow.TotalSeconds:F0} second(s)";
            notes.Add($"Rate limit: {provider.MaxRequestsPerWindow} requests/{window}.");
        }

        if (provider is IHistoricalAggregateBarProvider aggregateProvider)
        {
            var supported = aggregateProvider.SupportedGranularities
                .Select(g => g.ToDisplayName())
                .OrderBy(name => name)
                .ToArray();
            if (supported.Length > 0)
                notes.Add($"Intraday granularities: {string.Join(", ", supported)}.");
        }

        if (provider.RateLimitDelay > TimeSpan.Zero)
        {
            notes.Add($"Minimum delay between requests: {provider.RateLimitDelay.TotalMilliseconds:F0}ms.");
        }

        return notes.ToArray();
    }

    /// <summary>
    /// Creates the backfill service using providers from ProviderRegistry.
    /// Falls back to ProviderFactory or manual instantiation if registry is empty.
    /// </summary>
    private Meridian.Application.Backfill.HistoricalBackfillService CreateService()
    {
        var providers = GetProviders();
        return new Meridian.Application.Backfill.HistoricalBackfillService(providers, _log);
    }

    /// <summary>
    /// Gets backfill providers using a priority-based discovery approach:
    /// 1. ProviderRegistry.GetBackfillProviders() - unified registry
    /// 2. ProviderFactory.CreateBackfillProviders() - factory with credential resolution
    /// 3. Manual instantiation - backwards compatibility fallback
    /// </summary>
    private IReadOnlyList<IHistoricalDataProvider> GetProviders()
    {
        // Priority 1: Use ProviderRegistry if available and populated
        if (_registry != null)
        {
            var registryProviders = _registry.GetBackfillProviders();
            if (registryProviders.Count > 0)
            {
                _log.Information("Using {Count} providers from ProviderRegistry for preview", registryProviders.Count);
                return registryProviders;
            }
        }

        // Priority 2: Use ProviderFactory if available
        if (_factory != null)
        {
            var factoryProviders = _factory.CreateBackfillProviders();
            if (factoryProviders.Count > 0)
            {
                _log.Information("Using {Count} providers from ProviderFactory for preview", factoryProviders.Count);

                // Register with registry if available (populate for future use)
                if (_registry != null)
                {
                    foreach (var provider in factoryProviders)
                    {
                        _registry.Register(provider);
                    }
                }

                return factoryProviders;
            }
        }

        // Priority 3: Fallback to single Stooq provider (backwards compatibility)
        _log.Debug("Using fallback Stooq provider for preview");
        return new IHistoricalDataProvider[]
        {
            new StooqHistoricalDataProvider()
        };
    }
}
