using FluentAssertions;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.DataIntegration.Services.DataQuality;

/// <summary>
/// Tests for SequenceErrorTracker detection and reporting.
/// </summary>
public sealed class SequenceErrorTrackerTests : IDisposable
{
    private readonly SequenceErrorTracker _sut;

    public SequenceErrorTrackerTests()
    {
        _sut = new SequenceErrorTracker(new SequenceErrorConfig
        {
            GapThreshold = 1,
            ResetThreshold = 10000,
            MaxErrorsPerSymbol = 500,
            SignificantGapSize = 100
        });
    }

    public void Dispose() => _sut.Dispose();

    #region CheckSequence - No Errors

    [Fact]
    public void CheckSequence_FirstEvent_NoError()
    {
        var error = _sut.CheckSequence("SPY", "trade", 1, DateTimeOffset.UtcNow);

        error.Should().BeNull();
    }

    [Fact]
    public void CheckSequence_ConsecutiveSequences_NoError()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        var error = _sut.CheckSequence("SPY", "trade", 2, baseTime.AddSeconds(1));

        error.Should().BeNull();
    }

    [Fact]
    public void CheckSequence_IncrementsEventCount()
    {
        _sut.CheckSequence("SPY", "trade", 1, DateTimeOffset.UtcNow);
        _sut.CheckSequence("SPY", "trade", 2, DateTimeOffset.UtcNow.AddSeconds(1));

        _sut.TotalEventsChecked.Should().Be(2);
    }

    #endregion

    #region CheckSequence - Gap Detection

    [Fact]
    public void CheckSequence_Gap_ReturnsGapError()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        var error = _sut.CheckSequence("SPY", "trade", 5, baseTime.AddSeconds(1));

        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.Gap);
        error.Symbol.Should().Be("SPY");
        error.GapSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CheckSequence_Gap_IncrementsGapCounter()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1));

        _sut.TotalGapErrors.Should().Be(1);
    }

    #endregion

    #region CheckSequence - Out of Order

    [Fact]
    public void CheckSequence_OutOfOrder_ReturnsOutOfOrderError()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 5, baseTime);
        var error = _sut.CheckSequence("SPY", "trade", 3, baseTime.AddSeconds(1));

        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.OutOfOrder);
    }

    #endregion

    #region CheckSequence - Duplicate

    [Fact]
    public void CheckSequence_Duplicate_ReturnsDuplicateError()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 5, baseTime);
        var error = _sut.CheckSequence("SPY", "trade", 5, baseTime.AddSeconds(1));

        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.Duplicate);
    }

    [Fact]
    public void CheckSequence_Duplicate_IncrementsDuplicateCounter()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 5, baseTime);
        _sut.CheckSequence("SPY", "trade", 5, baseTime.AddSeconds(1));

        _sut.TotalDuplicateErrors.Should().Be(1);
    }

    #endregion

    #region CheckSequence - Independent Tracking

    [Fact]
    public void CheckSequence_DifferentSymbols_TrackedIndependently()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("AAPL", "trade", 1, baseTime);

        // Both should have their own sequence state
        var spyError = _sut.CheckSequence("SPY", "trade", 2, baseTime.AddSeconds(1));
        var aaplError = _sut.CheckSequence("AAPL", "trade", 2, baseTime.AddSeconds(1));

        spyError.Should().BeNull();
        aaplError.Should().BeNull();
    }

    [Fact]
    public void CheckSequence_DifferentEventTypes_TrackedIndependently()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "quote", 1, baseTime);

        var tradeError = _sut.CheckSequence("SPY", "trade", 2, baseTime.AddSeconds(1));
        var quoteError = _sut.CheckSequence("SPY", "quote", 5, baseTime.AddSeconds(1)); // Gap in quotes

        tradeError.Should().BeNull();
        quoteError.Should().NotBeNull();
    }

    #endregion

    #region Event Notification

    [Fact]
    public void OnSequenceError_FiredWhenErrorDetected()
    {
        SequenceError? notified = null;
        _sut.OnSequenceError += e => notified = e;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1)); // Gap

        notified.Should().NotBeNull();
        notified!.Symbol.Should().Be("SPY");
    }

    [Fact]
    public void OnSequenceError_NotFiredForValidSequences()
    {
        SequenceError? notified = null;
        _sut.OnSequenceError += e => notified = e;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 2, baseTime.AddSeconds(1));

        notified.Should().BeNull();
    }

    #endregion

    #region GetSummary

    [Fact]
    public void GetSummary_NoErrors_ReturnsZeroCounts()
    {
        _sut.CheckSequence("SPY", "trade", 1, DateTimeOffset.UtcNow);

        var summary = _sut.GetSummary("SPY");

        summary.TotalErrors.Should().Be(0);
        summary.GapErrors.Should().Be(0);
        summary.OutOfOrderErrors.Should().Be(0);
        summary.DuplicateErrors.Should().Be(0);
        summary.ResetErrors.Should().Be(0);
    }

    [Fact]
    public void GetSummary_WithErrors_ReturnsCorrectCounts()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1)); // Gap
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(2)); // Duplicate

        var summary = _sut.GetSummary("SPY");

        summary.TotalErrors.Should().BeGreaterThanOrEqualTo(2);
        summary.Symbol.Should().Be("SPY");
    }

    #endregion

    #region GetStatistics

    [Fact]
    public void GetStatistics_ReturnsGlobalCounts()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 5, baseTime.AddSeconds(1));

        var stats = _sut.GetStatistics();

        stats.TotalEventsChecked.Should().Be(2);
        stats.TotalErrors.Should().BeGreaterThanOrEqualTo(1);
        stats.ErrorsByType.Should().ContainKey(SequenceErrorType.Gap);
        stats.CalculatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region GetRecentErrors

    [Fact]
    public void GetRecentErrors_NoErrors_ReturnsEmptyList()
    {
        var errors = _sut.GetRecentErrors();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentErrors_WithErrors_ReturnsOrderedByTimestamp()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1));
        _sut.CheckSequence("AAPL", "trade", 1, baseTime.AddSeconds(2));
        _sut.CheckSequence("AAPL", "trade", 20, baseTime.AddSeconds(3));

        var errors = _sut.GetRecentErrors(10);

        errors.Should().HaveCountGreaterThanOrEqualTo(2);
        errors.Should().BeInDescendingOrder(e => e.Timestamp);
    }

    #endregion

    #region GetSymbolsWithMostErrors

    [Fact]
    public void GetSymbolsWithMostErrors_ReturnsOrderedByCount()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Create more errors for SPY than AAPL
        _sut.CheckSequence("SPY", "trade", 1, baseTime);
        _sut.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1));
        _sut.CheckSequence("SPY", "trade", 20, baseTime.AddSeconds(2));

        _sut.CheckSequence("AAPL", "trade", 1, baseTime.AddSeconds(3));
        _sut.CheckSequence("AAPL", "trade", 5, baseTime.AddSeconds(4));

        var topSymbols = _sut.GetSymbolsWithMostErrors(10);

        topSymbols.Should().NotBeEmpty();
        topSymbols[0].ErrorCount.Should().BeGreaterThanOrEqualTo(topSymbols.Last().ErrorCount);
    }

    #endregion

    #region ResetSymbolState

    [Fact]
    public void ResetSymbolState_AfterReset_NewSequenceAccepted()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("SPY", "trade", 100, baseTime);
        _sut.ResetSymbolState("SPY");

        // After reset, sequence 1 should not be an error
        var error = _sut.CheckSequence("SPY", "trade", 1, baseTime.AddSeconds(1));

        error.Should().BeNull();
    }

    [Fact]
    public void ResetSymbolState_DoesNotResetPrefixMatchingSymbol()
    {
        var baseTime = DateTimeOffset.UtcNow;
        _sut.CheckSequence("AAPL", "trade", 100, baseTime);
        _sut.CheckSequence("AAPL2", "trade", 100, baseTime);

        _sut.ResetSymbolState("AAPL");

        _sut.CheckSequence("AAPL", "trade", 1, baseTime.AddSeconds(1)).Should().BeNull();
        var prefixError = _sut.CheckSequence("AAPL2", "trade", 1, baseTime.AddSeconds(1));
        prefixError.Should().NotBeNull();
        prefixError!.ErrorType.Should().Be(SequenceErrorType.OutOfOrder);
    }

    [Fact]
    public void CheckSequence_DelimiterBearingIdentifiersUseDistinctStreamState()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence("A:B", "trade", 100, baseTime).Should().BeNull();
        _sut.CheckSequence("A", "B:trade", 1, baseTime).Should().BeNull();

        _sut.CheckSequence("A:B", "trade", 101, baseTime.AddSeconds(1)).Should().BeNull();
        _sut.CheckSequence("A", "B:trade", 2, baseTime.AddSeconds(1)).Should().BeNull();

        _sut.CheckSequence("MSFT", "quote:depth", 50, baseTime, "primary").Should().BeNull();
        _sut.CheckSequence("MSFT", "quote", 5, baseTime, "depth:primary").Should().BeNull();

        _sut.CheckSequence("MSFT", "quote:depth", 51, baseTime.AddSeconds(1), "primary").Should().BeNull();
        _sut.CheckSequence("MSFT", "quote", 6, baseTime.AddSeconds(1), "depth:primary").Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentCheckResetAndRead_UsesConsistentPerStreamStateAndSnapshots()
    {
        const int writerCount = 8;
        const int eventsPerWriter = 500;
        var baseTime = DateTimeOffset.UtcNow;
        var writers = Enumerable.Range(0, writerCount)
            .Select(writer => Task.Run(() =>
            {
                for (var index = 0; index < eventsPerWriter; index++)
                {
                    var sequence = (writer * eventsPerWriter) + index + 1;
                    _sut.CheckSequence("AAPL", "trade", sequence, baseTime.AddTicks(sequence), "primary");
                    if (index % 10 == 0)
                    {
                        _ = _sut.GetSummary("AAPL");
                        _ = _sut.GetRecentErrors();
                        _ = _sut.GetStatistics();
                    }
                }
            }));
        var resetter = Task.Run(() =>
        {
            for (var index = 0; index < eventsPerWriter; index++)
            {
                _sut.ResetSymbolState("AAPL", "trade", "primary");
            }
        });

        await Task.WhenAll(writers.Append(resetter));

        _sut.TotalEventsChecked.Should().Be(writerCount * eventsPerWriter);
        _sut.GetRecentErrors().Should().OnlyContain(error => error.Symbol == "AAPL");
    }

    [Fact]
    public void CheckSequence_CanonicalizesSymbolEventTypeAndProviderWithoutMergingProviders()
    {
        var baseTime = DateTimeOffset.UtcNow;

        _sut.CheckSequence(
                " aapl ",
                " Trade ",
                100,
                baseTime,
                streamId: "feed-1",
                provider: " Alpha ")
            .Should()
            .BeNull();
        _sut.CheckSequence(
                "AAPL",
                "trade",
                1,
                baseTime,
                streamId: "feed-1",
                provider: "Beta")
            .Should()
            .BeNull();

        _sut.CheckSequence(
                "AaPl",
                "TRADE",
                101,
                baseTime.AddSeconds(1),
                streamId: "feed-1",
                provider: "alpha")
            .Should()
            .BeNull();
        _sut.CheckSequence(
                "aapl",
                "trade",
                2,
                baseTime.AddSeconds(1),
                streamId: "feed-1",
                provider: " BETA ")
            .Should()
            .BeNull();

        var duplicate = _sut.CheckSequence(
            "AAPL",
            "Trade",
            101,
            baseTime.AddSeconds(2),
            streamId: "feed-1",
            provider: "ALPHA");

        duplicate.Should().NotBeNull();
        duplicate!.ErrorType.Should().Be(SequenceErrorType.Duplicate);
        _sut.GetErrors(" aapl ", " TRADE ")
            .Should()
            .ContainSingle(error => error.ErrorType == SequenceErrorType.Duplicate);
    }

    [Fact]
    public void ResetSymbolState_ResetsAllProvidersUnlessProviderIsExplicitlyScoped()
    {
        var baseTime = DateTimeOffset.UtcNow;
        _sut.CheckSequence("AAPL", "trade", 100, baseTime, "feed", "alpha");
        _sut.CheckSequence("AAPL", "trade", 200, baseTime, "feed", "beta");

        _sut.ResetSymbolState(" aapl ", " TRADE ", "feed", " ALPHA ");

        _sut.CheckSequence("AAPL", "trade", 1, baseTime.AddSeconds(1), "feed", "alpha")
            .Should()
            .BeNull();
        var betaError = _sut.CheckSequence(
            "AAPL",
            "trade",
            199,
            baseTime.AddSeconds(1),
            "feed",
            "BETA");
        betaError.Should().NotBeNull();
        betaError!.ErrorType.Should().Be(SequenceErrorType.OutOfOrder);

        _sut.ResetSymbolState("AAPL", "trade", "feed");

        _sut.CheckSequence("AAPL", "trade", 2, baseTime.AddSeconds(2), "feed", "alpha")
            .Should()
            .BeNull();
        _sut.CheckSequence("AAPL", "trade", 1, baseTime.AddSeconds(2), "feed", "beta")
            .Should()
            .BeNull();
    }

    [Fact]
    public void GetStatistics_SeparatesLifetimeErrorsFromRetainedRecords()
    {
        using var tracker = new SequenceErrorTracker(new SequenceErrorConfig
        {
            GapThreshold = 1,
            MaxErrorsPerSymbol = 1
        });
        var baseTime = DateTimeOffset.UtcNow;

        tracker.CheckSequence("SPY", "trade", 1, baseTime);
        tracker.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1));
        tracker.CheckSequence("SPY", "trade", 20, baseTime.AddSeconds(2));

        var stats = tracker.GetStatistics();

        stats.TotalEventsChecked.Should().Be(3);
        stats.TotalErrors.Should().Be(1);
        stats.RetainedTotalErrors.Should().Be(1);
        stats.LifetimeTotalErrors.Should().Be(2);
        stats.ErrorsByType[SequenceErrorType.Gap].Should().Be(2);
        stats.RetainedErrorRate.Should().BeApproximately(100d / 3d, 0.0001);
        stats.LifetimeErrorRate.Should().BeApproximately(200d / 3d, 0.0001);
    }

    [Fact]
    public async Task RunCleanup_DoesNotRemoveReplacementErrorBuffer()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        using var retired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hooks = new SequenceErrorTrackerTestHooks
        {
            ErrorBufferRetiredBeforeRemoval = () =>
            {
                retired.Set();
                WaitOrThrow(release, "release retired sequence error buffer");
            }
        };
        using var tracker = new SequenceErrorTracker(
            new SequenceErrorConfig { RetentionDays = 1 },
            timeProvider,
            hooks);
        tracker.RecordError(CreateGapError(timeProvider.GetUtcNow(), provider: "alpha"));
        timeProvider.Advance(TimeSpan.FromDays(2));

        var cleanupTask = Task.Run(tracker.RunCleanup);
        WaitOrThrow(retired, "retire sequence error buffer");

        var replacement = CreateGapError(timeProvider.GetUtcNow(), provider: "ALPHA");
        tracker.RecordError(replacement);
        release.Set();
        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

        tracker.GetRecentErrors().Should().ContainSingle().Which.Should().Be(replacement);
        var stats = tracker.GetStatistics();
        stats.RetainedTotalErrors.Should().Be(1);
        stats.LifetimeTotalErrors.Should().Be(2);
    }

    [Fact]
    public async Task RunCleanup_DoesNotRemoveReplacementSequenceState()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        using var retired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hooks = new SequenceErrorTrackerTestHooks
        {
            StateRetiredBeforeRemoval = () =>
            {
                retired.Set();
                WaitOrThrow(release, "release retired sequence state");
            }
        };
        using var tracker = new SequenceErrorTracker(null, timeProvider, hooks);
        tracker.CheckSequence("AAPL", "trade", 1, timeProvider.GetUtcNow(), provider: "alpha");
        timeProvider.Advance(TimeSpan.FromHours(7));

        var cleanupTask = Task.Run(tracker.RunCleanup);
        WaitOrThrow(retired, "retire sequence state");

        tracker.CheckSequence("aapl", "TRADE", 100, timeProvider.GetUtcNow(), provider: "ALPHA")
            .Should()
            .BeNull();
        tracker.CheckSequence("AAPL", "trade", 101, timeProvider.GetUtcNow(), provider: "alpha")
            .Should()
            .BeNull();
        release.Set();
        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

        tracker.CheckSequence("AAPL", "trade", 102, timeProvider.GetUtcNow(), provider: "alpha")
            .Should()
            .BeNull();
    }

    #endregion

    #region Configuration

    [Fact]
    public void SequenceErrorConfig_Default_HasSensibleValues()
    {
        var config = SequenceErrorConfig.Default;

        config.GapThreshold.Should().Be(1);
        config.ResetThreshold.Should().Be(10000);
        config.SignificantGapSize.Should().Be(100);
    }

    [Fact]
    public void Constructor_RejectsInvalidConfigurationRanges()
    {
        var invalidConfigurations = new[]
        {
            new SequenceErrorConfig { GapThreshold = 0 },
            new SequenceErrorConfig { SignificantGapSize = -1 },
            new SequenceErrorConfig { ResetThreshold = -1 },
            new SequenceErrorConfig { MaxErrorsPerSymbol = -1 },
            new SequenceErrorConfig { RetentionDays = -1 }
        };

        foreach (var config in invalidConfigurations)
        {
            Action construct = () => _ = new SequenceErrorTracker(config);
            construct.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void CheckSequence_ExtremeValuesUseSaturatingArithmetic()
    {
        using var tracker = new SequenceErrorTracker(new SequenceErrorConfig
        {
            GapThreshold = long.MaxValue,
            ResetThreshold = long.MaxValue,
            SignificantGapSize = long.MaxValue
        });
        var baseTime = DateTimeOffset.UtcNow;

        tracker.CheckSequence("SPY", "trade", 1, baseTime).Should().BeNull();
        tracker.CheckSequence("SPY", "trade", long.MaxValue, baseTime.AddSeconds(1))
            .Should()
            .BeNull();
        var error = tracker.CheckSequence("SPY", "trade", 0, baseTime.AddSeconds(2));

        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.OutOfOrder);
        error.ExpectedSequence.Should().Be(long.MaxValue);
        error.GapSize.Should().Be(long.MaxValue);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_AfterDispose_CheckSequenceReturnsNull()
    {
        var tracker = new SequenceErrorTracker();
        tracker.Dispose();

        var error = tracker.CheckSequence("SPY", "trade", 1, DateTimeOffset.UtcNow);

        error.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_WaitsForActiveCallbacksAndRejectsPostDisposeMutations()
    {
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var disposeRequested = new ManualResetEventSlim();
        var hooks = new SequenceErrorTrackerTestHooks
        {
            DisposeRequested = disposeRequested.Set
        };
        var tracker = new SequenceErrorTracker(null, TimeProvider.System, hooks);
        var callbackCount = 0;
        tracker.OnSequenceError += _ =>
        {
            Interlocked.Increment(ref callbackCount);
            callbackEntered.Set();
            WaitOrThrow(releaseCallback, "release sequence callback");
        };
        var baseTime = DateTimeOffset.UtcNow;
        tracker.CheckSequence("SPY", "trade", 1, baseTime);
        var checkTask = Task.Run(() =>
            tracker.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1)));
        WaitOrThrow(callbackEntered, "enter sequence callback");

        var disposeTask = Task.Run(tracker.Dispose);
        WaitOrThrow(disposeRequested, "request sequence tracker disposal");
        disposeTask.IsCompleted.Should().BeFalse();

        releaseCallback.Set();
        (await checkTask.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        var gapCount = tracker.TotalGapErrors;
        tracker.CheckSequence("SPY", "trade", 20, baseTime.AddSeconds(2)).Should().BeNull();
        tracker.RecordError(CreateGapError(baseTime.AddSeconds(2)));
        tracker.TotalGapErrors.Should().Be(gapCount);
        callbackCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_FromSequenceErrorCallback_DoesNotDeadlock()
    {
        var tracker = new SequenceErrorTracker();
        tracker.OnSequenceError += _ => tracker.Dispose();
        var baseTime = DateTimeOffset.UtcNow;
        tracker.CheckSequence("SPY", "trade", 1, baseTime);

        var error = await Task.Run(() =>
                tracker.CheckSequence("SPY", "trade", 10, baseTime.AddSeconds(1)))
            .WaitAsync(TimeSpan.FromSeconds(5));

        error.Should().NotBeNull();
        tracker.CheckSequence("SPY", "trade", 20, baseTime.AddSeconds(2)).Should().BeNull();
        tracker.Dispose();
    }

    #endregion

    private static SequenceError CreateGapError(DateTimeOffset timestamp, string provider = "alpha")
    {
        return new SequenceError(
            Timestamp: timestamp,
            Symbol: "AAPL",
            EventType: "trade",
            ErrorType: SequenceErrorType.Gap,
            ExpectedSequence: 2,
            ActualSequence: 10,
            GapSize: 8,
            StreamId: "feed",
            Provider: provider);
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
