using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.Integrity;

namespace Meridian.Tests.Calendars;

/// <summary>
/// Pins the business-day calendar seam: the built-in weekends-only calendar, holiday rolls, the
/// signed (from, to] business-day count, joint calendars, content hashing and the static source.
/// </summary>
public sealed class BusinessDayCalendarTests
{
    private static readonly IBusinessDayCalendar Weekends = BusinessDayCalendars.WeekendsOnly;

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void WeekendsOnly_IsSaturdaySundayVersionOneUnbounded()
    {
        Weekends.CalendarId.Should().Be("WEEKENDS");
        Weekends.Coverage.Should().Be(CalendarCoverage.Unbounded);
        var holiday = Weekends.Should().BeOfType<HolidayBusinessDayCalendar>().Subject;
        holiday.Version.Should().Be(1);
        holiday.WeekendDays.Should().Equal(DayOfWeek.Sunday, DayOfWeek.Saturday);
        holiday.Holidays.Should().BeEmpty();

        Weekends.IsBusinessDay(D(2026, 1, 31)).Should().BeFalse("2026-01-31 is a Saturday");
        Weekends.IsBusinessDay(D(2026, 1, 30)).Should().BeTrue("2026-01-30 is a Friday");
    }

    [Fact]
    public void Holiday_RollsNextAndPreviousAroundIt()
    {
        var calendar = new HolidayBusinessDayCalendar("TEST-XMAS", 1, [D(2026, 12, 25)]);

        calendar.IsBusinessDay(D(2026, 12, 25)).Should().BeFalse();
        calendar.NextOrSameBusinessDay(D(2026, 12, 25)).Should().Be(D(2026, 12, 28));
        calendar.PreviousOrSameBusinessDay(D(2026, 12, 25)).Should().Be(D(2026, 12, 24));
        calendar.NextOrSameBusinessDay(D(2026, 12, 24)).Should().Be(D(2026, 12, 24));
    }

    [Theory]
    [InlineData(2026, 1, 30, 2, 2026, 2, 3)]
    [InlineData(2025, 5, 31, 1, 2025, 6, 2)]
    [InlineData(2025, 6, 2, -1, 2025, 5, 30)]
    [InlineData(2025, 6, 2, 0, 2025, 6, 2)]
    public void AddBusinessDays_CountsFromTheNextDay(int y, int m, int d, int businessDays, int ey, int em, int ed)
        => Weekends.AddBusinessDays(D(y, m, d), businessDays).Should().Be(D(ey, em, ed));

    [Fact]
    public void CountBusinessDaysBetween_IsSignedOverHalfOpenInterval()
    {
        Weekends.CountBusinessDaysBetween(D(2026, 1, 30), D(2026, 2, 2)).Should().Be(1);
        Weekends.CountBusinessDaysBetween(D(2026, 2, 2), D(2026, 1, 30)).Should().Be(-1);
        Weekends.CountBusinessDaysBetween(D(2026, 2, 2), D(2026, 2, 2)).Should().Be(0);
    }

    [Fact]
    public void SevenWeekendDays_Throws()
    {
        var act = () => new HolidayBusinessDayCalendar("TEST-NONE", 1, weekendDays: Enum.GetValues<DayOfWeek>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UndefinedWeekendDay_Throws()
    {
        var act = () => new HolidayBusinessDayCalendar("TEST-BAD", 1, weekendDays: [(DayOfWeek)7]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankId_Throws(string id)
    {
        var act = () => new HolidayBusinessDayCalendar(id, 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VersionBelowOne_Throws()
    {
        var act = () => new HolidayBusinessDayCalendar("TEST-V0", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DegenerateCalendar_ThrowsInsteadOfWalkingForever()
    {
        var everyDay = Enumerable.Range(0, D(2036, 12, 31).DayNumber - D(2026, 1, 1).DayNumber + 1)
            .Select(offset => D(2026, 1, 1).AddDays(offset));
        var calendar = new HolidayBusinessDayCalendar("TEST-DEGENERATE", 1, everyDay);

        var add = () => calendar.AddBusinessDays(D(2026, 1, 1), 1);
        var roll = () => calendar.NextOrSameBusinessDay(D(2026, 1, 1));

        add.Should().Throw<InvalidOperationException>();
        roll.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void JointCalendar_RequiresBusinessDayInEveryMember()
    {
        var a = new HolidayBusinessDayCalendar("TEST-A", 1, [D(2025, 11, 27)]);
        var b = new HolidayBusinessDayCalendar("TEST-B", 1, [D(2025, 12, 26)]);

        var joint = BusinessDayCalendars.Joint([a, b]);

        joint.Adjust(D(2025, 11, 27), BusinessDayConvention.Following).Should().Be(D(2025, 11, 28));
        joint.Adjust(D(2025, 12, 26), BusinessDayConvention.Following).Should().Be(D(2025, 12, 29));
        a.IsBusinessDay(D(2025, 12, 26)).Should().BeTrue("TEST-A alone does not observe TEST-B's holiday");
        joint.CalendarId.Should().Be("TEST-A+TEST-B");
        BusinessDayCalendars.Joint([b, a]).CalendarId.Should().Be("TEST-A+TEST-B");
        BusinessDayCalendars.Joint([b, a]).ContentHash.Should().Be(joint.ContentHash);
        Sha256Digest.IsCanonical(joint.ContentHash).Should().BeTrue();
    }

    [Fact]
    public void Joint_OfOneCalendar_IsThatCalendar()
        => BusinessDayCalendars.Joint([Weekends]).Should().BeSameAs(Weekends);

    [Fact]
    public void JointCalendar_RefusesDuplicateIdsAndSingleMembers()
    {
        var a = new HolidayBusinessDayCalendar("TEST-A", 1);
        var alsoA = new HolidayBusinessDayCalendar("test-a", 2);

        var duplicate = () => new JointBusinessDayCalendar([a, alsoA]);
        var single = () => new JointBusinessDayCalendar([a]);
        var none = () => BusinessDayCalendars.Joint([]);

        duplicate.Should().Throw<ArgumentException>();
        single.Should().Throw<ArgumentException>();
        none.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void JointCalendar_CoverageIsTheIntersection()
    {
        var a = new HolidayBusinessDayCalendar("TEST-A", 1, coverage: new CalendarCoverage(D(2024, 1, 1), D(2030, 12, 31)));
        var b = new HolidayBusinessDayCalendar("TEST-B", 1, coverage: new CalendarCoverage(D(2025, 1, 1), null));

        var joint = BusinessDayCalendars.Joint([a, b]);

        joint.Coverage.Should().Be(new CalendarCoverage(D(2025, 1, 1), D(2030, 12, 31)));
        joint.Coverage.Covers(D(2024, 6, 1)).Should().BeFalse();
        joint.Coverage.Covers(D(2030, 12, 31)).Should().BeTrue();
        new CalendarCoverage(D(2026, 1, 1), D(2026, 12, 31)).Intersect(new CalendarCoverage(D(2027, 1, 1), null))
            .Covers(D(2026, 6, 1)).Should().BeFalse("an empty intersection covers nothing");
    }

    [Fact]
    public void ContentHash_IsOrderIndependentAndTracksVersionAndCoverage()
    {
        var holidays = new[] { D(2026, 12, 25), D(2026, 1, 1), D(2026, 7, 3) };
        var calendar = new HolidayBusinessDayCalendar("TEST-H", 1, holidays);
        var reordered = new HolidayBusinessDayCalendar("TEST-H", 1, Enumerable.Reverse(holidays).Append(D(2026, 1, 1)), [DayOfWeek.Sunday, DayOfWeek.Saturday]);
        var bumped = new HolidayBusinessDayCalendar("TEST-H", 2, holidays);
        var bounded = new HolidayBusinessDayCalendar("TEST-H", 1, holidays, coverage: new CalendarCoverage(D(2026, 1, 1), D(2026, 12, 31)));

        reordered.ContentHash.Should().Be(calendar.ContentHash);
        reordered.Holidays.Should().Equal(D(2026, 1, 1), D(2026, 7, 3), D(2026, 12, 25));
        bumped.ContentHash.Should().NotBe(calendar.ContentHash);
        bounded.ContentHash.Should().NotBe(calendar.ContentHash);
        Sha256Digest.IsCanonical(calendar.ContentHash).Should().BeTrue();
        Sha256Digest.IsCanonical(Weekends.ContentHash).Should().BeTrue();
    }

    [Fact]
    public void StaticSource_ResolvesKnownIdsOnlyAndNeverFallsBackToWeekends()
    {
        var a = new HolidayBusinessDayCalendar("TEST-A", 1, [D(2025, 11, 27)]);
        var b = new HolidayBusinessDayCalendar("TEST-B", 1, [D(2025, 12, 26)]);
        var source = new StaticBusinessDayCalendarSource([a, b]);

        source.TryResolve([], out _).Should().BeFalse();
        source.TryResolve(["TEST-UNKNOWN"], out _).Should().BeFalse();
        source.TryResolve(["TEST-A", "TEST-UNKNOWN"], out _).Should().BeFalse();
        source.TryGet("WEEKENDS", out _).Should().BeFalse();

        source.TryGet("test-a", out var single).Should().BeTrue();
        single.Should().BeSameAs(a);
        source.TryResolve(["TEST-B"], out var one).Should().BeTrue();
        one.Should().BeSameAs(b);
        source.TryResolve(["TEST-B", "TEST-A"], out var joint).Should().BeTrue();
        joint!.CalendarId.Should().Be("TEST-A+TEST-B");
        joint.IsBusinessDay(D(2025, 11, 27)).Should().BeFalse();
        joint.IsBusinessDay(D(2025, 12, 26)).Should().BeFalse();
    }

    [Fact]
    public void StaticSource_DuplicateIdThrows()
    {
        var act = () => new StaticBusinessDayCalendarSource(
            [new HolidayBusinessDayCalendar("TEST-A", 1), new HolidayBusinessDayCalendar("TEST-A", 2)]);

        act.Should().Throw<ArgumentException>();
    }
}
