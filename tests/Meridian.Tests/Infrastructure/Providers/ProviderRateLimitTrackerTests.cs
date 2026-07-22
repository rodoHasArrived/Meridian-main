using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderRateLimitTrackerTests
{
    [Fact]
    public void ExplicitRateLimit_UsesInjectedClockAndExpiresAsOneCoherentSnapshot()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        using var tracker = new ProviderRateLimitTracker(timeProvider: clock);
        tracker.RegisterProvider("test", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        tracker.RecordRateLimitHit("test", TimeSpan.FromSeconds(30));
        var limited = tracker.GetStatus("test")!;

        limited.ObservedAt.Should().Be(clock.GetUtcNow());
        limited.IsRateLimited.Should().BeTrue();
        limited.ResetAt.Should().Be(clock.GetUtcNow().AddSeconds(30));
        limited.TimeUntilReset.Should().Be(TimeSpan.FromSeconds(30));
        limited.Reason.Should().Be("provider-response");

        clock.Advance(TimeSpan.FromSeconds(31));
        var expired = tracker.GetStatus("test")!;

        expired.ObservedAt.Should().Be(clock.GetUtcNow());
        expired.IsRateLimited.Should().BeFalse();
        expired.ResetAt.Should().BeNull();
        expired.TimeUntilReset.Should().BeNull();
        expired.Reason.Should().BeNull();
    }

    [Fact]
    public void RegisterProvider_ReplacesConfigurationWithoutLeakingStaleState()
    {
        using var tracker = new ProviderRateLimitTracker();
        tracker.RegisterProvider("test", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);
        tracker.RecordRateLimitHit("test", TimeSpan.FromMinutes(5));

        tracker.RegisterProvider("TEST", 25, TimeSpan.FromHours(1), TimeSpan.Zero);
        var status = tracker.GetStatus("test")!;

        status.MaxRequestsPerWindow.Should().Be(25);
        status.Window.Should().Be(TimeSpan.FromHours(1));
        status.IsRateLimited.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentMutationAndReads_ReturnInternallyConsistentSnapshots()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        using var tracker = new ProviderRateLimitTracker(timeProvider: clock);
        tracker.RegisterProvider("test", 100_000, TimeSpan.FromHours(1), TimeSpan.Zero);

        var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                tracker.RecordRequest("test");
                if ((i + writer) % 11 == 0)
                    tracker.RecordRateLimitHit("test", TimeSpan.FromMinutes(1));
                else if ((i + writer) % 13 == 0)
                    tracker.ClearRateLimitState("test");
            }
        }));

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                var status = tracker.GetStatus("test")!;
                status.UsageRatio.Should().BeApproximately(
                    (double)status.RequestsInWindow / status.MaxRequestsPerWindow,
                    0.0000001);
                if (status.IsRateLimited && status.RequestsInWindow < status.MaxRequestsPerWindow)
                {
                    status.Reason.Should().Be("provider-response");
                    status.ResetAt.Should().NotBeNull();
                    status.TimeUntilReset.Should().Be(status.ResetAt - status.ObservedAt);
                }
            }
        }));

        await Task.WhenAll(writers.Concat(readers));
    }

    [Fact]
    public void Dispose_PreventsRegistrationAndReads()
    {
        var tracker = new ProviderRateLimitTracker();
        tracker.RegisterProvider("test", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        tracker.Dispose();

        tracker.Invoking(value => value.GetStatus("test"))
            .Should().Throw<ObjectDisposedException>();
        tracker.Invoking(value => value.RegisterProvider("test", 5, TimeSpan.FromMinutes(1), TimeSpan.Zero))
            .Should().Throw<ObjectDisposedException>();
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
