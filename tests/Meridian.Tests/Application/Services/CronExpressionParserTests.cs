using FluentAssertions;
using Meridian.Core.Scheduling;
using Xunit;

namespace Meridian.Tests;

public sealed class CronExpressionParserTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    #region IsValid

    [Theory]
    [InlineData("0 2 * * *")]      // Daily at 2 AM
    [InlineData("*/15 * * * *")]   // Every 15 min
    [InlineData("0 0 1 * *")]      // Monthly
    [InlineData("30 6 * * 1-5")]   // Weekdays at 6:30
    [InlineData("0 3 * * 0")]      // Sunday at 3 AM
    [InlineData("0,30 * * * *")]   // Every 30 min
    [InlineData("0 0 * * *")]      // Midnight daily
    public void IsValid_ValidExpressions_ReturnsTrue(string cron)
    {
        CronExpressionParser.IsValid(cron).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 2 * *")]           // Only 4 fields
    [InlineData("0 2 * * * *")]       // 6 fields
    [InlineData("60 * * * *")]        // Minute out of range
    [InlineData("* 24 * * *")]        // Hour out of range
    [InlineData("* * 0 * *")]         // Day-of-month 0 out of range
    [InlineData("* * 32 * *")]        // Day-of-month 32 out of range
    [InlineData("* * * 0 *")]         // Month 0 out of range
    [InlineData("* * * 13 *")]        // Month 13 out of range
    [InlineData("* * * * 7")]         // Day-of-week 7 out of range
    [InlineData("abc * * * *")]       // Non-numeric
    public void IsValid_InvalidExpressions_ReturnsFalse(string cron)
    {
        CronExpressionParser.IsValid(cron).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NullExpression_ReturnsFalse()
    {
        CronExpressionParser.IsValid(null!).Should().BeFalse();
    }

    #endregion

    #region TryParse

    [Fact]
    public void TryParse_SimpleExpression_ParsesCorrectly()
    {
        var result = CronExpressionParser.TryParse("30 14 * * *", out var schedule);

        result.Should().BeTrue();
        schedule.Minutes.Should().ContainSingle().Which.Should().Be(30);
        schedule.Hours.Should().ContainSingle().Which.Should().Be(14);
        schedule.DaysOfMonth.Should().HaveCount(31);
        schedule.Months.Should().HaveCount(12);
        schedule.DaysOfWeek.Should().HaveCount(7);
    }

    [Fact]
    public void TryParse_EveryMinute_ParsesAllMinutes()
    {
        CronExpressionParser.TryParse("* * * * *", out var schedule);
        schedule.Minutes.Should().HaveCount(60);
        schedule.Hours.Should().HaveCount(24);
    }

    [Fact]
    public void TryParse_StepExpression_ParsesCorrectly()
    {
        CronExpressionParser.TryParse("*/15 * * * *", out var schedule);
        schedule.Minutes.Should().BeEquivalentTo(new[] { 0, 15, 30, 45 });
    }

    [Fact]
    public void TryParse_RangeExpression_ParsesCorrectly()
    {
        CronExpressionParser.TryParse("* * * * 1-5", out var schedule);
        schedule.DaysOfWeek.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void TryParse_ListExpression_ParsesCorrectly()
    {
        CronExpressionParser.TryParse("0,15,30,45 * * * *", out var schedule);
        schedule.Minutes.Should().BeEquivalentTo(new[] { 0, 15, 30, 45 });
    }

    [Fact]
    public void TryParse_RangeWithStep_ParsesCorrectly()
    {
        // 1-5/2 means 1, 3, 5
        CronExpressionParser.TryParse("* * * * 1-5/2", out var schedule);
        schedule.DaysOfWeek.Should().BeEquivalentTo(new[] { 1, 3, 5 });
    }

    [Fact]
    public void TryParse_SpecificMonths_ParsesCorrectly()
    {
        CronExpressionParser.TryParse("0 0 1 1,6,12 *", out var schedule);
        schedule.Months.Should().BeEquivalentTo(new[] { 1, 6, 12 });
    }

    [Fact]
    public void TryParse_StartWithStep_ParsesCorrectly()
    {
        // 5/10 in minute field means 5, 15, 25, 35, 45, 55
        CronExpressionParser.TryParse("5/10 * * * *", out var schedule);
        schedule.Minutes.Should().BeEquivalentTo(new[] { 5, 15, 25, 35, 45, 55 });
    }

    #endregion

    #region GetNextOccurrence

    [Fact]
    public void GetNextOccurrence_DailyAt2AM_ReturnsCorrectTime()
    {
        // "0 2 * * *" = daily at 2:00 AM
        var from = new DateTimeOffset(2025, 1, 6, 1, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 2 * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Hour.Should().Be(2);
        next.Value.Minute.Should().Be(0);
        next.Value.Day.Should().Be(6); // Same day since before 2 AM
    }

    [Fact]
    public void GetNextOccurrence_DailyAt2AM_AfterTime_ReturnsNextDay()
    {
        // Already past 2 AM
        var from = new DateTimeOffset(2025, 1, 6, 3, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 2 * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Day.Should().Be(7); // Next day
        next.Value.Hour.Should().Be(2);
    }

    [Fact]
    public void GetNextOccurrence_WeekdaysOnly_SkipsWeekend()
    {
        // "0 9 * * 1-5" = weekdays at 9 AM
        // 2025-01-04 is Saturday
        var from = new DateTimeOffset(2025, 1, 4, 0, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 9 * * 1-5", Utc, from);

        next.Should().NotBeNull();
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Value.Day.Should().Be(6);
    }

    [Fact]
    public void GetNextOccurrence_SundayOnly_FindsNextSunday()
    {
        // "0 3 * * 0" = Sunday at 3 AM
        // 2025-01-06 is Monday
        var from = new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 3 * * 0", Utc, from);

        next.Should().NotBeNull();
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        next.Value.Day.Should().Be(12); // Next Sunday
    }

    [Fact]
    public void GetNextOccurrence_Every15Minutes_ReturnsNextQuarter()
    {
        // "*/15 * * * *"
        var from = new DateTimeOffset(2025, 1, 6, 10, 3, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("*/15 * * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Minute.Should().Be(15);
        next.Value.Hour.Should().Be(10);
    }

    [Fact]
    public void GetNextOccurrence_MonthlyFirstDay_ReturnsNextMonth()
    {
        // "0 0 1 * *" = 1st of month at midnight
        // From Jan 2 → next is Feb 1
        var from = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 0 1 * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Month.Should().Be(2);
        next.Value.Day.Should().Be(1);
        next.Value.Hour.Should().Be(0);
        next.Value.Minute.Should().Be(0);
    }

    [Fact]
    public void GetNextOccurrence_InvalidExpression_ReturnsNull()
    {
        var from = DateTimeOffset.UtcNow;
        var result = CronExpressionParser.GetNextOccurrence("invalid", Utc, from);
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextOccurrence_WithTimezone_RespectsOffset()
    {
        // Test with Eastern Time
        TimeZoneInfo et;
        try
        { et = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch { et = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }

        // "0 9 * * *" = 9 AM in target timezone
        var from = new DateTimeOffset(2025, 1, 6, 12, 0, 0, TimeSpan.Zero); // 12 PM UTC = 7 AM ET
        var next = CronExpressionParser.GetNextOccurrence("0 9 * * *", et, from);

        next.Should().NotBeNull();
        // The result should be 9 AM ET
        var nextEt = TimeZoneInfo.ConvertTime(next!.Value, et);
        nextEt.Hour.Should().Be(9);
        nextEt.Minute.Should().Be(0);
    }

    [Fact]
    public void GetNextOccurrence_ExactTime_AdvancesToNextMinute()
    {
        // If current time exactly matches, should return next occurrence (not same minute)
        var from = new DateTimeOffset(2025, 1, 6, 2, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 2 * * *", Utc, from);

        next.Should().NotBeNull();
        // Should be next day since it starts from "from + 1 minute"
        next!.Value.Day.Should().Be(7);
    }

    [Fact]
    public void GetNextOccurrence_SpecificMonths_SkipsNonMatchingMonths()
    {
        // "0 0 1 6 *" = June 1 at midnight
        var from = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 0 1 6 *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Month.Should().Be(6);
        next.Value.Day.Should().Be(1);
    }

    #endregion

    #region GetDescription

    [Fact]
    public void GetDescription_DailyAt2AM_ReturnsReadableDescription()
    {
        var desc = CronExpressionParser.GetDescription("0 2 * * *");
        desc.Should().Contain("02:00");
    }

    [Fact]
    public void GetDescription_EveryMinute_ReturnsEveryMinute()
    {
        var desc = CronExpressionParser.GetDescription("* * * * *");
        desc.Should().Contain("every minute");
    }

    [Fact]
    public void GetDescription_WeekdaysOnly_IncludesDayNames()
    {
        var desc = CronExpressionParser.GetDescription("0 9 * * 1-5");
        desc.Should().Contain("Mon");
        desc.Should().Contain("Fri");
    }

    [Fact]
    public void GetDescription_InvalidExpression_ReturnsErrorMessage()
    {
        var desc = CronExpressionParser.GetDescription("invalid");
        desc.Should().Be("Invalid cron expression");
    }

    [Fact]
    public void GetDescription_EveryHourAtMinute30_DescribesCorrectly()
    {
        var desc = CronExpressionParser.GetDescription("30 * * * *");
        desc.Should().Contain("every hour");
        desc.Should().Contain("30");
    }

    [Fact]
    public void GetDescription_SpecificMonths_IncludesMonths()
    {
        var desc = CronExpressionParser.GetDescription("0 0 1 1,6 *");
        desc.Should().Contain("months");
    }

    #endregion

    #region CronSchedule.GetNextOccurrence - Edge Cases

    [Fact]
    public void GetNextOccurrence_EveryMinuteEveryHour_ReturnsNextMinute()
    {
        var from = new DateTimeOffset(2025, 1, 6, 10, 30, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("* * * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Minute.Should().Be(31);
        next.Value.Hour.Should().Be(10);
    }

    [Fact]
    public void GetNextOccurrence_LastMinuteOfDay_WrapsToNextDay()
    {
        // From 23:59, every hour at minute 0 → next is 00:00 next day
        var from = new DateTimeOffset(2025, 1, 6, 23, 59, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 * * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Day.Should().Be(7);
        next.Value.Hour.Should().Be(0);
        next.Value.Minute.Should().Be(0);
    }

    [Fact]
    public void GetNextOccurrence_EndOfMonth_WrapsToNextMonth()
    {
        // "0 0 * * *" from Jan 31 23:30 → Feb 1 00:00
        var from = new DateTimeOffset(2025, 1, 31, 23, 30, 0, TimeSpan.Zero);
        var next = CronExpressionParser.GetNextOccurrence("0 0 * * *", Utc, from);

        next.Should().NotBeNull();
        next!.Value.Month.Should().Be(2);
        next.Value.Day.Should().Be(1);
    }

    #endregion

    #region ParseField Boundary Tests

    [Fact]
    public void TryParse_MinuteRange_0To59()
    {
        CronExpressionParser.TryParse("0-59 * * * *", out var schedule);
        schedule.Minutes.Should().HaveCount(60);
    }

    [Fact]
    public void TryParse_HourRange_0To23()
    {
        CronExpressionParser.TryParse("0 0-23 * * *", out var schedule);
        schedule.Hours.Should().HaveCount(24);
    }

    [Fact]
    public void TryParse_DayOfMonthRange_1To31()
    {
        CronExpressionParser.TryParse("0 0 1-31 * *", out var schedule);
        schedule.DaysOfMonth.Should().HaveCount(31);
    }

    [Fact]
    public void TryParse_MonthRange_1To12()
    {
        CronExpressionParser.TryParse("0 0 * 1-12 *", out var schedule);
        schedule.Months.Should().HaveCount(12);
    }

    [Fact]
    public void TryParse_DayOfWeekRange_0To6()
    {
        CronExpressionParser.TryParse("0 0 * * 0-6", out var schedule);
        schedule.DaysOfWeek.Should().HaveCount(7);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void TryParse_ComplexListAndRange_ParsesCorrectly()
    {
        // Minutes: 0,15,30,45  Hours: 9-17  Weekdays only
        CronExpressionParser.TryParse("0,15,30,45 9-17 * * 1-5", out var schedule);

        schedule.Minutes.Should().BeEquivalentTo(new[] { 0, 15, 30, 45 });
        schedule.Hours.Should().HaveCount(9); // 9,10,11,12,13,14,15,16,17
        schedule.DaysOfWeek.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void TryParse_MultipleExtraSpaces_HandlesGracefully()
    {
        // Extra spaces between fields
        var result = CronExpressionParser.TryParse("0  2  *  *  *", out var schedule);
        result.Should().BeTrue();
        schedule.Minutes.Should().ContainSingle().Which.Should().Be(0);
        schedule.Hours.Should().ContainSingle().Which.Should().Be(2);
    }

    #endregion
}
