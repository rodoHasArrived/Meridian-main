using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

// Part 3: rolls, refusals, range edges, RollDates and the year/month equivalence.
public sealed partial class ScheduleGeneratorTests
{
    [Fact]
    public void EndOfMonthRoll_QuarterlyFromLeapDay()
    {
        var result = Ok(new ScheduleSpec(D(2024, 2, 29), D(2026, 2, 28), ScheduleTenor.Months(3), Roll: ScheduleRoll.EndOfMonth));

        Ends(result).Should().Equal(
            D(2024, 5, 31), D(2024, 8, 31), D(2024, 11, 30), D(2025, 2, 28),
            D(2025, 5, 31), D(2025, 8, 31), D(2025, 11, 30), D(2026, 2, 28));
    }

    [Fact]
    public void FixedDayRoll_RequireRegular_ClampsOnlyFebruary()
    {
        var result = Ok(new ScheduleSpec(
            D(2024, 2, 29), D(2026, 2, 28), ScheduleTenor.Months(3), Roll: ScheduleRoll.FixedDay(29), StubPolicy: ScheduleStubPolicy.RequireRegular));

        Ends(result).Should().Equal(
            D(2024, 5, 29), D(2024, 8, 29), D(2024, 11, 29), D(2025, 2, 28),
            D(2025, 5, 29), D(2025, 8, 29), D(2025, 11, 29), D(2026, 2, 28));
    }

    [Fact]
    public void ImmRoll_ThirdWednesdays()
    {
        var result = Ok(new ScheduleSpec(D(2025, 3, 19), D(2026, 3, 18), ScheduleTenor.Months(3), Roll: ScheduleRoll.Imm));

        Ends(result).Should().Equal(D(2025, 6, 18), D(2025, 9, 17), D(2025, 12, 17), D(2026, 3, 18));
        result.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void ForAnchor_ChoosesEndOfMonthOnlyWhenTheRuleIsSet()
    {
        ScheduleRoll.ForAnchor(D(2024, 2, 29), endOfMonthRule: true).Should().Be(ScheduleRoll.EndOfMonth);
        ScheduleRoll.ForAnchor(D(2024, 2, 29), endOfMonthRule: false).Should().Be(ScheduleRoll.FixedDay(29));
        ScheduleRoll.ForAnchor(D(2024, 2, 15), endOfMonthRule: true).Should().Be(ScheduleRoll.FixedDay(15));
        default(ScheduleRoll).Should().Be(ScheduleRoll.AnchorDay);
        var outOfRange = () => ScheduleRoll.FixedDay(32);
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Refusals_AnchorNotOnRollDay()
    {
        Refused(new ScheduleSpec(D(2026, 1, 15), D(2027, 1, 15), ScheduleTenor.Months(3), Roll: ScheduleRoll.FixedDay(20)), ScheduleFailureCode.AnchorNotOnRollDay)
            .Date.Should().Be(D(2026, 1, 15));
        Refused(new ScheduleSpec(D(2026, 1, 15), D(2027, 1, 15), ScheduleTenor.Months(3), Roll: ScheduleRoll.EndOfMonth), ScheduleFailureCode.AnchorNotOnRollDay);
        Refused(
            new ScheduleSpec(D(2025, 1, 1), D(2026, 3, 15), ScheduleTenor.Months(6), Roll: ScheduleRoll.FixedDay(15), FirstRegularDate: D(2025, 3, 14)),
            ScheduleFailureCode.AnchorNotOnRollDay).Date.Should().Be(D(2025, 3, 14));
    }

    [Fact]
    public void Refusals_RegularDatesAndModes()
    {
        Refused(
            new ScheduleSpec(D(2025, 1, 1), D(2026, 12, 31), ScheduleTenor.Months(6), FirstRegularDate: D(2025, 3, 15), LastRegularDate: D(2026, 8, 15)),
            ScheduleFailureCode.RegularDatesNotOnGrid).Date.Should().Be(D(2026, 8, 15));
        Refused(
            ScheduleSpec.CompoundedForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)) with { Roll = ScheduleRoll.EndOfMonth },
            ScheduleFailureCode.CompoundedModeRestricted);
        Refused(
            ScheduleSpec.CompoundedForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)) with { StubPolicy = ScheduleStubPolicy.Long },
            ScheduleFailureCode.CompoundedModeRestricted);
        Refused(
            new ScheduleSpec(D(2025, 3, 15), D(2026, 3, 15), ScheduleTenor.Months(6), FirstRegularDate: D(2025, 3, 1)),
            ScheduleFailureCode.RegularDateOutOfRange).Date.Should().Be(D(2025, 3, 1));
        Refused(
            new ScheduleSpec(D(2025, 1, 1), D(2026, 3, 15), ScheduleTenor.Months(6), Direction: ScheduleDirection.Backward, FirstRegularDate: D(2025, 3, 15)),
            ScheduleFailureCode.DirectionConflictsWithRegularDates);
        Refused(
            new ScheduleSpec(D(2025, 1, 1), D(2026, 3, 15), ScheduleTenor.Term, FirstRegularDate: D(2025, 3, 15)),
            ScheduleFailureCode.TermTenorDisallowsRegularDates);
        Refused(
            new ScheduleSpec(D(2025, 1, 6), D(2025, 2, 3), ScheduleTenor.Weeks(1), Direction: ScheduleDirection.Backward),
            ScheduleFailureCode.TenorNotAllowedForDirection);
        Refused(
            new ScheduleSpec(D(2025, 1, 6), D(2025, 2, 3), ScheduleTenor.Days(7), Roll: ScheduleRoll.FixedDay(6)),
            ScheduleFailureCode.RollNotAllowedForTenor);
        Refused(
            new ScheduleSpec(D(2025, 1, 6), D(2025, 2, 3), default),
            ScheduleFailureCode.InvalidSpecification);
        Refused(
            ScheduleSpec.IssueAnchoredForward(D(2025, 1, 6), D(2026, 1, 6), ScheduleTenor.Months(1)) with { MaxPeriods = 0 },
            ScheduleFailureCode.InvalidPeriodLimit);
        Refused(
            ScheduleSpec.IssueAnchoredForward(D(2025, 1, 6), D(2026, 1, 6), ScheduleTenor.Months(1)) with { PaymentLagBusinessDays = -1 },
            ScheduleFailureCode.NegativePaymentLag);
    }

    [Fact]
    public void Refusals_AdjustmentsNeedCalendars()
    {
        var spec = ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3));

        Refused(spec with { AccrualAdjustment = new BusinessDayAdjustment(BusinessDayConvention.ModifiedFollowing, null) }, ScheduleFailureCode.AdjustmentRequiresCalendar);
        Refused(spec with { PaymentAdjustment = new BusinessDayAdjustment(BusinessDayConvention.Following, null) }, ScheduleFailureCode.AdjustmentRequiresCalendar);
        Refused(spec with { PaymentLagBusinessDays = 2 }, ScheduleFailureCode.PaymentLagRequiresCalendar);
        Refused(
            spec with { PaymentLagBusinessDays = 2, PaymentAdjustment = BusinessDayAdjustment.Unadjusted },
            ScheduleFailureCode.PaymentLagRequiresCalendar);
    }

    [Fact]
    public void EmptySpan_IsAnEmptySuccessUnlessRegularIsRequired()
    {
        var empty = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2026, 1, 31), ScheduleTenor.Months(3)));
        empty.Succeeded.Should().BeTrue();
        empty.Periods.Should().BeEmpty();

        var inverted = ScheduleGenerator.Generate(ScheduleSpec.CompoundedForward(D(2026, 2, 1), D(2026, 1, 31), ScheduleTenor.Months(3)));
        inverted.Succeeded.Should().BeTrue();
        inverted.Periods.Should().BeEmpty();

        Refused(
            new ScheduleSpec(D(2026, 1, 31), D(2026, 1, 31), ScheduleTenor.Months(3), StubPolicy: ScheduleStubPolicy.RequireRegular),
            ScheduleFailureCode.DoesNotFitRegularGrid);
    }

    [Fact]
    public void TermTenor_GivesOneTermPeriod()
    {
        var result = Ok(new ScheduleSpec(D(2026, 1, 15), D(2029, 1, 15), ScheduleTenor.Term));

        result.Periods.Should().ContainSingle();
        var period = result.Periods[0];
        period.Kind.Should().Be(SchedulePeriodKind.Term);
        period.IsStub.Should().BeFalse();
        (period.UnadjustedStart, period.UnadjustedEnd).Should().Be((D(2026, 1, 15), D(2029, 1, 15)));
        period.ReferencePeriods.Should().Equal(R(D(2026, 1, 15), D(2029, 1, 15)));
    }

    [Fact]
    public void WeeklyTenor_GivesFourPeriods()
    {
        var result = Ok(new ScheduleSpec(D(2025, 1, 6), D(2025, 2, 3), ScheduleTenor.Weeks(1)));

        Ends(result).Should().Equal(D(2025, 1, 13), D(2025, 1, 20), D(2025, 1, 27), D(2025, 2, 3));
        result.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void PeriodLimit_RefusesPastTheLimitAndLegacyFactoriesHaveNone()
    {
        var legacy = ScheduleSpec.CompoundedForward(D(2000, 1, 1), D(2101, 1, 1), ScheduleTenor.Months(1));
        legacy.MaxPeriods.Should().BeNull();

        Refused(legacy with { MaxPeriods = ScheduleSpec.DefaultMaxPeriods }, ScheduleFailureCode.PeriodLimitExceeded);
        Ok(legacy).Periods.Should().HaveCount(101 * 12);
        Ok(legacy with { MaxPeriods = 101 * 12 }).Periods.Should().HaveCount(101 * 12);
    }

    [Fact]
    public void GridBeyondTheDateRange_ClosesAtTerminationWithoutThrowing()
    {
        var anchored = Ok(ScheduleSpec.IssueAnchoredForward(D(9998, 12, 15), D(9999, 12, 20), ScheduleTenor.Months(12)));
        Spans(anchored).Should().Equal((D(9998, 12, 15), D(9999, 12, 15)), (D(9999, 12, 15), D(9999, 12, 20)));
        anchored.Periods[^1].Kind.Should().Be(SchedulePeriodKind.ShortFinalStub);
        anchored.Periods[^1].ReferencePeriods.Should().BeEmpty("the notional period ends beyond the supported date range");

        var compounded = Ok(ScheduleSpec.CompoundedForward(D(9999, 9, 30), D(9999, 12, 31), ScheduleTenor.Months(6)));
        Spans(compounded).Should().Equal((D(9999, 9, 30), D(9999, 12, 31)));

        var backward = Ok(ScheduleSpec.BackwardFromTermination(D(1, 1, 5), D(1, 12, 15), ScheduleTenor.Months(12), ScheduleRoll.FixedDay(15)));
        Spans(backward).Should().Equal((D(1, 1, 5), D(1, 12, 15)));
        backward.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        backward.Periods[0].ReferencePeriods.Should().BeEmpty();
    }

    [Fact]
    public void RollDates_CompoundedDriftsAnchoredDoesNot()
    {
        ScheduleGenerator.RollDates(D(2026, 1, 31), ScheduleTenor.Months(1), ScheduleAnchorMode.Compounded).Take(4)
            .Should().Equal(D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 28), D(2026, 4, 28));
        ScheduleGenerator.RollDates(D(2026, 1, 31), ScheduleTenor.Months(1), ScheduleAnchorMode.Anchored).Take(4)
            .Should().Equal(D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31), D(2026, 4, 30));
        ScheduleGenerator.RollDates(D(2026, 1, 15), ScheduleTenor.Months(1), ScheduleAnchorMode.Anchored, ScheduleRoll.EndOfMonth).Take(3)
            .Should().Equal(D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31));
        ScheduleGenerator.RollDates(D(9999, 10, 31), ScheduleTenor.Months(1), ScheduleAnchorMode.Anchored)
            .Should().Equal(D(9999, 10, 31), D(9999, 11, 30), D(9999, 12, 31));
        ScheduleGenerator.RollDates(D(9999, 12, 29), ScheduleTenor.Days(1), ScheduleAnchorMode.Compounded)
            .Should().Equal(D(9999, 12, 29), D(9999, 12, 30), D(9999, 12, 31));
    }

    [Fact]
    public void RollDates_RefusesEagerly()
    {
        var term = () => ScheduleGenerator.RollDates(D(2026, 1, 31), ScheduleTenor.Term, ScheduleAnchorMode.Anchored);
        var compoundedRoll = () => ScheduleGenerator.RollDates(D(2026, 1, 31), ScheduleTenor.Months(1), ScheduleAnchorMode.Compounded, ScheduleRoll.EndOfMonth);

        term.Should().Throw<ArgumentException>();
        compoundedRoll.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void YearsTenor_EqualsTwelveMonthMultiples_ForEveryAnchor()
    {
        for (var anchor = D(2020, 1, 1); anchor <= D(2032, 12, 31); anchor = anchor.AddDays(1))
        {
            foreach (var years in new[] { 1, 2 })
            {
                var maturity = anchor.AddMonths((12 * years * 3) + 5);
                var asYears = ScheduleTenor.Years(years);
                var asMonths = ScheduleTenor.Months(12 * years);
                AssertSameShape(ScheduleSpec.IssueAnchoredForward(anchor, maturity, asYears), ScheduleSpec.IssueAnchoredForward(anchor, maturity, asMonths));
                AssertSameShape(ScheduleSpec.CompoundedForward(anchor, maturity, asYears), ScheduleSpec.CompoundedForward(anchor, maturity, asMonths));
                var roll = ScheduleRoll.ForAnchor(maturity, endOfMonthRule: true);
                AssertSameShape(
                    ScheduleSpec.BackwardFromTermination(anchor, maturity, asYears, roll),
                    ScheduleSpec.BackwardFromTermination(anchor, maturity, asMonths, roll));
            }
        }

        static void AssertSameShape(ScheduleSpec years, ScheduleSpec months)
        {
            var left = ScheduleGenerator.Generate(years);
            var right = ScheduleGenerator.Generate(months);
            left.Succeeded.Should().BeTrue();
            right.Succeeded.Should().BeTrue();
            left.Periods.Select(Shape).Should().Equal(right.Periods.Select(Shape), $"{years.EffectiveDate:yyyy-MM-dd} {years.Tenor} {years.Direction}");
        }

        static string Shape(SchedulePeriod period)
            => $"{period.UnadjustedStart:yyyy-MM-dd}|{period.UnadjustedEnd:yyyy-MM-dd}|{period.Kind}|"
                + string.Join(",", period.ReferencePeriods.Select(reference => $"{reference.Start:yyyy-MM-dd}/{reference.End:yyyy-MM-dd}"));
    }
}
