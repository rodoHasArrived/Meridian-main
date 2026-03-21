using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Application.Services.DataQuality;

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

    #endregion
}
