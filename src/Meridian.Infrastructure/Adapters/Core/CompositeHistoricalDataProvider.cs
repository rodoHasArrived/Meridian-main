using System.Collections.Concurrent;
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
    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly ConcurrentDictionary<string, ProviderHealthStatus> _healthStatus = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _providerFailures = new();
    private readonly TimeSpan _failureBackoffDuration;
    private readonly bool _enableCrossValidation;
    private readonly bool _enableRateLimitRotation;
    private readonly double _rateLimitRotationThreshold;
    private readonly TimeSpan _maxRateLimitRetryBudget;
    private readonly ILogger _log;
    private bool _disposed;

    // Maximum number of times the whole provider chain is retried after every candidate has
    // been rate limited. Kept small so a persistent rate limit cannot wedge a single request.
    private const int MaxRateLimitRetries = 3;

    // A single rate-limit reset longer than this is treated as "not worth waiting for"; the
    // request fails fast rather than blocking on it.
    private static readonly TimeSpan MaxSingleRateLimitWait = TimeSpan.FromMinutes(5);

    // Upper bound on the random jitter added to each rate-limit wait so that many concurrent
    // requests do not resume in lock-step and hammer the provider at the same instant.
    private static readonly TimeSpan RateLimitRetryJitter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Event raised as a request progresses through the provider chain: when a provider
    /// attempt starts, succeeds, fails, is rate limited, or all providers are exhausted.
    /// Subscriber exceptions are logged and never interrupt the data path.
    /// </summary>
    public event Action<ProviderBackfillProgress>? OnProgressUpdate;

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
    public IReadOnlyDictionary<string, ProviderHealthStatus> ProviderHealth => _healthStatus;

    /// <summary>
    /// Get current rate limit status for all providers.
    /// </summary>
    public IReadOnlyDictionary<string, RateLimitStatus> RateLimitStatus => _rateLimitTracker.GetAllStatus();

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
        ILogger? log = null)
    {
        _providers = providers
            .OrderBy(p => p.Priority)
            .ToList();

        if (_providers.Count == 0)
            throw new ArgumentException("At least one provider is required", nameof(providers));

        _symbolResolver = symbolResolver;
        _failureBackoffDuration = failureBackoffDuration ?? TimeSpan.FromMinutes(5);
        _enableCrossValidation = enableCrossValidation;
        _enableRateLimitRotation = enableRateLimitRotation;
        _rateLimitRotationThreshold = rateLimitRotationThreshold;
        // Overall wall-clock budget for waiting out rate limits across all retry attempts.
        // Bounds the worst case (previously ~15 min: 3 retries x 5 min) to a single knob.
        _maxRateLimitRetryBudget = maxRateLimitRetryBudget ?? TimeSpan.FromMinutes(5);
        _log = log ?? LoggingSetup.ForContext<CompositeHistoricalDataProvider>();

        // Initialize rate limit tracker
        _rateLimitTracker = new ProviderRateLimitTracker(_log);
        foreach (var provider in _providers)
        {
            _rateLimitTracker.RegisterProvider(provider);
        }

        // Initialize health status
        foreach (var provider in _providers)
        {
            _healthStatus[provider.Name] = new ProviderHealthStatus(provider.Name, true, "Not checked");
        }
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
                ? (bars, provider, token) => ValidateBarsAsync(bars, symbol, from, to, provider.Name, token)
                : null,
            allFailedMessageFactory: summary => $"All providers failed for {symbol}: {summary}",
            ct).ConfigureAwait(false);
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
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared failover skeleton for the daily-bar and aggregate-bar paths: iterate the ordered
    /// candidate providers, skipping backed-off and rate-limited ones, and return the first
    /// non-empty result. If every candidate is rate limited it waits (bounded by
    /// <see cref="_maxRateLimitRetryBudget"/>, with jitter) for the earliest reset and retries.
    /// When all providers fail it raises the exhausted progress event and throws an
    /// <see cref="AggregateException"/>; when they simply return no data it returns an empty list.
    /// </summary>
    private async Task<IReadOnlyList<TResult>> ExecuteWithFailoverAsync<TResult>(
        string symbol,
        string operationLabel,
        Func<IEnumerable<IHistoricalDataProvider>> candidateSelector,
        Func<IHistoricalDataProvider, string, CancellationToken, Task<IReadOnlyList<TResult>>> fetchAsync,
        Func<IReadOnlyList<TResult>, IHistoricalDataProvider, CancellationToken, Task>? onSuccessAsync,
        Func<string, string> allFailedMessageFactory,
        CancellationToken ct)
    {
        var requestStartedAt = DateTimeOffset.UtcNow;
        var retryDeadline = requestStartedAt + _maxRateLimitRetryBudget;

        for (var attempt = 0; ; attempt++)
        {
            List<(string Provider, Exception Error)> errors = [];

            foreach (var provider in candidateSelector())
            {
                // Skip providers in backoff period
                if (IsInBackoffPeriod(provider.Name))
                {
                    _log.Debug("Skipping {Provider} - in backoff period", provider.Name);
                    continue;
                }

                // Skip rate-limited providers if rotation is enabled
                if (_enableRateLimitRotation && _rateLimitTracker.IsRateLimited(provider.Name))
                {
                    var resetTime = _rateLimitTracker.GetTimeUntilReset(provider.Name);
                    _log.Debug("Skipping {Provider} - rate limited, resets in {ResetTime}", provider.Name, resetTime);
                    continue;
                }

                try
                {
                    // Resolve symbol for this provider if resolver is available
                    var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, provider.Name, ct).ConfigureAwait(false);

                    _log.Information("Trying {Provider} for {Symbol} {Operation} (resolved: {Resolved})",
                        provider.Name, symbol, operationLabel, resolvedSymbol);
                    RaiseProgress(symbol, provider.Name, requestStartedAt, status: "trying");

                    var startTime = DateTimeOffset.UtcNow;

                    // Record the request attempt
                    _rateLimitTracker.RecordRequest(provider.Name);

                    var results = await fetchAsync(provider, resolvedSymbol, ct).ConfigureAwait(false);
                    var elapsed = DateTimeOffset.UtcNow - startTime;

                    if (results is { Count: > 0 })
                    {
                        // Update health status and clear any rate limit state
                        UpdateHealthStatus(provider.Name, true, $"Retrieved {results.Count} {operationLabel}", elapsed);
                        ClearFailure(provider.Name);
                        _rateLimitTracker.ClearRateLimitState(provider.Name);

                        _log.Information("Successfully retrieved {Count} {Operation} from {Provider} for {Symbol}",
                            results.Count, operationLabel, provider.Name, symbol);
                        RaiseProgress(symbol, provider.Name, requestStartedAt, results.Count, status: "completed");

                        // Optionally validate against other providers
                        if (onSuccessAsync is not null)
                        {
                            await onSuccessAsync(results, provider, ct).ConfigureAwait(false);
                        }

                        return results;
                    }

                    _log.Debug("No {Operation} returned from {Provider} for {Symbol}, trying next", operationLabel, provider.Name, symbol);
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
                        _rateLimitTracker.RecordRateLimitHit(provider.Name, retryAfter);
                        _log.Warning("Provider {Provider} hit rate limit for {Symbol} {Operation}, rotating to next provider",
                            provider.Name, symbol, operationLabel);
                        RaiseProgress(symbol, provider.Name, requestStartedAt, status: "rate-limited", error: ex.Message);
                    }
                    else
                    {
                        _log.Warning(ex, "Provider {Provider} failed for {Symbol} {Operation}", provider.Name, symbol, operationLabel);
                        RecordFailure(provider.Name, ex.Message);
                        RaiseProgress(symbol, provider.Name, requestStartedAt, status: "failed", error: ex.Message);
                    }
                    errors.Add((provider.Name, ex));
                }
            }

            // All providers failed - if they were all just rate limited and we still have retry
            // attempts and time budget left, wait for the earliest reset (with jitter) and retry.
            if (_enableRateLimitRotation
                && errors.Count > 0
                && errors.All(e => IsRateLimitException(e.Error))
                && attempt < MaxRateLimitRetries)
            {
                var wait = ComputeRateLimitWait(retryDeadline);
                if (wait.HasValue)
                {
                    _log.Information(
                        "All providers rate limited for {Symbol} {Operation} (attempt {Attempt}/{MaxRetries}). Waiting {WaitTime} for rate limit reset...",
                        symbol, operationLabel, attempt + 1, MaxRateLimitRetries, wait.Value);
                    await Task.Delay(wait.Value, ct).ConfigureAwait(false);
                    continue;
                }
            }

            // All providers failed
            if (errors.Count > 0)
            {
                var errorSummary = string.Join("; ", errors.Select(e => $"{e.Provider}: {e.Error.Message}"));
                RaiseProgress(symbol, Name, requestStartedAt, status: "all-providers-failed", error: errorSummary);
                throw new AggregateException(allFailedMessageFactory(errorSummary), errors.Select(e => e.Error));
            }

            _log.Warning("No data found from any provider for {Symbol} {Operation}", symbol, operationLabel);
            RaiseProgress(symbol, Name, requestStartedAt, status: "no-data");
            return Array.Empty<TResult>();
        }
    }

    /// <summary>
    /// Determine how long to wait for the earliest provider rate-limit reset before the next
    /// retry, honoring the overall <paramref name="retryDeadline"/> budget and adding jitter.
    /// Returns <see langword="null"/> when waiting is not worthwhile (no reset known, the reset
    /// is too far out, or the remaining budget is exhausted), signalling the caller to fail fast.
    /// </summary>
    private TimeSpan? ComputeRateLimitWait(DateTimeOffset retryDeadline)
    {
        var shortestWait = GetShortestRateLimitWait();
        if (!shortestWait.HasValue || shortestWait.Value >= MaxSingleRateLimitWait)
            return null;

        var remaining = retryDeadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero || shortestWait.Value > remaining)
            return null;

        // Add bounded random jitter so concurrent requests don't resume in lock-step.
        var jitterMs = Random.Shared.Next(0, (int)RateLimitRetryJitter.TotalMilliseconds + 1);
        var wait = shortestWait.Value + TimeSpan.FromMilliseconds(jitterMs);

        // Never let jitter push the wait past the remaining budget.
        return wait > remaining ? remaining : wait;
    }

    /// <summary>
    /// Raise <see cref="OnProgressUpdate"/> without letting subscriber failures
    /// interrupt the data path.
    /// </summary>
    private void RaiseProgress(
        string symbol,
        string provider,
        DateTimeOffset startedAt,
        int barsDownloaded = 0,
        string? status = null,
        string? error = null)
    {
        var handlers = OnProgressUpdate;
        if (handlers is null)
            return;

        try
        {
            handlers(new ProviderBackfillProgress(
                symbol,
                provider,
                barsDownloaded,
                TotalSymbols: 1,
                CurrentSymbolIndex: 0,
                startedAt,
                status,
                error));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "OnProgressUpdate subscriber threw for {Symbol} via {Provider}", symbol, provider);
        }
    }

    /// <summary>
    /// Get all providers ordered by rate limit capacity when rotation is enabled.
    /// </summary>
    private IEnumerable<IHistoricalDataProvider> GetOrderedProviders()
        => OrderByRateLimitAvailability(_providers);

    /// <summary>
    /// Order an arbitrary provider set by rate-limit capacity when rotation is enabled:
    /// providers with capacity first, then those approaching their limit, then rate-limited ones
    /// last. When rotation is disabled the input order (priority order) is preserved.
    /// </summary>
    private IEnumerable<IHistoricalDataProvider> OrderByRateLimitAvailability(IEnumerable<IHistoricalDataProvider> providers)
    {
        if (!_enableRateLimitRotation)
            return providers;

        // Order by: not rate limited first, then by usage ratio (lowest first), then by priority
        return providers.OrderBy(p =>
        {
            if (_rateLimitTracker.IsRateLimited(p.Name))
                return 1000; // Put rate-limited providers last

            if (_rateLimitTracker.IsApproachingLimit(p.Name, _rateLimitRotationThreshold))
                return 100 + (int)(_rateLimitTracker.GetStatus(p.Name)?.UsagePercent ?? 0);

            return p.Priority;
        });
    }

    /// <summary>
    /// Check if an exception indicates a rate limit error (HTTP 429).
    /// Supports both the strongly-typed RateLimitException and legacy string-based detection.
    /// </summary>
    private static bool IsRateLimitException(Exception ex) =>
        ex is RateLimitException ||
        ex.InnerException is RateLimitException ||
        ex.Message.Contains("429") ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extract Retry-After duration from an exception if available.
    /// Supports both RateLimitException with structured data and legacy string parsing.
    /// </summary>
    private static TimeSpan? ExtractRetryAfter(Exception ex)
    {
        // Check for strongly-typed RateLimitException first
        if (ex is RateLimitException rle && rle.RetryAfter.HasValue)
            return rle.RetryAfter;

        // Check inner exception
        if (ex.InnerException is RateLimitException innerRle && innerRle.RetryAfter.HasValue)
            return innerRle.RetryAfter;

        // Fall back to string-based parsing for backwards compatibility
        // Try to parse Retry-After from exception message
        // Format: "Retry-After: 60" or similar
        var message = ex.Message;
        var retryAfterIdx = message.IndexOf("retry-after", StringComparison.OrdinalIgnoreCase);
        if (retryAfterIdx >= 0)
        {
            var remaining = message[(retryAfterIdx + 12)..];
            var match = System.Text.RegularExpressions.Regex.Match(remaining, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return null;
    }

    /// <summary>
    /// Get the shortest wait time until any provider's rate limit resets.
    /// </summary>
    private TimeSpan? GetShortestRateLimitWait() =>
        _providers
            .Select(p => _rateLimitTracker.GetTimeUntilReset(p.Name))
            .Where(w => w.HasValue)
            .Min();

    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Get providers that support adjusted prices, ordered by rate limit availability
        var adjustedProviders = OrderByRateLimitAvailability(
            _providers.Where(p => p.Capabilities.AdjustedPrices));

        foreach (var provider in adjustedProviders)
        {
            if (IsInBackoffPeriod(provider.Name))
                continue;

            // Skip rate-limited providers if rotation is enabled
            if (_enableRateLimitRotation && _rateLimitTracker.IsRateLimited(provider.Name))
            {
                _log.Debug("Skipping {Provider} for adjusted bars - rate limited", provider.Name);
                continue;
            }

            try
            {
                var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, provider.Name, ct).ConfigureAwait(false);

                // Record the request attempt
                _rateLimitTracker.RecordRequest(provider.Name);

                var bars = await provider.GetAdjustedDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);

                if (bars is { Count: > 0 })
                {
                    ClearFailure(provider.Name);
                    _rateLimitTracker.ClearRateLimitState(provider.Name);
                    return bars;
                }
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
                    _rateLimitTracker.RecordRateLimitHit(provider.Name, retryAfter);
                    _log.Warning("Provider {Provider} hit rate limit for adjusted bars, rotating to next",
                        provider.Name);
                }
                else
                {
                    _log.Warning(ex, "Provider {Provider} failed for adjusted bars", provider.Name);
                    RecordFailure(provider.Name, ex.Message);
                }
            }
        }

        // Fallback to standard bars
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
                UpdateHealthStatus(p.Name, available, available ? "Healthy" : "Unavailable", elapsed);
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(p.Name, false, ex.Message);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return _healthStatus;
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
        catch (Exception ex)
        {
            _log.Debug(ex, "Symbol resolution failed for {Symbol} -> {Provider}", symbol, providerName);
            return symbol;
        }
    }

    private async Task ValidateBarsAsync(IReadOnlyList<HistoricalBar> bars, string symbol, DateOnly? from, DateOnly? to, string sourceProvider, CancellationToken ct)
    {
        // Try to validate with a different provider
        var validationProvider = _providers.FirstOrDefault(p => p.Name != sourceProvider);
        if (validationProvider is null)
            return;

        try
        {
            // Resolve the symbol for the validation provider too — providers can use
            // different symbol formats (e.g. "AAPL" vs "aapl.us"), and validating with
            // the unresolved symbol silently compares against the wrong (or no) data.
            var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, validationProvider.Name, ct).ConfigureAwait(false);
            var validationBars = await validationProvider.GetDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);

            if (validationBars.Count > 0)
            {
                var discrepancies = 0;
                foreach (var bar in bars.Take(5)) // Check first 5 bars
                {
                    var matchingBar = validationBars.FirstOrDefault(b => b.SessionDate == bar.SessionDate);
                    if (matchingBar is not null)
                    {
                        var closeDiff = Math.Abs(bar.Close - matchingBar.Close) / bar.Close;
                        if (closeDiff > 0.01m) // More than 1% difference
                        {
                            discrepancies++;
                            _log.Debug("Price discrepancy on {Date}: {Provider1}={Price1}, {Provider2}={Price2}",
                                bar.SessionDate, sourceProvider, bar.Close, validationProvider.Name, matchingBar.Close);
                        }
                    }
                }

                if (discrepancies > 0)
                {
                    _log.Warning("Found {Count} price discrepancies between {Provider1} and {Provider2} for {Symbol}",
                        discrepancies, sourceProvider, validationProvider.Name, symbol);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Cross-validation failed for {Symbol}", symbol);
        }
    }

    private bool IsInBackoffPeriod(string providerName)
    {
        if (_providerFailures.TryGetValue(providerName, out var failedAt))
        {
            return DateTimeOffset.UtcNow - failedAt < _failureBackoffDuration;
        }
        return false;
    }

    private void RecordFailure(string providerName, string message)
    {
        _providerFailures[providerName] = DateTimeOffset.UtcNow;
        UpdateHealthStatus(providerName, false, message);
    }

    private void ClearFailure(string providerName)
    {
        _providerFailures.TryRemove(providerName, out _);
    }

    private void UpdateHealthStatus(string providerName, bool isAvailable, string? message = null, TimeSpan? responseTime = null)
    {
        _healthStatus[providerName] = new ProviderHealthStatus(
            providerName,
            isAvailable,
            message,
            DateTimeOffset.UtcNow,
            responseTime
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _rateLimitTracker.Dispose();

        foreach (var provider in _providers.OfType<IDisposable>())
        {
            provider.Dispose();
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
