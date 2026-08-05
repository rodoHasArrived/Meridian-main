using System.Net;
using System.Threading;
using Meridian.Core.Exceptions;
using Meridian.Core.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Composite provider that chains multiple data providers with automatic failover.
/// Supports symbol resolution, provider health tracking, rate-limit aware rotation,
/// and cross-provider validation.
/// </summary>
[DataSource("composite", "Multi-Source (Auto-Failover)", DataSourceType.Historical, DataSourceCategory.Aggregator,
    Priority = 0,
    EnabledByDefault = false,
    Description = "Composite provider with automatic failover across multiple historical data sources")]
[ImplementsAdr("ADR-001", "Composite historical data provider with failover")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class CompositeHistoricalDataProvider : IHistoricalDataProvider, IHistoricalAggregateBarProvider, IDisposable
{
    private readonly List<IHistoricalDataProvider> _providers;
    private readonly ISymbolResolver? _symbolResolver;
    private readonly ProviderRotationStrategy _rotation;
    private readonly ProviderHealthTracker _health;
    private readonly CrossProviderValidator _validator;
    private readonly bool _enableCrossValidation;
    private readonly TimeSpan _maxRateLimitRetryBudget;
    private readonly BackfillProgressTracker _progressTracker;
    private readonly bool _ownsProgressTracker;
    private readonly ILogger _log;
    private bool _disposed;

    // Maximum number of times the whole provider chain is retried after every candidate has
    // been rate limited. Kept small so a persistent rate limit cannot wedge a single request.
    private const int MaxRateLimitRetries = 3;

    /// <summary>
    /// Event raised as a request progresses through the provider chain: when a provider
    /// attempt starts, succeeds, fails, is rate limited, or all providers are exhausted.
    /// Notifications flow through a bounded drop-oldest dispatcher, so slow or failing
    /// subscribers never interrupt the data path.
    /// </summary>
    public event Action<ProviderBackfillProgress>? OnProgressUpdate
    {
        add => _progressTracker.ProgressPublished += value;
        remove => _progressTracker.ProgressPublished -= value;
    }

    /// <summary>Coherent provider-attempt snapshot used by workers and API projections.</summary>
    public BackfillProgressTracker ProgressTracker => _progressTracker;

    public string Name => "composite";
    public string DisplayName => "Multi-Source (Auto-Failover)";
    public string Description => $"Automatically tries multiple providers ({string.Join(", ", _providers.Select(p => p.Name))}) with failover support.";

    public int Priority => 0;
    public TimeSpan RateLimitDelay => TimeSpan.Zero;
    public int MaxRequestsPerWindow => int.MaxValue;
    public TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    /// <summary>
    /// Aggregated capabilities from all child providers.
    /// A capability is supported if ANY child provider supports it.
    /// </summary>
    public HistoricalDataCapabilities Capabilities => new()
    {
        AdjustedPrices = _providers.Any(p => p.Capabilities.AdjustedPrices),
        Intraday = _providers.Any(p => p.Capabilities.Intraday),
        Dividends = _providers.Any(p => p.Capabilities.Dividends),
        Splits = _providers.Any(p => p.Capabilities.Splits),
        Quotes = _providers.Any(p => p.Capabilities.Quotes),
        Trades = _providers.Any(p => p.Capabilities.Trades),
        Auctions = _providers.Any(p => p.Capabilities.Auctions),
        SupportedMarkets = _providers
            .SelectMany(p => p.Capabilities.SupportedMarkets)
            .Distinct()
            .ToList()
    };

    /// <summary>
    /// Get current health status of all providers.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderHealthStatus> ProviderHealth => _health.Health;

    /// <summary>
    /// Get current rate limit status for all providers.
    /// </summary>
    public IReadOnlyDictionary<string, RateLimitStatus> RateLimitStatus => _rotation.GetAllStatus();

    public IReadOnlyList<DataGranularity> SupportedGranularities =>
        _providers
            .OfType<IHistoricalAggregateBarProvider>()
            .SelectMany(p => p.SupportedGranularities)
            .Distinct()
            .OrderBy(g => g)
            .ToArray();

    public CompositeHistoricalDataProvider(
        IEnumerable<IHistoricalDataProvider> providers,
        ISymbolResolver? symbolResolver = null,
        TimeSpan? failureBackoffDuration = null,
        bool enableCrossValidation = false,
        bool enableRateLimitRotation = true,
        double rateLimitRotationThreshold = 0.8,
        TimeSpan? maxRateLimitRetryBudget = null,
        ILogger? log = null,
        BackfillProgressTracker? progressTracker = null)
    {
        _providers = providers
            .OrderBy(p => p.Priority)
            .ToList();

        if (_providers.Count == 0)
            throw new ArgumentException("At least one provider is required", nameof(providers));

        _symbolResolver = symbolResolver;
        _enableCrossValidation = enableCrossValidation;
        // Overall wall-clock budget for waiting out rate limits across all retry attempts.
        // Bounds the worst case (previously ~15 min: 3 retries x 5 min) to a single knob.
        _maxRateLimitRetryBudget = maxRateLimitRetryBudget ?? TimeSpan.FromMinutes(5);
        _log = log ?? LoggingSetup.ForContext<CompositeHistoricalDataProvider>();
        _progressTracker = progressTracker ?? new BackfillProgressTracker();
        _ownsProgressTracker = progressTracker is null;

        // Rate-limit aware rotation policy, backed by a per-provider rate-limit state tracker.
        var rateLimitTracker = new ProviderRateLimitTracker(_log);
        foreach (var provider in _providers)
        {
            rateLimitTracker.RegisterProvider(provider);
        }
        _rotation = new ProviderRotationStrategy(rateLimitTracker, enableRateLimitRotation, rateLimitRotationThreshold);

        // Per-provider health status and failure backoff bookkeeping.
        _health = new ProviderHealthTracker(
            _providers.Select(p => p.Name),
            failureBackoffDuration ?? TimeSpan.FromMinutes(5));

        // Best-effort cross-provider validation (only invoked when enabled).
        _validator = new CrossProviderValidator(_providers, ResolveSymbolForProviderAsync, _log);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Available if any provider is available
        foreach (var provider in _providers)
        {
            if (await provider.IsAvailableAsync(ct).ConfigureAwait(false))
                return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        return await ExecuteWithFailoverAsync<HistoricalBar>(
            symbol,
            operationLabel: "bars",
            candidateSelector: GetOrderedProviders,
            fetchAsync: (provider, resolved, token) => provider.GetDailyBarsAsync(resolved, from, to, token),
            onSuccessAsync: _enableCrossValidation
                ? (bars, provider, token) => _validator.ValidateAsync(bars, symbol, from, to, provider.Name, token)
                : null,
            allFailedMessageFactory: summary => $"All providers failed for {symbol}: {summary}",
            rangeStart: from,
            rangeEnd: to,
            recencyEvaluator: bars => BackfillBarValidation.EvaluateDailyRecency(bars, to),
            ct: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AggregateBar>> GetAggregateBarsAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (!SupportedGranularities.Contains(granularity))
            throw new InvalidOperationException($"Composite provider does not support {granularity.ToDisplayName()} backfill.");

        return await ExecuteWithFailoverAsync<AggregateBar>(
            symbol,
            operationLabel: $"{granularity.ToDisplayName()} aggregate bars",
            candidateSelector: () => GetOrderedProviders().Where(p =>
                p is IHistoricalAggregateBarProvider aggregateProvider &&
                aggregateProvider.SupportedGranularities.Contains(granularity)),
            fetchAsync: (provider, resolved, token) =>
                ((IHistoricalAggregateBarProvider)provider).GetAggregateBarsAsync(resolved, granularity, from, to, token),
            onSuccessAsync: null,
            allFailedMessageFactory: summary =>
                $"All aggregate-capable providers failed for {symbol} ({granularity.ToDisplayName()}): {summary}",
            rangeStart: from,
            rangeEnd: to,
            recencyEvaluator: null,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared failover skeleton for the daily-bar and aggregate-bar paths: iterate the ordered
    /// candidate providers, skipping backed-off and rate-limited ones, and return the first
    /// non-empty result that passes the optional recency evaluation. A stale result (e.g. a
    /// frozen dataset ending years before the requested range end) is held as a last-resort
    /// fallback while fresher providers are tried; if only stale data exists it is returned
    /// with an error-level log so consumers cannot mistake it for fresh coverage. If every
    /// candidate is rate limited it waits (bounded by <see cref="_maxRateLimitRetryBudget"/>,
    /// with jitter) for the earliest reset and retries. When all providers fail it raises the
    /// exhausted progress event and throws an <see cref="AggregateException"/>; when they
    /// simply return no data it returns an empty list.
    /// </summary>
    private async Task<IReadOnlyList<TResult>> ExecuteWithFailoverAsync<TResult>(
        string symbol,
        string operationLabel,
        Func<IEnumerable<IHistoricalDataProvider>> candidateSelector,
        Func<IHistoricalDataProvider, string, CancellationToken, Task<IReadOnlyList<TResult>>> fetchAsync,
        Func<IReadOnlyList<TResult>, IHistoricalDataProvider, CancellationToken, Task>? onSuccessAsync,
        Func<string, string> allFailedMessageFactory,
        DateOnly? rangeStart,
        DateOnly? rangeEnd,
        Func<IReadOnlyList<TResult>, StaleBarsVerdict?>? recencyEvaluator,
        CancellationToken ct)
    {
        var requestStartedAt = DateTimeOffset.UtcNow;
        var retryDeadline = requestStartedAt + _maxRateLimitRetryBudget;
        var providerAttempt = 0;
        IReadOnlyList<TResult>? freshestStaleResult = null;
        StaleBarsVerdict? freshestStaleVerdict = null;
        string? freshestStaleProvider = null;

        for (var attempt = 0; ; attempt++)
        {
            List<(string Provider, Exception Error)> errors = [];

            void Report(
                string providerName,
                string status,
                int barsDownloaded = 0,
                string? error = null) =>
                RaiseProgress(
                    symbol,
                    providerName,
                    requestStartedAt,
                    barsDownloaded,
                    status,
                    error,
                    rangeStart,
                    rangeEnd,
                    providerAttempt,
                    attempt,
                    operationLabel);

            foreach (var provider in candidateSelector())
            {
                // Skip providers in backoff period
                if (_health.IsInBackoffPeriod(provider.Name))
                {
                    _log.Debug("Skipping {Provider} - in backoff period", provider.Name);
                    Report(provider.Name, "skipped-backoff");
                    continue;
                }

                // Skip rate-limited providers if rotation is enabled
                if (_rotation.Enabled && _rotation.IsRateLimited(provider.Name))
                {
                    var resetTime = _rotation.GetTimeUntilReset(provider.Name);
                    _log.Debug("Skipping {Provider} - rate limited, resets in {ResetTime}", provider.Name, resetTime);
                    Report(provider.Name, "skipped-rate-limited");
                    continue;
                }

                providerAttempt++;
                try
                {
                    // Resolve symbol for this provider if resolver is available
                    var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, provider.Name, ct).ConfigureAwait(false);

                    _log.Information("Trying {Provider} for {Symbol} {Operation} (resolved: {Resolved})",
                        provider.Name, symbol, operationLabel, resolvedSymbol);
                    Report(provider.Name, "trying");

                    var startTime = DateTimeOffset.UtcNow;

                    // Record the request attempt
                    _rotation.RecordRequest(provider.Name);

                    var results = await fetchAsync(provider, resolvedSymbol, ct).ConfigureAwait(false);
                    var elapsed = DateTimeOffset.UtcNow - startTime;

                    if (results is { Count: > 0 })
                    {
                        // Update health status and clear any rate limit state
                        _health.UpdateHealth(provider.Name, true, $"Retrieved {results.Count} {operationLabel}", elapsed);
                        _health.ClearFailure(provider.Name);
                        _rotation.ClearRateLimitState(provider.Name);

                        var staleVerdict = recencyEvaluator?.Invoke(results);
                        if (staleVerdict is not null)
                        {
                            _log.Warning(
                                "Provider {Provider} returned stale {Operation} for {Symbol}: {StaleReason}. Trying fresher providers before accepting.",
                                provider.Name, operationLabel, symbol, staleVerdict.Description);
                            Report(provider.Name, "stale-data", results.Count, error: staleVerdict.Description);

                            if (freshestStaleVerdict is null ||
                                staleVerdict.LatestSessionDate > freshestStaleVerdict.LatestSessionDate)
                            {
                                freshestStaleResult = results;
                                freshestStaleVerdict = staleVerdict;
                                freshestStaleProvider = provider.Name;
                            }

                            continue;
                        }

                        _log.Information("Successfully retrieved {Count} {Operation} from {Provider} for {Symbol}",
                            results.Count, operationLabel, provider.Name, symbol);
                        Report(provider.Name, "completed", results.Count);

                        // Optionally validate against other providers
                        if (onSuccessAsync is not null)
                        {
                            await onSuccessAsync(results, provider, ct).ConfigureAwait(false);
                        }

                        return results;
                    }

                    _log.Debug("No {Operation} returned from {Provider} for {Symbol}, trying next", operationLabel, provider.Name, symbol);
                    Report(provider.Name, "no-data");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Check if this is a rate limit error
                    if (IsRateLimitException(ex))
                    {
                        var retryAfter = ExtractRetryAfter(ex);
                        _rotation.RecordRateLimitHit(provider.Name, retryAfter);
                        _log.Warning("Provider {Provider} hit rate limit for {Symbol} {Operation}, rotating to next provider",
                            provider.Name, symbol, operationLabel);
                        Report(provider.Name, "rate-limited", error: ex.Message);
                    }
                    else
                    {
                        _log.Warning(ex, "Provider {Provider} failed for {Symbol} {Operation}", provider.Name, symbol, operationLabel);
                        _health.RecordFailure(provider.Name, ex.Message);
                        Report(provider.Name, "failed", error: ex.Message);
                    }
                    errors.Add((provider.Name, ex));
                }
            }

            // All providers failed - if they were all just rate limited and we still have retry
            // attempts and time budget left, wait for the earliest reset (with jitter) and retry.
            if (_rotation.Enabled
                && errors.Count > 0
                && errors.All(e => IsRateLimitException(e.Error))
                && attempt < MaxRateLimitRetries)
            {
                var wait = _rotation.ComputeRateLimitWait(_providers, retryDeadline);
                if (wait.HasValue)
                {
                    _log.Information(
                        "All providers rate limited for {Symbol} {Operation} (attempt {Attempt}/{MaxRetries}). Waiting {WaitTime} for rate limit reset...",
                        symbol, operationLabel, attempt + 1, MaxRateLimitRetries, wait.Value);
                    Report(Name, "waiting-for-rate-limit");
                    await Task.Delay(wait.Value, ct).ConfigureAwait(false);
                    continue;
                }
            }

            // No provider produced a fresh result. If a stale result was held back, return it
            // loudly rather than failing outright — old data with an error-level signal beats
            // no data — but never let it masquerade as fresh coverage.
            if (freshestStaleResult is not null)
            {
                _log.Error(
                    "All providers returned stale {Operation} for {Symbol}; accepting freshest stale result from {Provider} ({StaleReason})",
                    operationLabel, symbol, freshestStaleProvider, freshestStaleVerdict!.Description);
                Report(freshestStaleProvider!, "stale-data-accepted", freshestStaleResult.Count, error: freshestStaleVerdict.Description);
                return freshestStaleResult;
            }

            // All providers failed
            if (errors.Count > 0)
            {
                var errorSummary = string.Join("; ", errors.Select(e => $"{e.Provider}: {e.Error.Message}"));
                Report(Name, "all-providers-failed", error: errorSummary);
                throw new AggregateException(allFailedMessageFactory(errorSummary), errors.Select(e => e.Error));
            }

            _log.Warning("No data found from any provider for {Symbol} {Operation}", symbol, operationLabel);
            Report(Name, "no-data");
            return Array.Empty<TResult>();
        }
    }

    /// <summary>
    /// Publish a provider-attempt observation without letting subscriber latency or failures
    /// interrupt the data path.
    /// </summary>
    private void RaiseProgress(
        string symbol,
        string provider,
        DateTimeOffset startedAt,
        int barsDownloaded = 0,
        string? status = null,
        string? error = null,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        int providerAttempt = 0,
        int retryRound = 0,
        string? operation = null)
    {
        var progress = new ProviderBackfillProgress(
            symbol,
            provider,
            barsDownloaded,
            TotalSymbols: 1,
            CurrentSymbolIndex: 1,
            startedAt,
            status,
            error,
            RangeStart: rangeStart,
            RangeEnd: rangeEnd,
            ProviderAttempt: providerAttempt,
            RetryRound: retryRound,
            Operation: operation,
            ObservedAt: DateTimeOffset.UtcNow);

        if (!_progressTracker.Publish(progress))
        {
            _log.Debug(
                "Provider progress tracker is closed; skipping {Status} observation for {Symbol} via {Provider}",
                status,
                symbol,
                provider);
        }
    }

    /// <summary>
    /// Get all providers ordered by rate limit capacity when rotation is enabled.
    /// </summary>
    private IEnumerable<IHistoricalDataProvider> GetOrderedProviders()
        => _rotation.OrderByAvailability(_providers);

    /// <summary>
    /// Check if an exception indicates a rate limit error from structured provider metadata.
    /// Adapters should map HTTP 429 responses to <see cref="RateLimitException"/>; raw
    /// <see cref="HttpRequestException.StatusCode"/> is accepted as a fallback for transport
    /// seams that preserve the status code without wrapping it.
    /// </summary>
    private static bool IsRateLimitException(Exception ex) =>
        FindRateLimitException(ex) is not null || HasHttpTooManyRequestsStatus(ex);

    /// <summary>
    /// Extract Retry-After duration from a structured <see cref="RateLimitException"/>, if available.
    /// </summary>
    private static TimeSpan? ExtractRetryAfter(Exception ex)
        => FindRateLimitException(ex)?.RetryAfter;

    private static RateLimitException? FindRateLimitException(Exception ex)
    {
        if (ex is RateLimitException rateLimit)
            return rateLimit;

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                var match = FindRateLimitException(inner);
                if (match is not null)
                    return match;
            }
        }

        return ex.InnerException is null ? null : FindRateLimitException(ex.InnerException);
    }

    private static bool HasHttpTooManyRequestsStatus(Exception ex)
    {
        if (ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            return true;

        if (ex is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.Any(HasHttpTooManyRequestsStatus);

        return ex.InnerException is not null && HasHttpTooManyRequestsStatus(ex.InnerException);
    }

    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requestStartedAt = DateTimeOffset.UtcNow;
        var providerAttempt = 0;

        void Report(string providerName, string status, int barsDownloaded = 0, string? error = null) =>
            RaiseProgress(
                symbol,
                providerName,
                requestStartedAt,
                barsDownloaded,
                status,
                error,
                from,
                to,
                providerAttempt,
                retryRound: 0,
                operation: "adjusted bars");

        // Get providers that support adjusted prices, ordered by rate limit availability
        var adjustedProviders = _rotation.OrderByAvailability(
            _providers.Where(p => p.Capabilities.AdjustedPrices));

        foreach (var provider in adjustedProviders)
        {
            if (_health.IsInBackoffPeriod(provider.Name))
            {
                Report(provider.Name, "skipped-backoff");
                continue;
            }

            // Skip rate-limited providers if rotation is enabled
            if (_rotation.Enabled && _rotation.IsRateLimited(provider.Name))
            {
                _log.Debug("Skipping {Provider} for adjusted bars - rate limited", provider.Name);
                Report(provider.Name, "skipped-rate-limited");
                continue;
            }

            providerAttempt++;
            try
            {
                var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, provider.Name, ct).ConfigureAwait(false);
                Report(provider.Name, "trying");

                // Record the request attempt
                _rotation.RecordRequest(provider.Name);

                var bars = await provider.GetAdjustedDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);

                if (bars is { Count: > 0 })
                {
                    _health.ClearFailure(provider.Name);
                    _rotation.ClearRateLimitState(provider.Name);
                    Report(provider.Name, "completed", bars.Count);
                    return bars;
                }

                Report(provider.Name, "no-data");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsRateLimitException(ex))
                {
                    var retryAfter = ExtractRetryAfter(ex);
                    _rotation.RecordRateLimitHit(provider.Name, retryAfter);
                    _log.Warning("Provider {Provider} hit rate limit for adjusted bars, rotating to next",
                        provider.Name);
                    Report(provider.Name, "rate-limited", error: ex.Message);
                }
                else
                {
                    _log.Warning(ex, "Provider {Provider} failed for adjusted bars", provider.Name);
                    _health.RecordFailure(provider.Name, ex.Message);
                    Report(provider.Name, "failed", error: ex.Message);
                }
            }
        }

        // Fallback to standard bars
        Report(Name, "falling-back-to-unadjusted");
        var standardBars = await GetDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return standardBars.Select(b => new AdjustedHistoricalBar(
            b.Symbol, b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume, b.Source, b.SequenceNumber
        )).ToList();
    }

    /// <summary>
    /// Check health of all providers.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ProviderHealthStatus>> CheckAllProvidersHealthAsync(CancellationToken ct = default)
    {
        var tasks = _providers.Select(async p =>
        {
            var startTime = DateTimeOffset.UtcNow;
            try
            {
                var available = await p.IsAvailableAsync(ct).ConfigureAwait(false);
                var elapsed = DateTimeOffset.UtcNow - startTime;
                _health.UpdateHealth(p.Name, available, available ? "Healthy" : "Unavailable", elapsed);
            }
            catch (Exception ex)
            {
                _health.UpdateHealth(p.Name, false, ex.Message);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return _health.Health;
    }

    private async Task<string> ResolveSymbolForProviderAsync(string symbol, string providerName, CancellationToken ct)
    {
        if (_symbolResolver is null)
            return symbol;

        try
        {
            var mapped = await _symbolResolver.MapSymbolAsync(symbol, "input", providerName, ct).ConfigureAwait(false);
            return mapped ?? symbol;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Symbol resolution failed for {Symbol} -> {Provider}", symbol, providerName);
            return symbol;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var failures = new List<Exception>();

        CaptureDisposalFailure(
            failures,
            _rotation.Dispose,
            "Failed to dispose the provider rotation strategy.");

        if (_ownsProgressTracker)
        {
            CaptureDisposalFailure(
                failures,
                _progressTracker.Dispose,
                "Failed to dispose the owned backfill progress tracker.");
        }

        foreach (var provider in _providers)
        {
            CaptureDisposalFailure(
                failures,
                provider.Dispose,
                $"Failed to dispose historical provider '{provider.Name}'.");
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "Composite historical provider disposal completed with failures.",
                failures);
        }
    }

    private static void CaptureDisposalFailure(
        ICollection<Exception> failures,
        Action dispose,
        string message)
    {
        try
        {
            dispose();
        }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException(message, ex));
        }
    }
}

/// <summary>
/// Configuration for composite provider behavior.
/// </summary>
public sealed record CompositeProviderOptions
{
    /// <summary>
    /// Duration to skip a provider after failure.
    /// </summary>
    public TimeSpan FailureBackoffDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable cross-validation of data between providers.
    /// </summary>
    public bool EnableCrossValidation { get; init; } = false;

    /// <summary>
    /// Maximum number of retries per provider.
    /// </summary>
    public int MaxRetriesPerProvider { get; init; } = 2;

    /// <summary>
    /// Prefer providers that support adjusted prices.
    /// </summary>
    public bool PreferAdjustedPrices { get; init; } = true;

    /// <summary>
    /// Enable rate-limit aware provider rotation.
    /// When enabled, providers approaching their rate limit will be deprioritized
    /// and rate-limited providers will be skipped until their limit resets.
    /// </summary>
    public bool EnableRateLimitRotation { get; init; } = true;

    /// <summary>
    /// Threshold (0.0 to 1.0) at which a provider is considered "approaching" its rate limit.
    /// Providers exceeding this threshold will be deprioritized in favor of providers with more capacity.
    /// Default: 0.8 (80% of rate limit used).
    /// </summary>
    public double RateLimitRotationThreshold { get; init; } = 0.8;

    /// <summary>
    /// Overall wall-clock budget for waiting out provider rate limits across all retry attempts
    /// for a single request. Bounds the worst-case blocking time (jitter is applied to each wait).
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxRateLimitRetryBudget { get; init; } = TimeSpan.FromMinutes(5);
}
