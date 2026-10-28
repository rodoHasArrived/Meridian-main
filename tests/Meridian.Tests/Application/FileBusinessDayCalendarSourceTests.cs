using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;

namespace Meridian.Tests.Application;

public sealed class FileBusinessDayCalendarSourceTests
{
    [Fact]
    public void LoadedFile_IsImmutableAndResolvesHolidayAndJointCalendars()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                [{"calendarId":"TEST","version":1,"holidays":["2026-05-01"],
                  "coverageFrom":"2026-01-01","coverageTo":"2026-12-31"}]
                """);
            var source = FileBusinessDayCalendarSource.Load(path);
            File.WriteAllText(path, "[]");
            source.TryResolve(["TEST", "WEEKENDS"], out var calendar).Should().BeTrue();
            calendar!.IsBusinessDay(new DateOnly(2026, 5, 1)).Should().BeFalse();
            calendar.IsBusinessDay(new DateOnly(2026, 5, 4)).Should().BeTrue();
            source.TryResolve(["UNKNOWN"], out _).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"calendarId\":\"TEST\",\"version\":1,\"holidays\":[]}]")]
    [InlineData("[{\"calendarId\":\"TEST\",\"version\":1,\"holidays\":[],\"coverageFrom\":\"2026-12-31\",\"coverageTo\":\"2026-01-01\"}]")]
    public void InvalidFile_DoesNotSubstituteWeekendCalendar(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var action = () => FileBusinessDayCalendarSource.Load(path);
            action.Should().Throw<Exception>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnconfiguredSource_ResolvesOnlyExplicitWeekendCalendar()
    {
        var source = FileBusinessDayCalendarSource.Load(null);
        source.TryGet(BusinessDayCalendars.WeekendsOnlyId, out _).Should().BeTrue();
        source.TryGet("USD", out _).Should().BeFalse();
    }
}
