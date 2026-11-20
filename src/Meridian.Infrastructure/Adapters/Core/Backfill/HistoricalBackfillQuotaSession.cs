using System.Collections.Concurrent;
using System.Diagnostics;
using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Job-scoped policy and evidence shared by direct and composite historical calls.</summary>
public sealed class HistoricalBackfillQuotaSession(
    IReadOnlyDictionary<string, BackfillQuotaPolicy>? policies,
    bool retryRateLimits) : IDisposable
{
    private static readonly AsyncLocal<HistoricalBackfillQuotaSession?> Ambient = new();
    private readonly ConcurrentDictionary<string, ProviderActivity> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string Provider, string Reason), ThrottleActivity> _throttling = new();
    private int _waiting;

    public bool IsThrottled => Volatile.Read(ref _waiting) > 0;

    public IDisposable Enter()
    {
        var previous = Ambient.Value;
        Ambient.Value = this;
        return new Scope(() => Ambient.Value = previous);
    }

    public static async Task<T> FetchAsync<T>(
        IHistoricalDataProvider provider, Func<Task<T>> fetch, CancellationToken ct, bool allowRateLimitRetry = true)
    {
        var session = Ambient.Value;
        if (session is null || provider is CompositeHistoricalDataProvider)
            return await fetch().ConfigureAwait(false);

        var activity = session.GetActivity(provider);
        if (activity.Concurrency is { } gate && !gate.Wait(0, ct))
        {
            using var waiting = session.Wait(provider.Name, "concurrency");
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // The adapter handles pagination internally. Retrying a rejected fetch cannot
            // duplicate written bars because the caller publishes only after the fetch returns.
            for (var retry = 0; ; retry++)
            {
                Interlocked.Increment(ref activity.FetchAttempts);
                try
                {
                    return await fetch().ConfigureAwait(false);
                }
                catch (RateLimitException ex)
                {
                    if (provider is not BaseHistoricalDataProvider)
                        RecordProviderRateLimit(provider.Name);
                    if (!allowRateLimitRetry || !session.RetryRateLimits || retry > 0)
                        throw;

                    var delay = ex.RetryAfter ?? provider.RateLimitWindow;
                    if (delay > TimeSpan.Zero)
                    {
                        using var waiting = session.Wait(provider.Name, "retry-after");
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            activity.Concurrency?.Release();
        }
    }

    public static async Task WaitForRequestAsync(
        IHistoricalDataProvider provider,
        RateLimiter limiter,
        CancellationToken ct)
    {
        var session = Ambient.Value;
        if (session is null || !session._providers.TryGetValue(provider.Name, out var activity))
        {
            await limiter.WaitForSlotAsync(ct).ConfigureAwait(false);
            return;
        }

        await limiter.WaitForUnitsAsync(1, ct, activity.Policy,
            reason => session.Wait(provider.Name, reason)).ConfigureAwait(false);
        Interlocked.Increment(ref activity.ObservedRequests);
    }

    public static IDisposable? WaitForProviderReset(string provider) => Ambient.Value?.Wait(provider, "provider-cooldown");

    public static void RecordProviderRateLimit(string provider)
    {
        var session = Ambient.Value;
        if (session is not null)
        {
            var activity = session._throttling.GetOrAdd((provider, "provider-rate-limit"), static _ => new());
            Interlocked.Increment(ref activity.Count);
        }
    }

    public BackfillQuotaReceipt BuildReceipt(
        BackfillQuotaEstimate? estimate,
        TimeSpan duration,
        IEnumerable<string> sources)
    {
        var usage = _providers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Where(pair => pair.Value.FetchAttempts > 0)
            .Select(pair => new BackfillProviderUsage(pair.Key, pair.Value.FetchAttempts,
                pair.Value.RequestsInstrumented ? pair.Value.ObservedRequests : null))
            .ToArray();
        var requests = usage.All(p => p.ObservedRequests.HasValue) ? (long?)usage.Sum(p => p.ObservedRequests) : null;
        var providers = sources.Concat(usage.Where(p => p.ObservedRequests != 0).Select(p => p.Provider))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var throttling = _throttling.OrderBy(pair => pair.Key.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Reason, StringComparer.Ordinal)
            .Select(pair => new BackfillThrottleEvent(pair.Key.Provider, pair.Key.Reason,
                pair.Value.Count, Math.Round(TimeSpan.FromTicks(pair.Value.WaitTicks).TotalSeconds, 3)))
            .ToArray();

        return new BackfillQuotaReceipt(estimate, providers, usage, throttling, requests, duration.TotalSeconds,
            requests - estimate?.ExpectedRequests, duration.TotalSeconds - estimate?.EstimatedDurationSeconds);
    }

    private bool RetryRateLimits { get; } = retryRateLimits;

    private ProviderActivity GetActivity(IHistoricalDataProvider provider) =>
        _providers.GetOrAdd(provider.Name, _ => new ProviderActivity(BackfillQuotaPolicy.ForProvider(policies, provider.Name),
            provider is BaseHistoricalDataProvider || provider is IBHistoricalDataProvider && provider is IProviderRateLimitDiagnosticsSource));

    private IDisposable Wait(string provider, string reason)
    {
        var activity = _throttling.GetOrAdd((provider, reason), static _ => new());
        Interlocked.Increment(ref activity.Count);
        Interlocked.Increment(ref _waiting);
        var started = Stopwatch.GetTimestamp();
        return new Scope(() =>
        {
            Interlocked.Add(ref activity.WaitTicks, Stopwatch.GetElapsedTime(started).Ticks);
            Interlocked.Decrement(ref _waiting);
        });
    }

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
            provider.Concurrency?.Dispose();
    }

    private sealed class ProviderActivity(BackfillQuotaPolicy policy, bool requestsInstrumented)
    {
        public BackfillQuotaPolicy Policy { get; } = policy;
        public SemaphoreSlim? Concurrency { get; } = policy.MaxConcurrentRequests is > 0 and var max ? new(max, max) : null;
        public bool RequestsInstrumented { get; } = requestsInstrumented;
        public long FetchAttempts;
        public long ObservedRequests;
    }

    private sealed class ThrottleActivity
    {
        public int Count;
        public long WaitTicks;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
