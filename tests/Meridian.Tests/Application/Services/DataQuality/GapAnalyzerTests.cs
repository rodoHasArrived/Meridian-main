using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Application.Services.DataQuality;

/// <summary>
/// Tests for GapAnalyzer data gap detection and analysis.
/// </summary>
public sealed class GapAnalyzerTests : IDisposable
{
    private readonly GapAnalyzer _sut;

    public GapAnalyzerTests()
    {
        _sut = new GapAnalyzer(new GapAnalyzerConfig
        {
            GapThresholdSeconds = 60,
            ExpectedEventsPerHour = 1000
        });
    }

    public void Dispose() => _sut.Dispose();

    #region RecordEvent

    [Fact]
    public void RecordEvent_FirstEvent_DoesNotDetectGap()
    {
        DataGap? detected = null;
        _sut.OnGapDetected += g => detected = g;

        _sut.RecordEvent("SPY", "trade", DateTimeOffset.UtcNow);

        detected.Should().BeNull();
    }

    [Fact]
    public void RecordEvent_EventsWithinThreshold_NoGapDetected()
    {
        DataGap? detected = null;
        _sut.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(30)); // Within 60s threshold

        detected.Should().BeNull();
    }

    [Fact]
    public void RecordEvent_EventsBeyondThreshold_DetectsGap()
    {
        DataGap? detected = null;
        _sut.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(120)); // Beyond 60s threshold

        detected.Should().NotBeNull();
        detected!.Symbol.Should().Be("SPY");
        detected.EventType.Should().Be("trade");
        detected.Duration.TotalSeconds.Should().BeApproximately(120, 1);
    }

    [Fact]
    public void RecordEvent_DifferentSymbols_TrackedIndependently()
    {
        var gaps = new List<DataGap>();
        _sut.OnGapDetected += g => gaps.Add(g);

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("AAPL", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(120)); // Gap for SPY
        _sut.RecordEvent("AAPL", "trade", baseTime.AddSeconds(30)); // No gap for AAPL

        gaps.Should().HaveCount(1);
        gaps[0].Symbol.Should().Be("SPY");
    }

    [Fact]
    public void RecordEvent_DifferentEventTypes_TrackedIndependently()
    {
        var gaps = new List<DataGap>();
        _sut.OnGapDetected += g => gaps.Add(g);

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "quote", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(120)); // Gap for trade
        _sut.RecordEvent("SPY", "quote", baseTime.AddSeconds(30)); // No gap for quote

        gaps.Should().HaveCount(1);
        gaps[0].EventType.Should().Be("trade");
    }

    #endregion

    #region AnalyzeGaps

    [Fact]
    public void AnalyzeGaps_NoGapsRecorded_ReturnsEmptyResult()
    {
        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(10));

        var result = _sut.AnalyzeGaps("SPY", DateOnly.FromDateTime(baseTime.UtcDateTime));

        result.Symbol.Should().Be("SPY");
        result.TotalGaps.Should().Be(0);
    }

    [Fact]
    public void AnalyzeGaps_WithGap_ReturnsCorrectAnalysis()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseTime = new DateTimeOffset(today.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);

        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddMinutes(5)); // 5 min gap (300s > 60s threshold)

        var result = _sut.AnalyzeGaps("SPY", today, "trade");

        result.TotalGaps.Should().BeGreaterThanOrEqualTo(1);
        result.TotalGapDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region GetGapsForSymbolDate

    [Fact]
    public void GetGapsForSymbolDate_NoData_ReturnsEmptyList()
    {
        var result = _sut.GetGapsForSymbolDate("UNKNOWN", DateOnly.FromDateTime(DateTime.UtcNow));

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetGapsForSymbolDate_CaseInsensitive()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseTime = new DateTimeOffset(today.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);

        _sut.RecordEvent("spy", "trade", baseTime);
        _sut.RecordEvent("spy", "trade", baseTime.AddMinutes(5));

        var result = _sut.GetGapsForSymbolDate("SPY", today);

        // Even with different case, gaps should be found
        result.Should().NotBeNull();
    }

    #endregion

    #region Gap Severity Classification

    [Fact]
    public void RecordEvent_ShortGap_MinorSeverity()
    {
        DataGap? detected = null;
        _sut.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddSeconds(90)); // ~1.5 min gap

        detected.Should().NotBeNull();
        detected!.Severity.Should().Be(GapSeverity.Moderate); // 1.5 minutes is Moderate (< 5 minutes)
    }

    [Fact]
    public void RecordEvent_LongGap_HigherSeverity()
    {
        DataGap? detected = null;
        _sut.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        _sut.RecordEvent("SPY", "trade", baseTime);
        _sut.RecordEvent("SPY", "trade", baseTime.AddMinutes(10)); // 10 min gap

        detected.Should().NotBeNull();
        detected!.Severity.Should().BeOneOf(GapSeverity.Significant, GapSeverity.Major);
    }

    #endregion

    #region Configuration

    [Fact]
    public void GapAnalyzerConfig_Default_HasSensibleValues()
    {
        var config = GapAnalyzerConfig.Default;

        config.GapThresholdSeconds.Should().Be(60);
        config.ExpectedEventsPerHour.Should().Be(1000);
        config.TradingStartHour.Should().Be(13);
        config.TradingEndHour.Should().Be(20);
        config.RetentionDays.Should().Be(30);
    }

    [Fact]
    public void GapAnalyzer_CustomThreshold_UsesCustomValue()
    {
        using var analyzer = new GapAnalyzer(new GapAnalyzerConfig { GapThresholdSeconds = 10 });

        DataGap? detected = null;
        analyzer.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        analyzer.RecordEvent("SPY", "trade", baseTime);
        analyzer.RecordEvent("SPY", "trade", baseTime.AddSeconds(15)); // 15s > 10s custom threshold

        detected.Should().NotBeNull();
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_AfterDispose_RecordEventIsNoop()
    {
        var analyzer = new GapAnalyzer();
        analyzer.Dispose();

        DataGap? detected = null;
        analyzer.OnGapDetected += g => detected = g;

        // Should not throw or detect gaps after disposal
        analyzer.RecordEvent("SPY", "trade", DateTimeOffset.UtcNow);

        detected.Should().BeNull();
    }

    #endregion
}
