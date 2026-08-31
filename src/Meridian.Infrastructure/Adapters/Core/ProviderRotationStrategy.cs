namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Rate-limit-aware rotation policy for the <see cref="CompositeHistoricalDataProvider"/>.
/// Decides the order in which providers are attempted and how long to wait for a rate-limit
/// reset before retrying. Rate-limit <em>state</em> lives in the injected
/// <see cref="ProviderRateLimitTracker"/>; this collaborator owns only the ordering and
/// wait <em>policy</em>.
/// </summary>
internal sealed class ProviderRotationStrategy
{
    // A single rate-limit reset longer than this is treated as "not worth waiting for"; the
    // request fails fast rather than blocking on it.
    private static readonly TimeSpan MaxSingleRateLimitWait = TimeSpan.FromMinutes(5);

    // Upper bound on the random jitter added to each rate-limit wait so that many concurrent
    // requests do not resume in lock-step and hammer the provider at the same instant.
    private static readonly TimeSpan RateLimitRetryJitter = TimeSpan.FromSeconds(1);

    private readonly ProviderRateLimitTracker _rateLimitTracker;
    private readonly double _approachingThreshold;

    public ProviderRotationStrategy(
        ProviderRateLimitTracker rateLimitTracker,
        bool enabled,
        double approachingThreshold)
    {
        _rateLimitTracker = rateLimitTracker;
        Enabled = enabled;
        _approachingThreshold = approachingThreshold;
    }

    /// <summary>Whether rate-limit aware rotation is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// Order a provider set by rate-limit capacity when rotation is enabled: providers with
    /// capacity first, then those approaching their limit, then rate-limited ones last. When
    /// rotation is disabled the input order (priority order) is preserved.
    /// </summary>
    public IEnumerable<IHistoricalDataProvider> OrderByAvailability(IEnumerable<IHistoricalDataProvider> providers)
    {
        if (!Enabled)
            return providers;

        return providers.OrderBy(p =>
        {
            if (_rateLimitTracker.IsRateLimited(p.Name))
                return 1000; // Put rate-limited providers last

            if (_rateLimitTracker.IsApproachingLimit(p.Name, _approachingThreshold))
                return 100 + (int)(_rateLimitTracker.GetStatus(p.Name)?.UsagePercent ?? 0);

            return p.Priority;
        });
    }

    /// <summary>
    /// Determine how long to wait for the earliest provider rate-limit reset before the next
    /// retry, honoring the overall <paramref name="retryDeadline"/> budget and adding jitter.
    /// Returns <see langword="null"/> when waiting is not worthwhile (no reset known, the reset
    /// is too far out, or the remaining budget is exhausted), signalling the caller to fail fast.
    /// </summary>
    public TimeSpan? ComputeRateLimitWait(IEnumerable<IHistoricalDataProvider> providers, DateTimeOffset retryDeadline)
    {
        var shortestWait = providers
            .Select(p => _rateLimitTracker.GetTimeUntilReset(p.Name))
            .Where(w => w.HasValue)
            .Min();

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

    // --- Rate-limit state operations (delegated to the tracker) ---

    public bool IsRateLimited(string providerName) => _rateLimitTracker.IsRateLimited(providerName);

    public TimeSpan? GetTimeUntilReset(string providerName) => _rateLimitTracker.GetTimeUntilReset(providerName);

    public void RecordRequest(string providerName) => _rateLimitTracker.RecordRequest(providerName);

    public void RecordRateLimitHit(string providerName, TimeSpan? retryAfter)
        => _rateLimitTracker.RecordRateLimitHit(providerName, retryAfter);

    public void ClearRateLimitState(string providerName) => _rateLimitTracker.ClearRateLimitState(providerName);

    public IReadOnlyDictionary<string, RateLimitStatus> GetAllStatus() => _rateLimitTracker.GetAllStatus();

    public void Dispose() => _rateLimitTracker.Dispose();
}
