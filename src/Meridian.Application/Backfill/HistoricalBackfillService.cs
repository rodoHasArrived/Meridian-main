using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Core.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.Tracing;
using Serilog;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Backfill;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Backfill;

/// <summary>
/// Orchestrates historical backfills from free/public data providers into the storage pipeline.
/// </summary>
public sealed class HistoricalBackfillService
{
    private readonly IReadOnlyDictionary<string, IHistoricalDataProvider> _providers;
    private readonly ILogger _log;
    private readonly IEventMetrics _metrics;
    private readonly BackfillJobsConfig _jobsConfig;
    private readonly BackfillStatusStore? _checkpointStore;
    private readonly Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? _symbolTimelineResolver;

    public HistoricalBackfillService(
        IEnumerable<IHistoricalDataProvider> providers,
        ILogger? logger = null,
        IEventMetrics? metrics = null,
        BackfillJobsConfig? jobsConfig = null,
        BackfillStatusStore? checkpointStore = null,
        Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? symbolTimelineResolver = null)
    {
        _providers = providers.ToDictionary(p => p.Name.ToLowerInvariant());
        _log = logger ?? LoggingSetup.ForContext<HistoricalBackfillService>();
        _metrics = metrics ?? new DefaultEventMetrics();
        _jobsConfig = jobsConfig ?? new BackfillJobsConfig();
        _checkpointStore = checkpointStore;
        _symbolTimelineResolver = symbolTimelineResolver;
    }

    public IReadOnlyCollection<IHistoricalDataProvider> Providers => _providers.Values.ToList();

    public void ValidateRequest(BackfillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbols = BackfillSymbolNormalizer.Normalize(request.Symbols);
        if (symbols.Length == 0)
            throw new InvalidOperationException("At least one symbol is required for backfill.");

        if (!_providers.TryGetValue(request.Provider.ToLowerInvariant(), out var provider))
            throw new InvalidOperationException($"Unknown backfill provider '{request.Provider}'.");

        if (!request.Granularity.IsIntraday())
            return;

        if (provider is not IHistoricalAggregateBarProvider aggregateProvider)
        {
            throw new InvalidOperationException(
                $"Provider '{provider.DisplayName}' does not support {request.Granularity.ToDisplayName()} intraday backfill.");
        }

        if (!aggregateProvider.SupportedGranularities.Contains(request.Granularity))
        {
            var supported = string.Join(", ", aggregateProvider.SupportedGranularities.Select(g => g.ToDisplayName()));
            throw new InvalidOperationException(
                $"Provider '{provider.DisplayName}' does not support {request.Granularity.ToDisplayName()} backfill. " +
                $"Supported granularities: {supported}.");
        }
    }

    public async Task<BackfillResult> RunAsync(BackfillRequest request, EventPipeline pipeline, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pipeline);

        var started = DateTimeOffset.UtcNow;
        var symbols = BackfillSymbolNormalizer.Normalize(request.Symbols);
        ValidateRequest(request);
        var provider = _providers[request.Provider.ToLowerInvariant()];
        var aggregateProvider = provider as IHistoricalAggregateBarProvider;

        // Load per-symbol checkpoints when the caller opts into resume mode.
        IReadOnlyDictionary<string, DateOnly>? symbolCheckpoints = null;
        if (request.ResumeFromCheckpoint && _checkpointStore is not null)
        {
            symbolCheckpoints = _checkpointStore.TryReadSymbolCheckpoints(request.Granularity);
            if (symbolCheckpoints is { Count: > 0 })
            {
                _log.Information(
                    "Resume mode: {Count} symbol checkpoints loaded for {Granularity}",
                    symbolCheckpoints.Count,
                    request.Granularity.ToDisplayName());
            }
        }
        else if (!request.ResumeFromCheckpoint && _checkpointStore is not null)
        {
            // Fresh runs clear only the matching granularity lane so other resume paths survive.
            await _checkpointStore.ClearSymbolCheckpointsAsync(request.Granularity, ct).ConfigureAwait(false);
        }

        // Determine concurrency: per-request override → config default (floor: 1)
        int maxConcurrent = Math.Max(1, request.MaxConcurrentSymbols ?? _jobsConfig.MaxConcurrentRequests);

        // Normalise the priority map once (case-insensitive keys)
        Dictionary<string, int>? normalizedPriorities = null;
        if (request.SymbolPriorities is { Count: > 0 })
        {
            normalizedPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in request.SymbolPriorities)
                normalizedPriorities[k] = v;
        }

        // Sort by priority when a map is supplied; otherwise preserve input order
        IEnumerable<string> ordered = normalizedPriorities is not null
            ? symbols.OrderBy(s => normalizedPriorities.TryGetValue(s, out var p) ? p : 0)
            : symbols;
        var sortedSymbols = ordered.ToArray();

        // Thread-safe accumulators
        long barsWritten = 0;
        var failedSymbols = new ConcurrentBag<string>();
        var errorMessages = new ConcurrentBag<string>();
        var skippedSymbols = new ConcurrentBag<string>();
        var perSymbolBars = new System.Collections.Concurrent.ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var validationSignals = new ConcurrentBag<SymbolValidationSignal>();

        // Pre-load bar counts from checkpoint sidecar for skip reconciliation.
        IReadOnlyDictionary<string, long>? checkpointBarCounts = null;
        if (request.ResumeFromCheckpoint && _checkpointStore is not null)
            checkpointBarCounts = _checkpointStore.TryReadSymbolBarCounts(request.Granularity);

        // Adaptive concurrency gate: starts at maxConcurrent, decrements by 1 on RateLimitException
        int currentConcurrency = maxConcurrent;
        var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        async Task ProcessSymbolAsync(string symbol, CancellationToken ct = default)
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();

                // Determine effective date range: resume from checkpoint if available.
                var effectiveFrom = request.From;
                if (symbolCheckpoints is not null &&
                    symbolCheckpoints.TryGetValue(symbol, out var lastCompleted))
                {
                    var resumeFrom = lastCompleted.AddDays(1);
                    // If the entire requested range was already covered, skip this symbol.
                    if (request.To.HasValue && resumeFrom > request.To.Value)
                    {
                        _log.Debug("Skipping {Symbol}: fully covered by checkpoint through {LastCompleted}", symbol, lastCompleted);
                        skippedSymbols.Add(symbol);
                        if (checkpointBarCounts is not null && checkpointBarCounts.TryGetValue(symbol, out var cpCount) && cpCount > 0)
                        {
                            validationSignals.Add(SymbolValidationSignal.PassSkipped(symbol, cpCount, lastCompleted));
                        }
                        else
                        {
                            validationSignals.Add(SymbolValidationSignal.Warn(
                                symbol,
                                null,
                                lastCompleted,
                                "Checkpoint coverage exists but bar-count evidence is missing; cannot assert completeness."));
                        }
                        return;
                    }
                    // Advance the start date to the day after the last checkpoint.
                    if (effectiveFrom is null || resumeFrom > effectiveFrom.Value)
                        effectiveFrom = resumeFrom;

                    _log.Information("Resuming {Symbol} from {ResumeFrom} (checkpoint: {LastCompleted})", symbol, effectiveFrom, lastCompleted);
                }
                else
                {
                    _log.Information("Starting backfill for {Symbol} via {Provider}", symbol, provider.DisplayName);
                }

                DateOnly? firstBarDate = null;
                DateOnly? lastBarDate = null;
                long symbolBars = 0;
                var executionPartitions = BuildExecutionPartitions(provider, request.Granularity, effectiveFrom, request.To);
                if (request.Granularity.IsIntraday())
                {
                    if (aggregateProvider is null)
                    {
                        throw new InvalidOperationException(
                            $"Provider '{provider.DisplayName}' does not support {request.Granularity.ToDisplayName()} intraday backfill.");
                    }

                    if (executionPartitions is { Count: > 0 })
                    {
                        foreach (var partition in executionPartitions)
                        {
                            var bars = await aggregateProvider.GetAggregateBarsAsync(
                                symbol,
                                request.Granularity,
                                partition.From,
                                BackfillPartitionPlanner.ToInclusiveEnd(partition),
                                ct).ConfigureAwait(false);

                            await PublishAggregateBarsAsync(bars).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var bars = await aggregateProvider.GetAggregateBarsAsync(symbol, request.Granularity, effectiveFrom, request.To, ct).ConfigureAwait(false);
                        await PublishAggregateBarsAsync(bars).ConfigureAwait(false);
                    }
                }
                else
                {
                    if (executionPartitions is { Count: > 0 })
                    {
                        foreach (var partition in executionPartitions)
                        {
                            // A range spanning a ticker rename must query the provider with the
                            // era-correct symbol per chunk, then land under the requested symbol.
                            var eraSymbol = await ResolveEraSymbolAsync(symbol, partition.From, ct).ConfigureAwait(false);
                            var bars = await provider.GetDailyBarsAsync(
                                eraSymbol,
                                partition.From,
                                BackfillPartitionPlanner.ToInclusiveEnd(partition),
                                ct).ConfigureAwait(false);

                            await PublishHistoricalBarsAsync(RetagBars(bars, symbol, eraSymbol)).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        var eraSymbol = effectiveFrom.HasValue
                            ? await ResolveEraSymbolAsync(symbol, effectiveFrom.Value, ct).ConfigureAwait(false)
                            : symbol;
                        var bars = await provider.GetDailyBarsAsync(eraSymbol, effectiveFrom, request.To, ct).ConfigureAwait(false);
                        await PublishHistoricalBarsAsync(RetagBars(bars, symbol, eraSymbol)).ConfigureAwait(false);
                    }
                }

                perSymbolBars[symbol] = symbolBars;

                // Persist per-symbol checkpoint after successful completion.
                if (_checkpointStore is not null && lastBarDate.HasValue)
                {
                    await _checkpointStore.WriteSymbolCheckpointAsync(
                        symbol,
                        request.Granularity,
                        lastBarDate.Value,
                        symbolBars,
                        ct).ConfigureAwait(false);
                }

                // Emit validation signal. Recency comes first: a provider whose dataset is
                // frozen (e.g. Nasdaq WIKI ended March 2018) can return plenty of bars that
                // still end years before the requested range end, and an open-ended request
                // (To == null) implicitly expects data through today.
                var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
                var expectedThrough = request.To is { } requestedTo && requestedTo < utcToday ? requestedTo : utcToday;
                var staleDays = lastBarDate.HasValue ? expectedThrough.DayNumber - lastBarDate.Value.DayNumber : 0;
                // Only requests that expect data through (roughly) now can be "stale"; an
                // explicitly historical range that falls short is partial coverage, not staleness.
                var requestExpectsFreshData = request.To is null ||
                    request.To.Value.DayNumber >= utcToday.DayNumber - BackfillBarValidation.DefaultStaleToleranceDays;
                var isStale = requestExpectsFreshData && symbolBars > 0 && lastBarDate.HasValue &&
                    staleDays > BackfillBarValidation.DefaultStaleToleranceDays;

                var coversRequestedRange =
                    symbolBars > 0 &&
                    firstBarDate.HasValue &&
                    (effectiveFrom is null || firstBarDate.Value <= effectiveFrom.Value) &&
                    (!request.To.HasValue || (lastBarDate.HasValue && lastBarDate.Value >= request.To.Value));

                if (isStale)
                {
                    var staleReason =
                        $"Backfilled data is stale: newest bar {lastBarDate:yyyy-MM-dd} is {staleDays} calendar days " +
                        $"short of the expected range end {expectedThrough:yyyy-MM-dd}. The provider's dataset may be frozen or paywalled.";
                    _log.Warning(
                        "Stale backfill for {Symbol} via {Provider}: newest bar {LastBarDate} vs expected {ExpectedThrough} ({StaleDays} days)",
                        symbol, provider.DisplayName, lastBarDate, expectedThrough, staleDays);
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, request.To, staleReason));
                }
                else if (coversRequestedRange)
                    validationSignals.Add(SymbolValidationSignal.Pass(symbol, symbolBars, effectiveFrom, lastBarDate));
                else if (symbolBars > 0)
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, request.To, "Provider returned partial bars that do not cover the requested date range"));
                else
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, request.To, "Provider returned zero bars for the requested date range"));

                async Task PublishAggregateBarsAsync(IReadOnlyList<AggregateBar> bars)
                {
                    var futureCutoff = DateTimeOffset.UtcNow.AddDays(1);
                    var futureDropped = 0;
                    foreach (var bar in bars)
                    {
                        // A bar ending in the future is provider garbage, not history.
                        if (bar.EndTime > futureCutoff)
                        {
                            futureDropped++;
                            continue;
                        }

                        var evt = MarketEvent.AggregateBar(bar.EndTime, bar.Symbol, bar, bar.SequenceNumber, provider.Name);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        symbolBars++;

                        var barDate = DateOnly.FromDateTime(bar.EndTime.UtcDateTime);
                        if (firstBarDate is null || barDate < firstBarDate.Value)
                            firstBarDate = barDate;
                        if (lastBarDate is null || barDate > lastBarDate.Value)
                            lastBarDate = barDate;
                    }

                    if (futureDropped > 0)
                    {
                        _log.Warning(
                            "Dropped {FutureDropped} future-dated aggregate bars for {Symbol} from {Provider}",
                            futureDropped, symbol, provider.Name);
                    }
                }

                async Task PublishHistoricalBarsAsync(IReadOnlyList<HistoricalBar> bars)
                {
                    bars = BackfillBarValidation.RemoveFutureDatedBars(bars, out var futureDropped);
                    if (futureDropped > 0)
                    {
                        _log.Warning(
                            "Dropped {FutureDropped} future-dated daily bars for {Symbol} from {Provider}",
                            futureDropped, symbol, provider.Name);
                    }

                    foreach (var bar in bars)
                    {
                        var evt = MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.SequenceNumber, provider.Name);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        symbolBars++;
                        if (firstBarDate is null || bar.SessionDate < firstBarDate.Value)
                            firstBarDate = bar.SessionDate;
                        if (lastBarDate is null || bar.SessionDate > lastBarDate.Value)
                            lastBarDate = bar.SessionDate;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (RateLimitException ex)
            {
                // Adaptive throttle: reduce available concurrency by 1 (floor: 1) via lock-free CAS
                int observed;
                do
                {
                    observed = Volatile.Read(ref currentConcurrency);
                    if (observed <= 1)
                        break;
                }
                while (Interlocked.CompareExchange(ref currentConcurrency, observed - 1, observed) != observed);

                _log.Warning(ex, "Rate limit hit for {Symbol} via {Provider}; active concurrency reduced to {Concurrency}",
                    symbol, provider.Name, Volatile.Read(ref currentConcurrency));
                failedSymbols.Add(symbol);
                errorMessages.Add($"{symbol}: {ex.Message}");
                validationSignals.Add(SymbolValidationSignal.Fail(symbol, request.From, request.To, $"Rate limit exceeded: {ex.Message}"));
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Backfill failed for symbol {Symbol} via {Provider}, continuing with remaining symbols", symbol, provider.Name);
                failedSymbols.Add(symbol);
                errorMessages.Add($"{symbol}: {ex.Message}");
                validationSignals.Add(SymbolValidationSignal.Fail(symbol, request.From, request.To, ex.Message));
            }
            finally
            {
                semaphore.Release();
            }
        }

        var tasks = sortedSymbols.Select(s => ProcessSymbolAsync(s, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        try
        {
            await pipeline.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Pipeline flush failed after backfill");
        }

        var completed = DateTimeOffset.UtcNow;
        var failedList = failedSymbols.ToArray();
        var allSucceeded = failedList.Length == 0;
        var errorSummary = failedList.Length > 0
            ? $"Failed symbols ({failedList.Length}/{symbols.Length}): {string.Join("; ", errorMessages)}"
            : null;

        _log.Information("Backfill complete: {Count} bars written across {Total} symbols ({Failed} failed, {Skipped} skipped)",
            barsWritten, symbols.Length, failedList.Length, skippedSymbols.Count);

        return new BackfillResult(
            allSucceeded, provider.Name, symbols, request.From, request.To, barsWritten, started, completed,
            Error: errorSummary,
            SkippedSymbols: skippedSymbols.ToArray(),
            SymbolValidationSignals: validationSignals.ToArray());
    }

    /// <summary>
    /// Resolves the era-correct ticker for a chunk starting at <paramref name="chunkStart"/>.
    /// Intentionally fail-open: any resolution failure falls back to the requested symbol so
    /// symbology issues can never break a backfill run. Applied to the daily path only —
    /// intraday aggregate bars cannot currently be re-tagged to the canonical symbol.
    /// </summary>
    private async Task<string> ResolveEraSymbolAsync(string symbol, DateOnly chunkStart, CancellationToken ct)
    {
        if (_symbolTimelineResolver is null)
        {
            return symbol;
        }

        try
        {
            return await _symbolTimelineResolver.ResolveTickerForDateAsync(symbol, chunkStart, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "Era-symbol resolution failed for {Symbol} at {ChunkStart}; using the requested symbol",
                symbol, chunkStart);
            return symbol;
        }
    }

    /// <summary>
    /// Re-tags bars fetched under an era ticker back to the canonical requested symbol so a
    /// rename-spanning backfill lands as one continuous series.
    /// </summary>
    private static IReadOnlyList<HistoricalBar> RetagBars(
        IReadOnlyList<HistoricalBar> bars,
        string canonicalSymbol,
        string fetchedSymbol)
    {
        if (bars.Count == 0 || string.Equals(canonicalSymbol, fetchedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return bars;
        }

        return bars
            .Select(bar => new HistoricalBar(
                canonicalSymbol,
                bar.SessionDate,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume,
                bar.Source,
                bar.SequenceNumber))
            .ToArray();
    }

    private static IReadOnlyList<BackfillPartitionEstimate>? BuildExecutionPartitions(
        IHistoricalDataProvider provider,
        DataGranularity granularity,
        DateOnly? fromInclusive,
        DateOnly? toInclusive)
    {
        if (!fromInclusive.HasValue || !toInclusive.HasValue || toInclusive.Value < fromInclusive.Value)
        {
            return null;
        }

        return BackfillPartitionPlanner.Build(
            provider,
            granularity,
            fromInclusive.Value,
            toInclusive.Value.AddDays(1),
            symbolCount: 1);
    }
}
