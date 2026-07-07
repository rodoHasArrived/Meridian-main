using System;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Shared.Streaming;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Direct coverage of the generic fan-out engine, especially the empty-topic eviction path that the
/// quote broadcaster (which retains empty topics) does not exercise.
/// </summary>
public sealed class StreamBroadcasterTests
{
    private static StreamBroadcaster<string> Create(
        Func<StreamTopic, string?> build,
        bool evictEmptyTopics = false,
        int cap = 4)
        => new(
            new StreamConnectionRegistry(cap),
            build,
            StringComparer.Ordinal,
            coalesceIntervalMs: 0,
            subscriberChannelCapacity: 1,
            evictEmptyTopics: evictEmptyTopics);

    [Fact]
    public async Task TrySubscribe_SeedsCurrentPayload()
    {
        await using var engine = Create(_ => "seed");
        await using var sub = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");

        sub.Should().NotBeNull();
        sub!.Reader.TryRead(out var seeded).Should().BeTrue();
        seeded.Should().Be("seed");
    }

    [Fact]
    public async Task PublishPending_SharesOneBuildAndCoalescesUnchanged()
    {
        var builds = 0;
        var current = "v1";
        await using var engine = Create(_ =>
        {
            builds++;
            return current;
        });

        await using var a = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "sa");
        await using var b = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "sb");
        a!.Reader.TryRead(out _); // drain seeds
        b!.Reader.TryRead(out _);
        var afterSeed = builds;

        current = "v2";
        engine.PublishPending();

        (builds - afterSeed).Should().Be(1); // one build shared across both subscribers
        a.Reader.TryRead(out var receivedByA).Should().BeTrue();
        b.Reader.TryRead(out var receivedByB).Should().BeTrue();
        receivedByA.Should().Be("v2");
        receivedByB.Should().Be("v2");

        engine.PublishPending(); // unchanged => coalesced, no further push
        a.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task TrySubscribe_ReturnsNull_WhenSessionCapExceeded()
    {
        await using var engine = Create(_ => "x", cap: 1);
        await using var first = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");

        first.Should().NotBeNull();
        engine.TrySubscribe(StreamTopic.ReportRun("r2"), "s1").Should().BeNull();
    }

    [Fact]
    public async Task Evict_RemovesEmptyTopicOnLastUnsubscribe_AndReleasesSlot()
    {
        var registry = new StreamConnectionRegistry(1);
        await using var engine = new StreamBroadcaster<string>(
            registry, _ => "x", StringComparer.Ordinal, 0, 1, evictEmptyTopics: true);

        var sub = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");
        sub.Should().NotBeNull();
        engine.ActiveTopicCount.Should().Be(1);

        await sub!.DisposeAsync();
        await sub.DisposeAsync(); // idempotent — must not double-release or double-evict

        engine.ActiveTopicCount.Should().Be(0); // empty topic evicted
        registry.ActiveCount("s1").Should().Be(0); // slot released exactly once
        // Eviction + release let the same session re-subscribe (a fresh topic state is created).
        var again = engine.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");
        again.Should().NotBeNull();
        engine.ActiveTopicCount.Should().Be(1);
        await again!.DisposeAsync();
    }

    [Fact]
    public async Task Retain_KeepsEmptyTopic_WhenEvictionOff()
    {
        var registry = new StreamConnectionRegistry(1);
        await using var engine = new StreamBroadcaster<string>(
            registry, _ => "x", StringComparer.Ordinal, 0, 1, evictEmptyTopics: false);

        var sub = engine.TrySubscribe(StreamTopic.AllQuotes, "s1");
        await sub!.DisposeAsync();

        engine.ActiveTopicCount.Should().Be(1); // empty topic retained (quote semantics)
        registry.ActiveCount("s1").Should().Be(0);
        engine.TrySubscribe(StreamTopic.AllQuotes, "s1").Should().NotBeNull();
    }
}
