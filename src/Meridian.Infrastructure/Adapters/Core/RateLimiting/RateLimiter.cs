using System.Collections.Concurrent;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Thread-safe rate limiter for API calls with sliding window support.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly TimeSpan _minDelay;
    private readonly ILogger _log;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private bool _disposed;

    public RateLimiter(int maxRequestsPerWindow, TimeSpan window, TimeSpan? minDelayBetweenRequests = null, ILogger? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxRequestsPerWindow, 0, nameof(maxRequestsPerWindow));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(window));

        _maxRequests = maxRequestsPerWindow;
        _window = window;
        _minDelay = minDelayBetweenRequests ?? TimeSpan.Zero;
        _log = log ?? LoggingSetup.ForContext<RateLimiter>();
    }

    /// <summary>
    /// Wait until a request can be made within rate limits.
    /// Returns the time waited.
    /// </summary>
    public async Task<TimeSpan> WaitForSlotAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var waitStart = DateTimeOffset.UtcNow;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Clean up old timestamps outside the window
            CleanupOldTimestamps();

            // Check if we need to wait for rate limit window
            while (_requestTimestamps.Count >= _maxRequests)
            {
                if (_requestTimestamps.TryPeek(out var oldest))
                {
                    var waitTime = oldest.Add(_window) - DateTimeOffset.UtcNow;
                    if (waitTime > TimeSpan.Zero)
                    {
                        _log.Debug("Rate limit reached, waiting {WaitMs}ms", waitTime.TotalMilliseconds);
                        await Task.Delay(waitTime, ct).ConfigureAwait(false);
                    }
                }
                CleanupOldTimestamps();
            }

            // Enforce minimum delay between requests
            var timeSinceLastRequest = DateTimeOffset.UtcNow - _lastRequest;
            if (timeSinceLastRequest < _minDelay)
            {
                var delayNeeded = _minDelay - timeSinceLastRequest;
                _log.Debug("Enforcing min delay, waiting {WaitMs}ms", delayNeeded.TotalMilliseconds);
                await Task.Delay(delayNeeded, ct).ConfigureAwait(false);
            }

            // Record this request
            var now = DateTimeOffset.UtcNow;
            _requestTimestamps.Enqueue(now);
            _lastRequest = now;

            return now - waitStart;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Record a request without waiting (for tracking external calls).
    /// </summary>
    public void RecordRequest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestTimestamps.Enqueue(DateTimeOffset.UtcNow);
        _lastRequest = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get current usage statistics.
    /// </summary>
    public (int RequestsInWindow, int MaxRequests, TimeSpan WindowRemaining) GetStatus()
    {
        CleanupOldTimestamps();
        var remaining = TimeSpan.Zero;

        if (_requestTimestamps.TryPeek(out var oldest))
        {
            remaining = oldest.Add(_window) - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
        }

        return (_requestTimestamps.Count, _maxRequests, remaining);
    }

    private void CleanupOldTimestamps()
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        while (_requestTimestamps.TryPeek(out var oldest) && oldest < cutoff)
        {
            _requestTimestamps.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _semaphore.Dispose();
    }
}

/// <summary>
/// Manages rate limiters for multiple providers.
/// </summary>
public sealed class RateLimiterRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _limiters = new();
    private readonly ILogger _log;
    private bool _disposed;

    public RateLimiterRegistry(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<RateLimiterRegistry>();
    }

    /// <summary>
    /// Get or create a rate limiter for a provider.
    /// </summary>
    public RateLimiter GetOrCreate(string providerName, int maxRequests, TimeSpan window, TimeSpan? minDelay = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _limiters.GetOrAdd(providerName, _ =>
        {
            _log.Information("Creating rate limiter for {Provider}: {MaxReq} requests per {Window}",
                providerName, maxRequests, window);
            return new RateLimiter(maxRequests, window, minDelay, _log);
        });
    }

    /// <summary>
    /// Get status for all registered rate limiters.
    /// </summary>
    public IReadOnlyDictionary<string, (int RequestsInWindow, int MaxRequests, TimeSpan WindowRemaining)> GetAllStatus()
    {
        return _limiters.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStatus()
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
        _limiters.Clear();
    }
}
