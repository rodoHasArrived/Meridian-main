using System.Runtime.CompilerServices;
using Meridian.Contracts.Api;
using Meridian.Core.Exceptions;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Bounded process-lifetime quota evidence, excluding symbols, credentials and request payloads.</summary>
public sealed class ProviderRateLimitHistory
{
    public static ProviderRateLimitHistory Shared { get; } = new();
    private static readonly ConditionalWeakTable<HttpResponseMessage, ThrottleObservation> HttpThrottles = new();
    private static readonly ConditionalWeakTable<RateLimitException, ThrottleObservation> ObservedExceptions = new();
    private readonly object _sync = new();
    private readonly Queue<(string Provider, ProviderRateLimitEventDto Event)> _events = new();
    private readonly ConditionalWeakTable<ThrottleObservation, object> _recordedThrottles = new();
    private readonly int _capacity;

    public ProviderRateLimitHistory(int capacity = 1024)
    {
        if (capacity is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Record(string provider, RateLimitStatus? status, string? nextProvider = null, int? httpStatus = null)
    {
        var entry = new ProviderRateLimitEventDto(DateTimeOffset.UtcNow, status?.RequestsInWindow ?? 0,
            status?.UsageRatio ?? 0, true, status?.ResetAt,
            nextProvider is null ? "rate-limited" : "rotated", nextProvider, httpStatus);
        lock (_sync)
            Append(provider, entry);
    }

    internal void RecordHttpThrottle(HttpResponseMessage response, string provider, RateLimitInfo? quota, DateTimeOffset? resetAt = null)
    {
        var observation = HttpThrottles.GetValue(response,
            _ => CreateThrottle(provider, quota, resetAt, (int)response.StatusCode));
        RecordObservedThrottle(observation);
    }

    internal static void LinkHttpThrottle(HttpResponseMessage response, RateLimitException exception)
    {
        if (HttpThrottles.TryGetValue(response, out var observation))
            ObservedExceptions.Add(exception, observation);
    }

    internal void RecordProviderThrottle(RateLimitException exception, RateLimitInfo quota)
    {
        var observation = ObservedExceptions.GetValue(exception,
            _ => CreateThrottle(quota.ProviderName, quota, null, httpStatus: null));
        RecordObservedThrottle(observation);
    }

    internal void RecordCompositeThrottle(string provider, Exception exception, RateLimitStatus? status, int? httpStatus)
    {
        // Raw or typed exceptions from other adapters can still be observed by the composite.
        // Exceptions mapped from known provider responses retain the original observation.
        var observation = FindObservedThrottle(exception);
        if (observation is null)
            Record(provider, status, httpStatus: httpStatus);
        else
            RecordObservedThrottle(observation);
    }

    private static ThrottleObservation? FindObservedThrottle(Exception exception)
    {
        if (exception is RateLimitException rateLimit && ObservedExceptions.TryGetValue(rateLimit, out var observation))
            return observation;

        return exception is AggregateException aggregate
            ? aggregate.InnerExceptions.Select(FindObservedThrottle).FirstOrDefault(static match => match is not null)
            : exception.InnerException is { } inner ? FindObservedThrottle(inner) : null;
    }

    private static ThrottleObservation CreateThrottle(
        string provider, RateLimitInfo? quota, DateTimeOffset? resetAt, int? httpStatus) =>
        new(provider, new ProviderRateLimitEventDto(
            DateTimeOffset.UtcNow, quota?.RequestsMade ?? 0, quota?.UsageRatio ?? 0,
            true, quota?.ResetsAt ?? resetAt, HttpStatus: httpStatus));

    private void RecordObservedThrottle(ThrottleObservation observation)
    {
        lock (_sync)
        {
            if (_recordedThrottles.TryGetValue(observation, out _))
                return;
            _recordedThrottles.Add(observation, new object());
            Append(observation.Provider, observation.Event);
        }
    }

    private void Append(string provider, ProviderRateLimitEventDto entry)
    {
        while (_events.Count >= _capacity)
            _events.Dequeue();
        _events.Enqueue((provider, entry));
    }

    public IReadOnlyList<ProviderRateLimitEventDto> Read(string provider, int hours, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddHours(-Math.Clamp(hours, 1, 168));
        lock (_sync)
            return _events.Where(item => string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                item.Event.Timestamp >= cutoff).Select(item => item.Event).OrderByDescending(item => item.Timestamp).ToArray();
    }

    private sealed record ThrottleObservation(string Provider, ProviderRateLimitEventDto Event);
}
