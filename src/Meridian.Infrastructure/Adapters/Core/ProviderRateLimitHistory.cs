using System.Collections.Concurrent;
using Meridian.Contracts.Api;
using Meridian.Core.IO;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Recent quota events and durable UTC daily totals, without request payloads or credentials.</summary>
public sealed class ProviderRateLimitHistory
{
    private static readonly ConcurrentDictionary<string, ProviderRateLimitHistory> Histories =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public static ProviderRateLimitHistory Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Queue<(string Provider, ProviderRateLimitEventDto Event)> _events = new();
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

    public void Record(string provider, RateLimitStatus? status, string? nextProvider = null, int? httpStatus = null)
    {
        var now = _timeProvider.GetUtcNow();
        var entry = new ProviderRateLimitEventDto(now, status?.RequestsInWindow ?? 0,
            status?.UsageRatio ?? 0, true, status?.ResetAt,
            nextProvider is null ? "rate-limited" : "rotated", nextProvider, httpStatus);
        lock (_sync)
        {
            while (_events.Count >= _capacity)
                _events.Dequeue();
            _events.Enqueue((provider, entry));
        }
        _rollups?.Record(provider, DateOnly.FromDateTime(now.UtcDateTime), rotated: nextProvider is not null);
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
}
