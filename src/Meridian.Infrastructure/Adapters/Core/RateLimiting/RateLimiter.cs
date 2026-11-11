using System.Security.Cryptography;
using System.Text;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Thread-safe rate limiter for API calls with sliding window support.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Queue<DateTimeOffset> _requestTimestamps = new();
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly TimeSpan _minDelay;
    private readonly ILogger _log;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private DateTimeOffset? _cooldownUntil;
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

        _maxRequests = maxRequestsPerWindow;
        _window = window;
        _minDelay = minDelayBetweenRequests ?? TimeSpan.Zero;
        _log = log ?? LoggingSetup.ForContext<RateLimiter>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Wait until a request can be made within rate limits, and charge that attempt.
    /// Returns the time waited. Cancelled waits do not consume capacity.
    /// </summary>
    public async Task<TimeSpan> WaitForSlotAsync(CancellationToken ct = default)
    {
        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        }

        using (linked)
        {
            var waitStart = _timeProvider.GetUtcNow();
            await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    TimeSpan wait;
                    lock (_sync)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var now = _timeProvider.GetUtcNow();
                        CleanupOldTimestamps(now);
                        var next = _lastRequest == DateTimeOffset.MinValue ? now : _lastRequest + _minDelay;
                        if (_requestTimestamps.Count >= _maxRequests && _requestTimestamps.TryPeek(out var oldest))
                            next = Max(next, oldest + _window);
                        if (_cooldownUntil is { } cooldown)
                            next = Max(next, cooldown);
                        wait = next - now;
                        if (wait <= TimeSpan.Zero)
                        {
                            _requestTimestamps.Enqueue(now);
                            _lastRequest = now;
                            return now - waitStart;
                        }
                    }

                    _log.Debug("Rate limit reached, waiting {WaitMs}ms", wait.TotalMilliseconds);
                    await Task.Delay(wait, _timeProvider, linked.Token).ConfigureAwait(false);
                    // A response from another in-flight request can extend the cooldown while
                    // this caller sleeps; recheck all constraints before charging and sending.
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>Extend the shared provider cooldown without shortening an existing one.</summary>
    public void ApplyCooldown(TimeSpan duration)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (duration <= TimeSpan.Zero)
                return;
            var until = _timeProvider.GetUtcNow() + duration;
            if (_cooldownUntil is null || until > _cooldownUntil)
                _cooldownUntil = until;
        }
    }

    public DateTimeOffset? GetCooldownUntil()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _cooldownUntil > _timeProvider.GetUtcNow() ? _cooldownUntil : null;
        }
    }

    /// <summary>Record a request without waiting (for tracking external calls).</summary>
    public void RecordRequest()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _timeProvider.GetUtcNow();
            _requestTimestamps.Enqueue(now);
            _lastRequest = now;
        }
    }

    /// <summary>Get current usage statistics, including a provider-imposed cooldown.</summary>
    public (int RequestsInWindow, int MaxRequests, TimeSpan WindowRemaining) GetStatus()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _timeProvider.GetUtcNow();
            CleanupOldTimestamps(now);
            var reset = now;
            if (_requestTimestamps.TryPeek(out var oldest))
                reset = Max(reset, oldest + _window);
            if (_cooldownUntil is { } cooldown)
                reset = Max(reset, cooldown);
            return (_requestTimestamps.Count, _maxRequests, reset - now);
        }
    }

    private void CleanupOldTimestamps(DateTimeOffset now)
    {
        var cutoff = now - _window;
        while (_requestTimestamps.TryPeek(out var oldest) && oldest <= cutoff)
            _requestTimestamps.Dequeue();
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _lifetime.Cancel();
        // Waiters own the semaphore until their finally blocks run. Do not dispose it
        // under them; it has no unmanaged wait handle in this async-only implementation.
    }
}

/// <summary>Manages request budgets shared across provider capabilities.</summary>
public sealed class RateLimiterRegistry : IDisposable
{
    internal static RateLimiterRegistry Shared { get; } = new();
    internal TimeProvider TimeProvider => _timeProvider;

    private readonly Dictionary<string, (RateLimiter Limiter, int Maximum, TimeSpan Window, TimeSpan Delay)> _limiters = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly ILogger _log;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public RateLimiterRegistry(ILogger? log = null, TimeProvider? timeProvider = null)
    {
        _log = log ?? LoggingSetup.ForContext<RateLimiterRegistry>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Get or create a legacy provider-wide limiter.</summary>
    public RateLimiter GetOrCreate(string providerName, int maxRequests, TimeSpan window, TimeSpan? minDelay = null)
        => GetOrCreateCore(providerName, maxRequests, window, minDelay ?? TimeSpan.Zero, enforceConfiguration: false);

    /// <summary>
    /// Get the vendor quota shared by capabilities using the same credential. Credential
    /// values are hashed before becoming registry keys and never appear in status or logs.
    /// The registry owns these limiters; individual providers must not dispose them.
    /// </summary>
    public RateLimiter GetOrCreate(
        string providerName,
        string credential,
        string quotaGroup,
        int maxRequests,
        TimeSpan window,
        TimeSpan? minDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(quotaGroup);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));
        var key = $"{providerName.Trim().ToLowerInvariant()}:{quotaGroup.Trim().ToLowerInvariant()}:{fingerprint}";
        return GetOrCreateCore(key, maxRequests, window, minDelay ?? TimeSpan.Zero, enforceConfiguration: true);
    }

    private RateLimiter GetOrCreateCore(string key, int maximum, TimeSpan window, TimeSpan delay, bool enforceConfiguration)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_limiters.TryGetValue(key, out var existing))
            {
                if (enforceConfiguration && (existing.Maximum != maximum || existing.Window != window || existing.Delay != delay))
                    throw new InvalidOperationException("Capabilities sharing a vendor quota must use the same request budget configuration.");
                return existing.Limiter;
            }
            var limiter = new RateLimiter(maximum, window, delay, _log, _timeProvider);
            _limiters.Add(key, (limiter, maximum, window, delay));
            return limiter;
        }
    }

    public IReadOnlyDictionary<string, (int RequestsInWindow, int MaxRequests, TimeSpan WindowRemaining)> GetAllStatus()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _limiters.ToDictionary(pair => pair.Key, pair => pair.Value.Limiter.GetStatus());
        }
    }

    public void Dispose()
    {
        RateLimiter[] limiters;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            limiters = _limiters.Values.Select(entry => entry.Limiter).ToArray();
            _limiters.Clear();
        }
        foreach (var limiter in limiters)
            limiter.Dispose();
    }
}
