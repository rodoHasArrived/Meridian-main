using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins business-day adjustment in the schedule kernel: payment conventions and FpML payment lags
/// counted from the accrual end, swap-style adjusted accrual, calendar evidence, coverage diagnostics
/// and collapsed periods.
/// </summary>
public sealed class ScheduleAdjustmentTests
{
    private static readonly IBusinessDayCalendar Weekends = BusinessDayCalendars.WeekendsOnly;

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static ScheduleSpec Quarterly2026()
        => ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3));

    private static ScheduleGenerationResult Ok(ScheduleSpec spec)
    {
        var result = ScheduleGenerator.Generate(spec);
        result.Succeeded.Should().BeTrue(result.Failure?.Message);
        return result;
    }

    private static DateOnly[] Payments(ScheduleGenerationResult result) => result.Periods.Select(period => period.PaymentDate).ToArray();

    [Fact]
    public void PaymentConventions_LeaveUnadjustedAccrualAlone()
    {
        var modifiedFollowing = Ok(Quarterly2026() with { PaymentAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, Weekends) });
        var following = Ok(Quarterly2026() with { PaymentAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.Following, Weekends) });

        Payments(modifiedFollowing).Should().Equal(D(2026, 4, 30), D(2026, 7, 31), D(2026, 10, 30), D(2027, 1, 29));
        Payments(following).Should().Equal(D(2026, 4, 30), D(2026, 7, 31), D(2026, 11, 2), D(2027, 2, 1));
        foreach (var result in new[] { modifiedFollowing, following })
        {
            result.Periods.Should().OnlyContain(period =>
                period.AccrualStart == period.UnadjustedStart && period.AccrualEnd == period.UnadjustedEnd);
            result.CalendarsUsed.Should().Equal(new ScheduleCalendarUsage("WEEKENDS", Weekends.ContentHash, ScheduleCalendarPurpose.Payment));
        }
    }

    [Theory]
    [InlineData(BusinessDayConvention.ModifiedFollowing)]
    [InlineData(BusinessDayConvention.Following)]
    [InlineData(BusinessDayConvention.Unadjusted)]
    public void PaymentLag_CountsBusinessDaysFromTheAccrualEnd(BusinessDayConvention convention)
    {
        // Adjusting the end first and then lagging would give 2026-11-04 and 2027-02-03 under Following.
        var result = Ok(Quarterly2026() with
        {
            PaymentAdjustment = new BusinessDayAdjustment(convention, Weekends),
            PaymentLagBusinessDays = 2,
        });

        Payments(result).Should().Equal(D(2026, 5, 4), D(2026, 8, 4), D(2026, 11, 3), D(2027, 2, 2));
    }

    [Fact]
    public void SwapStyle_AdjustedAccrualAndLaggedPayment()
    {
        var modifiedFollowing = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, Weekends);
        var result = Ok(new ScheduleSpec(
            D(2025, 1, 31),
            D(2026, 1, 31),
            ScheduleTenor.Months(3),
            Roll: ScheduleRoll.EndOfMonth,
            AccrualAdjustment: modifiedFollowing,
            PaymentAdjustment: modifiedFollowing,
            PaymentLagBusinessDays: 2));

        result.Periods.Select(period => period.UnadjustedEnd).Should().Equal(D(2025, 4, 30), D(2025, 7, 31), D(2025, 10, 31), D(2026, 1, 31));
        result.Periods.Select(period => period.AccrualEnd).Should().Equal(D(2025, 4, 30), D(2025, 7, 31), D(2025, 10, 31), D(2026, 1, 30));
        Payments(result).Should().Equal(D(2025, 5, 2), D(2025, 8, 4), D(2025, 11, 4), D(2026, 2, 3));
        result.Periods.Select(period => DayCountConventions.Days(DayCountConvention.Actual360, period.AccrualStart, period.AccrualEnd))
            .Should().Equal(89, 92, 92, 91);
        SchedulePeriodId.PeriodKey(result.Periods[^1]).Should().EndWith("2026-01-31");
        result.CalendarsUsed.Select(usage => usage.Purpose).Should().BeEquivalentTo(
            [ScheduleCalendarPurpose.Accrual, ScheduleCalendarPurpose.EffectiveDate, ScheduleCalendarPurpose.TerminationDate, ScheduleCalendarPurpose.Payment]);
        result.CalendarsUsed.Should().OnlyContain(usage => usage.CalendarId == "WEEKENDS" && usage.ContentHash == Weekends.ContentHash);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void AdjustedDates_AreDerivedFromTheUnadjustedGrid()
    {
        // 2026-01-31 is a Saturday: the effective date rolls but later boundaries stay on the grid.
        var result = Ok(Quarterly2026() with { AccrualAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.Following, Weekends) });

        result.Periods[0].AccrualStart.Should().Be(D(2026, 2, 2));
        result.Periods.Select(period => period.AccrualEnd).Should().Equal(D(2026, 4, 30), D(2026, 7, 31), D(2026, 11, 2), D(2027, 2, 1));
        result.Periods.Select(period => period.UnadjustedEnd).Should().Equal(D(2026, 4, 30), D(2026, 7, 31), D(2026, 10, 31), D(2027, 1, 31));
        result.Periods.Should().OnlyContain(period => period.PaymentDate == period.AccrualEnd);
    }

    [Fact]
    public void TerminationDateAdjustment_OverridesAccrualAtTheLastBoundaryOnly()
    {
        var result = Ok(Quarterly2026() with
        {
            AccrualAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, Weekends),
            EffectiveDateAdjustment = BusinessDayAdjustment.Unadjusted,
            TerminationDateAdjustment = BusinessDayAdjustment.Unadjusted,
        });

        result.Periods[0].AccrualStart.Should().Be(D(2026, 1, 31));
        result.Periods.Select(period => period.AccrualEnd).Should().Equal(D(2026, 4, 30), D(2026, 7, 31), D(2026, 10, 30), D(2027, 1, 31));
        result.CalendarsUsed.Select(usage => usage.Purpose).Should().Equal(ScheduleCalendarPurpose.Accrual);
    }

    [Fact]
    public void CalendarCoverageExceeded_IsADiagnosticNotAFailure()
    {
        var calendar = new HolidayBusinessDayCalendar("TEST-COV-2025", 1, coverage: new CalendarCoverage(D(2025, 1, 1), D(2025, 12, 31)));
        var following = BusinessDayAdjustment.Of(BusinessDayConvention.Following, calendar);

        var result = Ok(new ScheduleSpec(
            D(2026, 3, 2),
            D(2026, 6, 2),
            ScheduleTenor.Months(3),
            AccrualAdjustment: following,
            PaymentAdjustment: following));

        result.Diagnostics.Should().HaveCount(2, "each (calendar, date) is reported once");
        result.Diagnostics.Should().OnlyContain(diagnostic =>
            diagnostic.Code == ScheduleDiagnosticCode.CalendarCoverageExceeded && diagnostic.CalendarId == "TEST-COV-2025");
        result.Diagnostics.Select(diagnostic => diagnostic.Date).Should().Equal(D(2026, 3, 2), D(2026, 6, 2));
        result.Periods.Should().ContainSingle();
    }

    [Fact]
    public void CoveredDates_RaiseNoDiagnostic()
    {
        var calendar = new HolidayBusinessDayCalendar("TEST-COV-2026", 1, coverage: new CalendarCoverage(D(2026, 1, 1), D(2026, 12, 31)));

        var result = Ok(new ScheduleSpec(
            D(2026, 3, 2),
            D(2026, 6, 2),
            ScheduleTenor.Months(3),
            AccrualAdjustment: BusinessDayAdjustment.Of(BusinessDayConvention.Following, calendar)));

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void HolidaysThatCollapseAPeriod_AreRefused()
    {
        // 2025-01-13 under Preceding walks back over a holiday run and a weekend to 2025-01-06, the
        // first period's own start.
        var calendar = new HolidayBusinessDayCalendar(
            "TEST-COLLAPSE",
            1,
            [D(2025, 1, 7), D(2025, 1, 8), D(2025, 1, 9), D(2025, 1, 10), D(2025, 1, 13)]);

        var result = ScheduleGenerator.Generate(new ScheduleSpec(
            D(2025, 1, 6),
            D(2025, 1, 27),
            ScheduleTenor.Weeks(1),
            AccrualAdjustment: BusinessDayAdjustment.Of(BusinessDayConvention.Preceding, calendar)));

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be(ScheduleFailureCode.AdjustedPeriodCollapsed);
        result.Failure.Date.Should().Be(D(2025, 1, 13));
        result.Periods.Should().BeEmpty();
    }

    [Fact]
    public void DegenerateCalendar_IsAFailureNotAnException()
    {
        var everyDay = Enumerable.Range(0, 4100).Select(offset => D(2026, 1, 1).AddDays(offset));
        var calendar = new HolidayBusinessDayCalendar("TEST-DEGENERATE", 1, everyDay);

        var result = ScheduleGenerator.Generate(Quarterly2026() with
        {
            PaymentAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.Following, calendar),
        });

        result.Succeeded.Should().BeFalse();
        result.Failure!.Code.Should().Be(ScheduleFailureCode.BusinessDayAdjustmentFailed);
    }
}
