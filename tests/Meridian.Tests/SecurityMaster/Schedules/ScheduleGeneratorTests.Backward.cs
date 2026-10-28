using FluentAssertions;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

// Part 2: backward generation and explicit first and last regular dates.
public sealed partial class ScheduleGeneratorTests
{
    [Fact]
    public void Backward_ShortInitialStubReferencesTheQuasiCouponPeriod()
    {
        var result = Ok(ScheduleSpec.BackwardFromTermination(D(2026, 8, 1), D(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.AnchorDay));

        Spans(result).Should().Equal((D(2026, 8, 1), D(2026, 12, 15)), (D(2026, 12, 15), D(2027, 6, 15)));
        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2026, 6, 15), D(2026, 12, 15)));
        result.Periods[1].Kind.Should().Be(SchedulePeriodKind.Regular);
    }

    [Fact]
    public void Backward_LongInitialStubReferencesTwoNotionalPeriods()
    {
        var result = Ok(ScheduleSpec.BackwardFromTermination(
            D(2026, 5, 1), D(2027, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.AnchorDay, ScheduleStubPolicy.Long));

        Spans(result).Should().Equal((D(2026, 5, 1), D(2026, 12, 15)), (D(2026, 12, 15), D(2027, 6, 15)));
        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.LongInitialStub);
        result.Periods[0].ReferencePeriods.Should().Equal(
            R(D(2025, 12, 15), D(2026, 6, 15)),
            R(D(2026, 6, 15), D(2026, 12, 15)));
    }

    [Fact]
    public void Backward_EndOfMonthFromFebruaryMaturity_KeepsMonthEndsAndLeapQuasiCoupon()
    {
        var result = Ok(ScheduleSpec.BackwardFromTermination(D(2024, 5, 10), D(2026, 2, 28), ScheduleTenor.Months(6), ScheduleRoll.EndOfMonth));

        Ends(result).Should().Equal(D(2024, 8, 31), D(2025, 2, 28), D(2025, 8, 31), D(2026, 2, 28));
        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2024, 2, 29), D(2024, 8, 31)));
    }

    [Fact]
    public void Backward_FixedDay_ElevenPeriodsEndingRegular()
    {
        var result = Ok(ScheduleSpec.BackwardFromTermination(D(2025, 3, 10), D(2030, 6, 15), ScheduleTenor.Months(6), ScheduleRoll.FixedDay(15)));

        result.Periods.Should().HaveCount(11);
        var first = result.Periods[0];
        first.Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        (first.UnadjustedStart, first.UnadjustedEnd).Should().Be((D(2025, 3, 10), D(2025, 6, 15)));
        first.ReferencePeriods.Should().Equal(R(D(2024, 12, 15), D(2025, 6, 15)));
        var last = result.Periods[^1];
        last.Kind.Should().Be(SchedulePeriodKind.Regular);
        (last.UnadjustedStart, last.UnadjustedEnd).Should().Be((D(2029, 12, 15), D(2030, 6, 15)));
        last.IsFinal.Should().BeTrue();
        result.Periods.Skip(1).Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void ExplicitFirstRegularDate_ShortInitialStubOnEndOfMonthGrid()
    {
        var result = Ok(new ScheduleSpec(
            D(2026, 2, 10),
            D(2027, 3, 31),
            ScheduleTenor.Months(3),
            Roll: ScheduleRoll.EndOfMonth,
            FirstRegularDate: D(2026, 3, 31)));

        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        (result.Periods[0].UnadjustedStart, result.Periods[0].UnadjustedEnd).Should().Be((D(2026, 2, 10), D(2026, 3, 31)));
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2025, 12, 31), D(2026, 3, 31)));
        Ends(result).Skip(1).Should().Equal(D(2026, 6, 30), D(2026, 9, 30), D(2026, 12, 31), D(2027, 3, 31));
        result.Periods.Skip(1).Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void ExplicitFirstRegularDate_LongInitialStubStepsBackOverTwoGridPeriods()
    {
        var result = Ok(new ScheduleSpec(
            D(2024, 11, 1),
            D(2027, 6, 15),
            ScheduleTenor.Months(6),
            Roll: ScheduleRoll.FixedDay(15),
            FirstRegularDate: D(2025, 6, 15)));

        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.LongInitialStub);
        (result.Periods[0].UnadjustedStart, result.Periods[0].UnadjustedEnd).Should().Be((D(2024, 11, 1), D(2025, 6, 15)));
        result.Periods[0].ReferencePeriods.Should().Equal(
            R(D(2024, 6, 15), D(2024, 12, 15)),
            R(D(2024, 12, 15), D(2025, 6, 15)));
        result.Periods.Should().HaveCount(5);
        result.Periods.Skip(1).Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
        Ends(result).Skip(1).Should().Equal(D(2025, 12, 15), D(2026, 6, 15), D(2026, 12, 15), D(2027, 6, 15));
    }

    [Fact]
    public void ExplicitFirstAndLastRegularDates_StubsAtBothEnds()
    {
        var result = Ok(new ScheduleSpec(
            D(2025, 2, 1),
            D(2026, 10, 20),
            ScheduleTenor.Months(6),
            Roll: ScheduleRoll.FixedDay(15),
            FirstRegularDate: D(2025, 3, 15),
            LastRegularDate: D(2026, 9, 15)));

        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        (result.Periods[0].UnadjustedStart, result.Periods[0].UnadjustedEnd).Should().Be((D(2025, 2, 1), D(2025, 3, 15)));
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2024, 9, 15), D(2025, 3, 15)));
        Ends(result).Skip(1).Take(3).Should().Equal(D(2025, 9, 15), D(2026, 3, 15), D(2026, 9, 15));
        result.Periods.Skip(1).Take(3).Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
        var final = result.Periods[^1];
        final.Kind.Should().Be(SchedulePeriodKind.ShortFinalStub);
        (final.UnadjustedStart, final.UnadjustedEnd).Should().Be((D(2026, 9, 15), D(2026, 10, 20)));
        final.ReferencePeriods.Should().Equal(R(D(2026, 9, 15), D(2027, 3, 15)));
        result.Periods.Should().HaveCount(5);
    }

    [Fact]
    public void ExplicitFirstRegularDate_ClassifiesAgainstTheGridNotTheStubBoundary()
    {
        // The grid steps from F, never re-anchored at E: 2026-02-28 less three months is 2025-11-28,
        // so an effective date of 2025-11-29 is a one-day-short initial stub, not a regular period.
        var result = Ok(new ScheduleSpec(
            D(2025, 11, 29),
            D(2026, 11, 28),
            ScheduleTenor.Months(3),
            FirstRegularDate: D(2026, 2, 28)));

        result.Periods[0].Kind.Should().Be(SchedulePeriodKind.ShortInitialStub);
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2025, 11, 28), D(2026, 2, 28)));
        Ends(result).Should().Equal(D(2026, 2, 28), D(2026, 5, 28), D(2026, 8, 28), D(2026, 11, 28));
    }

    [Fact]
    public void ExplicitFirstRegularDate_OnTheGrid_IsARegularPeriod()
    {
        var result = Ok(new ScheduleSpec(
            D(2025, 12, 15),
            D(2026, 12, 15),
            ScheduleTenor.Months(6),
            Roll: ScheduleRoll.FixedDay(15),
            FirstRegularDate: D(2026, 6, 15)));

        result.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
        result.Periods[0].ReferencePeriods.Should().Equal(R(D(2025, 12, 15), D(2026, 6, 15)));
    }

    [Fact]
    public void LastRegularDateAlone_GeneratesBackwardWithAFinalStub()
    {
        var result = Ok(new ScheduleSpec(
            D(2025, 3, 15),
            D(2026, 11, 1),
            ScheduleTenor.Months(6),
            Direction: ScheduleDirection.Backward,
            Roll: ScheduleRoll.FixedDay(15),
            LastRegularDate: D(2026, 3, 15)));

        Ends(result).Should().Equal(D(2025, 9, 15), D(2026, 3, 15), D(2026, 11, 1));
        result.Periods[^1].Kind.Should().Be(SchedulePeriodKind.LongFinalStub);
        result.Periods[^1].ReferencePeriods.Should().Equal(
            R(D(2026, 3, 15), D(2026, 9, 15)),
            R(D(2026, 9, 15), D(2027, 3, 15)));
    }
}
