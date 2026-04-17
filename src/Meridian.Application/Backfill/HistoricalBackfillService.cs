using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;

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

    public HistoricalBackfillService(
        IEnumerable<IHistoricalDataProvider> providers,
        ILogger? logger = null,
        IEventMetrics? metrics = null,
        BackfillJobsConfig? jobsConfig = null,
        BackfillStatusStore? checkpointStore = null)
    {
        _providers = providers.ToDictionary(p => p.Name.ToLowerInvariant());
        _log = logger ?? LoggingSetup.ForContext<HistoricalBackfillService>();
        _metrics = metrics ?? new DefaultEventMetrics();
        _jobsConfig = jobsConfig ?? new BackfillJobsConfig();
        _checkpointStore = checkpointStore;
    }

    public IReadOnlyCollection<IHistoricalDataProvider> Providers => _providers.Values.ToList();

    public void ValidateRequest(BackfillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbols = request.Symbols?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray() ?? Array.Empty<string>();
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
        var symbols = request.Symbols?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray() ?? Array.Empty<string>();
        ValidateRequest(request);
        var provider = _providers[request.Provider.ToLowerInvariant()];
        var aggregateProvider = provider as IHistoricalAggregateBarProvider;

        // Load per-symbol checkpoints when the caller opts into resume mode.
        IReadOnlyDictionary<string, DateOnly>? symbolCheckpoints = null;
        if (request.ResumeFromCheckpoint && _checkpointStore is not null)
        {
            symbolCheckpoints = _checkpointStore.TryReadSymbolCheckpoints();
            if (symbolCheckpoints is { Count: > 0 })
                _log.Information("Resume mode: {Count} symbol checkpoints loaded", symbolCheckpoints.Count);
        }
        else if (!request.ResumeFromCheckpoint && _checkpointStore is not null)
        {
            // Fresh run — clear any stale checkpoints so a subsequent resume starts clean.
            await _checkpointStore.ClearSymbolCheckpointsAsync(ct).ConfigureAwait(false);
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
            checkpointBarCounts = _checkpointStore.TryReadSymbolBarCounts();

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
                        var checkpointBarCount = checkpointBarCounts is not null && checkpointBarCounts.TryGetValue(symbol, out var cpCount) ? cpCount : 0L;
                        validationSignals.Add(SymbolValidationSignal.PassSkipped(symbol, checkpointBarCount, lastCompleted));
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

                DateOnly? lastBarDate = null;
                long symbolBars = 0;
                if (request.Granularity.IsIntraday())
                {
                    if (aggregateProvider is null)
                    {
                        throw new InvalidOperationException(
                            $"Provider '{provider.DisplayName}' does not support {request.Granularity.ToDisplayName()} intraday backfill.");
                    }

                    var bars = await aggregateProvider.GetAggregateBarsAsync(symbol, request.Granularity, effectiveFrom, request.To, ct).ConfigureAwait(false);
                    foreach (var bar in bars)
                    {
                        var evt = MarketEvent.AggregateBar(bar.EndTime, bar.Symbol, bar, bar.SequenceNumber, provider.Name);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        symbolBars++;

                        var barDate = DateOnly.FromDateTime(bar.EndTime.UtcDateTime);
                        if (lastBarDate is null || barDate > lastBarDate.Value)
                            lastBarDate = barDate;
                    }
                }
                else
                {
                    var bars = await provider.GetDailyBarsAsync(symbol, effectiveFrom, request.To, ct).ConfigureAwait(false);
                    foreach (var bar in bars)
                    {
                        var evt = MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.SequenceNumber, provider.Name);
                        await pipeline.PublishAsync(evt, ct).ConfigureAwait(false);
                        _metrics.IncHistoricalBars();
                        Interlocked.Increment(ref barsWritten);
                        symbolBars++;
                        if (lastBarDate is null || bar.SessionDate > lastBarDate.Value)
                            lastBarDate = bar.SessionDate;
                    }
                }

                perSymbolBars[symbol] = symbolBars;

                // Persist per-symbol checkpoint after successful completion.
                if (_checkpointStore is not null && lastBarDate.HasValue)
                {
                    await _checkpointStore.WriteSymbolCheckpointAsync(symbol, lastBarDate.Value, symbolBars, ct).ConfigureAwait(false);
                }

                // Emit validation signal.
                if (symbolBars > 0)
                    validationSignals.Add(SymbolValidationSignal.Pass(symbol, symbolBars, effectiveFrom, lastBarDate));
                else
                    validationSignals.Add(SymbolValidationSignal.Warn(symbol, effectiveFrom, request.To, "Provider returned zero bars for the requested date range"));
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
}
