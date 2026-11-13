using Meridian.Contracts.Api;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Bounded process-lifetime quota evidence, excluding symbols, credentials and request payloads.</summary>
public sealed class ProviderRateLimitHistory
{
    public static ProviderRateLimitHistory Shared { get; } = new();
    private readonly object _sync = new();
    private readonly Queue<(string Provider, ProviderRateLimitEventDto Event)> _events = new();
    private readonly int _capacity;

    public ProviderRateLimitHistory(int capacity = 1024)
    {
        if (capacity is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Record(string provider, RateLimitStatus? status, string? nextProvider = null, int? httpStatus = null)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new ProviderRateLimitEventDto(now, status?.RequestsInWindow ?? 0,
            status?.UsageRatio ?? 0, true, status?.ResetAt,
            nextProvider is null ? "rate-limited" : "rotated", nextProvider, httpStatus);
        lock (_sync)
        {
            while (_events.Count >= _capacity) _events.Dequeue();
            _events.Enqueue((provider, entry));
        }
    }

    public IReadOnlyList<ProviderRateLimitEventDto> Read(string provider, int hours, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddHours(-Math.Clamp(hours, 1, 168));
        lock (_sync)
            return _events.Where(item => string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                item.Event.Timestamp >= cutoff).Select(item => item.Event).OrderByDescending(item => item.Timestamp).ToArray();
    }
}
