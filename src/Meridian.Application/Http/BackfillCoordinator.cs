using System.Threading;
using Meridian.Application.Backfill;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Storage.Sinks;
using Serilog;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Application.UI;

/// <summary>
/// Coordinates backfill operations using providers from <see cref="ProviderRegistry"/>.
/// All providers are resolved through the registry, which is populated during DI setup.
/// </summary>
[ImplementsAdr("ADR-001", "Uses ProviderRegistry for unified provider discovery")]
public sealed class BackfillCoordinator : IDisposable
{
    private readonly ConfigStore _store;
    private readonly ProviderRegistry? _registry;
    private readonly ProviderFactory? _factory;
    private readonly IEventMetrics _metrics;
    private readonly ILogger _log = LoggingSetup.ForContext<BackfillCoordinator>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OpenFigiSymbolResolver? _symbolResolver;
    private BackfillResult? _lastRun;
    private bool _disposed;

    /// <summary>
    /// Creates a BackfillCoordinator using the unified ProviderRegistry for provider discovery.
    /// </summary>
    /// <param name="store">Configuration store.</param>
    /// <param name="registry">Provider registry for unified provider discovery.</param>
    /// <param name="factory">Provider factory as fallback for creating providers if registry is empty.</param>
    /// <param name="metrics">Event metrics for tracking backfill operations.</param>
    public BackfillCoordinator(ConfigStore store, ProviderRegistry? registry = null, ProviderFactory? factory = null, IEventMetrics? metrics = null)
    {
        _store = store;
        _registry = registry;
        _factory = factory;
        _metrics = metrics ?? new DefaultEventMetrics();
        _lastRun = store.TryLoadBackfillStatus();

        // Initialize symbol resolver
        var cfg = store.Load();
        var openFigiConfig = cfg.Backfill?.Providers?.OpenFigi;
        if (openFigiConfig?.Enabled ?? true)
        {
            _symbolResolver = new OpenFigiSymbolResolver(openFigiConfig?.ApiKey, log: _log);
        }
    }

    public IEnumerable<object> DescribeProviders()
    {
        var providers = CreateProviders();
        return providers
            .Select(p => new
            {
                p.Name,
                p.DisplayName,
                p.Description,
                p.Priority,
                p.SupportsAdjustedPrices,
                p.SupportsDividends
            });
    }

    public BackfillResult? TryReadLast() => _lastRun ?? _store.TryLoadBackfillStatus();

    /// <summary>
    /// Gets current backfill progress. Returns null if no active backfill.
    /// </summary>
    public object? GetProgress()
    {
        if (_lastRun is null)
            return null;
        return new
        {
            lastRun = _lastRun,
            isActive = _gate.CurrentCount == 0,
            timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Get health status of all providers.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ProviderHealthStatus>> CheckProviderHealthAsync(CancellationToken ct = default)
    {
        var providers = CreateProviders();
        var results = new Dictionary<string, ProviderHealthStatus>();

        foreach (var provider in providers)
        {
            var startTime = DateTimeOffset.UtcNow;
            try
            {
                var isAvailable = await provider.IsAvailableAsync(ct).ConfigureAwait(false);
                var elapsed = DateTimeOffset.UtcNow - startTime;
                results[provider.Name] = new ProviderHealthStatus(
                    provider.Name,
                    isAvailable,
                    isAvailable ? "Healthy" : "Unavailable",
                    DateTimeOffset.UtcNow,
                    elapsed
                );
            }
            catch (Exception ex)
            {
                results[provider.Name] = new ProviderHealthStatus(
                    provider.Name,
                    false,
                    ex.Message,
                    DateTimeOffset.UtcNow
                );
            }
        }

        return results;
    }

    /// <summary>
    /// Resolve a symbol using OpenFIGI.
    /// </summary>
    public async Task<SymbolResolution?> ResolveSymbolAsync(string symbol, CancellationToken ct = default)
    {
        if (_symbolResolver is null)
        {
            _log.Warning("Symbol resolver not configured");
            return null;
        }

        return await _symbolResolver.ResolveAsync(symbol, ct: ct).ConfigureAwait(false);
    }

    public async Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A backfill is already running. Please try again after it completes.");

        try
        {
            var cfg = _store.Load();
            var compressionEnabled = cfg.Compress ?? false;
            var storageOpt = cfg.Storage?.ToStorageOptions(cfg.DataRoot, compressionEnabled)
                ?? StorageProfilePresets.CreateFromProfile(null, cfg.DataRoot, compressionEnabled);

            var policy = new JsonlStoragePolicy(storageOpt);
            await using var sink = new JsonlStorageSink(storageOpt, policy);
            await using var pipeline = new EventPipeline(sink, capacity: 20_000, enablePeriodicFlush: false, metrics: _metrics);

            // Keep pipeline counters scoped per run
            _metrics.Reset();

            var service = CreateService();
            var result = await service.RunAsync(request, pipeline, ct).ConfigureAwait(false);

            var statusStore = new BackfillStatusStore(_store.GetDataRoot(cfg));
            await statusStore.WriteAsync(result).ConfigureAwait(false);
            _lastRun = result;
            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Backfill failed");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates backfill providers using a priority-based discovery approach:
    /// 1. ProviderRegistry.GetBackfillProviders() - unified registry
    /// 2. ProviderFactory.CreateBackfillProviders() - factory with credential resolution
    /// 3. Manual instantiation - backwards compatibility fallback
    /// </summary>
    private List<IHistoricalDataProvider> CreateProviders()
    {
        // Priority 1: Use ProviderRegistry if available and populated
        if (_registry != null)
        {
            var registryProviders = _registry.GetBackfillProviders();
            if (registryProviders.Count > 0)
            {
                _log.Information("Using {Count} providers from ProviderRegistry", registryProviders.Count);
                return registryProviders.ToList();
            }
        }

        // Priority 2: Use ProviderFactory if available
        if (_factory != null)
        {
            var factoryProviders = _factory.CreateBackfillProviders();
            if (factoryProviders.Count > 0)
            {
                _log.Information("Using {Count} providers from ProviderFactory", factoryProviders.Count);

                // Register with registry if available (populate for future use)
                if (_registry != null)
                {
                    foreach (var provider in factoryProviders)
                    {
                        _registry.Register(provider);
                    }
                }

                return factoryProviders.ToList();
            }
        }

        // Priority 3: Fallback to manual instantiation (backwards compatibility)
        _log.Debug("Using fallback manual provider instantiation");
        return CreateProvidersManually();
    }

    /// <summary>
    /// Fallback method for manual provider creation when ProviderRegistry and ProviderFactory
    /// are not available. Maintains backwards compatibility.
    /// </summary>
    private List<IHistoricalDataProvider> CreateProvidersManually()
    {
        var cfg = _store.Load();
        var backfillCfg = cfg.Backfill;
        var providersCfg = backfillCfg?.Providers;

        var providers = new List<IHistoricalDataProvider>();

        // Stooq (always available, free)
        var stooqCfg = providersCfg?.Stooq;
        if (stooqCfg?.Enabled ?? true)
        {
            providers.Add(new StooqHistoricalDataProvider(log: _log));
        }

        // Yahoo Finance
        var yahooCfg = providersCfg?.Yahoo;
        if (yahooCfg?.Enabled ?? true)
        {
            providers.Add(new YahooFinanceHistoricalDataProvider(log: _log));
        }

        // Nasdaq Data Link (Quandl)
        var nasdaqCfg = providersCfg?.Nasdaq;
        if (nasdaqCfg?.Enabled ?? true)
        {
            providers.Add(new NasdaqDataLinkHistoricalDataProvider(
                apiKey: nasdaqCfg?.ApiKey,
                database: nasdaqCfg?.Database ?? "WIKI",
                log: _log
            ));
        }

        // FRED economic data
        var fredCfg = providersCfg?.Fred;
        if (fredCfg?.Enabled ?? false && !string.IsNullOrWhiteSpace(fredCfg.ApiKey))
        {
            providers.Add(new FredHistoricalDataProvider(
                apiKey: fredCfg.ApiKey,
                log: _log
            ));
        }

        // Sort by priority
        return providers
            .OrderBy(p => p.Priority)
            .ToList();
    }

    private HistoricalBackfillService CreateService()
    {
        var cfg = _store.Load();
        var backfillCfg = cfg.Backfill;

        var providers = CreateProviders();

        // If composite provider requested, wrap all providers
        if (string.Equals(backfillCfg?.Provider, "composite", StringComparison.OrdinalIgnoreCase)
            || (backfillCfg?.EnableFallback ?? true))
        {
            var composite = new CompositeHistoricalDataProvider(
                providers,
                backfillCfg?.EnableSymbolResolution ?? true ? _symbolResolver : null,
                enableCrossValidation: false,
                log: _log
            );

            // Combine composite (for fallback routing) with individual providers (for direct selection)
            var combined = new List<IHistoricalDataProvider> { composite };
            combined.AddRange(providers);
            providers = combined;
        }

        return new HistoricalBackfillService(providers, _log);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _symbolResolver?.Dispose();
        _gate.Dispose();
    }
}
