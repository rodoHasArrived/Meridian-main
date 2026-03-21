using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Tracks rate limit status across multiple providers for intelligent rotation.
/// </summary>
public sealed class ProviderRateLimitTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, ProviderRateLimitState> _providerStates = new();
    private readonly ILogger _log;
    private bool _disposed;

    public ProviderRateLimitTracker(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<ProviderRateLimitTracker>();
    }

    /// <summary>
    /// Register a provider with its rate limit configuration.
    /// </summary>
    public void RegisterProvider(string providerName, int maxRequestsPerWindow, TimeSpan window, TimeSpan minDelay)
    {
        _providerStates[providerName] = new ProviderRateLimitState(
            providerName,
            maxRequestsPerWindow,
            window,
            minDelay
        );
        _log.Debug("Registered rate limit tracking for {Provider}: {MaxReq} requests per {Window}",
            providerName, maxRequestsPerWindow, window);
    }

    /// <summary>
    /// Register a provider from its interface properties.
    /// </summary>
    public void RegisterProvider(IHistoricalDataProvider provider)
    {
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
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            state.RecordRequest();
        }
    }

    /// <summary>
    /// Record that a provider hit its rate limit (HTTP 429).
    /// </summary>
    public void RecordRateLimitHit(string providerName, TimeSpan? retryAfter = null)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            state.RecordRateLimitHit(retryAfter);
            _log.Warning("Provider {Provider} hit rate limit. Retry after: {RetryAfter}",
                providerName, state.RateLimitResetsAt - DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Check if a provider is currently rate limited.
    /// </summary>
    public bool IsRateLimited(string providerName)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            return state.IsRateLimited;
        }
        return false;
    }

    /// <summary>
    /// Check if a provider is approaching its rate limit threshold.
    /// </summary>
    public bool IsApproachingLimit(string providerName, double threshold = 0.8)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            return state.GetUsageRatio() >= threshold;
        }
        return false;
    }

    /// <summary>
    /// Get time until a provider's rate limit resets.
    /// </summary>
    public TimeSpan? GetTimeUntilReset(string providerName)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            return state.GetTimeUntilReset();
        }
        return null;
    }

    /// <summary>
    /// Get the best available provider from a list, preferring those with more capacity.
    /// </summary>
    public string? GetBestAvailableProvider(IEnumerable<string> providerNames)
    {
        string? bestProvider = null;
        double lowestUsage = double.MaxValue;

        foreach (var name in providerNames)
        {
            if (!_providerStates.TryGetValue(name, out var state))
            {
                // Unknown provider - assume it's available
                return name;
            }

            if (state.IsRateLimited)
                continue;

            var usage = state.GetUsageRatio();
            if (usage < lowestUsage)
            {
                lowestUsage = usage;
                bestProvider = name;
            }
        }

        return bestProvider;
    }

    /// <summary>
    /// Get status for all tracked providers.
    /// </summary>
    public IReadOnlyDictionary<string, RateLimitStatus> GetAllStatus()
    {
        var result = new Dictionary<string, RateLimitStatus>();
        foreach (var kvp in _providerStates)
        {
            result[kvp.Key] = kvp.Value.GetStatus();
        }
        return result;
    }

    /// <summary>
    /// Get status for a specific provider.
    /// </summary>
    public RateLimitStatus? GetStatus(string providerName)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            return state.GetStatus();
        }
        return null;
    }

    /// <summary>
    /// Clear rate limit state for a provider (e.g., after successful request).
    /// </summary>
    public void ClearRateLimitState(string providerName)
    {
        if (_providerStates.TryGetValue(providerName, out var state))
        {
            state.ClearRateLimitHit();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Dispose all tracked provider states (which now own RateLimiter instances)
        foreach (var state in _providerStates.Values)
        {
            state.Dispose();
        }
        _providerStates.Clear();
    }
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
    private readonly object _lock = new();

    private DateTimeOffset _rateLimitHitAt = DateTimeOffset.MinValue;
    private TimeSpan _rateLimitDuration = TimeSpan.Zero;
    private bool _isExplicitlyRateLimited;
    private bool _disposed;

    public ProviderRateLimitState(string providerName, int maxRequestsPerWindow, TimeSpan window, TimeSpan minDelay)
    {
        _providerName = providerName;
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _window = window;
        // Delegate sliding window tracking to the shared RateLimiter
        _rateLimiter = new RateLimiter(maxRequestsPerWindow, window, minDelay);
    }

    public DateTimeOffset RateLimitResetsAt => _rateLimitHitAt + _rateLimitDuration;

    public bool IsRateLimited
    {
        get
        {
            // Check explicit rate limit from 429 response
            if (_isExplicitlyRateLimited)
            {
                if (DateTimeOffset.UtcNow >= RateLimitResetsAt)
                {
                    _isExplicitlyRateLimited = false;
                }
                else
                {
                    return true;
                }
            }

            // Check if we've hit our request limit using the shared RateLimiter
            var (requestsInWindow, maxRequests, _) = _rateLimiter.GetStatus();
            return requestsInWindow >= maxRequests;
        }
    }

    public void RecordRequest()
    {
        _rateLimiter.RecordRequest();
    }

    public void RecordRateLimitHit(TimeSpan? retryAfter = null)
    {
        lock (_lock)
        {
            _rateLimitHitAt = DateTimeOffset.UtcNow;
            _rateLimitDuration = retryAfter ?? _window;
            _isExplicitlyRateLimited = true;
        }
    }

    public void ClearRateLimitHit()
    {
        lock (_lock)
        {
            _isExplicitlyRateLimited = false;
        }
    }

    public double GetUsageRatio()
    {
        var (requestsInWindow, maxRequests, _) = _rateLimiter.GetStatus();
        return (double)requestsInWindow / maxRequests;
    }

    public TimeSpan? GetTimeUntilReset()
    {
        if (_isExplicitlyRateLimited)
        {
            var remaining = RateLimitResetsAt - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        var (_, _, windowRemaining) = _rateLimiter.GetStatus();
        return windowRemaining > TimeSpan.Zero ? windowRemaining : null;
    }

    public RateLimitStatus GetStatus()
    {
        var (requestsInWindow, _, windowRemaining) = _rateLimiter.GetStatus();
        return new RateLimitStatus(
            _providerName,
            requestsInWindow,
            _maxRequestsPerWindow,
            _window,
            IsRateLimited,
            GetTimeUntilReset(),
            GetUsageRatio()
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _rateLimiter.Dispose();
    }
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
    double UsageRatio
)
{
    public int RemainingRequests => Math.Max(0, MaxRequestsPerWindow - RequestsInWindow);
    public double UsagePercent => UsageRatio * 100;
}
