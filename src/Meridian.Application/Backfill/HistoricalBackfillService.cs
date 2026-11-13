using System.Collections.Concurrent;
using System.Globalization;
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
    private static readonly TimeSpan AdaptiveConcurrencyRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly BackfillStatusStore? _checkpointStore;
    private readonly Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? _symbolTimelineResolver;
    private readonly TimeZoneInfo _intradaySessionTimeZone;

    public HistoricalBackfillService(
        IEnumerable<IHistoricalDataProvider> providers,
        ILogger? logger = null,
        IEventMetrics? metrics = null,
        BackfillJobsConfig? jobsConfig = null,
        BackfillStatusStore? checkpointStore = null,
        Meridian.Contracts.SecurityMaster.IHistoricalSymbolTimelineResolver? symbolTimelineResolver = null,
        TimeZoneInfo? intradaySessionTimeZone = null)
    {
        _providers = providers.ToDictionary(p => p.Name.ToLowerInvariant());
        _log = logger ?? LoggingSetup.ForContext<HistoricalBackfillService>();
        _metrics = metrics ?? new DefaultEventMetrics();
        _jobsConfig = jobsConfig ?? new BackfillJobsConfig();
        _checkpointStore = checkpointStore;
        _symbolTimelineResolver = symbolTimelineResolver;
        // The current intraday backfill providers serve US equity sessions. Keep the
        // session zone explicit so other markets can supply their own date convention.
        _intradaySessionTimeZone = intradaySessionTimeZone ?? TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    }

    public IReadOnlyCollection<IHistoricalDataProvider> Providers => _providers.Values.ToList();

    private static string SanitizeLogText(string? value) =>
        (value ?? string.Empty).Replace('\r', '_').Replace('\n', '_');

    private static string? FormatLogDate(DateOnly? value) =>
        value is { } date ? SanitizeLogText(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null;

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

        // Resume skips only dates recorded as durably written by this provider and feed. A
        // date-only checkpoint (symbol + granularity) cannot say which provider wrote it or where
        // its range started, so it is never used to skip or advance.
        var coverageFeed = provider.CoverageFeed;
        IReadOnlyDictionary<string, IReadOnlyList<BackfillCoverageRange>>? symbolCoverage = null;
        if (request.ResumeFromCheckpoint && _checkpointStore is not null)
        {
            symbolCoverage = _checkpointStore.TryReadSymbolCoverage(request.Granularity, provider.Name, coverageFeed);
            if (symbolCoverage is { Count: > 0 })
            {
                _log.Information(
                    "Resume mode: verified coverage loaded for {Count} symbols ({Granularity}, {Provider}, feed {Feed})",
                    symbolCoverage.Count,
                    request.Granularity.ToDisplayName(),
                    provider.Name,
                    coverageFeed);
            }
            else if (_checkpointStore.TryReadSymbolCheckpoints(request.Granularity) is { Count: > 0 } legacyCheckpoints)
            {
                _log.Information(
                    "Resume mode: {Count} date-only checkpoints exist for {Granularity} but no verified coverage for {Provider} (feed {Feed}); the requested range will be fetched",
                    legacyCheckpoints.Count,
                    request.Granularity.ToDisplayName(),
                    provider.Name,
                    coverageFeed);
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
        // Resume checkpoints are only durable evidence once the bars behind them are on disk, so
        // they are collected here and written after the final pipeline flush succeeds.
        var pendingCheckpoints = new ConcurrentBag<(string Symbol, DateOnly LastBarDate, long Bars)>();
        var pendingCoverage = new ConcurrentBag<BackfillCoverageUpdate>();

        // Adaptive concurrency gate: starts at maxConcurrent, decrements by 1 on RateLimitException
        int currentConcurrency = maxConcurrent;
        var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        // Workers currently past the admission gate. The semaphore caps them at maxConcurrent; the
        // gate below also holds them at currentConcurrency, which shrinks after a rate limit. The
        // reduced value used to be logged but never enforced, so a throttled provider kept getting
        // the full parallel load.
        int activeWorkers = 0;

        async Task ProcessSymbolAsync(string symbol, CancellationToken ct = default)
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            while (Interlocked.Increment(ref activeWorkers) > Volatile.Read(ref currentConcurrency))
            {
                // Over the adaptive limit: hand the slot back and retry shortly.
                Interlocked.Decrement(ref activeWorkers);
                semaphore.Release();
                await Task.Delay(AdaptiveConcurrencyRetryDelay, ct).ConfigureAwait(false);
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                // Fetch only the parts of the requested range that verified coverage does not
                // contain: dates before it, gaps inside it, and dates after it.
                IReadOnlyList<(DateOnly? From, DateOnly? To)> fetchRanges = [(request.From, request.To)];
                IReadOnlyList<BackfillCoverageRange> knownCoverage = [];
                DateOnly? newestCoveredDate = null;
                if (symbolCoverage is not null &&
                    symbolCoverage.TryGetValue(symbol, out var coveredRanges) &&
                    coveredRanges.Count > 0)
                {
                    knownCoverage = coveredRanges;
                    fetchRanges = BackfillCoverageMath.Missing(request.From, request.To, coveredRanges);
                    var coveredInRequest = coveredRanges
                        .Where(range => (!request.To.HasValue || range.From <= request.To.Value) &&
                                        (!request.From.HasValue || range.To >= request.From.Value))
                        .ToArray();
                    if (coveredInRequest.Length > 0)
                        newestCoveredDate = coveredInRequest.Max(static range => range.To);

                    if (fetchRanges.Count == 0)
                    {
                        var coveredBars = coveredInRequest.Sum(static range => range.Bars);
                        _log.Debug("Skipping {Symbol}: requested range is fully covered through {CoveredThrough}", symbol, newestCoveredDate);
                        skippedSymbols.Add(symbol);
                        if (coveredBars > 0)
                        {
                            validationSignals.Add(SymbolValidationSignal.PassSkipped(symbol, coveredBars, newestCoveredDate));
                        }
                        else
                        {
                            validationSignals.Add(SymbolValidationSignal.Warn(
                                symbol,
                                null,
                                newestCoveredDate,
                                "Checkpoint coverage exists but bar-count evidence is missing; cannot assert completeness."));
                        }
                        return;
                    }

                    _log.Information(
                        "Resuming {Symbol} via {Provider}: {RangeCount} uncovered range(s), first from {ResumeFrom} (covered through {CoveredThrough})",
                        SanitizeLogText(symbol), SanitizeLogText(provider.DisplayName), fetchRanges.Count,
                        FormatLogDate(fetchRanges[0].From), FormatLogDate(newestCoveredDate));
                }
                else
                {
                    _log.Information("Starting backfill for {Symbol} via {Provider}", symbol, provider.DisplayName);
                }

                // Outer bounds of what this run fetches, for the validation signal.
                var effectiveFrom = fetchRanges[0].From;
                var effectiveTo = fetchRanges[^1].To;

                DateOnly? firstBarDate = null;
                DateOnly? lastBarDate = null;
                long symbolBars = 0;
                // Per-range trackers: each fetched range records its own written session range.
                DateOnly? rangeFirstBarDate = null;
                DateOnly? rangeLastBarDate = null;
                long rangeBars = 0;
                var writtenRanges = new List<BackfillCoverageRange>();
                var servedBy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (rangeFrom, rangeTo) in fetchRanges)
                {
                    rangeFirstBarDate = null;
                    rangeLastBarDate = null;
                    rangeBars = 0;
                    var executionPartitions = BuildExecutionPartitions(provider, request.Granularity, rangeFrom, rangeTo);
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
                            var bars = await aggregateProvider.GetAggregateBarsAsync(symbol, request.Granularity, rangeFrom, rangeTo, ct).ConfigureAwait(false);
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
                            var eraSymbol = rangeFrom.HasValue
                                ? await ResolveEraSymbolAsync(symbol, rangeFrom.Value, ct).ConfigureAwait(false)
                                : symbol;
                            var bars = await provider.GetDailyBarsAsync(eraSymbol, rangeFrom, rangeTo, ct).ConfigureAwait(false);
                            await PublishHistoricalBarsAsync(RetagBars(bars, symbol, eraSymbol)).ConfigureAwait(false);
                        }
                    }

                    // Record what this range proved; see BackfillCoverageMath.WrittenRange.
                    var followedByCoverage = rangeTo is { } fetchedTo && fetchedTo < DateOnly.MaxValue &&
                        BackfillCoverageMath.Covers(knownCoverage, fetchedTo.AddDays(1));
                    if (BackfillCoverageMath.WrittenRange(rangeFrom, rangeTo, rangeFirstBarDate, rangeLastBarDate, rangeBars, followedByCoverage) is { } written)
                        writtenRanges.Add(written);
                }

                perSymbolBars[symbol] = symbolBars;

                // Queue the per-symbol coverage and checkpoint; they are persisted only after the
                // pipeline flush confirms these bars reached storage (evidence written while bars
                // are still queued lets a crash or flush failure skip dates that were never written).
                if (_checkpointStore is not null && lastBarDate.HasValue)
                {
                    pendingCheckpoints.Add((symbol, lastBarDate.Value, symbolBars));
                }

                if (_checkpointStore is not null && writtenRanges.Count > 0)
                    pendingCoverage.Add(new BackfillCoverageUpdate(symbol, writtenRanges, servedBy.ToArray()));

                // Emit validation signal. Recency comes first: a provider whose dataset is
                // frozen (e.g. Nasdaq WIKI ended March 2018) can return plenty of bars that
                // still end years before the requested range end, and an open-ended request
                // (To == null) implicitly expects data through today.
                var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
                var expectedThrough = request.To is { } requestedTo && requestedTo < utcToday ? requestedTo : utcToday;
                // A run that only filled an earlier gap is not stale when later dates are already covered.
                var newestKnownDate = lastBarDate.HasValue && newestCoveredDate.HasValue && newestCoveredDate.Value > lastBarDate.Value
                    ? newestCoveredDate
                    : lastBarDate;
                var staleDays = newestKnownDate.HasValue ? expectedThrough.DayNumber - newestKnownDate.Value.DayNumber : 0;
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
                    (!effectiveTo.HasValue || (lastBarDate.HasValue && lastBarDate.Value >= effectiveTo.Value));

                if (isStale)
                {
                    var staleReason =
                        $"Backfilled data is stale: newest bar {lastBarDate:yyyy-MM-dd} is {staleDays} calendar days " +
                        $"short of the expected range end {expectedThrough:yyyy-MM-dd}. The provider's dataset may be frozen or paywalled.";
                    _log.Warning(
                        "Stale backfill for {Symbol} via {Provider}: newest bar {LastBarDate} vs expected {ExpectedThrough} ({StaleDays} days)",
                        SanitizeLogText(symbol), SanitizeLogText(provider.DisplayName),
                        FormatLogDate(lastBarDate), FormatLogDate(expectedThrough), staleDays);
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, effectiveTo, staleReason));
                }
                else if (coversRequestedRange)
                    validationSignals.Add(SymbolValidationSignal.Pass(symbol, symbolBars, effectiveFrom, lastBarDate));
                else if (symbolBars > 0)
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, effectiveTo, "Provider returned partial bars that do not cover the requested date range"));
                else
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, effectiveTo, "Provider returned zero bars for the requested date range"));

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

                        // Attribute the event to the inner provider that actually served the
                        // bar (each provider stamps bar.Source with its own name), not to the
                        // top-level provider — which is "composite" on the failover path and
                        // would erase the winning vendor from the envelope and disk layout.
                        var eventSource = ResolveEventSource(bar.Source, provider.Name);
                        var evt = MarketEvent.AggregateBar(bar.EndTime, bar.Symbol, bar, eventSource, bar.SequenceNumber);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        // Coverage belongs to the session containing the start of the bar.
                        // The exclusive end may already be in the following UTC day.
                        var sessionDate = DateOnly.FromDateTime(
                            TimeZoneInfo.ConvertTime(bar.StartTime, _intradaySessionTimeZone).DateTime);
                        TrackPublishedBar(sessionDate, eventSource);
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
                        // Attribute the event to the inner provider that actually served the
                        // bar; "composite" must never appear as a durable Source value.
                        var eventSource = ResolveEventSource(bar.Source, provider.Name);
                        var evt = MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, eventSource, bar.SequenceNumber);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        TrackPublishedBar(bar.SessionDate, eventSource);
                    }
                }

                void TrackPublishedBar(DateOnly barDate, string eventSource)
                {
                    symbolBars++;
                    rangeBars++;
                    servedBy.Add(eventSource);
                    if (firstBarDate is null || barDate < firstBarDate.Value)
                        firstBarDate = barDate;
                    if (lastBarDate is null || barDate > lastBarDate.Value)
                        lastBarDate = barDate;
                    if (rangeFirstBarDate is null || barDate < rangeFirstBarDate.Value)
                        rangeFirstBarDate = barDate;
                    if (rangeLastBarDate is null || barDate > rangeLastBarDate.Value)
                        rangeLastBarDate = barDate;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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
                Interlocked.Decrement(ref activeWorkers);
                semaphore.Release();
            }
        }

        var tasks = sortedSymbols.Select(s => ProcessSymbolAsync(s, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        string? flushError = null;
        try
        {
            await pipeline.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Pipeline flush failed after backfill; resume checkpoints were not advanced");
            flushError = $"Pipeline flush failed: {ex.Message}";
        }

        if (flushError is null && _checkpointStore is not null)
        {
            // Coverage first: it is what a resumed run trusts. The date-only checkpoints remain
            // for status surfaces that list the last completed date per symbol.
            await _checkpointStore.WriteSymbolCoverageAsync(
                request.Granularity,
                provider.Name,
                coverageFeed,
                pendingCoverage.ToArray(),
                ct).ConfigureAwait(false);

            foreach (var (cpSymbol, cpLastBarDate, cpBars) in pendingCheckpoints)
            {
                await _checkpointStore.WriteSymbolCheckpointAsync(
                    cpSymbol,
                    request.Granularity,
                    cpLastBarDate,
                    cpBars,
                    ct).ConfigureAwait(false);
            }
        }

        var completed = DateTimeOffset.UtcNow;
        var failedList = failedSymbols.ToArray();
        var allSucceeded = failedList.Length == 0 && flushError is null;
        var errorParts = new List<string>(2);
        if (failedList.Length > 0)
            errorParts.Add($"Failed symbols ({failedList.Length}/{symbols.Length}): {string.Join("; ", errorMessages)}");
        if (flushError is not null)
            errorParts.Add(flushError);
        var errorSummary = errorParts.Count > 0 ? string.Join(" | ", errorParts) : null;

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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
                bar.SequenceNumber,
                bar.IsAdjusted))
            .ToArray();
    }

    /// <summary>
    /// Resolves the Source stamped on a published market event: the per-bar provider stamp
    /// (the inner provider that actually served the data on a composite/failover path) wins;
    /// the top-level provider name is only a fallback for bars without one. "composite" is
    /// an aggregator label, never a vendor, and must not become a durable Source value.
    /// </summary>
    internal static string ResolveEventSource(string? barSource, string providerName)
        => string.IsNullOrWhiteSpace(barSource) || string.Equals(barSource, "composite", StringComparison.OrdinalIgnoreCase)
            ? providerName
            : barSource!;

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
