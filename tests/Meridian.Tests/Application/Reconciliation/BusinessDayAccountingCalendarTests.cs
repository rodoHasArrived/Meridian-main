using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Coverage for the reconciliation business calendar: weekend/holiday classification, the
/// roll-forward posting convention for accounting periods, signed business-day distance, and the
/// fail-safe operator file loader (<see cref="FileAccountingCalendar"/>).
/// </summary>
public sealed class BusinessDayAccountingCalendarTests
{
    // 2026-05-28 Thursday, 2026-05-29 Friday, 2026-05-30 Saturday, 2026-06-01 Monday.
    private static readonly DateOnly Thursday = new(2026, 5, 28);
    private static readonly DateOnly Friday = new(2026, 5, 29);
    private static readonly DateOnly Saturday = new(2026, 5, 30);
    private static readonly DateOnly Monday = new(2026, 6, 1);

    [Fact]
    public void IsBusinessDay_WeekendsAndHolidaysAreNotBusinessDays()
    {
        var calendar = new BusinessDayAccountingCalendar(holidays: [Friday]);

        calendar.IsBusinessDay(Thursday).Should().BeTrue();
        calendar.IsBusinessDay(Friday).Should().BeFalse("it was declared a holiday");
        calendar.IsBusinessDay(Saturday).Should().BeFalse();
        calendar.IsBusinessDay(Monday).Should().BeTrue();
    }

    [Fact]
    public void ResolvePeriod_NonBusinessDayPostingsRollForwardToNextBusinessPeriod()
    {
        var calendar = BusinessDayAccountingCalendar.Default;

        calendar.ResolvePeriod(new DateTimeOffset(2026, 5, 28, 22, 0, 0, TimeSpan.Zero)).Should().Be(Thursday);
        calendar.ResolvePeriod(new DateTimeOffset(2026, 5, 30, 3, 0, 0, TimeSpan.Zero)).Should().Be(Monday);
        calendar.ResolvePeriod(new DateTimeOffset(2026, 5, 31, 23, 59, 0, TimeSpan.Zero)).Should().Be(Monday);
    }

    [Fact]
    public void ResolvePeriod_HolidayMondayRollsToTuesday()
    {
        var calendar = new BusinessDayAccountingCalendar(holidays: [Monday]);

        calendar.ResolvePeriod(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 6, 2));
    }

    [Fact]
    public void RollBackToBusinessDay_SundayRollsToFriday()
    {
        BusinessDayAccountingCalendar.Default.RollBackToBusinessDay(new DateOnly(2026, 5, 31)).Should().Be(Friday);
    }

    [Fact]
    public void CountBusinessDaysBetween_MeasuresSignedBusinessDistance()
    {
        var calendar = BusinessDayAccountingCalendar.Default;

        calendar.CountBusinessDaysBetween(Friday, Monday).Should().Be(1, "the weekend does not count");
        calendar.CountBusinessDaysBetween(Monday, Friday).Should().Be(-1);
        calendar.CountBusinessDaysBetween(Friday, Friday).Should().Be(0);
        calendar.CountBusinessDaysBetween(new DateOnly(2026, 5, 25), Friday).Should().Be(4);
    }

    [Fact]
    public void CountBusinessDaysBetween_SkipsHolidays()
    {
        var calendar = new BusinessDayAccountingCalendar(holidays: [Monday]);

        calendar.CountBusinessDaysBetween(Friday, new DateOnly(2026, 6, 2)).Should().Be(1, "the holiday Monday does not count");
    }

    [Fact]
    public void AddBusinessDays_WalksAcrossWeekendsInBothDirections()
    {
        var calendar = BusinessDayAccountingCalendar.Default;

        calendar.AddBusinessDays(Friday, 2).Should().Be(new DateOnly(2026, 6, 2));
        calendar.AddBusinessDays(Monday, -1).Should().Be(Friday);
        calendar.AddBusinessDays(Friday, 0).Should().Be(Friday);
    }

    [Fact]
    public void Constructor_AllSevenWeekendDays_Throws()
    {
        var act = () => new BusinessDayAccountingCalendar(weekendDays: Enum.GetValues<DayOfWeek>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FileLoad_ValidCalendarFile_LoadsHolidaysAndWeekend()
    {
        var root = CreateTempRoot();
        try
        {
            WriteCalendar(root, """{ "weekendDays": ["Friday", "Saturday"], "holidays": ["2026-06-01"] }""");

            var calendar = FileAccountingCalendar.Load(root, NullLogger.Instance);

            calendar.IsBusinessDay(Friday).Should().BeFalse("the file declares a Friday/Saturday weekend");
            calendar.IsBusinessDay(new DateOnly(2026, 5, 31)).Should().BeTrue("Sunday is a working day in this calendar");
            calendar.IsBusinessDay(Monday).Should().BeFalse("it is listed as a holiday");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileLoad_MissingFile_FallsBackToWeekendsOnlyDefault()
    {
        var root = CreateTempRoot();
        try
        {
            var calendar = FileAccountingCalendar.Load(root, NullLogger.Instance);

            calendar.IsBusinessDay(Saturday).Should().BeFalse();
            calendar.IsBusinessDay(Monday).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileLoad_MalformedFile_FallsBackToWeekendsOnlyDefault()
    {
        var root = CreateTempRoot();
        try
        {
            WriteCalendar(root, "{ not json ");

            var calendar = FileAccountingCalendar.Load(root, NullLogger.Instance);

            calendar.IsBusinessDay(Monday).Should().BeTrue();
            calendar.IsBusinessDay(Saturday).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-calendar-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteCalendar(string root, string json)
    {
        var directory = Path.Combine(root, "reconciliation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "business-calendar.json"), json);
    }
}
