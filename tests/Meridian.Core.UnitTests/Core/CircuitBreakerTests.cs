using FluentAssertions;
using Meridian.Core.Resilience;

namespace Meridian.Tests.Core;

public sealed class CircuitBreakerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class ControllableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private static (CircuitBreaker Breaker, ControllableTimeProvider Clock) Create(
        int threshold = 3,
        double breakSeconds = 60,
        double probeIntervalSeconds = 0)
    {
        var clock = new ControllableTimeProvider(Start);
        var breaker = new CircuitBreaker(
            new CircuitBreakerOptions
            {
                FailureThreshold = threshold,
                BreakDuration = TimeSpan.FromSeconds(breakSeconds),
                HalfOpenProbeInterval = TimeSpan.FromSeconds(probeIntervalSeconds),
            },
            clock);
        return (breaker, clock);
    }

    [Fact]
    public void StartsClosedAndAllowsCalls()
    {
        var (breaker, _) = Create();

        breaker.Status.Should().Be(CircuitStatus.Closed);
        breaker.TryAcquire().Should().BeTrue();
        breaker.OpenUntil.Should().BeNull();
    }

    [Fact]
    public void TripsOpenAfterThresholdConsecutiveFailures()
    {
        var (breaker, _) = Create(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.Status.Should().Be(CircuitStatus.Closed);

        breaker.RecordFailure();

        breaker.Status.Should().Be(CircuitStatus.Open);
        breaker.TryAcquire().Should().BeFalse();
        breaker.TripCount.Should().Be(1);
        breaker.OpenUntil.Should().Be(Start + TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void SuccessResetsConsecutiveFailures()
    {
        var (breaker, _) = Create(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.Status.Should().Be(CircuitStatus.Closed);
        breaker.ConsecutiveFailures.Should().Be(2);
        breaker.TotalFailures.Should().Be(4);
    }

    [Fact]
    public void BecomesHalfOpenAfterBreakElapses()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        breaker.RecordFailure();

        clock.Advance(TimeSpan.FromSeconds(59));
        breaker.Status.Should().Be(CircuitStatus.Open);
        breaker.TryAcquire().Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        breaker.Status.Should().Be(CircuitStatus.HalfOpen);
        breaker.TryAcquire().Should().BeTrue("the first call after the break elapses is the probe");
    }

    [Fact]
    public void HalfOpenProbeSuccessClosesTheBreaker()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(60));

        breaker.TryAcquire().Should().BeTrue();
        breaker.RecordSuccess();

        breaker.Status.Should().Be(CircuitStatus.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.OpenUntil.Should().BeNull();
    }

    [Fact]
    public void HalfOpenProbeFailureReopensWithFreshBreakWindow()
    {
        var (breaker, clock) = Create(threshold: 3, breakSeconds: 60);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(60));

        breaker.TryAcquire().Should().BeTrue();
        breaker.RecordFailure();

        breaker.Status.Should().Be(CircuitStatus.Open);
        breaker.TripCount.Should().Be(2, "a failed probe re-trips the breaker");
        breaker.OpenUntil.Should().Be(Start + TimeSpan.FromSeconds(120));
        breaker.TryAcquire().Should().BeFalse();
    }

    [Fact]
    public void FailureWhileStillOpenDoesNotReTrip()
    {
        var (breaker, _) = Create(threshold: 1, breakSeconds: 60);
        breaker.RecordFailure();

        // A call that slipped past the gate fails while the break is still running.
        breaker.RecordFailure();

        breaker.TripCount.Should().Be(1);
        breaker.TotalFailures.Should().Be(2);
        breaker.Status.Should().Be(CircuitStatus.Open);
    }

    [Fact]
    public void HalfOpenProbesAreThrottledByProbeInterval()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60, probeIntervalSeconds: 5);
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(60));

        breaker.TryAcquire().Should().BeTrue("first probe is allowed immediately");
        breaker.TryAcquire().Should().BeFalse("second probe within the interval is throttled");

        clock.Advance(TimeSpan.FromSeconds(5));
        breaker.TryAcquire().Should().BeTrue("a probe is allowed once the interval elapses");
    }

    [Fact]
    public void HalfOpenWithoutThrottleAllowsEveryProbe()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(60));

        breaker.TryAcquire().Should().BeTrue();
        breaker.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void ResetClosesTheBreaker()
    {
        var (breaker, _) = Create(threshold: 1);
        breaker.RecordFailure();

        breaker.Reset();

        breaker.Status.Should().Be(CircuitStatus.Closed);
        breaker.ConsecutiveFailures.Should().Be(0);
        breaker.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public void StateChangedFiresOnEveryTransition()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        var transitions = new List<(CircuitStatus From, CircuitStatus To)>();
        breaker.StateChanged += (from, to) => transitions.Add((from, to));

        breaker.RecordFailure();                    // Closed -> Open
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryAcquire();                       // Open -> HalfOpen
        breaker.RecordFailure();                    // HalfOpen -> Open
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryAcquire();                       // Open -> HalfOpen
        breaker.RecordSuccess();                    // HalfOpen -> Closed

        transitions.Should().Equal(
            (CircuitStatus.Closed, CircuitStatus.Open),
            (CircuitStatus.Open, CircuitStatus.HalfOpen),
            (CircuitStatus.HalfOpen, CircuitStatus.Open),
            (CircuitStatus.Open, CircuitStatus.HalfOpen),
            (CircuitStatus.HalfOpen, CircuitStatus.Closed));
    }

    [Fact]
    public void RecordFailureAfterElapsedBreakReTripsEvenWithoutTryAcquire()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        var transitions = new List<(CircuitStatus From, CircuitStatus To)>();
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(90));
        breaker.StateChanged += (from, to) => transitions.Add((from, to));

        // The caller gated on Status (non-mutating) rather than TryAcquire, probed, and failed.
        breaker.RecordFailure();

        breaker.TripCount.Should().Be(2);
        breaker.OpenUntil.Should().Be(Start + TimeSpan.FromSeconds(90 + 60));
        transitions.Should().Equal(
            (CircuitStatus.Open, CircuitStatus.HalfOpen),
            (CircuitStatus.HalfOpen, CircuitStatus.Open));
    }

    [Fact]
    public void RecordSuccessAfterElapsedBreakFiresHalfOpenThenClosed()
    {
        var (breaker, clock) = Create(threshold: 1, breakSeconds: 60);
        var transitions = new List<(CircuitStatus From, CircuitStatus To)>();
        breaker.RecordFailure();
        clock.Advance(TimeSpan.FromSeconds(90));
        breaker.StateChanged += (from, to) => transitions.Add((from, to));

        // The caller gated on Status (non-mutating) rather than TryAcquire, probed, and succeeded.
        breaker.RecordSuccess();

        breaker.Status.Should().Be(CircuitStatus.Closed);
        transitions.Should().Equal(
            (CircuitStatus.Open, CircuitStatus.HalfOpen),
            (CircuitStatus.HalfOpen, CircuitStatus.Closed));
    }

    [Fact]
    public void RejectsNonPositiveThreshold()
    {
        var act = () => new CircuitBreaker(new CircuitBreakerOptions { FailureThreshold = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
