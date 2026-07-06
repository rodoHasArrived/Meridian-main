using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Streaming;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ReportRunStreamBroadcasterTests
{
    private static readonly IServiceProvider Services = new EmptyServiceProvider();

    private static QuoteStreamOptions Options(int cap = 4) => new()
    {
        CoalesceIntervalMs = 0,
        MaxConcurrentStreamsPerSession = cap,
        SubscriberChannelCapacity = 1
    };

    private static ReportingRunAuditTrailDto Run(string runId, string status, params string[] actions)
    {
        var entries = actions
            .Select(action => new ReportingRunAuditEntryDto(runId, DateTimeOffset.UnixEpoch, action, "actor", "notes"))
            .ToArray();
        return new ReportingRunAuditTrailDto(runId, "tmpl", "2026-05-01", status, "AdHoc", 1, entries);
    }

    [Fact]
    public async Task TrySubscribe_SeedsCurrentRunStatus()
    {
        var current = Run("r1", "Draft", "RunGenerated");
        await using var broadcaster = new ReportRunStreamBroadcaster(
            Services, new StreamConnectionRegistry(4), Options(), (_, _) => current);

        await using var sub = broadcaster.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");

        sub.Should().NotBeNull();
        sub!.Reader.TryRead(out var seeded).Should().BeTrue();
        seeded!.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task PublishPending_PushesOnApprovalTransition_CoalescesUnchanged()
    {
        var current = Run("r1", "Draft", "RunGenerated");
        await using var broadcaster = new ReportRunStreamBroadcaster(
            Services, new StreamConnectionRegistry(4), Options(), (_, _) => current);
        await using var sub = broadcaster.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");
        sub!.Reader.TryRead(out _); // drain seed (seed does not set the coalesce baseline)

        broadcaster.PublishPending(); // first publish sets the baseline -> one push
        sub.Reader.TryRead(out _).Should().BeTrue();

        broadcaster.PublishPending(); // unchanged run -> coalesced, no push
        sub.Reader.TryRead(out _).Should().BeFalse();

        // An approval transition changes Status and appends an audit entry -> push.
        current = Run("r1", "InReview", "RunGenerated", "ApprovalTransition");
        broadcaster.PublishPending();
        sub.Reader.TryRead(out var updated).Should().BeTrue();
        updated!.Status.Should().Be("InReview");
    }

    [Fact]
    public async Task TrySubscribe_ReturnsNull_WhenSessionCapExceeded()
    {
        await using var broadcaster = new ReportRunStreamBroadcaster(
            Services, new StreamConnectionRegistry(1), Options(cap: 1), (_, _) => Run("r1", "Draft"));

        await using var first = broadcaster.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");
        first.Should().NotBeNull();

        broadcaster.TrySubscribe(StreamTopic.ReportRun("r2"), "s1").Should().BeNull();
    }

    [Fact]
    public async Task EmptyReportRunTopic_IsEvictedOnLastUnsubscribe()
    {
        await using var broadcaster = new ReportRunStreamBroadcaster(
            Services, new StreamConnectionRegistry(4), Options(), (_, _) => Run("r1", "Draft"));

        var sub = broadcaster.TrySubscribe(StreamTopic.ReportRun("r1"), "s1");
        sub.Should().NotBeNull();
        broadcaster.ActiveTopicCount.Should().Be(1);

        await sub!.DisposeAsync();

        // Run-id topics are an unbounded universe, so an empty one must not linger (leak guard).
        broadcaster.ActiveTopicCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyRunChanged_WithNoSubscribers_DoesNotThrow()
    {
        await using var broadcaster = new ReportRunStreamBroadcaster(
            Services, new StreamConnectionRegistry(4), Options(), (_, _) => Run("r1", "Draft"));

        var act = () => broadcaster.NotifyRunChanged("r1");

        act.Should().NotThrow();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
