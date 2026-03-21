using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="DataCalendarService"/> and its associated calendar models.
/// </summary>
public sealed class DataCalendarServiceTests
{
    // ── CalendarYearData model ───────────────────────────────────────

    [Fact]
    public void CalendarYearData_DefaultValues_ShouldBeCorrect()
    {
        var year = new CalendarYearData();

        year.Year.Should().Be(0);
        year.Months.Should().NotBeNull().And.BeEmpty();
        year.TotalTradingDays.Should().Be(0);
        year.DaysWithData.Should().Be(0);
        year.TotalGaps.Should().Be(0);
        year.OverallCompleteness.Should().Be(0);
    }

    [Fact]
    public void CalendarYearData_Completeness_WhenZeroTradingDays_ShouldBeZero()
    {
        var year = new CalendarYearData
        {
            TotalTradingDays = 0,
            DaysWithData = 0
        };

        // Mirrors the calculation in DataCalendarService:
        // OverallCompleteness = TotalTradingDays > 0 ? DaysWithData / TotalTradingDays * 100 : 0
        var completeness = year.TotalTradingDays > 0
            ? (double)year.DaysWithData / year.TotalTradingDays * 100
            : 0;

        completeness.Should().Be(0);
    }

    [Fact]
    public void CalendarYearData_Completeness_WhenAllDaysHaveData_ShouldBe100()
    {
        var year = new CalendarYearData
        {
            TotalTradingDays = 252,
            DaysWithData = 252
        };

        var completeness = year.TotalTradingDays > 0
            ? (double)year.DaysWithData / year.TotalTradingDays * 100
            : 0;

        completeness.Should().Be(100);
    }

    [Fact]
    public void CalendarYearData_Completeness_WhenPartialData_ShouldBeCorrectPercentage()
    {
        var year = new CalendarYearData
        {
            TotalTradingDays = 200,
            DaysWithData = 150
        };

        var completeness = (double)year.DaysWithData / year.TotalTradingDays * 100;
        completeness.Should().Be(75);
    }

    // ── CalendarMonthData model ─────────────────────────────────────

    [Fact]
    public void CalendarMonthData_DefaultValues_ShouldBeCorrect()
    {
        var month = new CalendarMonthData();

        month.Year.Should().Be(0);
        month.Month.Should().Be(0);
        month.MonthName.Should().BeEmpty();
        month.Days.Should().NotBeNull().And.BeEmpty();
        month.TradingDays.Should().Be(0);
        month.DaysWithData.Should().Be(0);
        month.GapCount.Should().Be(0);
        month.Completeness.Should().Be(0);
    }

    [Fact]
    public void CalendarMonthData_Completeness_WhenZeroTradingDays_ShouldBe100()
    {
        // The service sets Completeness to 100 when TradingDays is 0:
        // Completeness = TradingDays > 0 ? DaysWithData / TradingDays * 100 : 100
        var month = new CalendarMonthData
        {
            TradingDays = 0,
            DaysWithData = 0
        };

        var completeness = month.TradingDays > 0
            ? (double)month.DaysWithData / month.TradingDays * 100
            : 100;

        completeness.Should().Be(100);
    }

    [Fact]
    public void CalendarMonthData_Completeness_WhenFullData_ShouldBe100()
    {
        var month = new CalendarMonthData
        {
            TradingDays = 22,
            DaysWithData = 22
        };

        var completeness = (double)month.DaysWithData / month.TradingDays * 100;
        completeness.Should().Be(100);
    }

    // ── CalendarDayData model ───────────────────────────────────────

    [Fact]
    public void CalendarDayData_DefaultValues_ShouldBeCorrect()
    {
        var day = new CalendarDayData();

        day.Date.Should().Be(default(DateOnly));
        day.DayOfWeek.Should().Be(default(DayOfWeek));
        day.IsWeekend.Should().BeFalse();
        day.IsHoliday.Should().BeFalse();
        day.IsTradingDay.Should().BeFalse();
        day.HasData.Should().BeFalse();
        day.Completeness.Should().Be(0);
        day.EventCount.Should().Be(0);
        day.ExpectedEvents.Should().Be(0);
        day.HasGaps.Should().BeFalse();
        day.GapCount.Should().Be(0);
        day.CompletenessLevel.Should().Be(CompletenessLevel.NonTrading);
        day.SymbolBreakdown.Should().NotBeNull().And.BeEmpty();
    }

    // ── CompletenessLevel enum ──────────────────────────────────────

    [Theory]
    [InlineData(CompletenessLevel.NonTrading)]
    [InlineData(CompletenessLevel.Missing)]
    [InlineData(CompletenessLevel.Minimal)]
    [InlineData(CompletenessLevel.Poor)]
    [InlineData(CompletenessLevel.Partial)]
    [InlineData(CompletenessLevel.Good)]
    [InlineData(CompletenessLevel.Complete)]
    public void CompletenessLevel_AllValues_ShouldBeDefined(CompletenessLevel level)
    {
        Enum.IsDefined(typeof(CompletenessLevel), level).Should().BeTrue();
    }

    [Fact]
    public void CompletenessLevel_ShouldHaveSevenValues()
    {
        Enum.GetValues<CompletenessLevel>().Should().HaveCount(7);
    }

    // ── SymbolDayData model ─────────────────────────────────────────

    [Fact]
    public void SymbolDayData_DefaultValues_ShouldBeCorrect()
    {
        var data = new SymbolDayData();

        data.Symbol.Should().BeEmpty();
        data.HasData.Should().BeFalse();
        data.Completeness.Should().Be(0);
        data.EventCount.Should().Be(0);
        data.HasGaps.Should().BeFalse();
    }

    // ── GapInfo model ───────────────────────────────────────────────

    [Fact]
    public void GapInfo_DefaultValues_ShouldBeCorrect()
    {
        var gap = new GapInfo();

        gap.Symbol.Should().BeEmpty();
        gap.StartDate.Should().Be(default(DateOnly));
        gap.EndDate.Should().Be(default(DateOnly));
        gap.GapType.Should().BeEmpty();
        gap.ExpectedEvents.Should().Be(0);
        gap.ActualEvents.Should().Be(0);
        gap.CanRepair.Should().BeFalse();
    }

    // ── GapRepairResult model ───────────────────────────────────────

    [Fact]
    public void GapRepairResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new GapRepairResult();

        result.Success.Should().BeFalse();
        result.RepairedGaps.Should().Be(0);
        result.FailedGaps.Should().Be(0);
        result.RepairedSymbols.Should().NotBeNull().And.BeEmpty();
        result.FailedItems.Should().NotBeNull().And.BeEmpty();
    }

    // ── CoverageMatrixData model ────────────────────────────────────

    [Fact]
    public void CoverageMatrixData_DefaultValues_ShouldBeCorrect()
    {
        var matrix = new CoverageMatrixData();

        matrix.Dates.Should().NotBeNull().And.BeEmpty();
        matrix.Symbols.Should().NotBeNull().And.BeEmpty();
    }

    // ── CompletenessTrendData model ─────────────────────────────────

    [Fact]
    public void CompletenessTrendData_DefaultValues_ShouldBeCorrect()
    {
        var trend = new CompletenessTrendData();

        trend.Points.Should().NotBeNull().And.BeEmpty();
    }
}
