using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins period day counts taken from generated schedules: the context a period implies, exact ICMA
/// stubs from kernel reference periods, the 30E/360 (ISDA) termination carve-out, Business/252 from
/// the schedule's calendar, and refusals for adjusted ICMA and out-of-period windows.
/// </summary>
public sealed class ScheduleDayCountTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static ScheduleGenerationResult Ok(ScheduleSpec spec)
    {
        var result = ScheduleGenerator.Generate(spec);
        result.Succeeded.Should().BeTrue(result.Failure?.Message);
        return result;
    }

    private static decimal PeriodFraction(DayCountConvention convention, ScheduleGenerationResult schedule, int index)
    {
        ScheduleDayCount.TryPeriodFraction(convention, schedule.Periods[index], schedule, out var fraction, out var refusal)
            .Should().BeTrue(refusal);
        return fraction;
    }

    private static decimal Cash(decimal fraction) => Math.Round(1_000_000m * 0.05m * fraction, 4, MidpointRounding.AwayFromZero);

    [Fact]
    public void RegeneratedSchedule_PeriodsAreValueEqual_AndAcceptedByTheOriginalSchedule()
    {
        // Schedules are regenerated rather than stored, so a period from a second generation of the
        // same spec must equal its twin and be accepted against the first generation.
        var spec = ScheduleSpec.BackwardFromTermination(
            new DateOnly(2024, 11, 1), new DateOnly(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.FixedDay(15), ScheduleStubPolicy.Long);
        var first = ScheduleGenerator.Generate(spec);
        var second = ScheduleGenerator.Generate(spec);

        first.Succeeded.Should().BeTrue();
        second.Periods.Should().Equal(first.Periods);
        second.Periods.Select(static p => p.GetHashCode()).Should().Equal(first.Periods.Select(static p => p.GetHashCode()));
        second.Periods[0].ReferencePeriods.Should().HaveCount(2, "the long initial stub references two notional periods");
        ReferenceEquals(second.Periods[0].ReferencePeriods, first.Periods[0].ReferencePeriods).Should().BeFalse();

        foreach (var period in second.Periods)
        {
            ScheduleDayCount.TryPeriodFraction(DayCountConvention.ActualActualIcma, period, first, out var fraction, out var refusal)
                .Should().BeTrue(refusal);
            fraction.Should().BePositive();
            ScheduleDayCount.ContextFor(period, first).ReferencePeriods.Should().Equal(period.ReferencePeriods);
        }

        (second.Periods[0] with { ReferencePeriods = [second.Periods[0].ReferencePeriods[0]] })
            .Should().NotBe(first.Periods[0], "a different reference frame is a different period");
    }

    [Fact]
    public void ContextFor_CarriesReferencesFrequencyTerminationAndCalendar()
    {
        var calendar = BusinessDayCalendars.WeekendsOnly;
        var schedule = Ok(ScheduleSpec.BackwardFromTermination(D(2026, 8, 1), D(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.AnchorDay) with
        {
            PaymentAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.Following, calendar),
        });

        var context = ScheduleDayCount.ContextFor(schedule.Periods[0], schedule);

        context.ReferencePeriods.Should().BeSameAs(schedule.Periods[0].ReferencePeriods);
        context.PeriodsPerYear.Should().Be(2);
        context.TerminationDate.Should().Be(D(2027, 6, 15));
        context.IsBusinessDay.Should().NotBeNull("the payment calendar is the fallback predicate");
        context.IsBusinessDay!(D(2026, 8, 1)).Should().BeFalse("2026-08-01 is a Saturday");
    }

    [Fact]
    public void Icma_FrontStubsFromBackwardSchedules()
    {
        var shortFront = Ok(ScheduleSpec.BackwardFromTermination(D(2026, 8, 1), D(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.AnchorDay));
        var longFront = Ok(ScheduleSpec.BackwardFromTermination(
            D(2026, 5, 1), D(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.AnchorDay, ScheduleStubPolicy.Long));

        PeriodFraction(DayCountConvention.ActualActualIcma, shortFront, 0).Should().Be(136m / 366m);
        PeriodFraction(DayCountConvention.ActualActualIcma, shortFront, 1).Should().Be(0.5m);
        PeriodFraction(DayCountConvention.ActualActualIcma, longFront, 0).Should().Be((45m / 364m) + (183m / 366m));
    }

    [Fact]
    public void Icma_StubsAgainstIsdaOnAMillionAtFivePercent()
    {
        var backward = Ok(ScheduleSpec.BackwardFromTermination(D(2025, 3, 10), D(2030, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.FixedDay(15)));
        var explicitLong = Ok(new ScheduleSpec(
            D(2024, 11, 1),
            D(2027, 6, 15),
            ScheduleTenor.Months(6),
            Roll: ScheduleRoll.FixedDay(15),
            FirstRegularDate: D(2025, 6, 15)));

        var stub = PeriodFraction(DayCountConvention.ActualActualIcma, backward, 0);
        stub.Should().Be(97m / 364m);
        Cash(stub).Should().Be(13_324.1758m);

        var longStub = PeriodFraction(DayCountConvention.ActualActualIcma, explicitLong, 0);
        longStub.Should().Be((44m / 366m) + (182m / 364m));
        Cash(longStub).Should().Be(31_010.9290m);

        var secondPeriod = backward.Periods[1];
        (secondPeriod.UnadjustedStart, secondPeriod.UnadjustedEnd).Should().Be((D(2025, 6, 15), D(2025, 12, 15)));
        Cash(PeriodFraction(DayCountConvention.ActualActualIsda, backward, 1)).Should().Be(25_068.4932m);
        Cash(PeriodFraction(DayCountConvention.ActualActualIcma, backward, 1)).Should().Be(25_000.0000m);
    }

    [Fact]
    public void ThirtyE360Isda_UsesTheScheduleTermination()
    {
        var toLeapFebruary = Ok(ScheduleSpec.BackwardFromTermination(D(2027, 8, 31), D(2028, 2, 29), ScheduleTenor.Months(6), ScheduleRoll.EndOfMonth));
        var throughLeapFebruary = Ok(new ScheduleSpec(D(2027, 8, 31), D(2028, 8, 31), ScheduleTenor.Months(6), Roll: ScheduleRoll.EndOfMonth));
        var toFebruary = Ok(ScheduleSpec.BackwardFromTermination(D(2025, 8, 31), D(2026, 2, 28), ScheduleTenor.Months(6), ScheduleRoll.EndOfMonth));

        PeriodFraction(DayCountConvention.ThirtyE360Isda, toLeapFebruary, 0).Should().Be(179m / 360m);
        (throughLeapFebruary.Periods[0].UnadjustedEnd).Should().Be(D(2028, 2, 29));
        PeriodFraction(DayCountConvention.ThirtyE360Isda, throughLeapFebruary, 0).Should().Be(180m / 360m);
        PeriodFraction(DayCountConvention.ThirtyE360Isda, toFebruary, 0).Should().Be(178m / 360m);
    }

    [Fact]
    public void Business252_UsesTheAccrualCalendarAndRefusesWithoutOne()
    {
        var withCalendar = Ok(new ScheduleSpec(
            D(2025, 1, 1),
            D(2025, 2, 1),
            ScheduleTenor.Months(1),
            AccrualAdjustment: new BusinessDayAdjustment(BusinessDayConvention.Unadjusted, BusinessDayCalendars.WeekendsOnly)));
        var withoutCalendar = Ok(ScheduleSpec.IssueAnchoredForward(D(2025, 1, 1), D(2025, 2, 1), ScheduleTenor.Months(1)));

        PeriodFraction(DayCountConvention.Business252, withCalendar, 0).Should().Be(23m / 252m);
        ScheduleDayCount.TryPeriodFraction(DayCountConvention.Business252, withoutCalendar.Periods[0], withoutCalendar, out _, out var refusal)
            .Should().BeFalse();
        refusal.Should().Be("Business/252 requires a business-day calendar.");
    }

    [Fact]
    public void Icma_IsRefusedOnAnAccrualAdjustedSchedule()
    {
        var adjusted = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)) with
        {
            AccrualAdjustment = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, BusinessDayCalendars.WeekendsOnly),
        });

        ScheduleDayCount.TryPeriodFraction(DayCountConvention.ActualActualIcma, adjusted.Periods[2], adjusted, out _, out var refusal)
            .Should().BeFalse();
        refusal.Should().Contain("unadjusted");
        PeriodFraction(DayCountConvention.Actual360, adjusted, 2).Should().Be(91m / 360m);
    }

    [Fact]
    public void Window_MustLieWithinThePeriod()
    {
        var schedule = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)));
        var period = schedule.Periods[1];

        ScheduleDayCount.TryFraction(DayCountConvention.ActualActualIcma, period, schedule, D(2026, 5, 1), D(2026, 6, 1), out var inside, out _)
            .Should().BeTrue();
        inside.Should().Be(31m / (4m * 92m));
        ScheduleDayCount.TryFraction(DayCountConvention.Actual360, period, schedule, D(2026, 4, 29), D(2026, 6, 1), out _, out var before)
            .Should().BeFalse();
        before.Should().NotBeNullOrWhiteSpace();
        ScheduleDayCount.TryFraction(DayCountConvention.Actual360, period, schedule, D(2026, 5, 1), D(2026, 8, 1), out _, out var after)
            .Should().BeFalse();
        after.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeriodFromAnotherSchedule_IsRefused()
    {
        var schedule = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)));
        var other = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 15), D(2027, 1, 15), ScheduleTenor.Months(3)));

        ScheduleDayCount.TryPeriodFraction(DayCountConvention.Actual360, other.Periods[0], schedule, out _, out var refusal)
            .Should().BeFalse();
        refusal.Should().NotBeNullOrWhiteSpace();
        var act = () => ScheduleDayCount.ContextFor(other.Periods[0], schedule);
        act.Should().Throw<ArgumentException>();
    }
}
