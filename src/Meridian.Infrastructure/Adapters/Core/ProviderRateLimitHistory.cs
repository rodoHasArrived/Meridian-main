using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Contracts.Api;
using Meridian.Core.Exceptions;
using Meridian.Core.IO;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Recent quota events and durable UTC daily totals, without request payloads or credentials.</summary>
public sealed class ProviderRateLimitHistory
{
    private static readonly ConcurrentDictionary<string, ProviderRateLimitHistory> Histories =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly AsyncLocal<ProviderRateLimitHistory?> ActiveHistory = new();
    public static ProviderRateLimitHistory Shared { get; } = new();
    private static readonly ConditionalWeakTable<HttpResponseMessage, ThrottleObservation> HttpThrottles = new();
    private static readonly ConditionalWeakTable<RateLimitException, ThrottleObservation> ObservedExceptions = new();
    private readonly object _sync = new();
    private readonly Queue<(string Provider, ProviderRateLimitEventDto Event)> _events = new();
    private readonly ConditionalWeakTable<ThrottleObservation, object> _recordedThrottles = new();
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly ProviderRateLimitRollupStore? _rollups;

    public ProviderRateLimitHistory(int capacity = 1024)
        : this(capacity, TimeProvider.System)
    {
    }

    public ProviderRateLimitHistory(string dataRoot, IAtomicFileWriter writer, TimeProvider? timeProvider = null, int capacity = 1024)
        : this(capacity, timeProvider ?? TimeProvider.System)
    {
        _rollups = new ProviderRateLimitRollupStore(dataRoot, writer);
    }

    private ProviderRateLimitHistory(int capacity, TimeProvider timeProvider)
    {
        if (capacity is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _timeProvider = timeProvider;
    }

    /// <summary>Shares recent observations between host surfaces using the same data root.</summary>
    public static ProviderRateLimitHistory ForDataRoot(string dataRoot, IAtomicFileWriter writer)
    {
        var normalized = new RootedPathGuard(dataRoot).RootPath;
        return Histories.GetOrAdd(normalized, path => new ProviderRateLimitHistory(path, writer));
    }

    internal static ProviderRateLimitHistory Current => ActiveHistory.Value ?? Shared;

    /// <summary>Directs adapter observations to this history for the current asynchronous request.</summary>
    public IDisposable BeginScope()
    {
        var scope = new HistoryScope(ActiveHistory.Value);
        ActiveHistory.Value = this;
        return scope;
    }

    public void Record(string provider, RateLimitStatus? status, string? nextProvider = null, int? httpStatus = null)
    {
        var entry = new ProviderRateLimitEventDto(_timeProvider.GetUtcNow(), status?.RequestsInWindow ?? 0,
            status?.UsageRatio ?? 0, true, status?.ResetAt,
            nextProvider is null ? "rate-limited" : "rotated", nextProvider, httpStatus);
        lock (_sync)
            Append(provider, entry);
        RecordRollup(provider, entry);
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

    internal void RecordThrottle(string provider, Exception exception, RateLimitStatus? status, int? httpStatus)
    {
        // Routing can observe raw or typed exceptions from other adapters. Exceptions mapped
        // from provider responses retain the original observation and its actual attempt data.
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

    private ThrottleObservation CreateThrottle(
        string provider, RateLimitInfo? quota, DateTimeOffset? resetAt, int? httpStatus) =>
        new(provider, new ProviderRateLimitEventDto(
            _timeProvider.GetUtcNow(), quota?.RequestsMade ?? 0, quota?.UsageRatio ?? 0,
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
        RecordRollup(observation.Provider, observation.Event);
    }

    private void RecordRollup(string provider, ProviderRateLimitEventDto entry) =>
        _rollups?.Record(provider, DateOnly.FromDateTime(entry.Timestamp.UtcDateTime), rotated: entry.NextProvider is not null);

    private void Append(string provider, ProviderRateLimitEventDto entry)
    {
        while (_events.Count >= _capacity)
            _events.Dequeue();
        _events.Enqueue((provider, entry));
    }

    public IReadOnlyList<ProviderRateLimitEventDto> Read(string provider, int hours, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? _timeProvider.GetUtcNow()).AddHours(-Math.Clamp(hours, 1, 168));
        lock (_sync)
            return _events.Where(item => string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                item.Event.Timestamp >= cutoff).Select(item => item.Event).OrderByDescending(item => item.Timestamp).ToArray();
    }

    public bool TryReadDailyRollups(string provider, out IReadOnlyList<ProviderRateLimitDailyRollupDto> rollups)
    {
        if (_rollups is not null)
            return _rollups.TryRead(provider, DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime), out rollups);
        rollups = [];
        return false;
    }

    private sealed class HistoryScope(ProviderRateLimitHistory? previous) : IDisposable
    {
        public void Dispose() => ActiveHistory.Value = previous;
    }

    private sealed record ThrottleObservation(string Provider, ProviderRateLimitEventDto Event);
}
