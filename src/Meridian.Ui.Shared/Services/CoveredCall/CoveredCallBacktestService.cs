using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Default implementation of <see cref="ICoveredCallBacktestService"/>. Runs
/// covered-call backtests in-process behind a bounded channel, persists completed
/// runs through <see cref="IStrategyRepository"/>, and caches full results for
/// quick re-hydration.
/// </summary>
public sealed class CoveredCallBacktestService : ICoveredCallBacktestService, IHostedService, IAsyncDisposable
{
    /// <summary>Strategy identifier used when persisting <see cref="StrategyRunEntry"/>.</summary>
    public const string StrategyId = "covered-call-overwrite";

    private readonly Func<BacktestRequest, BacktestEngine> _engineFactory;
    private readonly ICoveredCallChainProviderFactory _chainFactory;
    private readonly IStrategyRepository _runRepository;
    private readonly IOptionsMonitor<CoveredCallBacktestOptions> _options;
    private readonly IMemoryCache _resultCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CoveredCallBacktestService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IEvidenceArtifactStore? _evidenceArtifactStore;

    private static readonly BoundedChannelOptions RunQueueOptions = new(capacity: 512)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    };

    private readonly Channel<CoveredCallCommand> _channel =
        Channel.CreateBounded<CoveredCallCommand>(RunQueueOptions);

    private const int MaxRetainedRuns = 2_000;
    private static readonly TimeSpan TerminalRunRetention = TimeSpan.FromMinutes(30);

    internal const int MaxOperatorAcceptanceCriteriaCount = 16;
    internal const int MaxRetainedEvidenceReferenceCount = 32;
    internal const int MaxAccountingRecordReferenceCount = 32;
    internal const int MaxApprovalReferenceCount = 32;
    internal const int MaxPaperValidationReferenceCount = 32;
    internal const int MaxGovernedReportReferenceCount = 32;
    internal const int MaxOperatorAcceptanceCriterionLength = 1_024;
    internal const int MaxEvidenceReferenceLength = 2_048;
    internal const int MaxAggregateEvidenceCharacters = 32_768;

    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks the <see cref="Task"/> for each in-flight run so <see cref="StopAsync"/> can wait
    /// for them to finish (or surface their cancellation) instead of returning while runs are
    /// still mutating the repository.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task> _activeRunTasks = new(StringComparer.Ordinal);

    // Marked volatile so a ResizeConcurrency that swaps in a new SemaphoreSlim is immediately
    // visible to the drain loop (which reads _concurrency before each WaitAsync). Old instances
    // are deliberately not disposed — runs that already acquired a ticket on a previous instance
    // must be able to Release() it cleanly; the field is therefore intentionally allowed to leak
    // a small constant number of SemaphoreSlim instances across the lifetime of the host (one per
    // hot-reload of MaxConcurrentRuns).
    private volatile SemaphoreSlim _concurrency;
    private int _configuredConcurrency;
    private CancellationTokenSource _hostCts = new();
    private Task? _drainLoop;
    private IDisposable? _optionsChangeSubscription;
    private int _stopStarted;

    public CoveredCallBacktestService(
        Func<BacktestRequest, BacktestEngine> engineFactory,
        ICoveredCallChainProviderFactory chainFactory,
        IStrategyRepository runRepository,
        IOptionsMonitor<CoveredCallBacktestOptions> options,
        IMemoryCache resultCache,
        ILoggerFactory loggerFactory,
        TimeProvider? timeProvider = null,
        IEvidenceArtifactStore? evidenceArtifactStore = null)
    {
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _chainFactory = chainFactory ?? throw new ArgumentNullException(nameof(chainFactory));
        _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _resultCache = resultCache ?? throw new ArgumentNullException(nameof(resultCache));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<CoveredCallBacktestService>();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _evidenceArtifactStore = evidenceArtifactStore;

        _configuredConcurrency = Math.Max(1, options.CurrentValue.MaxConcurrentRuns);
        _concurrency = new SemaphoreSlim(_configuredConcurrency, _configuredConcurrency);
    }

    // ------------------------------------------------------------------ //
    //  IHostedService                                                     //
    // ------------------------------------------------------------------ //

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _drainLoop = Task.Run(() => DrainAsync(_hostCts.Token), CancellationToken.None);
        _optionsChangeSubscription = _options.OnChange(opts =>
        {
            var desired = Math.Max(1, opts.MaxConcurrentRuns);
            ResizeConcurrency(desired);
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _hostCts.Cancel();

        if (_drainLoop is not null)
        {
            try
            { await _drainLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        // Wait for any in-flight runs to finish observing cancellation so they don't write to
        // the repository after StopAsync returns.
        var pending = _activeRunTasks.Values.ToArray();
        if (pending.Length > 0)
        {
            try
            { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Covered-call run task failed during shutdown");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _optionsChangeSubscription?.Dispose();
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _hostCts.Dispose();
        _concurrency.Dispose();
    }

    // ------------------------------------------------------------------ //
    //  ICoveredCallBacktestService                                        //
    // ------------------------------------------------------------------ //

    public async ValueTask<CoveredCallRunHandle> StartAsync(
        CoveredCallBacktestRequest request,
        CoveredCallRunScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(scope);
        ValidateEvidenceBudget(request);
        ValidateRequest(request);
        await ValidateRetainedEvidenceAuthorityAsync(request, scope, ct).ConfigureAwait(false);
        PruneTerminalRuns();

        if (_runs.Count >= MaxRetainedRuns)
        {
            throw new InvalidOperationException("Covered-call backtest queue is at capacity. Please retry shortly.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var queuedAt = _timeProvider.GetUtcNow();
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(_hostCts.Token);

        var state = new RunState
        {
            RunId = runId,
            Request = request,
            Scope = scope,
            Cts = runCts,
            QueuedAt = queuedAt,
            Phase = RunPhase.Queued
        };
        _runs[runId] = state;

        if (!_channel.Writer.TryWrite(new CoveredCallCommand.Start(runId)))
        {
            state.Phase = RunPhase.PersistenceDegraded;
            state.Failure =
                "Backtest service is shutting down or its queue is closed; the run was not queued and no durable lifecycle entry was recorded.";
            state.EndedAt = _timeProvider.GetUtcNow();
            runCts.Dispose();
            _logger.LogWarning(
                "Covered-call run {RunId} could not be queued and has no durable lifecycle entry",
                runId);
            return new CoveredCallRunHandle(runId, queuedAt);
        }

        _logger.LogInformation(
            "Covered-call run {RunId} queued for {Symbol} {From}-{To}",
            runId, request.UnderlyingSymbol.ToUpperInvariant(), request.From, request.To);

        return new CoveredCallRunHandle(runId, queuedAt);
    }

    public ValueTask<CoveredCallRunStatusDto?> GetStatusAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ValidateScope(scope);

        if (!_runs.TryGetValue(runId, out var state) || !ScopeEquals(state.Scope, scope))
        {
            return ValueTask.FromResult<CoveredCallRunStatusDto?>(null);
        }

        return ValueTask.FromResult<CoveredCallRunStatusDto?>(new CoveredCallRunStatusDto(
            RunId: runId,
            Phase: state.Phase.ToString(),
            PercentComplete: state.Percent,
            CurrentBacktestDate: state.CurrentDate,
            FailureMessage: state.Failure));
    }

    public async ValueTask<CoveredCallRunResult?> GetResultAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ValidateScope(scope);

        var entry = await TryGetRunEntryAsync(runId, scope, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        // A result is operator-visible only after the durable terminal completion event exists.
        // This deliberately checks the repository before consulting the cache so a failed
        // completion append can never leak a successful in-memory result.
        if (entry.LastLifecycleEvent != StrategyRunLifecycleEventType.Completed)
        {
            return null;
        }

        if (TryGetCachedResult(scope, runId, out var cached) && cached is not null)
        {
            return cached;
        }

        var persistedResult = TryReadPersistedResult(entry);
        if (persistedResult is null)
        {
            return null;
        }

        TryCacheResult(scope, runId, persistedResult);
        return persistedResult;
    }

    /// <summary>Key used to retain the serialised <see cref="CoveredCallRunResult"/> as terminal output metadata.</summary>
    internal const string PersistedResultParameterKey = "coveredCallResult";
    internal const string TenantParameterKey = "workstationTenantId";
    internal const string CompanyParameterKey = "workstationCompanyId";

    private async ValueTask<StrategyRunEntry?> TryGetRunEntryAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct)
    {
        // GetRunByIdAsync is keyed on runId across all strategies; QueryRunsAsync with Limit:1
        // would return the most-recently-updated entry for the strategy and miss arbitrary runIds.
        var entry = await _runRepository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        return entry is not null && EntryBelongsToScope(entry, scope) ? entry : null;
    }

    private CoveredCallRunResult? TryReadPersistedResult(StrategyRunEntry entry)
    {
        var metadata = entry.OutputMetadata.Count > 0
            ? entry.OutputMetadata
            : entry.ParameterSet;
        if (metadata is null ||
            !metadata.TryGetValue(PersistedResultParameterKey, out var serializedResult) ||
            string.IsNullOrWhiteSpace(serializedResult))
        {
            return null;
        }

        try
        {
            // ADR-014: route deserialisation through the source-generated context, not reflection.
            return System.Text.Json.JsonSerializer.Deserialize(serializedResult, CoveredCallJsonContext.Default.CoveredCallRunResult);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Covered-call run {RunId} contains unreadable persisted result data",
                entry.RunId);
            return null;
        }
    }

    private bool TryGetCachedResult(
        CoveredCallRunScope scope,
        string runId,
        out CoveredCallRunResult? result)
    {
        try
        {
            return _resultCache.TryGetValue(CacheKey(scope, runId), out result) && result is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Covered-call result cache read failed for run {RunId}; durable result rehydration will continue",
                runId);
            result = null;
            return false;
        }
    }

    private void TryCacheResult(CoveredCallRunScope scope, string runId, CoveredCallRunResult result)
    {
        var duration = ResolveResultCacheDuration();

        try
        {
            _resultCache.Set(
                CacheKey(scope, runId),
                result,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = duration
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Covered-call result cache write failed for run {RunId}; durable completion remains authoritative",
                runId);
        }
    }

    private TimeSpan ResolveResultCacheDuration()
    {
        try
        {
            var configured = _options.CurrentValue.ResultCacheDuration;
            if (configured > TimeSpan.Zero)
            {
                return configured;
            }

            _logger.LogWarning(
                "Covered-call ResultCacheDuration {ResultCacheDuration} is not positive; using default {DefaultResultCacheDuration}",
                configured,
                CoveredCallBacktestOptions.DefaultResultCacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Covered-call cache options could not be read; using default ResultCacheDuration {DefaultResultCacheDuration}",
                CoveredCallBacktestOptions.DefaultResultCacheDuration);
        }

        return CoveredCallBacktestOptions.DefaultResultCacheDuration;
    }

    private void TryRemoveCachedResult(CoveredCallRunScope scope, string runId)
    {
        try
        {
            _resultCache.Remove(CacheKey(scope, runId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Covered-call result cache removal failed for run {RunId}; lifecycle persistence will continue",
                runId);
        }
    }

    public async ValueTask<IReadOnlyList<CoveredCallRunSummary>> ListRunsAsync(
        CoveredCallRunScope scope,
        int limit = 50,
        CancellationToken ct = default)
    {
        ValidateScope(scope);
        var query = new StrategyRunRepositoryQuery(
            StrategyId: GetScopedStrategyId(scope),
            RunTypes: null,
            Status: null,
            Limit: Math.Max(1, limit));

        var entries = await _runRepository.QueryRunsAsync(query, ct).ConfigureAwait(false);

        var result = new List<CoveredCallRunSummary>(entries.Count);
        foreach (var entry in entries)
        {
            if (!EntryBelongsToScope(entry, scope))
            {
                continue;
            }
            // Derive the run summary fields from the in-memory state when available,
            // and fall back to the persisted entry for past runs.
            _runs.TryGetValue(entry.RunId, out var liveState);
            if (liveState is not null && !ScopeEquals(liveState.Scope, scope))
            {
                liveState = null;
            }

            var symbol = liveState?.Request.UnderlyingSymbol
                ?? entry.ParameterSet?.GetValueOrDefault("underlyingSymbol")
                ?? "(unknown)";
            var label = liveState?.Request.Label ?? entry.ParameterSet?.GetValueOrDefault("label");
            var from = liveState?.Request.From
                ?? (entry.ParameterSet is not null && entry.ParameterSet.TryGetValue("from", out var fromStr) && DateOnly.TryParse(fromStr, out var f) ? f : default);
            var to = liveState?.Request.To
                ?? (entry.ParameterSet is not null && entry.ParameterSet.TryGetValue("to", out var toStr) && DateOnly.TryParse(toStr, out var t) ? t : default);

            var statusStr = (liveState?.Phase ?? (entry.TerminalStatus switch
            {
                StrategyRunStatus.Failed => RunPhase.Failed,
                StrategyRunStatus.Cancelled => RunPhase.Cancelled,
                _ => entry.EndedAt.HasValue ? RunPhase.Completed : RunPhase.Running
            })).ToString();

            double? cagr = null, sharpe = null, winRate = null;
            if (TryGetCachedResult(scope, entry.RunId, out var cachedResult) && cachedResult is not null)
            {
                cagr = cachedResult.Metrics.Cagr;
                sharpe = cachedResult.Metrics.SharpeRatio;
                winRate = cachedResult.Metrics.WinRate;
            }
            else
            {
                var outputMetadata = entry.OutputMetadata.Count > 0
                    ? entry.OutputMetadata
                    : entry.ParameterSet;
                if (outputMetadata is not null)
                {
                    cagr = TryParseInvariantDouble(outputMetadata, "cagr");
                    sharpe = TryParseInvariantDouble(outputMetadata, "sharpe");
                    winRate = TryParseInvariantDouble(outputMetadata, "winRate");
                }
            }

            result.Add(new CoveredCallRunSummary(
                RunId: entry.RunId,
                UnderlyingSymbol: symbol,
                From: from,
                To: to,
                Label: label,
                Status: statusStr,
                StartedAt: entry.StartedAt,
                EndedAt: entry.EndedAt,
                Cagr: cagr,
                SharpeRatio: sharpe,
                WinRate: winRate));
        }

        return result;
    }

    public ValueTask CancelAsync(
        string runId,
        CoveredCallRunScope scope,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ValidateScope(scope);

        if (_runs.TryGetValue(runId, out var state) && ScopeEquals(state.Scope, scope))
        {
            try
            {
                state.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed — no-op
            }
            _logger.LogInformation("Covered-call run {RunId} cancellation requested", runId);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<CoveredCallChainPreview> PreviewChainAsync(CoveredCallChainPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UnderlyingSymbol);

        var preview = await _chainFactory
            .PreviewAsync(request.UnderlyingSymbol, request.AsOf, ct)
            .ConfigureAwait(false);

        var stubParams = new OptionsOverwriteParams
        {
            MinStrike = request.MinStrike,
            MaxDelta = request.MaxDelta,
            MinDte = request.MinDte,
            MaxDte = request.MaxDte,
            MinOpenInterest = request.MinOpenInterest,
            MinVolume = request.MinVolume,
            MaxSpreadPct = request.MaxSpreadPct,
            MinIvPercentile = 0.0  // disable for preview — operator can still filter manually
        };

        var rows = new List<CoveredCallChainRow>(preview.Candidates.Count);
        var passed = 0;
        foreach (var candidate in preview.Candidates)
        {
            var (meets, reason) = ApplyFilters(candidate, stubParams);
            if (meets)
                passed++;
            rows.Add(CoveredCallRunProjection.ToChainRow(candidate, meets, reason));
        }

        return new CoveredCallChainPreview(
            UnderlyingSymbol: request.UnderlyingSymbol.Trim().ToUpperInvariant(),
            AsOf: request.AsOf,
            UnderlyingPrice: preview.UnderlyingPrice,
            Candidates: rows,
            TotalContractsScanned: preview.Candidates.Count,
            FiltersPassed: passed);
    }

    // ------------------------------------------------------------------ //
    //  Drain loop                                                         //
    // ------------------------------------------------------------------ //

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (cmd is CoveredCallCommand.Start start)
                {
                    // Track each run task so StopAsync can wait for graceful completion.
                    var runId = start.RunId;
                    var task = Task.Run(() => ExecuteRunAsync(runId, ct), CancellationToken.None);
                    _activeRunTasks[runId] = task;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Covered-call drain loop terminated unexpectedly");
        }
    }

    private async Task ExecuteRunAsync(string runId, CancellationToken hostCt)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return;
        }

        // Capture the active semaphore so a concurrent resize cannot make us Release the wrong one.
        var semaphore = _concurrency;
        var acquired = false;
        StrategyRunEntry? initialEntry = null;
        try
        {
            await semaphore.WaitAsync(state.Cts.Token).ConfigureAwait(false);
            acquired = true;

            state.StartedAt = _timeProvider.GetUtcNow();
            state.Phase = RunPhase.WarmingUp;

            var request = state.Request;
            var paramSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TenantParameterKey] = state.Scope.TenantId.Trim(),
                [CompanyParameterKey] = state.Scope.CompanyId.Trim(),
                ["underlyingSymbol"] = request.UnderlyingSymbol.ToUpperInvariant(),
                ["from"] = request.From.ToString("O"),
                ["to"] = request.To.ToString("O"),
                ["minStrike"] = request.MinStrike.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["overwriteRatio"] = request.OverwriteRatio.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["maxDelta"] = request.MaxDelta.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            if (!string.IsNullOrWhiteSpace(request.Label))
            {
                paramSet["label"] = request.Label;
            }

            initialEntry = CoveredCallRunProjection.CreateEvidenceBackedRunEntry(
                request,
                state.Scope,
                runId,
                paramSet);
            await _runRepository.RecordRunAsync(initialEntry, hostCt).ConfigureAwait(false);

            // Build chain provider (eager materialisation).
            var maxDteForWindow = request.MaxDte ?? 365;
            var chainProvider = await _chainFactory
                .CreateAsync(request.UnderlyingSymbol, request.From, request.To, maxDteForWindow, state.Cts.Token)
                .ConfigureAwait(false);

            // Build strategy. The strategy assumes the underlying is pre-held in ctx.Positions, so
            // we wrap it in a seeder that issues a market buy for InitialUnderlyingShares on the
            // first bar — without this the strategy's chain-scan branch exits early on every day.
            var strategyParams = CoveredCallRunProjection.ToParams(request);
            var strategyLogger = _loggerFactory.CreateLogger<CoveredCallOverwriteStrategy>();
            var innerStrategy = new CoveredCallOverwriteStrategy(
                underlyingSymbol: request.UnderlyingSymbol,
                parameters: strategyParams,
                chainProvider: chainProvider,
                logger: strategyLogger);
            var executionStrategy = request.InitialUnderlyingShares > 0
                ? (IBacktestStrategy)new UnderlyingSeedingStrategy(innerStrategy, request.UnderlyingSymbol, request.InitialUnderlyingShares)
                : innerStrategy;

            // Build engine.
            var dataRoot = _options.CurrentValue.DataRootOverride ?? "./data";
            var backtestRequest = new BacktestRequest(
                From: request.From,
                To: request.To,
                Symbols: new[] { request.UnderlyingSymbol.Trim().ToUpperInvariant() },
                InitialCash: request.InitialCash,
                DataRoot: dataRoot,
                RiskFreeRate: request.RiskFreeRate);

            var engine = _engineFactory(backtestRequest);

            state.Phase = RunPhase.Running;
            var progress = new Progress<BacktestProgressEvent>(evt =>
            {
                state.Percent = evt.ProgressFraction;
                state.CurrentDate = evt.CurrentDate;
            });

            var backtestResult = await engine
                .RunAsync(backtestRequest, executionStrategy, progress, state.Cts.Token)
                .ConfigureAwait(false);

            // Project results.
            var equityCurve = innerStrategy.Metrics?.EquityCurve ?? [];
            var result = CoveredCallRunProjection.ToResult(runId, request, innerStrategy, equityCurve);

            // Retain terminal outputs separately from the immutable input parameter set so the
            // repository can verify the original request hash across the whole lifecycle.
            var outputMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cagr"] = result.Metrics.Cagr.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["sharpe"] = result.Metrics.SharpeRatio.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                ["winRate"] = result.Metrics.WinRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                // ADR-014: serialise via the source-generated context.
                [PersistedResultParameterKey] = System.Text.Json.JsonSerializer.Serialize(result, CoveredCallJsonContext.Default.CoveredCallRunResult)
            };
            var completedEntry = (initialEntry with { OutputMetadata = outputMetadata }).Complete(backtestResult);
            await _runRepository.RecordRunAsync(completedEntry, hostCt).ConfigureAwait(false);

            // Publish successful state and cache only after the terminal evidence append succeeds.
            // The durable repository is the authority for whether a result may be exposed.
            TryCacheResult(state.Scope, runId, result);
            state.Phase = RunPhase.Completed;
            state.Percent = 1.0;
            state.EndedAt = _timeProvider.GetUtcNow();

            _logger.LogInformation(
                "Covered-call run {RunId} completed: {Trades} trades, sharpe={Sharpe:F2}",
                runId, innerStrategy.CompletedTrades.Count, innerStrategy.Metrics?.SharpeRatio ?? double.NaN);
        }
        catch (OperationCanceledException)
        {
            TryRemoveCachedResult(state.Scope, runId);
            Exception? terminalPersistenceFailure = null;
            try
            {
                // Mutate the originally-persisted entry so StartedAt and ParameterSet survive.
                var baseEntry = initialEntry ?? CoveredCallRunProjection.CreateEvidenceBackedRunEntry(
                    state.Request,
                    state.Scope,
                    runId,
                    CreateScopedFallbackParameterSet(state.Scope));
                await _runRepository.RecordRunAsync(baseEntry.Cancel(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception persistEx)
            {
                terminalPersistenceFailure = persistEx;
                _logger.LogWarning(persistEx, "Failed to persist cancelled run {RunId}", runId);
            }

            state.Phase = terminalPersistenceFailure is null
                ? RunPhase.Cancelled
                : RunPhase.PersistenceDegraded;
            state.Failure = terminalPersistenceFailure is null
                ? null
                : $"Cancellation was observed locally, but the durable Cancelled lifecycle append failed: {terminalPersistenceFailure.Message}";
            state.EndedAt = _timeProvider.GetUtcNow();
            if (terminalPersistenceFailure is null)
            {
                _logger.LogInformation("Covered-call run {RunId} cancelled", runId);
            }
        }
        catch (Exception ex)
        {
            TryRemoveCachedResult(state.Scope, runId);
            _logger.LogError(ex, "Covered-call run {RunId} failed", runId);
            Exception? terminalPersistenceFailure = null;
            try
            {
                var baseEntry = initialEntry ?? CoveredCallRunProjection.CreateEvidenceBackedRunEntry(
                    state.Request,
                    state.Scope,
                    runId,
                    CreateScopedFallbackParameterSet(state.Scope));
                await _runRepository.RecordRunAsync(baseEntry.Fail(ex), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception persistEx)
            {
                terminalPersistenceFailure = persistEx;
                _logger.LogWarning(persistEx, "Failed to persist failed run {RunId}", runId);
            }

            state.Phase = terminalPersistenceFailure is null
                ? RunPhase.Failed
                : RunPhase.PersistenceDegraded;
            state.Failure = terminalPersistenceFailure is null
                ? ex.Message
                : $"The run stopped after '{ex.Message}', but the durable Failed lifecycle append also failed: {terminalPersistenceFailure.Message}";
            state.EndedAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _activeRunTasks.TryRemove(runId, out _);
            if (acquired)
            {
                try
                { semaphore.Release(); }
                catch (SemaphoreFullException) { /* benign during resize */ }
                catch (ObjectDisposedException) { /* benign during resize */ }
            }

            state.Cts.Dispose();
        }
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                            //
    // ------------------------------------------------------------------ //

    internal static string GetScopedStrategyId(CoveredCallRunScope scope)
    {
        ValidateScope(scope);
        var identity = $"{scope.TenantId.Trim().Length}:{scope.TenantId.Trim()}" +
                       $"{scope.CompanyId.Trim().Length}:{scope.CompanyId.Trim()}";
        var hash = Sha256Digest.ComputeUtf8(identity);
        return $"{StrategyId}:{hash[..16]}";
    }

    private static string CacheKey(CoveredCallRunScope scope, string runId) =>
        $"covered-call-result:{GetScopedStrategyId(scope)}:{runId}";

    private static Dictionary<string, string> CreateScopedFallbackParameterSet(CoveredCallRunScope scope) =>
        new(StringComparer.Ordinal)
        {
            [TenantParameterKey] = scope.TenantId.Trim(),
            [CompanyParameterKey] = scope.CompanyId.Trim()
        };

    private static bool EntryBelongsToScope(StrategyRunEntry entry, CoveredCallRunScope scope) =>
        string.Equals(entry.StrategyId, GetScopedStrategyId(scope), StringComparison.Ordinal) &&
        entry.ParameterSet is not null &&
        entry.ParameterSet.TryGetValue(TenantParameterKey, out var tenantId) &&
        entry.ParameterSet.TryGetValue(CompanyParameterKey, out var companyId) &&
        string.Equals(tenantId, scope.TenantId.Trim(), StringComparison.Ordinal) &&
        string.Equals(companyId, scope.CompanyId.Trim(), StringComparison.Ordinal);

    private static bool ScopeEquals(CoveredCallRunScope left, CoveredCallRunScope right) =>
        string.Equals(left.TenantId.Trim(), right.TenantId.Trim(), StringComparison.Ordinal) &&
        string.Equals(left.CompanyId.Trim(), right.CompanyId.Trim(), StringComparison.Ordinal);

    private static void ValidateScope(CoveredCallRunScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Actor);
    }

    private static double? TryParseInvariantDouble(IReadOnlyDictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var raw)
        && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static void ValidateEvidenceBudget(CoveredCallBacktestRequest request)
    {
        long aggregateCharacters = 0;
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.OperatorAcceptanceCriteria,
            nameof(request.OperatorAcceptanceCriteria),
            MaxOperatorAcceptanceCriteriaCount,
            MaxOperatorAcceptanceCriterionLength);
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.RetainedEvidenceReferences,
            nameof(request.RetainedEvidenceReferences),
            MaxRetainedEvidenceReferenceCount,
            MaxEvidenceReferenceLength);
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.AccountingRecordReferences,
            nameof(request.AccountingRecordReferences),
            MaxAccountingRecordReferenceCount,
            MaxEvidenceReferenceLength);
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.ApprovalReferences,
            nameof(request.ApprovalReferences),
            MaxApprovalReferenceCount,
            MaxEvidenceReferenceLength);
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.PaperValidationReferences,
            nameof(request.PaperValidationReferences),
            MaxPaperValidationReferenceCount,
            MaxEvidenceReferenceLength);
        aggregateCharacters += ValidateEvidenceCollectionBudget(
            request.GovernedReportReferences,
            nameof(request.GovernedReportReferences),
            MaxGovernedReportReferenceCount,
            MaxEvidenceReferenceLength);

        if (aggregateCharacters > MaxAggregateEvidenceCharacters)
        {
            throw new ArgumentException(
                $"Covered-call evidence declarations may contain at most " +
                $"{MaxAggregateEvidenceCharacters} aggregate characters across all six collections.",
                nameof(request));
        }
    }

    private static long ValidateEvidenceCollectionBudget(
        IReadOnlyList<string> values,
        string category,
        int maxCount,
        int maxValueLength)
    {
        if (values.Count > maxCount)
        {
            throw new ArgumentException(
                $"Covered-call {category} may contain at most {maxCount} values.",
                "request");
        }

        long totalLength = 0;
        foreach (var value in values)
        {
            var valueLength = value?.Length ?? 0;
            if (valueLength > maxValueLength)
            {
                throw new ArgumentException(
                    $"Each covered-call {category} value may contain at most {maxValueLength} characters.",
                    "request");
            }

            totalLength += valueLength;
        }

        return totalLength;
    }

    private static void ValidateRequest(CoveredCallBacktestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UnderlyingSymbol))
            throw new ArgumentException("UnderlyingSymbol is required.", nameof(request));
        if (request.To < request.From)
            throw new ArgumentException("'To' must be on or after 'From'.", nameof(request));
        if (request.MinStrike <= 0m)
            throw new ArgumentException("MinStrike must be greater than zero.", nameof(request));
        if (request.InitialCash <= 0m)
            throw new ArgumentException("InitialCash must be greater than zero.", nameof(request));
        if (request.InitialUnderlyingShares < 0)
            throw new ArgumentException("InitialUnderlyingShares cannot be negative.", nameof(request));

        _ = CoveredCallRunProjection.RequireEvidenceLoop(request);
    }

    private async ValueTask ValidateRetainedEvidenceAuthorityAsync(
        CoveredCallBacktestRequest request,
        CoveredCallRunScope scope,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_evidenceArtifactStore is null)
        {
            throw new ArgumentException(
                "A tenant-scoped Evidence Vault store is required before a covered-call run can be queued.",
                nameof(request));
        }

        var vaultIds = new List<string>();
        foreach (var reference in request.RetainedEvidenceReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (EvidenceVaultReference.TryParseCanonical(reference, out var vaultId, out var targetsEvidenceVault))
            {
                vaultIds.Add(vaultId);
                continue;
            }

            if (targetsEvidenceVault)
            {
                throw new ArgumentException(
                    $"Retained evidence reference '{reference}' is not a canonical " +
                    "'evidence://evidence-vault/{vaultId}' reference.",
                    nameof(request));
            }
        }

        if (vaultIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one canonical 'evidence://evidence-vault/{vaultId}' retained evidence reference is required.",
                nameof(request));
        }

        foreach (var vaultId in vaultIds.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            EvidenceManifestFile? manifest;
            try
            {
                manifest = await _evidenceArtifactStore
                    .TryOpenManifestByVaultIdAsync(
                        vaultId,
                        scope.TenantId.Trim(),
                        scope.CompanyId.Trim(),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Evidence Vault authority '{vaultId}' is unavailable for the authenticated tenant and company scope.",
                    nameof(request),
                    ex);
            }

            if (manifest is null)
            {
                throw new ArgumentException(
                    $"Evidence Vault authority '{vaultId}' was not found for the authenticated tenant and company scope.",
                    nameof(request));
            }

            try
            {
                manifest.Content.Dispose();
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Evidence Vault authority '{vaultId}' could not be released after validation.",
                    nameof(request),
                    ex);
            }
        }
    }


    private void PruneTerminalRuns()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in _runs)
        {
            var state = entry.Value;
            if (state.Phase is not (RunPhase.Completed or RunPhase.Failed or RunPhase.Cancelled or RunPhase.PersistenceDegraded))
            {
                continue;
            }

            if (!state.EndedAt.HasValue || now - state.EndedAt.Value < TerminalRunRetention)
            {
                continue;
            }

            if (_runs.TryRemove(entry.Key, out var removed))
            {
                removed.Cts.Dispose();
            }
        }
    }

    private void ResizeConcurrency(int desired)
    {
        // Replace the semaphore atomically. Existing in-flight runs hold their tickets on the
        // old semaphore; they will Release() it but it's safe to drop because we don't reuse it.
        if (desired == _configuredConcurrency)
        {
            return;
        }

        // Don't dispose the old semaphore — in-flight runs still hold tickets on it. The drain
        // loop will only Wait on the new semaphore going forward, so the old one is garbage-
        // collected once all current holders Release.
        _concurrency = new SemaphoreSlim(desired, desired);
        _configuredConcurrency = desired;
        _logger.LogInformation("Covered-call concurrency resized to {Concurrency}", desired);
    }

    private static (bool Pass, string? Reason) ApplyFilters(OptionCandidateInfo opt, OptionsOverwriteParams p)
    {
        if (!OptionsOverwriteFilters.PassesLiquidityFilter(opt, p))
        {
            if (opt.Bid <= 0m)
                return (false, "Zero bid");
            if (opt.OpenInterest < p.MinOpenInterest)
                return (false, $"OI < {p.MinOpenInterest}");
            if (opt.Volume < p.MinVolume)
                return (false, $"Volume < {p.MinVolume}");
            if (opt.SpreadPct > p.MaxSpreadPct)
                return (false, $"Spread > {p.MaxSpreadPct:P0}");
            return (false, "Liquidity filter");
        }

        if (!OptionsOverwriteFilters.PassesRiskFilter(opt, p))
        {
            if (opt.Strike < p.MinStrike)
                return (false, $"Strike < MinStrike ({p.MinStrike})");
            if (Math.Abs(opt.Delta) > p.MaxDelta)
                return (false, $"|Delta| > {p.MaxDelta:F2}");
            if (opt.DaysToExpiration < p.MinDte)
                return (false, $"DTE < {p.MinDte}");
            if (p.MaxDte.HasValue && opt.DaysToExpiration > p.MaxDte.Value)
                return (false, $"DTE > {p.MaxDte.Value}");
            return (false, "Risk filter");
        }

        return (true, null);
    }

    // ------------------------------------------------------------------ //
    //  Internal types                                                     //
    // ------------------------------------------------------------------ //

    /// <summary>Phase enumeration used in the in-memory <see cref="RunState"/>.</summary>
    internal enum RunPhase
    {
        Queued,
        WarmingUp,
        Running,
        Completed,
        Failed,
        Cancelled,
        PersistenceDegraded
    }

    /// <summary>Per-run mutable state held in-memory until eviction.</summary>
    private sealed class RunState
    {
        public required string RunId { get; init; }
        public required CoveredCallBacktestRequest Request { get; init; }
        public required CoveredCallRunScope Scope { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public DateTimeOffset QueuedAt { get; init; }
        public RunPhase Phase { get; set; } = RunPhase.Queued;
        public double Percent { get; set; }
        public DateOnly? CurrentDate { get; set; }
        public string? Failure { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
    }

    /// <summary>Command discriminator for the channel-driven drain loop.</summary>
    private abstract record CoveredCallCommand(string RunId)
    {
        public sealed record Start(string RunId) : CoveredCallCommand(RunId);
    }

    /// <summary>
    /// Decorates <see cref="CoveredCallOverwriteStrategy"/> with a single market buy of the
    /// underlying on the first bar. The underlying strategy expects a pre-held position in
    /// <see cref="IBacktestContext.Positions"/>; without seeding it never opens a short call.
    /// </summary>
    private sealed class UnderlyingSeedingStrategy : IBacktestStrategy
    {
        private readonly CoveredCallOverwriteStrategy _inner;
        private readonly string _underlyingSymbol;
        private readonly long _shares;
        private bool _seeded;

        public UnderlyingSeedingStrategy(CoveredCallOverwriteStrategy inner, string underlyingSymbol, long shares)
        {
            _inner = inner;
            _underlyingSymbol = underlyingSymbol.Trim().ToUpperInvariant();
            _shares = shares;
        }

        public string Name => _inner.Name;
        public void Initialize(IBacktestContext ctx) => _inner.Initialize(ctx);
        public void OnTrade(Trade trade, IBacktestContext ctx) => _inner.OnTrade(trade, ctx);
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) => _inner.OnQuote(quote, ctx);

        public void OnBar(HistoricalBar bar, IBacktestContext ctx)
        {
            if (!_seeded && _shares > 0 &&
                bar.Symbol.Equals(_underlyingSymbol, StringComparison.OrdinalIgnoreCase))
            {
                ctx.PlaceMarketOrder(bar.Symbol, _shares);
                _seeded = true;
            }
            _inner.OnBar(bar, ctx);
        }

        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => _inner.OnOrderBook(snapshot, ctx);
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) => _inner.OnOrderFill(fill, ctx);
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) => _inner.OnDayEnd(date, ctx);
        public void OnFinished(IBacktestContext ctx) => _inner.OnFinished(ctx);
    }
}
