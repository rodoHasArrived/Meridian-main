using System.Collections.Concurrent;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Tracks rate limit status across multiple providers for intelligent rotation.
/// </summary>
public sealed class ProviderRateLimitTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, ProviderRateLimitState> _providerStates
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleSync = new();
    private readonly ILogger _log;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public ProviderRateLimitTracker(ILogger? log = null, TimeProvider? timeProvider = null)
    {
        _log = log ?? LoggingSetup.ForContext<ProviderRateLimitTracker>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Register a provider with its rate limit configuration.
    /// </summary>
    public void RegisterProvider(string providerName, int maxRequestsPerWindow, TimeSpan window, TimeSpan minDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ProviderRateLimitState? previous = null;
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            var replacement = new ProviderRateLimitState(
                providerName,
                maxRequestsPerWindow,
                window,
                minDelay,
                _timeProvider);
            _providerStates.TryGetValue(providerName, out previous);
            _providerStates[providerName] = replacement;
        }

        previous?.Dispose();
        _log.Debug("Registered rate limit tracking for {Provider}: {MaxReq} requests per {Window}",
            providerName, maxRequestsPerWindow, window);
    }

    /// <summary>
    /// Register a provider from its interface properties.
    /// </summary>
    public void RegisterProvider(IHistoricalDataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        RegisterProvider(
            provider.Name,
            provider.MaxRequestsPerWindow,
            provider.RateLimitWindow,
            provider.RateLimitDelay
        );
    }

    /// <summary>
    /// Record a successful request to a provider.
    /// </summary>
    public void RecordRequest(string providerName)
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            if (_providerStates.TryGetValue(providerName, out var state))
                state.RecordRequest();
        }
    }

    /// <summary>
    /// Record that a provider hit its rate limit (HTTP 429).
    /// </summary>
    public void RecordRateLimitHit(string providerName, TimeSpan? retryAfter = null)
    {
        RateLimitStatus? status = null;
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            if (_providerStates.TryGetValue(providerName, out var state))
            {
                state.RecordRateLimitHit(retryAfter);
                status = state.GetStatus();
            }
        }

        if (status is not null)
            _log.Warning("Provider {Provider} hit rate limit. Retry after: {RetryAfter}",
                providerName, status.TimeUntilReset);
    }

    /// <summary>
    /// Check if a provider is currently rate limited.
    /// </summary>
    public bool IsRateLimited(string providerName)
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            return _providerStates.TryGetValue(providerName, out var state) && state.GetStatus().IsRateLimited;
        }
    }

    /// <summary>
    /// Check if a provider is approaching its rate limit threshold.
    /// </summary>
    public bool IsApproachingLimit(string providerName, double threshold = 0.8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 0, nameof(threshold));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 1, nameof(threshold));
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            return _providerStates.TryGetValue(providerName, out var state)
                && state.GetStatus().UsageRatio >= threshold;
        }
    }

    /// <summary>
    /// Get time until a provider's rate limit resets.
    /// </summary>
    public TimeSpan? GetTimeUntilReset(string providerName)
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            return _providerStates.TryGetValue(providerName, out var state)
                ? state.GetStatus().TimeUntilReset
                : null;
        }
    }

    /// <summary>
    /// Get the best available provider from a list, preferring those with more capacity.
    /// </summary>
    public string? GetBestAvailableProvider(IEnumerable<string> providerNames)
    {
        ArgumentNullException.ThrowIfNull(providerNames);
        var names = providerNames.ToArray();

        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            string? bestProvider = null;
            double lowestUsage = double.MaxValue;

            foreach (var name in names)
            {
                if (!_providerStates.TryGetValue(name, out var state))
                    return name;

                var status = state.GetStatus();
                if (status.IsRateLimited)
                    continue;

                if (status.UsageRatio < lowestUsage)
                {
                    lowestUsage = status.UsageRatio;
                    bestProvider = name;
                }
            }

            return bestProvider;
        }
    }

    /// <summary>
    /// Get status for all tracked providers.
    /// </summary>
    public IReadOnlyDictionary<string, RateLimitStatus> GetAllStatus()
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            return _providerStates.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.GetStatus(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Get status for a specific provider.
    /// </summary>
    public RateLimitStatus? GetStatus(string providerName)
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            return _providerStates.TryGetValue(providerName, out var state) ? state.GetStatus() : null;
        }
    }

    /// <summary>
    /// Clear rate limit state for a provider (e.g., after successful request).
    /// </summary>
    public void ClearRateLimitState(string providerName)
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            if (_providerStates.TryGetValue(providerName, out var state))
                state.ClearRateLimitHit();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var state in _providerStates.Values)
                state.Dispose();
            _providerStates.Clear();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>
/// Internal state tracking for a single provider's rate limits.
/// Uses the shared <see cref="RateLimiter"/> internally to eliminate duplicate sliding window logic.
/// </summary>
internal sealed class ProviderRateLimitState : IDisposable
{
    private readonly string _providerName;
    private readonly int _maxRequestsPerWindow;
    private readonly TimeSpan _window;
    private readonly RateLimiter _rateLimiter;
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;

    private DateTimeOffset _rateLimitHitAt = DateTimeOffset.MinValue;
    private TimeSpan _rateLimitDuration = TimeSpan.Zero;
    private bool _isExplicitlyRateLimited;
    private bool _disposed;

    public ProviderRateLimitState(
        string providerName,
        int maxRequestsPerWindow,
        TimeSpan window,
        TimeSpan minDelay,
        TimeProvider timeProvider)
    {
        _providerName = providerName;
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _window = window;
        _timeProvider = timeProvider;
        // Delegate sliding window tracking to the shared RateLimiter
        _rateLimiter = new RateLimiter(maxRequestsPerWindow, window, minDelay, timeProvider: timeProvider);
    }

    public void RecordRequest()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _rateLimiter.RecordRequest();
        }
    }

    public void RecordRateLimitHit(TimeSpan? retryAfter = null)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _rateLimitHitAt = _timeProvider.GetUtcNow();
            _rateLimitDuration = retryAfter is { } value && value > TimeSpan.Zero ? value : _window;
            _isExplicitlyRateLimited = true;
        }
    }

    public void ClearRateLimitHit()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _isExplicitlyRateLimited = false;
            _rateLimitDuration = TimeSpan.Zero;
            _rateLimitHitAt = DateTimeOffset.MinValue;
        }
    }

    public RateLimitStatus GetStatus()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var observedAt = _timeProvider.GetUtcNow();
            var (requestsInWindow, maxRequests, windowRemaining) = _rateLimiter.GetStatus();
            var explicitResetAt = _rateLimitHitAt + _rateLimitDuration;
            if (_isExplicitlyRateLimited && observedAt >= explicitResetAt)
            {
                _isExplicitlyRateLimited = false;
                _rateLimitDuration = TimeSpan.Zero;
                _rateLimitHitAt = DateTimeOffset.MinValue;
            }

            var windowLimited = requestsInWindow >= maxRequests;
            var isRateLimited = _isExplicitlyRateLimited || windowLimited;
            DateTimeOffset? resetAt = _isExplicitlyRateLimited
                ? explicitResetAt
                : requestsInWindow > 0 && windowRemaining > TimeSpan.Zero
                    ? observedAt + windowRemaining
                    : null;
            TimeSpan? timeUntilReset = resetAt is { } value && value > observedAt
                ? value - observedAt
                : null;
            var usageRatio = maxRequests > 0 ? (double)requestsInWindow / maxRequests : 0;
            var reason = _isExplicitlyRateLimited
                ? "provider-response"
                : windowLimited ? "configured-window" : null;

            return new RateLimitStatus(
                _providerName,
                requestsInWindow,
                _maxRequestsPerWindow,
                _window,
                isRateLimited,
                timeUntilReset,
                usageRatio,
                observedAt,
                resetAt,
                reason);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _rateLimiter.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>
/// Status of a provider's rate limit.
/// </summary>
public sealed record RateLimitStatus(
    string ProviderName,
    int RequestsInWindow,
    int MaxRequestsPerWindow,
    TimeSpan Window,
    bool IsRateLimited,
    TimeSpan? TimeUntilReset,
    double UsageRatio,
    DateTimeOffset ObservedAt = default,
    DateTimeOffset? ResetAt = null,
    string? Reason = null
)
{
    public int RemainingRequests => Math.Max(0, MaxRequestsPerWindow - RequestsInWindow);
    public double UsagePercent => UsageRatio * 100;
}
