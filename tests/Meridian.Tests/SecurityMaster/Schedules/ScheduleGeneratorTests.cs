using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins the contractual schedule kernel's unadjusted generation: forward and compounded grids, stub
/// placement and reference periods (part 1), backward generation and explicit regular dates
/// (part 2), and rolls, refusals and range edges (part 3). Every expected date is a market-convention
/// value checked by hand against the grid definition.
/// </summary>
public sealed partial class ScheduleGeneratorTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DayCountReferencePeriod R(DateOnly start, DateOnly end) => new(start, end);

    private static ScheduleGenerationResult Ok(ScheduleSpec spec)
    {
        var result = ScheduleGenerator.Generate(spec);
        result.Succeeded.Should().BeTrue(result.Failure?.Message);
        result.KernelVersion.Should().Be("contractual-schedule-kernel-v1");
        result.Periods.Select(period => period.Sequence).Should().Equal(Enumerable.Range(1, result.Periods.Count));
        result.Periods.Select(period => period.IsFinal).Should().Equal(result.Periods.Select((_, i) => i == result.Periods.Count - 1));
        for (var i = 1; i < result.Periods.Count; i++)
        {
            result.Periods[i].UnadjustedStart.Should().Be(result.Periods[i - 1].UnadjustedEnd);
        }

        return result;
    }

    private static ScheduleFailure Refused(ScheduleSpec spec, ScheduleFailureCode expected)
    {
        var result = ScheduleGenerator.Generate(spec);
        result.Succeeded.Should().BeFalse();
        result.Periods.Should().BeEmpty();
        result.Failure!.Code.Should().Be(expected, result.Failure.Message);
        return result.Failure;
    }

    private static (DateOnly Start, DateOnly End)[] Spans(ScheduleGenerationResult result)
        => result.Periods.Select(period => (period.UnadjustedStart, period.UnadjustedEnd)).ToArray();

    private static DateOnly[] Ends(ScheduleGenerationResult result)
        => result.Periods.Select(period => period.UnadjustedEnd).ToArray();

    // ── Part 1: forward generation ───────────────────────────────────────────────────────────────

    [Fact]
    public void IssueAnchoredForward_MonthEndIssue_GivesFourRegularQuarters()
    {
        var result = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)));

        Spans(result).Should().Equal(
            (D(2026, 1, 31), D(2026, 4, 30)),
            (D(2026, 4, 30), D(2026, 7, 31)),
            (D(2026, 7, 31), D(2026, 10, 31)),
            (D(2026, 10, 31), D(2027, 1, 31)));
        result.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular && !period.IsStub);
        result.Periods.Should().OnlyContain(period =>
            period.AccrualStart == period.UnadjustedStart
            && period.AccrualEnd == period.UnadjustedEnd
            && period.PaymentDate == period.UnadjustedEnd);
        result.Periods.Should().OnlyContain(period =>
            period.ReferencePeriods.Count == 1
            && period.ReferencePeriods[0] == R(period.UnadjustedStart, period.UnadjustedEnd));
        result.Diagnostics.Should().BeEmpty();
        result.CalendarsUsed.Should().BeEmpty();
    }

    [Fact]
    public void CompoundedForward_MonthEndIssue_DriftsAndEmitsOneDayStub()
    {
        var result = Ok(ScheduleSpec.CompoundedForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3)));

        Spans(result).Should().Equal(
            (D(2026, 1, 31), D(2026, 4, 30)),
            (D(2026, 4, 30), D(2026, 7, 30)),
            (D(2026, 7, 30), D(2026, 10, 30)),
            (D(2026, 10, 30), D(2027, 1, 30)),
            (D(2027, 1, 30), D(2027, 1, 31)));
        result.Periods[^1].Kind.Should().Be(SchedulePeriodKind.ShortFinalStub);
        result.Periods[^1].ReferencePeriods.Should().Equal(R(D(2027, 1, 30), D(2027, 4, 30)));
        result.Periods.Take(4).Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void IssueAnchoredForward_LeapDayIssue_ClampsEachDateIndependently()
    {
        var anchorDay = Ok(ScheduleSpec.IssueAnchoredForward(D(2024, 2, 29), D(2026, 2, 28), ScheduleTenor.Months(6)));
        var endOfMonth = Ok(ScheduleSpec.IssueAnchoredForward(D(2024, 2, 29), D(2026, 2, 28), ScheduleTenor.Months(6)) with
        {
            Roll = ScheduleRoll.EndOfMonth,
        });

        Ends(anchorDay).Should().Equal(D(2024, 8, 29), D(2025, 2, 28), D(2025, 8, 29), D(2026, 2, 28));
        Ends(endOfMonth).Should().Equal(D(2024, 8, 31), D(2025, 2, 28), D(2025, 8, 31), D(2026, 2, 28));
        anchorDay.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
        endOfMonth.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void ForwardRemainder_ShortStubReferencesTheNextGridDate()
    {
        var result = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 15), D(2026, 12, 1), ScheduleTenor.Months(3)));

        Ends(result).Should().Equal(D(2026, 4, 15), D(2026, 7, 15), D(2026, 10, 15), D(2026, 12, 1));
        var stub = result.Periods[^1];
        stub.Kind.Should().Be(SchedulePeriodKind.ShortFinalStub);
        stub.IsStub.Should().BeTrue();
        (stub.UnadjustedStart, stub.UnadjustedEnd).Should().Be((D(2026, 10, 15), D(2026, 12, 1)));
        (stub.UnadjustedEnd.DayNumber - stub.UnadjustedStart.DayNumber).Should().Be(47);
        stub.ReferencePeriods.Should().Equal(R(D(2026, 10, 15), D(2027, 1, 15)));
    }

    [Fact]
    public void ForwardRemainder_LongStubMergesIntoThePreviousPeriod()
    {
        var result = Ok(ScheduleSpec.IssueAnchoredForward(D(2026, 1, 15), D(2026, 12, 1), ScheduleTenor.Months(3)) with
        {
            StubPolicy = ScheduleStubPolicy.Long,
        });

        Spans(result).Should().Equal(
            (D(2026, 1, 15), D(2026, 4, 15)),
            (D(2026, 4, 15), D(2026, 7, 15)),
            (D(2026, 7, 15), D(2026, 12, 1)));
        result.Periods[^1].Kind.Should().Be(SchedulePeriodKind.LongFinalStub);
        result.Periods[^1].ReferencePeriods.Should().Equal(
            R(D(2026, 7, 15), D(2026, 10, 15)),
            R(D(2026, 10, 15), D(2027, 1, 15)));
    }

    [Fact]
    public void FixedDayForward_StubPolicies()
    {
        var spec = new ScheduleSpec(D(2025, 1, 15), D(2026, 3, 1), ScheduleTenor.Months(3), Roll: ScheduleRoll.FixedDay(15));

        var shortStub = Ok(spec);
        shortStub.Periods[^1].Kind.Should().Be(SchedulePeriodKind.ShortFinalStub);
        (shortStub.Periods[^1].UnadjustedStart, shortStub.Periods[^1].UnadjustedEnd).Should().Be((D(2026, 1, 15), D(2026, 3, 1)));
        shortStub.Periods[^1].ReferencePeriods.Should().Equal(R(D(2026, 1, 15), D(2026, 4, 15)));

        var longStub = Ok(spec with { StubPolicy = ScheduleStubPolicy.Long });
        longStub.Periods[^1].Kind.Should().Be(SchedulePeriodKind.LongFinalStub);
        (longStub.Periods[^1].UnadjustedStart, longStub.Periods[^1].UnadjustedEnd).Should().Be((D(2025, 10, 15), D(2026, 3, 1)));
        longStub.Periods[^1].ReferencePeriods.Should().Equal(
            R(D(2025, 10, 15), D(2026, 1, 15)),
            R(D(2026, 1, 15), D(2026, 4, 15)));

        Refused(spec with { StubPolicy = ScheduleStubPolicy.RequireRegular }, ScheduleFailureCode.DoesNotFitRegularGrid);

        var regular = Ok(spec with { TerminationDate = D(2026, 1, 15), StubPolicy = ScheduleStubPolicy.RequireRegular });
        Ends(regular).Should().Equal(D(2025, 4, 15), D(2025, 7, 15), D(2025, 10, 15), D(2026, 1, 15));
        regular.Periods.Should().OnlyContain(period => period.Kind == SchedulePeriodKind.Regular);
    }

    [Fact]
    public void ScheduleTenor_ShapeAndPaymentFrequencyMapping()
    {
        ScheduleTenor.Months(3).ToString().Should().Be("3M");
        ScheduleTenor.Years(1).ToString().Should().Be("1Y");
        ScheduleTenor.Weeks(2).ToString().Should().Be("2W");
        ScheduleTenor.Days(1).ToString().Should().Be("1D");
        ScheduleTenor.Term.ToString().Should().Be("T");
        new ScheduleTenor(5, ScheduleTenorUnit.Term).Count.Should().Be(1);

        ScheduleTenor.Months(3).PeriodsPerYear.Should().Be(4);
        ScheduleTenor.Months(5).PeriodsPerYear.Should().BeNull();
        ScheduleTenor.Years(1).PeriodsPerYear.Should().Be(1);
        ScheduleTenor.Years(2).PeriodsPerYear.Should().BeNull();
        ScheduleTenor.Weeks(1).PeriodsPerYear.Should().Be(52);
        ScheduleTenor.Weeks(2).PeriodsPerYear.Should().Be(26);
        ScheduleTenor.Days(1).PeriodsPerYear.Should().Be(365);
        ScheduleTenor.Term.PeriodsPerYear.Should().BeNull();
        ScheduleTenor.Years(2).MonthsPerPeriod.Should().Be(24);
        ScheduleTenor.Weeks(1).MonthsPerPeriod.Should().BeNull();

        var zero = () => ScheduleTenor.Months(0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        var advanceTerm = () => ScheduleTenor.Term.Advance(D(2026, 1, 1), 1);
        advanceTerm.Should().Throw<InvalidOperationException>();
        ScheduleTenor.Months(1).TryAdvance(D(9999, 12, 1), 1, out _).Should().BeFalse();
        ScheduleTenor.Months(1).Advance(D(2026, 1, 31), 1).Should().Be(D(2026, 2, 28));

        var expected = new Dictionary<SecurityPaymentFrequency, ScheduleTenor>
        {
            [SecurityPaymentFrequency.Daily] = ScheduleTenor.Days(1),
            [SecurityPaymentFrequency.Weekly] = ScheduleTenor.Weeks(1),
            [SecurityPaymentFrequency.BiWeekly] = ScheduleTenor.Weeks(2),
            [SecurityPaymentFrequency.Monthly] = ScheduleTenor.Months(1),
            [SecurityPaymentFrequency.BiMonthly] = ScheduleTenor.Months(2),
            [SecurityPaymentFrequency.Quarterly] = ScheduleTenor.Months(3),
            [SecurityPaymentFrequency.SemiAnnual] = ScheduleTenor.Months(6),
            [SecurityPaymentFrequency.Annual] = ScheduleTenor.Months(12),
            [SecurityPaymentFrequency.AtMaturity] = ScheduleTenor.Term,
        };
        foreach (var (frequency, tenor) in expected)
        {
            ScheduleTenor.TryFromPaymentFrequency(frequency, out var mapped).Should().BeTrue(frequency.ToString());
            mapped.Should().Be(tenor, frequency.ToString());
        }

        ScheduleTenor.TryFromPaymentFrequency(SecurityPaymentFrequency.Irregular, out _).Should().BeFalse();
    }
}
