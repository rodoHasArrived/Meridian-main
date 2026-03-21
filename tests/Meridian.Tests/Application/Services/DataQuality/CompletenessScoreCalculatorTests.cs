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
