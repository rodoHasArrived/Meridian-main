using System.Collections.Concurrent;
using System.Threading;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Thread-safe rate limiter for API calls with sliding window support.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentQueue<DateTimeOffset> _requestTimestamps = new();
    private sealed record Limits(int MaxRequests, TimeSpan Window, TimeSpan MinDelay);
    private Limits _limits;
    private readonly ILogger _log;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private bool _disposed;

    public RateLimiter(
        int maxRequestsPerWindow,
        TimeSpan window,
        TimeSpan? minDelayBetweenRequests = null,
        ILogger? log = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxRequestsPerWindow, 0, nameof(maxRequestsPerWindow));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(window));

        ArgumentOutOfRangeException.ThrowIfLessThan(minDelayBetweenRequests ?? TimeSpan.Zero, TimeSpan.Zero, nameof(minDelayBetweenRequests));
        _limits = new Limits(maxRequestsPerWindow, window, minDelayBetweenRequests ?? TimeSpan.Zero);
        _log = log ?? LoggingSetup.ForContext<RateLimiter>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Updates admission limits in place, retaining recent request history.</summary>
    public void UpdateLimits(int maxRequestsPerWindow, TimeSpan window, TimeSpan? minDelayBetweenRequests = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxRequestsPerWindow, 0, nameof(maxRequestsPerWindow));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(window));
        ArgumentOutOfRangeException.ThrowIfLessThan(minDelayBetweenRequests ?? TimeSpan.Zero, TimeSpan.Zero, nameof(minDelayBetweenRequests));
        Volatile.Write(ref _limits, new Limits(maxRequestsPerWindow, window, minDelayBetweenRequests ?? TimeSpan.Zero));
    }

    /// <summary>
    /// Wait until a request can be made within rate limits.
    /// Returns the time waited.
    /// </summary>
    public async Task<TimeSpan> WaitForSlotAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var waitStart = _timeProvider.GetUtcNow();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Clean up old timestamps outside the window
            CleanupOldTimestamps();

            // Check if we need to wait for rate limit window
            while (_requestTimestamps.Count >= Volatile.Read(ref _limits).MaxRequests)
            {
                if (_requestTimestamps.TryPeek(out var oldest))
                {
                    var waitTime = oldest.Add(Volatile.Read(ref _limits).Window) - _timeProvider.GetUtcNow();
                    if (waitTime > TimeSpan.Zero)
                    {
                        _log.Debug("Rate limit reached, waiting {WaitMs}ms", waitTime.TotalMilliseconds);
                        await Task.Delay(waitTime, _timeProvider, ct).ConfigureAwait(false);
                    }
                }
                CleanupOldTimestamps();
            }

            // Enforce minimum delay between requests
            var minDelay = Volatile.Read(ref _limits).MinDelay;
            var timeSinceLastRequest = _timeProvider.GetUtcNow() - _lastRequest;
            if (timeSinceLastRequest < minDelay)
            {
                var delayNeeded = minDelay - timeSinceLastRequest;
                _log.Debug("Enforcing min delay, waiting {WaitMs}ms", delayNeeded.TotalMilliseconds);
                await Task.Delay(delayNeeded, _timeProvider, ct).ConfigureAwait(false);
            }

            // Record this request
            var now = _timeProvider.GetUtcNow();
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
        var now = _timeProvider.GetUtcNow();
        _requestTimestamps.Enqueue(now);
        _lastRequest = now;
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
            remaining = oldest.Add(Volatile.Read(ref _limits).Window) - _timeProvider.GetUtcNow();
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
        }

        return (_requestTimestamps.Count, Volatile.Read(ref _limits).MaxRequests, remaining);
    }

    private void CleanupOldTimestamps()
    {
        var cutoff = _timeProvider.GetUtcNow() - Volatile.Read(ref _limits).Window;
        while (_requestTimestamps.TryPeek(out var oldest) && oldest <= cutoff)
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
    private readonly object _registryGate = new();
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

        lock (_registryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_limiters.TryGetValue(providerName, out var existing))
            {
                existing.UpdateLimits(maxRequests, window, minDelay);
                return existing;
            }
            _log.Information("Creating rate limiter for {Provider}: {MaxReq} requests per {Window}",
                providerName, maxRequests, window);
            var limiter = new RateLimiter(maxRequests, window, minDelay, _log);
            _limiters[providerName] = limiter;
            return limiter;
        }
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
        lock (_registryGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var limiter in _limiters.Values)
                limiter.Dispose();
            _limiters.Clear();
        }
    }
}
