using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Application.Services.DataQuality;

/// <summary>
/// Tests for CompletenessScoreCalculator.
/// </summary>
public sealed class CompletenessScoreCalculatorTests : IDisposable
{
    private readonly CompletenessScoreCalculator _sut;

    public CompletenessScoreCalculatorTests()
    {
        _sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 100,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0
        });
    }

    public void Dispose() => _sut.Dispose();

    #region RecordEvent

    [Fact]
    public void RecordEvent_SingleEvent_CreatesState()
    {
        var timestamp = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero); // During trading hours UTC
        _sut.RecordEvent("SPY", timestamp, "trade");

        var score = _sut.GetScore("SPY", DateOnly.FromDateTime(timestamp.UtcDateTime));

        score.Should().NotBeNull();
        score!.Symbol.Should().Be("SPY");
        score.ActualEvents.Should().Be(1);
    }

    [Fact]
    public void RecordEvent_MultipleEvents_AccumulatesCount()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 50; i++)
        {
            _sut.RecordEvent("SPY", baseTime.AddMinutes(i), "trade");
        }

        var score = _sut.GetScore("SPY", DateOnly.FromDateTime(baseTime.UtcDateTime));

        score.Should().NotBeNull();
        score!.ActualEvents.Should().Be(50);
    }

    [Fact]
    public void RecordEvents_Batch_AccumulatesAll()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var timestamps = Enumerable.Range(0, 20).Select(i => baseTime.AddMinutes(i));

        _sut.RecordEvents("SPY", timestamps, "trade");

        var score = _sut.GetScore("SPY", DateOnly.FromDateTime(baseTime.UtcDateTime));
        score.Should().NotBeNull();
        score!.ActualEvents.Should().Be(20);
    }

    #endregion

    #region GetScore

    [Fact]
    public void GetScore_UnknownSymbol_ReturnsNull()
    {
        var score = _sut.GetScore("NONEXISTENT", DateOnly.FromDateTime(DateTime.UtcNow));

        score.Should().BeNull();
    }

    [Fact]
    public void GetScore_WithEvents_ReturnsValidScore()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        _sut.RecordEvent("SPY", baseTime, "trade");

        var score = _sut.GetScore("SPY", DateOnly.FromDateTime(baseTime.UtcDateTime));

        score.Should().NotBeNull();
        score!.Score.Should().BeInRange(0.0, 1.0);
        score.ExpectedEvents.Should().BeGreaterThan(0);
        score.MissingEvents.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetScore_ManyEvents_HigherScore()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseTime = new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);

        // Record a few events
        for (int i = 0; i < 5; i++)
            _sut.RecordEvent("LOW", baseTime.AddMinutes(i), "trade");

        // Record many events covering more minutes
        for (int i = 0; i < 100; i++)
            _sut.RecordEvent("HIGH", baseTime.AddMinutes(i % 60).AddSeconds(i), "trade");

        var lowScore = _sut.GetScore("LOW", date);
        var highScore = _sut.GetScore("HIGH", date);

        lowScore.Should().NotBeNull();
        highScore.Should().NotBeNull();
        highScore!.Score.Should().BeGreaterThanOrEqualTo(lowScore!.Score);
    }

    #endregion

    #region GetAllScores

    [Fact]
    public void GetAllScores_NoData_ReturnsEmptyList()
    {
        var scores = _sut.GetAllScores();

        scores.Should().BeEmpty();
    }

    [Fact]
    public void GetAllScores_MultipleSymbols_ReturnsAll()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        _sut.RecordEvent("SPY", baseTime, "trade");
        _sut.RecordEvent("AAPL", baseTime, "trade");
        _sut.RecordEvent("MSFT", baseTime, "trade");

        var scores = _sut.GetAllScores();

        scores.Should().HaveCount(3);
    }

    #endregion

    #region GetScoresForDate

    [Fact]
    public void GetScoresForDate_ReturnsOnlyMatchingDate()
    {
        var day1 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 2, 12, 14, 0, 0, TimeSpan.Zero);

        _sut.RecordEvent("SPY", day1, "trade");
        _sut.RecordEvent("SPY", day2, "trade");

        var scores = _sut.GetScoresForDate(DateOnly.FromDateTime(day1.UtcDateTime));

        scores.Should().HaveCount(1);
        scores[0].Date.Should().Be(DateOnly.FromDateTime(day1.UtcDateTime));
    }

    #endregion

    #region GetScoresForSymbol

    [Fact]
    public void GetScoresForSymbol_ReturnsOnlyMatchingSymbol()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        _sut.RecordEvent("SPY", baseTime, "trade");
        _sut.RecordEvent("AAPL", baseTime, "trade");

        var scores = _sut.GetScoresForSymbol("SPY");

        scores.Should().HaveCount(1);
        scores[0].Symbol.Should().Be("SPY");
    }

    [Fact]
    public void GetScoresForSymbol_CaseInsensitive()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        _sut.RecordEvent("SPY", baseTime, "trade");

        var scores = _sut.GetScoresForSymbol("spy");

        scores.Should().HaveCount(1);
    }

    #endregion

    #region CompletenessScore Properties

    [Fact]
    public void CompletenessScore_HasExpectedFields()
    {
        var baseTime = new DateTimeOffset(2026, 2, 11, 15, 0, 0, TimeSpan.Zero);
        _sut.RecordEvent("SPY", baseTime, "trade");

        var score = _sut.GetScore("SPY", DateOnly.FromDateTime(baseTime.UtcDateTime));

        score.Should().NotBeNull();
        score!.TradingDuration.Should().BeGreaterThan(TimeSpan.Zero);
        score.CalculatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region Configuration

    [Fact]
    public void CompletenessConfig_Default_HasSensibleValues()
    {
        var config = CompletenessConfig.Default;

        config.ExpectedEventsPerHour.Should().Be(1000);
        config.TradingStartHour.Should().Be(13);
        config.TradingStartMinute.Should().Be(30);
        config.TradingEndHour.Should().Be(20);
        config.TradingEndMinute.Should().Be(0);
        config.RetentionDays.Should().Be(30);
        config.EnableAutoCalibration.Should().BeTrue();
        config.CalibrationWindowMinutes.Should().Be(60);
        config.CalibrationMinEvents.Should().Be(10);
    }

    #endregion

    #region AutoCalibration

    [Fact]
    public void AutoCalibration_AfterWindowElapsesWithEnoughEvents_AdjustsExpectedRate()
    {
        // High default of 5000/hour deliberately chosen so the default score is terrible.
        using var sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 5000,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0,
            EnableAutoCalibration = true,
            CalibrationWindowMinutes = 60,
            CalibrationMinEvents = 5
        });

        // Symbol actually trades at ~6 events/hour.
        // Simulate 5 events in the first 50 minutes (not enough time yet).
        var t0 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(t0.UtcDateTime);

        for (int i = 0; i < 5; i++)
            sut.RecordEvent("LOWVOL", t0.AddMinutes(i * 10), "trade");

        var scoreBefore = sut.GetScore("LOWVOL", date)!;
        scoreBefore.IsAutoCalibrated.Should().BeFalse("calibration window has not elapsed yet");
        scoreBefore.Score.Should().BeLessThan(0.05, "default of 5000/hr makes 5 events look terrible");

        // 6th event arrives 61 minutes after the first → triggers calibration.
        sut.RecordEvent("LOWVOL", t0.AddMinutes(61), "trade");

        var scoreAfter = sut.GetScore("LOWVOL", date)!;
        scoreAfter.IsAutoCalibrated.Should().BeTrue("calibration window has now elapsed with enough events");
        scoreAfter.Score.Should().BeGreaterThan(scoreBefore.Score,
            "expected rate was adjusted down to match observed activity");
    }

    [Fact]
    public void AutoCalibration_ExplicitProfileRegistered_PreventsCalibration()
    {
        using var sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 5000,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0,
            EnableAutoCalibration = true,
            CalibrationWindowMinutes = 60,
            CalibrationMinEvents = 5
        });

        // Explicitly configure liquidity before any events arrive.
        sut.RegisterSymbolLiquidity("LOWVOL", Meridian.Contracts.Domain.Enums.LiquidityProfile.VeryLow);

        var t0 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(t0.UtcDateTime);

        // Simulate 10 events spanning 90 minutes — would normally trigger auto-calibration.
        for (int i = 0; i < 10; i++)
            sut.RecordEvent("LOWVOL", t0.AddMinutes(i * 9), "trade");

        var score = sut.GetScore("LOWVOL", date)!;
        score.IsAutoCalibrated.Should().BeFalse(
            "explicit RegisterSymbolLiquidity suppresses auto-calibration");
    }

    [Fact]
    public void AutoCalibration_TooFewEventsInWindow_DoesNotCalibrate()
    {
        using var sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 5000,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0,
            EnableAutoCalibration = true,
            CalibrationWindowMinutes = 60,
            CalibrationMinEvents = 10 // Requires at least 10 events
        });

        var t0 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(t0.UtcDateTime);

        // Only 3 events over 90 minutes — below CalibrationMinEvents threshold.
        sut.RecordEvent("SPARSE", t0, "trade");
        sut.RecordEvent("SPARSE", t0.AddMinutes(45), "trade");
        sut.RecordEvent("SPARSE", t0.AddMinutes(91), "trade");

        var score = sut.GetScore("SPARSE", date)!;
        score.IsAutoCalibrated.Should().BeFalse(
            "fewer than CalibrationMinEvents events observed — calibration should not fire");
    }

    [Fact]
    public void AutoCalibration_Disabled_NeverCalibrates()
    {
        using var sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 5000,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0,
            EnableAutoCalibration = false,
            CalibrationWindowMinutes = 60,
            CalibrationMinEvents = 5
        });

        var t0 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(t0.UtcDateTime);

        for (int i = 0; i < 20; i++)
            sut.RecordEvent("SPY", t0.AddMinutes(i * 5), "trade");

        var score = sut.GetScore("SPY", date)!;
        score.IsAutoCalibrated.Should().BeFalse(
            "EnableAutoCalibration=false means calibration is never applied");
    }

    [Fact]
    public void AutoCalibration_SetExpectedEventsPerHour_PreventsCalibration()
    {
        using var sut = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 5000,
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0,
            EnableAutoCalibration = true,
            CalibrationWindowMinutes = 60,
            CalibrationMinEvents = 5
        });

        var t0 = new DateTimeOffset(2026, 2, 11, 14, 0, 0, TimeSpan.Zero);
        var date = DateOnly.FromDateTime(t0.UtcDateTime);

        // Record first event to create the state, then override expected rate explicitly.
        sut.RecordEvent("SPY", t0, "trade");
        sut.SetExpectedEventsPerHour("SPY", 300);

        // Record more events spanning 90 minutes — would normally trigger auto-calibration.
        for (int i = 1; i < 15; i++)
            sut.RecordEvent("SPY", t0.AddMinutes(i * 6), "trade");

        var score = sut.GetScore("SPY", date)!;
        score.IsAutoCalibrated.Should().BeFalse(
            "SetExpectedEventsPerHour marks the state as explicitly configured");
        score.ExpectedEvents.Should().Be(300L * 65 / 10, // 300/hr * 6.5 hr
            "explicitly set rate should be preserved");
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_AfterDispose_RecordEventIsNoop()
    {
        var calc = new CompletenessScoreCalculator();
        calc.Dispose();

        // Should not throw after disposal
        calc.RecordEvent("SPY", DateTimeOffset.UtcNow, "trade");

        var score = calc.GetScore("SPY", DateOnly.FromDateTime(DateTime.UtcNow));
        score.Should().BeNull();
    }

    #endregion
}
