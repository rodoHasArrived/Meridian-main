using FluentAssertions;
using Meridian.Contracts.Calendars;

namespace Meridian.Tests.Calendars;

/// <summary>
/// Pins the four FpML business-day conventions on the weekends-only calendar, including the month
/// boundary that makes the modified conventions turn back.
/// </summary>
public sealed class BusinessDayAdjustmentTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DateOnly Apply(BusinessDayConvention convention, DateOnly date)
        => BusinessDayAdjustment.Of(convention, BusinessDayCalendars.WeekendsOnly).Apply(date);

    [Theory]
    [InlineData(BusinessDayConvention.Following, 2026, 2, 2)]
    [InlineData(BusinessDayConvention.ModifiedFollowing, 2026, 1, 30)]
    [InlineData(BusinessDayConvention.Preceding, 2026, 1, 30)]
    [InlineData(BusinessDayConvention.ModifiedPreceding, 2026, 1, 30)]
    [InlineData(BusinessDayConvention.Unadjusted, 2026, 1, 31)]
    public void SaturdayAtMonthEnd(BusinessDayConvention convention, int y, int m, int d)
        => Apply(convention, D(2026, 1, 31)).Should().Be(D(y, m, d));

    [Theory]
    [InlineData(BusinessDayConvention.Preceding, 2026, 7, 31)]
    [InlineData(BusinessDayConvention.ModifiedPreceding, 2026, 8, 3)]
    [InlineData(BusinessDayConvention.ModifiedFollowing, 2026, 8, 3)]
    [InlineData(BusinessDayConvention.Following, 2026, 8, 3)]
    public void SaturdayAtMonthStart(BusinessDayConvention convention, int y, int m, int d)
        => Apply(convention, D(2026, 8, 1)).Should().Be(D(y, m, d));

    [Fact]
    public void BusinessDay_IsUnchangedByEveryConvention()
    {
        foreach (var convention in Enum.GetValues<BusinessDayConvention>())
        {
            Apply(convention, D(2026, 1, 30)).Should().Be(D(2026, 1, 30), convention.ToString());
        }
    }

    [Fact]
    public void Unadjusted_WithoutCalendar_ReturnsTheDate()
    {
        BusinessDayAdjustment.Unadjusted.IsUnadjusted.Should().BeTrue();
        BusinessDayAdjustment.Unadjusted.Apply(D(2026, 1, 31)).Should().Be(D(2026, 1, 31));
        new BusinessDayAdjustment(BusinessDayConvention.Unadjusted, null).Apply(D(2026, 1, 31)).Should().Be(D(2026, 1, 31));
    }

    [Fact]
    public void Following_WithoutCalendar_Throws()
    {
        var act = () => new BusinessDayAdjustment(BusinessDayConvention.Following, null).Apply(D(2026, 1, 31));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Of_RequiresCalendar()
    {
        var act = () => BusinessDayAdjustment.Of(BusinessDayConvention.Following, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_ComparesCalendarsByReference()
    {
        var first = new HolidayBusinessDayCalendar("TEST-EQ", 1);
        var second = new HolidayBusinessDayCalendar("TEST-EQ", 1);

        BusinessDayAdjustment.Of(BusinessDayConvention.Following, first)
            .Should().Be(BusinessDayAdjustment.Of(BusinessDayConvention.Following, first));
        BusinessDayAdjustment.Of(BusinessDayConvention.Following, first)
            .Should().NotBe(BusinessDayAdjustment.Of(BusinessDayConvention.Following, second));
    }
}
