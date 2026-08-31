using FluentAssertions;
using Meridian.DataIntegration.Monitoring;
using Xunit;

namespace Meridian.Tests.DataIntegration.Monitoring;

public sealed class TimestampMonotonicityCheckerTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublicConstructor_PreservesSingleConfigurationParameter()
    {
        typeof(TimestampMonotonicityChecker)
            .GetConstructor([typeof(TimestampMonotonicityConfig)])
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void CheckTimestamp_LateEventsPreserveHighestWatermark()
    {
        using var checker = CreateChecker(alertCooldownMs: 0);
        var violations = new List<MonotonicityViolation>();
        checker.OnViolation += violations.Add;
        var watermark = ObservedAt.AddMilliseconds(100);

        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-10)).Should().BeTrue();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-5)).Should().BeTrue();

        checker.TotalViolations.Should().Be(2);
        violations.Should().HaveCount(2);
        violations.Should().OnlyContain(violation => violation.PreviousTimestamp == watermark);
        violations.Should().OnlyContain(violation => violation.DetectedAt == ObservedAt);

        var stats = checker.GetStats().SymbolStats.Should().ContainSingle().Subject;
        stats.LastEventTimestamp.Should().Be(watermark);
        stats.LastViolationTime.Should().Be(ObservedAt);
        stats.TotalEvents.Should().Be(3);
        stats.TotalViolations.Should().Be(2);
    }

    [Fact]
    public void CheckTimestamp_ConcurrentLateArrivalsRemainViolationsAgainstHighestWatermark()
    {
        using var checker = CreateChecker(alertCooldownMs: int.MaxValue);
        var watermark = ObservedAt.AddMilliseconds(100);
        var lateTimestamp = watermark.AddMilliseconds(-1);
        const int arrivalCount = 512;
        var detectedCount = 0;

        checker.CheckTimestamp("MSFT", "quote", watermark).Should().BeFalse();

        Parallel.For(0, arrivalCount, _ =>
        {
            if (checker.CheckTimestamp("MSFT", "quote", lateTimestamp))
            {
                Interlocked.Increment(ref detectedCount);
            }
        });

        detectedCount.Should().Be(arrivalCount);
        checker.TotalViolations.Should().Be(arrivalCount);

        var stats = checker.GetStats().SymbolStats.Should().ContainSingle().Subject;
        stats.LastEventTimestamp.Should().Be(watermark);
        stats.TotalEvents.Should().Be(arrivalCount + 1);
        stats.TotalViolations.Should().Be(arrivalCount);
    }

    [Fact]
    public void GetStats_ColonBearingSymbolAndEventTypeRemainDistinct()
    {
        using var checker = CreateChecker(alertCooldownMs: int.MaxValue);
        var watermark = ObservedAt.AddMilliseconds(100);

        checker.CheckTimestamp("FX:EURUSD", "trade", watermark).Should().BeFalse();
        checker.CheckTimestamp("FX:EURUSD", "trade", watermark.AddMilliseconds(-1)).Should().BeTrue();
        checker.CheckTimestamp("FX", "EURUSD:trade", watermark).Should().BeFalse();
        checker.CheckTimestamp("FX", "EURUSD:trade", watermark.AddMilliseconds(-1)).Should().BeTrue();

        var stats = checker.GetStats().SymbolStats;
        stats.Should().HaveCount(2);
        stats.Should().ContainSingle(stat => stat.Symbol == "FX:EURUSD" && stat.EventType == "trade");
        stats.Should().ContainSingle(stat => stat.Symbol == "FX" && stat.EventType == "EURUSD:trade");
        checker.GetSymbolsWithViolations().Should().BeEquivalentTo("FX:EURUSD", "FX");
    }

    [Fact]
    public void CheckTimestamp_CanonicalizesSymbolAndEventTypeIdentity()
    {
        using var checker = CreateChecker(alertCooldownMs: 0);
        var watermark = ObservedAt.AddMilliseconds(100);

        checker.CheckTimestamp(" aapl ", " Trade ", watermark).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)).Should().BeTrue();

        checker.TotalEventsChecked.Should().Be(2);
        checker.TotalViolations.Should().Be(1);
        var stats = checker.GetStats().SymbolStats.Should().ContainSingle().Subject;
        stats.Symbol.Should().BeEquivalentTo("AAPL");
        stats.EventType.Should().BeEquivalentTo("trade");
        stats.TotalEvents.Should().Be(2);
    }

    [Fact]
    public void CheckTimestamp_NonViolationResetsConsecutiveViolationStreak()
    {
        using var checker = new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 100,
                AlertCooldownMs = 0,
                DetectTimeGaps = false
            },
            new FixedTimeProvider(ObservedAt));
        var violations = new List<MonotonicityViolation>();
        checker.OnViolation += violations.Add;
        var watermark = ObservedAt.AddSeconds(1);

        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-200)).Should().BeTrue();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-50)).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-200)).Should().BeTrue();

        violations.Select(violation => violation.ConsecutiveViolations)
            .Should()
            .Equal(1, 1);
    }

    [Fact]
    public async Task ResetStats_AtomicallySeparatesInFlightObservationsFromNewGeneration()
    {
        using var observationCommitted = new ManualResetEventSlim();
        using var releaseObservation = new ManualResetEventSlim();
        var hooks = new TimestampMonotonicityCheckerTestHooks();
        using var checker = new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 0,
                AlertCooldownMs = 0,
                DetectTimeGaps = false
            },
            new FixedTimeProvider(ObservedAt),
            hooks);
        var watermark = ObservedAt.AddMilliseconds(100);
        var callbackCount = 0;
        checker.OnViolation += _ => Interlocked.Increment(ref callbackCount);
        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();
        hooks.ObservationCommittedBeforePublish = () =>
        {
            observationCommitted.Set();
            WaitOrThrow(releaseObservation, "release timestamp observation");
        };

        var checkTask = Task.Run(() =>
            checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)));
        WaitOrThrow(observationCommitted, "commit timestamp observation");

        checker.ResetStats();
        releaseObservation.Set();
        (await checkTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        checker.TotalEventsChecked.Should().Be(0);
        checker.TotalViolations.Should().Be(0);
        checker.TotalGaps.Should().Be(0);
        var stats = checker.GetStats();
        stats.SymbolStats.Should().BeEmpty();
        stats.RetainedStateEvents.Should().Be(0);
        stats.RetainedStateViolations.Should().Be(0);
        stats.RetainedStateGaps.Should().Be(0);
        callbackCount.Should().Be(0, "the reset is a publication barrier for the retired generation");
    }

    [Fact]
    public async Task ResetStats_WaitsForCallbackAlreadyInProgress()
    {
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var resetStarted = new ManualResetEventSlim();
        using var checker = CreateChecker(alertCooldownMs: 0);
        checker.OnViolation += _ =>
        {
            callbackEntered.Set();
            WaitOrThrow(releaseCallback, "release timestamp callback before reset");
        };
        var watermark = ObservedAt.AddMilliseconds(100);
        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();
        var checkTask = Task.Run(() =>
            checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)));
        WaitOrThrow(callbackEntered, "enter timestamp callback before reset");

        var resetTask = Task.Run(() =>
        {
            resetStarted.Set();
            checker.ResetStats();
        });
        WaitOrThrow(resetStarted, "start timestamp reset");
        resetTask.IsCompleted.Should().BeFalse();

        releaseCallback.Set();
        (await checkTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await resetTask.WaitAsync(TimeSpan.FromSeconds(5));

        checker.TotalEventsChecked.Should().Be(0);
        checker.TotalViolations.Should().Be(0);
        checker.GetStats().SymbolStats.Should().BeEmpty();
    }

    [Fact]
    public async Task RunCleanup_DoesNotRemoveReplacementTimestampState()
    {
        var timeProvider = new MutableTimeProvider(ObservedAt);
        using var stateRetired = new ManualResetEventSlim();
        using var releaseState = new ManualResetEventSlim();
        var hooks = new TimestampMonotonicityCheckerTestHooks
        {
            StateRetiredBeforeRemoval = () =>
            {
                stateRetired.Set();
                WaitOrThrow(releaseState, "release retired timestamp state");
            }
        };
        using var checker = new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 0,
                AlertCooldownMs = 0,
                DetectTimeGaps = false
            },
            timeProvider,
            hooks);
        checker.CheckTimestamp("AAPL", "trade", ObservedAt).Should().BeFalse();
        timeProvider.Advance(TimeSpan.FromHours(25));

        var cleanupTask = Task.Run(checker.RunCleanup);
        WaitOrThrow(stateRetired, "retire timestamp state");

        var replacementWatermark = ObservedAt.AddHours(1);
        checker.CheckTimestamp("aapl", "TRADE", replacementWatermark).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", replacementWatermark.AddMilliseconds(-1))
            .Should()
            .BeTrue();
        releaseState.Set();
        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

        var stats = checker.GetStats();
        stats.TotalEventsChecked.Should().Be(3);
        stats.TotalViolations.Should().Be(1);
        stats.RetainedStateEvents.Should().Be(2);
        stats.RetainedStateViolations.Should().Be(1);
        stats.SymbolStats.Should().ContainSingle().Which.TotalEvents.Should().Be(2);
    }

    [Fact]
    public void GetStats_SeparatesLifetimeCountersFromRetainedStateMetrics()
    {
        var timeProvider = new MutableTimeProvider(ObservedAt);
        using var checker = new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 0,
                AlertCooldownMs = 0,
                DetectTimeGaps = false
            },
            timeProvider);
        var watermark = ObservedAt.AddMilliseconds(100);
        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)).Should().BeTrue();
        timeProvider.Advance(TimeSpan.FromHours(25));

        checker.RunCleanup();

        var stats = checker.GetStats();
        stats.TotalEventsChecked.Should().Be(2);
        stats.TotalViolations.Should().Be(1);
        stats.SymbolStats.Should().BeEmpty();
        stats.RetainedStateEvents.Should().Be(0);
        stats.RetainedStateViolations.Should().Be(0);
        stats.RetainedStateGaps.Should().Be(0);
    }

    [Fact]
    public void Constructor_RejectsInvalidConfigurationRanges()
    {
        var invalidConfigurations = new[]
        {
            new TimestampMonotonicityConfig { ToleranceMs = -1 },
            new TimestampMonotonicityConfig { AlertCooldownMs = -1 },
            new TimestampMonotonicityConfig { TimeGapThresholdSeconds = -1 },
            new TimestampMonotonicityConfig { GapAlertCooldownMs = -1 }
        };

        foreach (var config in invalidConfigurations)
        {
            Action construct = () => _ = new TimestampMonotonicityChecker(config);
            construct.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void CheckTimestamp_ExtremeGapThresholdDoesNotOverflow()
    {
        using var checker = new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 0,
                DetectTimeGaps = true,
                TimeGapThresholdSeconds = int.MaxValue,
                GapAlertCooldownMs = 0
            },
            new FixedTimeProvider(ObservedAt));

        checker.CheckTimestamp("AAPL", "trade", ObservedAt).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", ObservedAt.AddDays(1)).Should().BeFalse();
        checker.TotalGaps.Should().Be(0);
    }

    [Fact]
    public void CheckTimestamp_DateTimeOffsetMinValueIsAValidFirstWatermark()
    {
        using var checker = CreateChecker(alertCooldownMs: 0);

        checker.CheckTimestamp("AAPL", "trade", DateTimeOffset.MinValue).Should().BeFalse();
        checker.CheckTimestamp("AAPL", "trade", DateTimeOffset.MinValue.AddMilliseconds(1))
            .Should()
            .BeFalse();

        checker.TotalEventsChecked.Should().Be(2);
        checker.GetStats().RetainedStateEvents.Should().Be(2);
    }

    [Fact]
    public async Task Dispose_WaitsForActiveCleanupAndRejectsPostDisposeMutations()
    {
        var timeProvider = new MutableTimeProvider(ObservedAt);
        using var stateRetired = new ManualResetEventSlim();
        using var releaseCleanup = new ManualResetEventSlim();
        using var disposeRequested = new ManualResetEventSlim();
        var hooks = new TimestampMonotonicityCheckerTestHooks
        {
            StateRetiredBeforeRemoval = () =>
            {
                stateRetired.Set();
                WaitOrThrow(releaseCleanup, "release timestamp cleanup");
            },
            DisposeRequested = disposeRequested.Set
        };
        var checker = new TimestampMonotonicityChecker(null, timeProvider, hooks);
        checker.CheckTimestamp("AAPL", "trade", ObservedAt).Should().BeFalse();
        timeProvider.Advance(TimeSpan.FromHours(25));
        var cleanupTask = Task.Run(checker.RunCleanup);
        WaitOrThrow(stateRetired, "retire timestamp state before disposal");

        var disposeTask = Task.Run(checker.Dispose);
        WaitOrThrow(disposeRequested, "request timestamp checker disposal");
        disposeTask.IsCompleted.Should().BeFalse();

        releaseCleanup.Set();
        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        var totalEvents = checker.TotalEventsChecked;
        checker.CheckTimestamp("AAPL", "trade", ObservedAt.AddSeconds(1)).Should().BeFalse();
        checker.ResetStats();
        checker.TotalEventsChecked.Should().Be(totalEvents);
    }

    [Fact]
    public async Task Dispose_FromViolationCallback_DoesNotDeadlock()
    {
        var checker = CreateChecker(alertCooldownMs: 0);
        checker.OnViolation += _ => checker.Dispose();
        var watermark = ObservedAt.AddMilliseconds(100);
        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();

        var detected = await Task.Run(() =>
                checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)))
            .WaitAsync(TimeSpan.FromSeconds(5));

        detected.Should().BeTrue();
        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-2)).Should().BeFalse();
        checker.Dispose();
    }

    [Fact]
    public async Task ResetStats_FromViolationCallback_DoesNotDeadlockAndStartsNewGeneration()
    {
        using var checker = CreateChecker(alertCooldownMs: 0);
        var callbackCount = 0;
        var staleFollowerCallbackCount = 0;
        checker.OnViolation += _ =>
        {
            Interlocked.Increment(ref callbackCount);
            checker.ResetStats();
        };
        checker.OnViolation += _ => Interlocked.Increment(ref staleFollowerCallbackCount);
        var watermark = ObservedAt.AddMilliseconds(100);
        checker.CheckTimestamp("AAPL", "trade", watermark).Should().BeFalse();

        var detected = await Task.Run(() =>
                checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(-1)))
            .WaitAsync(TimeSpan.FromSeconds(5));

        detected.Should().BeTrue();
        callbackCount.Should().Be(1);
        staleFollowerCallbackCount.Should().Be(0,
            "a reentrant reset retires the alert before later subscribers are dispatched");
        checker.TotalEventsChecked.Should().Be(0);
        checker.TotalViolations.Should().Be(0);
        checker.GetStats().SymbolStats.Should().BeEmpty();

        checker.CheckTimestamp("AAPL", "trade", watermark.AddMilliseconds(1)).Should().BeFalse();
        checker.TotalEventsChecked.Should().Be(1);
    }

    private static TimestampMonotonicityChecker CreateChecker(int alertCooldownMs)
    {
        return new TimestampMonotonicityChecker(
            new TimestampMonotonicityConfig
            {
                ToleranceMs = 0,
                AlertCooldownMs = alertCooldownMs,
                DetectTimeGaps = false
            },
            new FixedTimeProvider(ObservedAt));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static void WaitOrThrow(ManualResetEventSlim signal, string operation)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Timed out waiting to {operation}.");
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public void Advance(TimeSpan duration)
        {
            lock (_sync)
            {
                _utcNow += duration;
            }
        }
    }
}
