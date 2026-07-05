using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Streaming;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class QuoteStreamBroadcasterTests
{
    private static readonly IServiceProvider Services = new EmptyServiceProvider();

    private static QuoteStreamOptions Options(int cap = 4) => new()
    {
        CoalesceIntervalMs = 0,
        MaxConcurrentStreamsPerSession = cap,
        SubscriberChannelCapacity = 1
    };

    private static QuotesSnapshotResponse Snapshot(params string[] symbols)
    {
        var quotes = new List<QuotesSnapshotItem>();
        long seq = 1;
        foreach (var symbol in symbols)
        {
            quotes.Add(new QuotesSnapshotItem(
                Symbol: symbol,
                Timestamp: DateTimeOffset.UnixEpoch,
                BidPrice: 100m,
                BidSize: 10,
                AskPrice: 101m,
                AskSize: 10,
                MidPrice: 100.5m,
                Spread: 1m,
                LastPrice: 100m,
                LastSize: 5,
                LastTradeTimestamp: DateTimeOffset.UnixEpoch,
                SequenceNumber: seq++,
                StreamId: null,
                Venue: null));
        }

        return new QuotesSnapshotResponse(DateTimeOffset.UnixEpoch, quotes.Count, quotes);
    }

    private static Func<IServiceProvider, string?, QuotesSnapshotResponse?> Factory(Func<QuotesSnapshotResponse?> build)
        => (_, _) => build();

    [Fact]
    public async Task TrySubscribe_SeedsInitialSnapshot()
    {
        await using var broadcaster = new QuoteStreamBroadcaster(
            Services, new StreamConnectionRegistry(4), Options(), Factory(() => Snapshot("AAPL")));

        await using var sub = broadcaster.TrySubscribe(StreamTopic.Quotes("AAPL"), "s1");

        sub.Should().NotBeNull();
        sub!.Reader.TryRead(out var seeded).Should().BeTrue();
        seeded!.Quotes.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task TrySubscribe_ReturnsNull_WhenSessionCapExceeded()
    {
        await using var broadcaster = new QuoteStreamBroadcaster(
            Services, new StreamConnectionRegistry(1), Options(cap: 1), Factory(() => Snapshot()));

        await using var first = broadcaster.TrySubscribe(StreamTopic.AllQuotes, "s1");
        first.Should().NotBeNull();

        broadcaster.TrySubscribe(StreamTopic.AllQuotes, "s1").Should().BeNull();
    }

    [Fact]
    public async Task PublishPending_FansOutOneBuildToAllSubscribers_AndCoalesces()
    {
        var builds = 0;
        var current = Snapshot("AAPL");
        await using var broadcaster = new QuoteStreamBroadcaster(
            Services,
            new StreamConnectionRegistry(4),
            Options(),
            Factory(() =>
            {
                builds++;
                return current;
            }));

        await using var a = broadcaster.TrySubscribe(StreamTopic.Quotes("AAPL"), "sa");
        await using var b = broadcaster.TrySubscribe(StreamTopic.Quotes("AAPL"), "sb");
        a!.Reader.TryRead(out _); // drain initial seeds
        b!.Reader.TryRead(out _);
        var buildsAfterSeed = builds;

        current = Snapshot("AAPL", "MSFT");
        broadcaster.PublishPending();

        (builds - buildsAfterSeed).Should().Be(1); // one build shared across both subscribers
        a.Reader.TryRead(out var receivedByA).Should().BeTrue();
        b.Reader.TryRead(out var receivedByB).Should().BeTrue();
        receivedByA!.Quotes.Should().HaveCount(2);
        receivedByB!.Quotes.Should().HaveCount(2);

        // Unchanged rows coalesce — no further push.
        broadcaster.PublishPending();
        a.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_RemovesSubscriber_AndReleasesSlot_Idempotently()
    {
        var registry = new StreamConnectionRegistry(1);
        await using var broadcaster = new QuoteStreamBroadcaster(
            Services, registry, Options(cap: 1), Factory(() => Snapshot()));

        var sub = broadcaster.TrySubscribe(StreamTopic.AllQuotes, "s1");
        sub.Should().NotBeNull();
        broadcaster.TrySubscribe(StreamTopic.AllQuotes, "s1").Should().BeNull(); // capped

        await sub!.DisposeAsync();
        await sub.DisposeAsync(); // idempotent — must not double-release

        registry.ActiveCount("s1").Should().Be(0);
        broadcaster.TrySubscribe(StreamTopic.AllQuotes, "s1").Should().NotBeNull();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
