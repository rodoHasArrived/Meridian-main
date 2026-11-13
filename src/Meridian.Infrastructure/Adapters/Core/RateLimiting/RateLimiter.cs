using Meridian.Contracts.Integrity;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Thread-safe weighted admission with a sliding window and optional vendor-specific daily limit.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private sealed record Charge(DateTimeOffset At, int Units);
    private readonly Queue<Charge> _requestTimestamps = new();
    private readonly Queue<Charge> _dailyTimestamps = new();
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private RequestBudgetConfiguration _limits;
    private int _unitsInWindow;
    private int _dailyUnits;
    private DateOnly _dailyDate;
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
        ArgumentOutOfRangeException.ThrowIfLessThan(minDelayBetweenRequests ?? TimeSpan.Zero, TimeSpan.Zero, nameof(minDelayBetweenRequests));
        _limits = new(maxRequestsPerWindow, window, minDelayBetweenRequests ?? TimeSpan.Zero);
        _limits.Validate();
        _log = log ?? LoggingSetup.ForContext<RateLimiter>();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RateLimiter(RequestBudgetConfiguration configuration, ILogger? log = null, TimeProvider? timeProvider = null)
        : this(configuration.Maximum, configuration.Window, configuration.MinimumDelay, log, timeProvider)
    {
        configuration.Validate();
        _limits = configuration;
    }

    /// <summary>Updates admission limits in place, retaining recent request history and cooldown.</summary>
    public void UpdateLimits(int maxRequestsPerWindow, TimeSpan window, TimeSpan? minDelayBetweenRequests = null)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxRequestsPerWindow, 0, nameof(maxRequestsPerWindow));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(window));
            ArgumentOutOfRangeException.ThrowIfLessThan(minDelayBetweenRequests ?? TimeSpan.Zero, TimeSpan.Zero, nameof(minDelayBetweenRequests));
            _limits = _limits with { Maximum = maxRequestsPerWindow, Window = window, MinimumDelay = minDelayBetweenRequests ?? TimeSpan.Zero };
        }
    }

    /// <summary>
    /// Wait until a request can be made within rate limits, and charge that attempt.
    /// Returns the time waited. Cancelled waits do not consume capacity.
    /// </summary>
    public Task<TimeSpan> WaitForSlotAsync(CancellationToken ct = default) => WaitForUnitsAsync(1, ct);

    /// <summary>
    /// Atomically reserve the complete request cost against short and daily limits. A wait
    /// cancelled before admission charges neither limit. No HTTP timeout runs during admission.
    /// </summary>
    public async Task<TimeSpan> WaitForUnitsAsync(int units, CancellationToken ct = default)
    {
        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateCost(units);
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
                        ValidateCost(units);
                        var next = NextAdmissionAt(now, units);
                        wait = next - now;
                        if (wait <= TimeSpan.Zero)
                        {
                            RecordUnitsCore(now, units);
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
        => RecordUnits(1);

    /// <summary>Record a known external request's full cost without waiting.</summary>
    public void RecordUnits(int units)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(units, 0);
            var now = _timeProvider.GetUtcNow();
            CleanupOldTimestamps(now);
            RecordUnitsCore(now, units);
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
                reset = Max(reset, oldest.At + _limits.Window);
            if (_cooldownUntil is { } cooldown)
                reset = Max(reset, cooldown);
            if (_limits.DailyMaximum is { } daily && _dailyUnits >= daily && DailyResetAt(now) is { } dailyReset)
                reset = Max(reset, dailyReset);
            return (_unitsInWindow, _limits.Maximum, reset - now);
        }
    }

    /// <summary>Get weighted usage and the explicitly configured vendor daily boundary.</summary>
    public RequestBudgetStatus GetBudgetStatus()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _timeProvider.GetUtcNow();
            CleanupOldTimestamps(now);
            var next = NextAdmissionAt(now, 1);
            return new(_limits.Unit, _unitsInWindow, _limits.Maximum, _limits.Window,
                _limits.DailyMaximum.HasValue ? _dailyUnits : null, _limits.DailyMaximum,
                DailyResetAt(now), _limits.DailyReset, next > now ? next : null);
        }
    }

    private void CleanupOldTimestamps(DateTimeOffset now)
    {
        var cutoff = now - _limits.Window;
        while (_requestTimestamps.TryPeek(out var oldest) && oldest.At <= cutoff)
            _unitsInWindow -= _requestTimestamps.Dequeue().Units;
        if (_limits.DailyMaximum is null)
            return;
        if (_limits.DailyReset == DailyQuotaResetMode.UtcMidnight)
        {
            var date = DateOnly.FromDateTime(now.UtcDateTime);
            if (date > _dailyDate)
            {
                _dailyDate = date;
                _dailyUnits = 0;
            }
        }
        else
        {
            var dailyCutoff = now - TimeSpan.FromDays(1);
            while (_dailyTimestamps.TryPeek(out var oldestDaily) && oldestDaily.At <= dailyCutoff)
                _dailyUnits -= _dailyTimestamps.Dequeue().Units;
        }
    }

    private void ValidateCost(int units)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(units, 0);
        if (units > _limits.Maximum || (_limits.DailyMaximum is { } daily && units > daily))
            throw new ArgumentOutOfRangeException(nameof(units), "A request cost exceeds the declared account capacity.");
    }

    private void RecordUnitsCore(DateTimeOffset now, int units)
    {
        var charge = new Charge(now, units);
        _requestTimestamps.Enqueue(charge);
        _unitsInWindow = checked(_unitsInWindow + units);
        if (_limits.DailyMaximum.HasValue)
        {
            if (_limits.DailyReset == DailyQuotaResetMode.Rolling24Hours)
                _dailyTimestamps.Enqueue(charge);
            _dailyUnits = checked(_dailyUnits + units);
        }
        _lastRequest = now;
    }

    private DateTimeOffset NextAdmissionAt(DateTimeOffset now, int units)
    {
        var next = _lastRequest == DateTimeOffset.MinValue ? now : _lastRequest + _limits.MinimumDelay;
        next = Max(next, CapacityAvailableAt(_requestTimestamps, _unitsInWindow, _limits.Maximum, units, _limits.Window, now));
        if (_limits.DailyMaximum is { } daily && (long)_dailyUnits + units > daily)
            next = Max(next, _limits.DailyReset == DailyQuotaResetMode.UtcMidnight
                ? NextUtcMidnight(now)
                : CapacityAvailableAt(_dailyTimestamps, _dailyUnits, daily, units, TimeSpan.FromDays(1), now));
        if (_cooldownUntil is { } cooldown)
            next = Max(next, cooldown);
        return next;
    }

    private static DateTimeOffset CapacityAvailableAt(Queue<Charge> charges, int used, int maximum, int units, TimeSpan window, DateTimeOffset now)
    {
        var needed = (long)used + units - maximum;
        if (needed <= 0)
            return now;
        foreach (var charge in charges)
        {
            needed -= charge.Units;
            if (needed <= 0)
                return charge.At + window;
        }
        return now;
    }

    private DateTimeOffset? DailyResetAt(DateTimeOffset now)
        => _limits.DailyMaximum is null ? null : _limits.DailyReset == DailyQuotaResetMode.UtcMidnight
            ? NextUtcMidnight(now)
            : _dailyTimestamps.TryPeek(out var oldest) ? oldest.At + TimeSpan.FromDays(1) : null;

    private static DateTimeOffset NextUtcMidnight(DateTimeOffset now)
        => new(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

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

    private readonly Dictionary<string, (RateLimiter Limiter, RequestBudgetConfiguration Configuration)> _limiters = new(StringComparer.Ordinal);
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
        => GetOrCreateCore(providerName, new(maxRequests, window, minDelay ?? TimeSpan.Zero), enforceConfiguration: false);

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
        return GetOrCreate(providerName, credential, quotaGroup, new(maxRequests, window, minDelay ?? TimeSpan.Zero));
    }

    /// <summary>Get the immutable effective account profile shared across all capabilities.</summary>
    public RateLimiter GetOrCreate(string providerName, string credential, string quotaGroup, RequestBudgetConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(quotaGroup);
        var fingerprint = Sha256Digest.ComputeUtf8(credential);
        var key = $"{providerName.Trim().ToLowerInvariant()}:{quotaGroup.Trim().ToLowerInvariant()}:{fingerprint}";
        return GetOrCreateCore(key, configuration, enforceConfiguration: true);
    }

    private RateLimiter GetOrCreateCore(string key, RequestBudgetConfiguration configuration, bool enforceConfiguration)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            configuration.Validate();
            if (_limiters.TryGetValue(key, out var existing))
            {
                if (enforceConfiguration && existing.Configuration != configuration)
                    throw new InvalidOperationException("Capabilities sharing a vendor quota must use the same request budget configuration.");
                if (!enforceConfiguration)
                {
                    // Legacy provider registries support live reconfiguration. Update the
                    // existing limiter so requests already admitted keep consuming quota.
                    existing.Limiter.UpdateLimits(configuration.Maximum, configuration.Window, configuration.MinimumDelay);
                    _limiters[key] = (existing.Limiter, configuration);
                }
                return existing.Limiter;
            }
            var limiter = new RateLimiter(configuration, _log, _timeProvider);
            _limiters.Add(key, (limiter, configuration));
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
