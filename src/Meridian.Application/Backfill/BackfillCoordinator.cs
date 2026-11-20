using System.Threading;
using System.Threading.Channels;
using Meridian.Core.Pipeline;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Contracts;
using Meridian.Platform.Tracing;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Storage.Sinks;
using Meridian.Storage.Services;
using Serilog;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Api;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

/// <summary>
/// Coordinates backfill operations using providers from <see cref="ProviderRegistry"/>.
/// All providers are resolved through the registry, which is populated during DI setup.
/// Owns execution, provider discovery, and preview estimation; the
/// <c>Meridian.Ui.Shared.Services.BackfillCoordinator</c> is a thin read-model façade over this type.
/// </summary>
[ImplementsAdr("ADR-001", "Uses ProviderRegistry for unified provider discovery")]
public sealed class BackfillCoordinator : IDisposable
{
    private readonly ConfigStore _store;
    private readonly ProviderRegistry? _registry;
    private readonly ProviderFactory? _factory;
    private readonly IProviderCatalog _providerCatalog;
    private readonly IEventMetrics _metrics;
    private readonly ILogger _log = LoggingSetup.ForContext<BackfillCoordinator>();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BackfillProgressTracker _progressTracker = new();
    private readonly ISymbolResolver? _symbolResolver;
    private readonly OpenFigiSymbolResolver? _ownedSymbolResolver;
    private readonly Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? _symbolTimelineResolver;
    private BackfillResult? _lastRun;
    private volatile ActiveQuotaRun? _activeQuotaRun;
    private bool _disposed;

    /// <summary>
    /// Creates a BackfillCoordinator using the unified ProviderRegistry for provider discovery.
    /// </summary>
    /// <param name="store">Configuration store.</param>
    /// <param name="registry">Provider registry for unified provider discovery.</param>
    /// <param name="factory">Provider factory as fallback for creating providers if registry is empty.</param>
    /// <param name="metrics">Event metrics for tracking backfill operations.</param>
    /// <param name="symbolResolver">
    ///     Shared symbol resolution spine (typically the canonical-registry resolver from DI).
    ///     When null, a coordinator-owned OpenFIGI resolver is created from configuration.
    /// </param>
    /// <param name="symbolTimelineResolver">
    ///     Optional security-master timeline resolver; when present, daily backfill chunks
    ///     spanning a ticker rename query the provider with the era-correct symbol.
    /// </param>
    /// <param name="providerCatalog">Provider metadata owned by this application's service provider.</param>
    public BackfillCoordinator(
        ConfigStore store,
        ProviderRegistry? registry = null,
        ProviderFactory? factory = null,
        IEventMetrics? metrics = null,
        ISymbolResolver? symbolResolver = null,
        Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? symbolTimelineResolver = null,
        IProviderCatalog? providerCatalog = null)
    {
        _store = store;
        _registry = registry;
        _factory = factory;
        _providerCatalog = providerCatalog ?? new RuntimeProviderCatalog();
        _metrics = metrics ?? new DefaultEventMetrics();
        _symbolTimelineResolver = symbolTimelineResolver;
        _lastRun = store.TryLoadBackfillStatus();

        if (symbolResolver is not null)
        {
            _symbolResolver = symbolResolver;
            return;
        }

        // No shared spine supplied — fall back to a coordinator-owned OpenFIGI resolver.
        var cfg = store.Load();
        var openFigiConfig = cfg.Backfill?.Providers?.OpenFigi;
        if (openFigiConfig?.Enabled ?? true)
        {
            _ownedSymbolResolver = new OpenFigiSymbolResolver(openFigiConfig?.ApiKey, log: _log, options: openFigiConfig);
            _symbolResolver = _ownedSymbolResolver;
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
                p.SupportsDividends,
                p.SupportsIntraday,
                SupportedGranularities = GetSupportedGranularityValues(p)
            });
    }

    public void ValidateRequest(BackfillRequest request)
    {
        var config = _store.Load().Backfill;
        var providers = CreateProviders();
        using var composite = CreateComposite(request, providers, config);
        CreateService(providers, composite, config).ValidateRequest(request);
    }

    public bool HasHistoricalProvider(string provider) =>
        FindProvider(CreateProviders(), provider) is not null;

    internal string ResolveDataRoot() => _store.GetDataRoot(_store.Load());

    public BackfillResult? TryReadLast() => _lastRun ?? _store.TryLoadBackfillStatus();

    /// <summary>
    /// Returns whether a backfill run is currently active.
    /// </summary>
    public bool IsActive => _gate.CurrentCount == 0;

    /// <summary>
    /// Returns the per-symbol checkpoint map saved during the last backfill run,
    /// or <c>null</c> when no checkpoints have been persisted yet.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly>? TryReadSymbolCheckpoints()
    {
        var cfg = _store.Load();
        var statusStore = new BackfillStatusStore(_store.GetDataRoot(cfg));
        return statusStore.TryReadSymbolCheckpoints();
    }

    /// <summary>
    /// Returns the per-symbol bar-count map saved during the last backfill run,
    /// or <c>null</c> when no bar-count data has been persisted yet.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts()
    {
        var cfg = _store.Load();
        var statusStore = new BackfillStatusStore(_store.GetDataRoot(cfg));
        return statusStore.TryReadSymbolBarCounts();
    }

    /// <summary>
    /// Gets current backfill progress. Returns null if no active backfill.
    /// </summary>
    public BackfillRunProgressResponse? GetProgress()
    {
        var snapshot = _progressTracker.GetSnapshot();
        if (_lastRun is null && !IsActive && snapshot.TotalSymbols == 0)
            return null;

        return new BackfillRunProgressResponse(
            _lastRun,
            IsActive,
            ToContract(snapshot),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Get health status of all providers.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ProviderHealthStatus>> CheckProviderHealthAsync(CancellationToken ct = default)
    {
        var providers = await CreateProvidersAsync(ct).ConfigureAwait(false);
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

    public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default) =>
        RunCoreAsync(request, null, null, null, null, ct);

    internal Task<BackfillResult> RunQueuedAsync(
        BackfillRequest request, string jobId, BackfillQuotaEstimate estimate, string dataRoot,
        Action onStarted, CancellationToken ct) =>
        RunCoreAsync(request, jobId, estimate, dataRoot, onStarted, ct);

    internal bool IsJobThrottled(string jobId) =>
        _activeQuotaRun is { } active && active.JobId == jobId && active.Session.IsThrottled;

    private async Task<BackfillResult> RunCoreAsync(
        BackfillRequest request,
        string? jobId,
        BackfillQuotaEstimate? retainedEstimate,
        string? queuedDataRoot,
        Action? onStarted,
        CancellationToken ct)
    {
        if (jobId is not null)
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        else if (!await _gate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A backfill is already running. Please try again after it completes.");

        try
        {
            onStarted?.Invoke();
            var normalizedSymbols = BackfillSymbolNormalizer.Normalize(request.Symbols);
            if (jobId is null)
            {
                _progressTracker.Clear();
                foreach (var symbol in normalizedSymbols)
                    _progressTracker.RegisterSymbol(symbol, request.From, request.To);
            }

            var cfg = _store.Load();
            var compressionEnabled = cfg.Compress ?? false;
            var dataRoot = queuedDataRoot ?? _store.GetDataRoot(cfg);
            var baseStorageOptions = cfg.Storage?.ToStorageOptions(dataRoot, compressionEnabled)
                ?? StorageProfilePresets.CreateFromProfile(null, dataRoot, compressionEnabled);
            var storageOpt = CreateStorageOptionsForRequest(baseStorageOptions, request);

            var policy = new JsonlStoragePolicy(storageOpt);
            await using var sink = new JsonlStorageSink(storageOpt, policy);
            // Backfill is a persistence run, not a live feed: when the provider outpaces the sink the
            // producer must wait rather than evict queued bars. DropOldest here silently left
            // permanent gaps on disk while bar counts (and the run's Success flag) still included them.
            await using var pipeline = new EventPipeline(
                sink,
                new EventPipelinePolicy(Capacity: 20_000, FullMode: BoundedChannelFullMode.Wait),
                enablePeriodicFlush: false,
                metrics: _metrics);

            // Keep pipeline counters scoped per run
            _metrics.Reset();

            var statusStore = new BackfillStatusStore(dataRoot);
            var providers = await CreateProvidersAsync(ct).ConfigureAwait(false);
            using var composite = CreateComposite(request, providers, cfg.Backfill, jobId is null ? _progressTracker : null);
            using var quotaSession = new HistoricalBackfillQuotaSession(cfg.Backfill?.QuotaPolicies, retryRateLimits: jobId is not null);
            var estimate = retainedEstimate ?? EstimateQuota(request, providers, cfg.Backfill);
            _activeQuotaRun = new ActiveQuotaRun(jobId, quotaSession);
            var service = CreateService(providers, composite, cfg.Backfill, jobId is null ? statusStore : null);
            var result = await service.RunAsync(request, pipeline, quotaSession, estimate, ct).ConfigureAwait(false);

            // Queued jobs have their own tenant-scoped history and status.
            if (jobId is not null)
                return result;

            await statusStore.WriteAsync(result).ConfigureAwait(false);
            _lastRun = result;

            var skipped = result.SkippedSymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in normalizedSymbols)
            {
                if (skipped.Contains(symbol))
                    _progressTracker.MarkSkipped(symbol);
                else if (result.Success)
                    _progressTracker.MarkCompleted(symbol);
                else
                    _progressTracker.MarkFailed(symbol, result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            if (jobId is null)
            {
                foreach (var symbol in BackfillSymbolNormalizer.Normalize(request.Symbols))
                    _progressTracker.MarkFailed(symbol, ex.Message);
            }
            _log.Error(ex, "Backfill failed");
            throw;
        }
        finally
        {
            _activeQuotaRun = null;
            _gate.Release();
        }
    }

    /// <summary>
    /// Previews a backfill operation without fetching data: which provider would serve it,
    /// estimated bar counts and duration, and existing on-disk coverage per symbol.
    /// </summary>
    public async Task<BackfillPreviewResult> PreviewAsync(BackfillRequest request, CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var providers = await CreateProvidersAsync(ct).ConfigureAwait(false);
        using var composite = CreateComposite(request, providers, cfg.Backfill);
        CreateService(providers, composite, cfg.Backfill).ValidateRequest(request);
        var provider = FindProvider(providers, request.Provider);
        var policy = BackfillQuotaPolicy.ForProvider(cfg.Backfill?.QuotaPolicies, request.Provider);
        var approved = (policy.ApprovedEquivalentProviders ?? [])
            .Where(name => !name.Equals(request.Provider, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => FindProvider(providers, name))
            .OfType<IHistoricalDataProvider>()
            .Where(p => p is not CompositeHistoricalDataProvider && p.IsSimulated == provider?.IsSimulated &&
                (!request.Granularity.IsIntraday() || p is IHistoricalAggregateBarProvider aggregate &&
                    aggregate.SupportedGranularities.Contains(request.Granularity)))
            .Select(p => new BackfillEquivalentProvider(p.Name, p.DisplayName))
            .ToArray();
        var dataRoot = _store.GetDataRoot(cfg);

        var quota = EstimateQuota(request, providers, cfg.Backfill);
        return BackfillPreviewPlanner.BuildPreview(request, provider, dataRoot, _providerCatalog, quota, approved);
    }

    internal BackfillQuotaEstimate EstimateQuota(BackfillRequest request) =>
        EstimateQuota(request, CreateProviders(), _store.Load().Backfill);

    private BackfillQuotaEstimate EstimateQuota(
        BackfillRequest request, List<IHistoricalDataProvider> providers, BackfillConfig? config)
    {
        var provider = FindProvider(providers, request.Provider);
        var hasLiveAdapter = _registry?.GetBackfillProviders().Any(p => ReferenceEquals(p, provider)) == true;
        return BackfillQuotaPlanner.Estimate(request, provider,
            BackfillQuotaPolicy.ForProvider(config?.QuotaPolicies, request.Provider), hasLiveAdapter);
    }

    private static IHistoricalDataProvider? FindProvider(List<IHistoricalDataProvider> providers, string name) =>
        providers.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Creates backfill providers using a priority-based discovery approach:
    /// 1. ProviderRegistry.GetBackfillProviders() - unified registry
    /// 2. ProviderFactory.CreateBackfillProviders() - factory with credential resolution
    /// 3. Manual instantiation - backwards compatibility fallback
    /// </summary>
    /// <summary>
    /// <see cref="CreateProviders"/> for asynchronous callers. When the factory has to construct
    /// the providers, their credentials are read first so the construction does not wait on
    /// credential storage. A populated registry needs no read.
    /// </summary>
    private async ValueTask<List<IHistoricalDataProvider>> CreateProvidersAsync(CancellationToken ct)
    {
        if (_factory is null || _registry?.GetBackfillProviders().Count > 0)
            return CreateProviders();

        var prepared = await _factory.PrepareCredentialsAsync(ct).ConfigureAwait(false);
        using (prepared.Activate())
        {
            return CreateProviders();
        }
    }

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
            providers.Add(new StooqHistoricalDataProvider(log: _log, runtimeOptions: BackfillProviderRuntimeOptions.FromConfig(providersCfg, "stooq")));
        }

        // Yahoo Finance
        var yahooCfg = providersCfg?.Yahoo;
        if (yahooCfg?.Enabled ?? true)
        {
            providers.Add(new YahooFinanceHistoricalDataProvider(log: _log, runtimeOptions: BackfillProviderRuntimeOptions.FromConfig(providersCfg, "yahoo")));
        }

        // Nasdaq Data Link (Quandl)
        var nasdaqCfg = providersCfg?.Nasdaq;
        if (nasdaqCfg?.Enabled ?? true)
        {
            providers.Add(new NasdaqDataLinkHistoricalDataProvider(
                apiKey: nasdaqCfg?.ApiKey,
                database: nasdaqCfg?.Database ?? "WIKI",
                runtimeOptions: BackfillProviderRuntimeOptions.FromConfig(providersCfg, "nasdaqdatalink"),
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

    private CompositeHistoricalDataProvider? CreateComposite(
        BackfillRequest request, List<IHistoricalDataProvider> providers, BackfillConfig? config,
        BackfillProgressTracker? progressTracker = null)
    {
        if (request.Provider.Equals("composite", StringComparison.OrdinalIgnoreCase) &&
            FindProvider(providers, request.Provider) is null && providers.Count > 0 &&
            (string.Equals(config?.Provider, "composite", StringComparison.OrdinalIgnoreCase) || (config?.EnableFallback ?? true)))
        {
            return new CompositeHistoricalDataProvider(
                providers,
                config?.EnableSymbolResolution ?? true ? _symbolResolver : null,
                enableCrossValidation: false,
                log: _log,
                progressTracker: progressTracker,
                validationProfiles: config?.ValidationProfiles,
                crossValidationPolicy: () => CrossProviderTolerancePolicy.FromConfig(_store.Load())
            );

        }
        return null;
    }

    private HistoricalBackfillService CreateService(
        List<IHistoricalDataProvider> providers,
        CompositeHistoricalDataProvider? composite,
        BackfillConfig? config,
        BackfillStatusStore? checkpointStore = null) =>
        new(
            composite is null ? providers : [composite, .. providers],
            _log,
            jobsConfig: config?.Jobs,
            checkpointStore: checkpointStore,
            symbolTimelineResolver: _symbolTimelineResolver,
            validationProfiles: config?.ValidationProfiles);

    private sealed record ActiveQuotaRun(string? JobId, HistoricalBackfillQuotaSession Session);

    private static string[] GetSupportedGranularityValues(IHistoricalDataProvider provider)
    {
        var values = new List<string> { DataGranularity.Daily.ToUiValue() };
        if (provider is IHistoricalAggregateBarProvider aggregateProvider)
        {
            values.AddRange(aggregateProvider.SupportedGranularities
                .Where(g => g != DataGranularity.Daily)
                .Select(g => g.ToUiValue()));
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static StorageOptions CreateStorageOptionsForRequest(StorageOptions baseOptions, BackfillRequest request)
    {
        var planner = new AdaptivePartitionPlacementPlanner();
        var placementRequest = CreateAdaptivePartitionPlacementRequest(request);
        var recommendation = planner.Recommend(placementRequest);
        var filePrefix = request.Granularity.IsIntraday()
            ? AppendFilePrefix(baseOptions.FilePrefix, request.Granularity.ToStorageFilePrefix())
            : baseOptions.FilePrefix;

        return planner.ApplyRecommendation(baseOptions, recommendation, filePrefix);
    }

    private static AdaptivePartitionPlacementRequest CreateAdaptivePartitionPlacementRequest(BackfillRequest request)
    {
        var symbols = BackfillSymbolNormalizer.Normalize(request.Symbols);
        var coverage = EstimateCoverage(request.From, request.To);
        var estimatedEvents = EstimateBackfillEventCount(symbols.Length, coverage, request.Granularity);
        var sourceCount = string.Equals(request.Provider, "composite", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

        return new AdaptivePartitionPlacementRequest(
            TotalEvents: estimatedEvents,
            Coverage: coverage,
            SymbolCount: symbols.Length,
            SourceCount: sourceCount,
            EventTypeCount: 1,
            LatencySensitive: request.Granularity.IsIntraday(),
            ArchivalPromotion: request.Granularity is DataGranularity.Weekly or DataGranularity.Monthly ||
                coverage.TotalDays >= 90);
    }

    private static TimeSpan EstimateCoverage(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && to.Value >= from.Value)
            return TimeSpan.FromDays(to.Value.DayNumber - from.Value.DayNumber + 1);

        return TimeSpan.FromDays(1);
    }

    private static long EstimateBackfillEventCount(int symbolCount, TimeSpan coverage, DataGranularity granularity)
    {
        var effectiveSymbolCount = Math.Max(1, symbolCount);
        var intervalSeconds = Math.Max(1d, granularity.ToTimeSpan().TotalSeconds);
        var intervals = Math.Max(1d, Math.Ceiling(coverage.TotalSeconds / intervalSeconds));
        var total = intervals * effectiveSymbolCount;

        return total >= long.MaxValue ? long.MaxValue : (long)total;
    }

    private static string AppendFilePrefix(string? existingPrefix, string granularityPrefix)
        => string.IsNullOrWhiteSpace(existingPrefix)
            ? granularityPrefix
            : $"{existingPrefix}_{granularityPrefix}";

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _ownedSymbolResolver?.Dispose();
        _progressTracker.Dispose();
        _gate.Dispose();
    }

    private static BackfillProviderProgressSnapshotDto ToContract(BackfillProgressSnapshot snapshot)
    {
        var symbols = snapshot.Symbols.ToDictionary(
            static item => item.Key,
            static item =>
            {
                var value = item.Value;
                return new BackfillProviderSymbolProgressDto(
                    value.Symbol,
                    value.FromDate == DateOnly.MinValue ? null : value.FromDate,
                    value.ToDate == DateOnly.MinValue ? null : value.ToDate,
                    value.TotalDays,
                    value.CompletedDays,
                    value.PercentComplete,
                    value.IsCompleted,
                    value.IsFailed,
                    value.IsSkipped,
                    value.CurrentProvider,
                    value.CurrentStatus,
                    value.ProviderAttempt,
                    value.RetryRound,
                    value.Operation,
                    value.AttemptStartedAt,
                    value.LastUpdatedAt,
                    value.Error);
            },
            StringComparer.OrdinalIgnoreCase);

        var attempts = (snapshot.RecentProviderAttempts ?? Array.Empty<ProviderBackfillProgress>())
            .Select(static progress => new BackfillProviderAttemptProgressDto(
                progress.Symbol,
                progress.Provider,
                progress.RangeStart,
                progress.RangeEnd,
                progress.ProviderAttempt,
                progress.RetryRound,
                progress.Operation,
                progress.CurrentStatus,
                progress.BarsDownloaded,
                progress.StartedAt,
                progress.EffectiveObservedAt,
                progress.Error))
            .ToArray();

        return new BackfillProviderProgressSnapshotDto(
            symbols,
            attempts,
            snapshot.OverallPercentComplete,
            snapshot.TotalSymbols,
            snapshot.CompletedSymbols,
            snapshot.FailedSymbols,
            snapshot.DroppedProviderNotifications,
            snapshot.Timestamp);
    }
}
