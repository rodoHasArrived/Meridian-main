using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;

namespace Meridian.Tests.Application;

public sealed class ScheduleSpecFactoryTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static readonly DateOnly End = new(2027, 1, 31);
    private static readonly IBusinessDayCalendarSource Calendars = new StaticBusinessDayCalendarSource(
        [new HolidayBusinessDayCalendar("TEST", 1, [new DateOnly(2026, 5, 1)])]);

    [Fact]
    public void DeclaredDates_SeparateAccrualFromAdjustedAndLaggedPayment()
    {
        var plan = ScheduleSpecFactory.Create(JsonSerializer.SerializeToElement(new
        {
            businessDayConvention = "Following", paymentCalendars = new[] { "TEST" },
            paymentLagBusinessDays = 1, couponRateUnit = "Percent", couponCashBasis = "DayCount"
        }), Start, End, "Quarterly", Calendars)!;
        var first = plan.Schedule.Periods[0];
        first.AccrualEnd.Should().Be(new DateOnly(2026, 4, 30));
        first.PaymentDate.Should().Be(new DateOnly(2026, 5, 4));
        plan.Schedule.CalendarsUsed.Should().NotBeEmpty();
        plan.RateIsPercent.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"paymentCalendars\":[\"MISSING\"],\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\"}")]
    [InlineData("{\"businessDayConvention\":\"Following\",\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\"}")]
    [InlineData("{\"couponRateUnit\":\"Guess\",\"couponCashBasis\":\"Level\"}")]
    [InlineData("{\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\",\"paymentLagBusinessDays\":1.5}")]
    [InlineData("{\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\",\"accrualAdjusted\":\"false\"}")]
    [InlineData("{\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\",\"direction\":\"99\"}")]
    [InlineData("{\"couponRateUnit\":\"Percent\",\"couponCashBasis\":\"Level\",\"paymentCalendars\":[null]}")]
    [InlineData("{\"couponRateUnit\":\"Percent\",\"CouponRateUnit\":\"Fraction\",\"couponCashBasis\":\"Level\"}")]
    public void InvalidDeclaration_RefusesInsteadOfFallingBack(string json)
    {
        var action = () => ScheduleSpecFactory.Create(JsonSerializer.Deserialize<JsonElement>(json), Start, End, "Quarterly", Calendars);
        action.Should().Throw<InvalidOperationException>().WithMessage("Declared schedule blocked:*");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"couponCashBasis\":null}")]
    public void UnsetDeclaration_LeavesLegacyPathSelected(string json)
        => ScheduleSpecFactory.Create(JsonSerializer.Deserialize<JsonElement>(json), Start, End, "Quarterly", Calendars).Should().BeNull();

    [Fact]
    public void UnknownFrequency_DoesNotBecomeSemiAnnual()
    {
        var block = JsonSerializer.SerializeToElement(new { couponRateUnit = "Percent", couponCashBasis = "Level" });
        var action = () => ScheduleSpecFactory.Create(block, Start, End, "FortnightsMaybe", Calendars);
        action.Should().Throw<InvalidOperationException>().WithMessage("*frequency*");
    }

    [Fact]
    public void CalendarCoverageGap_BlocksProjection()
    {
        var calendars = new StaticBusinessDayCalendarSource([new HolidayBusinessDayCalendar("SHORT", 1,
            coverage: new CalendarCoverage(Start, Start.AddMonths(3)))]);
        var block = JsonSerializer.SerializeToElement(new
        {
            businessDayConvention = "Following", paymentCalendars = new[] { "SHORT" },
            couponRateUnit = "Percent", couponCashBasis = "DayCount"
        });
        var action = () => ScheduleSpecFactory.Create(block, Start, End, "Quarterly", calendars);
        action.Should().Throw<InvalidOperationException>().WithMessage("*coverage*");
    }
}
