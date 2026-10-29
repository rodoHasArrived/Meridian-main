using FluentAssertions;
using Meridian.Application.Subscriptions.Services;

namespace Meridian.Tests.SymbolSearch;

public sealed class BoundedSearchCacheTests
{
    [Fact]
    public void Cache_ExpiresAtDeadlineWithoutExtendingLifetimeOnRead()
    {
        var clock = new ManualClock();
        var cache = new BoundedSearchCache<string>(TimeSpan.FromMinutes(1), timeProvider: clock);
        cache.Set("AAPL", "original");
        clock.Advance(TimeSpan.FromSeconds(59));
        cache.TryGetValue("aapl", out var value).Should().BeTrue();
        value.Should().Be("original");

        clock.Advance(TimeSpan.FromSeconds(1));

        cache.TryGetValue("AAPL", out _).Should().BeFalse();
    }

    [Fact]
    public void Cache_EvictsOldestWriteAndReplacesKeysCaseInsensitively()
    {
        var cache = new BoundedSearchCache<string>(TimeSpan.FromMinutes(1), capacity: 2);
        cache.Set("AAPL", "first");
        cache.Set("MSFT", "second");
        cache.Set("aapl", "replacement");
        cache.Set("NVDA", "third");

        cache.TryGetValue("MSFT", out _).Should().BeFalse();
        cache.TryGetValue("AAPL", out var value).Should().BeTrue();
        value.Should().Be("replacement");
        cache.TryGetValue("NVDA", out _).Should().BeTrue();
    }

    [Fact]
    public void Cache_ConcurrentWritesStayWithinCapacity()
    {
        var cache = new BoundedSearchCache<string>(TimeSpan.FromMinutes(1), capacity: 32);
        Parallel.For(0, 1000, index => cache.Set(index.ToString(), "result"));

        Enumerable.Range(0, 1000).Count(index => cache.TryGetValue(index.ToString(), out _)).Should().Be(32);
        cache.Clear();
        Enumerable.Range(0, 1000).Any(index => cache.TryGetValue(index.ToString(), out _)).Should().BeFalse();
    }

    [Fact]
    public void Cache_ExpiredReplacementDoesNotEvictLiveEntries()
    {
        var clock = new ManualClock();
        var cache = new BoundedSearchCache<string>(TimeSpan.FromMinutes(1), capacity: 2, timeProvider: clock);
        cache.Set("expired", "old");
        clock.Advance(TimeSpan.FromSeconds(30));
        cache.Set("live", "retained");
        clock.Advance(TimeSpan.FromSeconds(30));
        cache.Set("new", "fresh");

        cache.TryGetValue("expired", out _).Should().BeFalse();
        cache.TryGetValue("live", out _).Should().BeTrue();
        cache.TryGetValue("new", out _).Should().BeTrue();
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
