using System.Collections.Concurrent;
using System.Threading;
using Meridian.Application.Exceptions;
using Meridian.Application.Logging;
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
    Priority = 0, Description = "Composite provider with automatic failover across multiple historical data sources")]
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
    private readonly ILogger _log;
    private bool _disposed;

    /// <summary>
    /// Event raised when progress is updated during backfill.
    /// </summary>
#pragma warning disable CS0067 // Event is never used - Reserved for future extensibility
    public event Action<ProviderBackfillProgress>? OnProgressUpdate;
#pragma warning restore CS0067

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

    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => GetDailyBarsInternalAsync(symbol, from, to, rateLimitRetries: 0, ct);

    public Task<IReadOnlyList<AggregateBar>> GetAggregateBarsAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
        => GetAggregateBarsInternalAsync(symbol, granularity, from, to, rateLimitRetries: 0, ct);

    private async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsInternalAsync(string symbol, DateOnly? from, DateOnly? to, int rateLimitRetries, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        const int maxRateLimitRetries = 3;
        List<(string Provider, Exception Error)> errors = [];

        // Get providers ordered by rate limit availability if rotation is enabled
        var orderedProviders = GetOrderedProviders();

        foreach (var provider in orderedProviders)
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

                _log.Information("Trying {Provider} for {Symbol} (resolved: {Resolved})",
                    provider.Name, symbol, resolvedSymbol);

                var startTime = DateTimeOffset.UtcNow;

                // Record the request attempt
                _rateLimitTracker.RecordRequest(provider.Name);

                var bars = await provider.GetDailyBarsAsync(resolvedSymbol, from, to, ct).ConfigureAwait(false);
                var elapsed = DateTimeOffset.UtcNow - startTime;

                if (bars is { Count: > 0 })
                {
                    // Update health status and clear any rate limit state
                    UpdateHealthStatus(provider.Name, true, $"Retrieved {bars.Count} bars", elapsed);
                    ClearFailure(provider.Name);
                    _rateLimitTracker.ClearRateLimitState(provider.Name);

                    _log.Information("Successfully retrieved {Count} bars from {Provider} for {Symbol}",
                        bars.Count, provider.Name, symbol);

                    // Optionally validate against other providers
                    if (_enableCrossValidation && bars.Count > 0)
                    {
                        await ValidateBarsAsync(bars, symbol, from, to, provider.Name, ct).ConfigureAwait(false);
                    }

                    return bars;
                }

                _log.Debug("No bars returned from {Provider} for {Symbol}, trying next", provider.Name, symbol);
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
                    _log.Warning("Provider {Provider} hit rate limit for {Symbol}, rotating to next provider",
                        provider.Name, symbol);
                }
                else
                {
                    _log.Warning(ex, "Provider {Provider} failed for {Symbol}", provider.Name, symbol);
                    RecordFailure(provider.Name, ex.Message);
                }
                errors.Add((provider.Name, ex));
            }
        }

        // All providers failed - check if any are just rate limited and we should wait
        if (_enableRateLimitRotation && errors.All(e => IsRateLimitException(e.Error)) && rateLimitRetries < maxRateLimitRetries)
        {
            var shortestWait = GetShortestRateLimitWait();
            if (shortestWait.HasValue && shortestWait.Value < TimeSpan.FromMinutes(5))
            {
                _log.Information("All providers rate limited (attempt {Attempt}/{MaxRetries}). Waiting {WaitTime} for rate limit reset...",
                    rateLimitRetries + 1, maxRateLimitRetries, shortestWait.Value);
                await Task.Delay(shortestWait.Value, ct).ConfigureAwait(false);

                // Retry after waiting with incremented counter
                return await GetDailyBarsInternalAsync(symbol, from, to, rateLimitRetries + 1, ct).ConfigureAwait(false);
            }
        }

        // All providers failed
        if (errors.Count > 0)
        {
            var errorSummary = string.Join("; ", errors.Select(e => $"{e.Provider}: {e.Error.Message}"));
            throw new AggregateException($"All providers failed for {symbol}: {errorSummary}",
                errors.Select(e => e.Error));
        }

        _log.Warning("No data found from any provider for {Symbol}", symbol);
        return Array.Empty<HistoricalBar>();
    }

    private async Task<IReadOnlyList<AggregateBar>> GetAggregateBarsInternalAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        int rateLimitRetries,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (!SupportedGranularities.Contains(granularity))
            throw new InvalidOperationException($"Composite provider does not support {granularity.ToDisplayName()} backfill.");

        const int maxRateLimitRetries = 3;
        List<(string Provider, Exception Error)> errors = [];

        var orderedProviders = GetOrderedProviders()
            .Where(p => p is IHistoricalAggregateBarProvider aggregateProvider &&
                        aggregateProvider.SupportedGranularities.Contains(granularity));

        foreach (var provider in orderedProviders)
        {
            if (IsInBackoffPeriod(provider.Name))
            {
                _log.Debug("Skipping {Provider} - in backoff period", provider.Name);
                continue;
            }

            if (_enableRateLimitRotation && _rateLimitTracker.IsRateLimited(provider.Name))
            {
                var resetTime = _rateLimitTracker.GetTimeUntilReset(provider.Name);
                _log.Debug("Skipping {Provider} - rate limited, resets in {ResetTime}", provider.Name, resetTime);
                continue;
            }

            try
            {
                var resolvedSymbol = await ResolveSymbolForProviderAsync(symbol, provider.Name, ct).ConfigureAwait(false);
                _log.Information("Trying {Provider} for {Symbol} {Granularity} aggregates (resolved: {Resolved})",
                    provider.Name, symbol, granularity.ToDisplayName(), resolvedSymbol);

                var startTime = DateTimeOffset.UtcNow;
                _rateLimitTracker.RecordRequest(provider.Name);

                var aggregateProvider = (IHistoricalAggregateBarProvider)provider;
                var bars = await aggregateProvider.GetAggregateBarsAsync(resolvedSymbol, granularity, from, to, ct).ConfigureAwait(false);
                var elapsed = DateTimeOffset.UtcNow - startTime;

                if (bars is { Count: > 0 })
                {
                    UpdateHealthStatus(provider.Name, true, $"Retrieved {bars.Count} aggregate bars", elapsed);
                    ClearFailure(provider.Name);
                    _rateLimitTracker.ClearRateLimitState(provider.Name);

                    _log.Information("Successfully retrieved {Count} {Granularity} bars from {Provider} for {Symbol}",
                        bars.Count, granularity.ToDisplayName(), provider.Name, symbol);
                    return bars;
                }

                _log.Debug("No aggregate bars returned from {Provider} for {Symbol}, trying next", provider.Name, symbol);
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
                    _log.Warning("Provider {Provider} hit rate limit for {Symbol} {Granularity}, rotating to next provider",
                        provider.Name, symbol, granularity.ToDisplayName());
                }
                else
                {
                    _log.Warning(ex, "Provider {Provider} failed for {Symbol} {Granularity} aggregates",
                        provider.Name, symbol, granularity.ToDisplayName());
                    RecordFailure(provider.Name, ex.Message);
                }

                errors.Add((provider.Name, ex));
            }
        }

        if (_enableRateLimitRotation && errors.All(e => IsRateLimitException(e.Error)) && rateLimitRetries < maxRateLimitRetries)
        {
            var shortestWait = GetShortestRateLimitWait();
            if (shortestWait.HasValue && shortestWait.Value < TimeSpan.FromMinutes(5))
            {
                _log.Information(
                    "All aggregate providers rate limited for {Granularity} (attempt {Attempt}/{MaxRetries}). Waiting {WaitTime}...",
                    granularity.ToDisplayName(), rateLimitRetries + 1, maxRateLimitRetries, shortestWait.Value);
                await Task.Delay(shortestWait.Value, ct).ConfigureAwait(false);
                return await GetAggregateBarsInternalAsync(symbol, granularity, from, to, rateLimitRetries + 1, ct).ConfigureAwait(false);
            }
        }

        if (errors.Count > 0)
        {
            var errorSummary = string.Join("; ", errors.Select(e => $"{e.Provider}: {e.Error.Message}"));
            throw new AggregateException(
                $"All aggregate-capable providers failed for {symbol} ({granularity.ToDisplayName()}): {errorSummary}",
                errors.Select(e => e.Error));
        }

        _log.Warning("No aggregate-capable provider found for {Symbol} at {Granularity}", symbol, granularity.ToDisplayName());
        return Array.Empty<AggregateBar>();
    }

    /// <summary>
    /// Get providers ordered by rate limit capacity when rotation is enabled.
    /// </summary>
    private IEnumerable<IHistoricalDataProvider> GetOrderedProviders()
    {
        if (!_enableRateLimitRotation)
            return _providers;

        // Order by: not rate limited first, then by usage ratio (lowest first), then by priority
        return _providers.OrderBy(p =>
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
        var adjustedProviders = _providers.Where(p => p.Capabilities.AdjustedPrices);

        if (_enableRateLimitRotation)
        {
            adjustedProviders = adjustedProviders.OrderBy(p =>
            {
                if (_rateLimitTracker.IsRateLimited(p.Name))
                    return 1000;
                if (_rateLimitTracker.IsApproachingLimit(p.Name, _rateLimitRotationThreshold))
                    return 100 + (int)(_rateLimitTracker.GetStatus(p.Name)?.UsagePercent ?? 0);
                return p.Priority;
            });
        }

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
            var validationBars = await validationProvider.GetDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);

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
}
