using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins the period-context overload of the canonical day-count engine: byte-for-byte parity with the
/// period-less dispatch for conventions that need no context, exact Actual/Actual (ICMA) over
/// reference periods (ISDA 2006 §4.16(c)), the 30E/360 (ISDA) termination carve-out, Business/252
/// with a calendar, and refusals instead of degradation.
/// </summary>
public sealed class DayCountConventionsPeriodContextTests
{
    private static readonly DayCountPeriodContext NoContext = new([], null);

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static DayCountPeriodContext Icma(int? periodsPerYear, params (DateOnly Start, DateOnly End)[] references)
        => new(references.Select(reference => new DayCountReferencePeriod(reference.Start, reference.End)).ToArray(), periodsPerYear);

    private static decimal Cash(decimal fraction) => Math.Round(1_000_000m * 0.05m * fraction, 4, MidpointRounding.AwayFromZero);

    [Fact]
    public void ContextFreeConventions_MatchThePeriodLessFractionExactly()
    {
        var conventions = Enum.GetValues<DayCountConvention>()
            .Where(convention => convention is not (DayCountConvention.ActualActualIcma or DayCountConvention.ThirtyE360Isda or DayCountConvention.Business252))
            .ToArray();
        var context = new DayCountPeriodContext(
            [new DayCountReferencePeriod(D(2024, 1, 1), D(2027, 1, 1))],
            2,
            D(2026, 2, 28),
            BusinessDayCalendars.WeekendsOnly.IsBusinessDay);
        int[] lengths = [0, 1, 15, 28, 29, 30, 31, 59, 90, 181, 182, 183, 365, 366, 400];
        var compared = 0;
        for (var start = D(2024, 1, 1); start <= D(2026, 12, 31); start = start.AddDays(3))
        {
            foreach (var length in lengths)
            {
                var end = start.AddDays(length);
                foreach (var convention in conventions)
                {
                    DayCountConventions.TryFraction(convention, start, end, context, out var fraction, out var refusal).Should().BeTrue(refusal);
                    fraction.Should().Be(DayCountConventions.Fraction(convention, start, end));
                    DayCountConventions.TryFraction(convention, start, end, NoContext, out var bare, out _).Should().BeTrue();
                    bare.Should().Be(fraction);
                    compared++;
                }
            }
        }

        compared.Should().BeGreaterThan(30_000);
    }

    [Fact]
    public void Icma_RegularPeriodIsExactlyOneOverFrequency()
        => DayCountConventions.Fraction(
                DayCountConvention.ActualActualIcma,
                D(2026, 6, 15),
                D(2026, 12, 15),
                Icma(2, (D(2026, 6, 15), D(2026, 12, 15))))
            .Should().Be(0.5m);

    [Fact]
    public void Icma_ShortFrontStub()
        => DayCountConventions.Fraction(
                DayCountConvention.ActualActualIcma,
                D(2026, 8, 1),
                D(2026, 12, 15),
                Icma(2, (D(2026, 6, 15), D(2026, 12, 15))))
            .Should().Be(136m / 366m);

    [Fact]
    public void Icma_LongFrontStubSumsOverBothReferencePeriods()
        => DayCountConventions.Fraction(
                DayCountConvention.ActualActualIcma,
                D(2026, 5, 1),
                D(2026, 12, 15),
                Icma(2, (D(2025, 12, 15), D(2026, 6, 15)), (D(2026, 6, 15), D(2026, 12, 15))))
            .Should().Be((45m / 364m) + (183m / 366m));

    [Fact]
    public void Icma_StubAmountsOnAMillionAtFivePercent()
    {
        var shortStub = DayCountConventions.Fraction(
            DayCountConvention.ActualActualIcma,
            D(2025, 3, 10),
            D(2025, 6, 15),
            Icma(2, (D(2024, 12, 15), D(2025, 6, 15))));
        var longStub = DayCountConventions.Fraction(
            DayCountConvention.ActualActualIcma,
            D(2024, 11, 1),
            D(2025, 6, 15),
            Icma(2, (D(2024, 6, 15), D(2024, 12, 15)), (D(2024, 12, 15), D(2025, 6, 15))));
        var regular = DayCountConventions.Fraction(
            DayCountConvention.ActualActualIcma,
            D(2025, 6, 15),
            D(2025, 12, 15),
            Icma(2, (D(2025, 6, 15), D(2025, 12, 15))));
        var isda = DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, D(2025, 6, 15), D(2025, 12, 15));

        shortStub.Should().Be(97m / 364m);
        Cash(shortStub).Should().Be(13_324.1758m);
        longStub.Should().Be((44m / 366m) + (182m / 364m));
        Cash(longStub).Should().Be(31_010.9290m);
        isda.Should().Be(183m / 365m);
        Cash(isda).Should().Be(25_068.4932m);
        Cash(regular).Should().Be(25_000.0000m);
    }

    [Fact]
    public void ThirtyE360Isda_KeepsFebruaryEndOnlyAtTermination()
    {
        var atTermination = new DayCountPeriodContext([], null, TerminationDate: D(2028, 2, 29));

        DayCountConventions.Fraction(DayCountConvention.ThirtyE360Isda, D(2027, 8, 31), D(2028, 2, 29), atTermination)
            .Should().Be(179m / 360m);
        DayCountConventions.Fraction(DayCountConvention.ThirtyE360Isda, D(2027, 8, 31), D(2028, 2, 29), NoContext)
            .Should().Be(180m / 360m);
        DayCountConventions.Fraction(
                DayCountConvention.ThirtyE360Isda,
                D(2025, 8, 31),
                D(2026, 2, 28),
                new DayCountPeriodContext([], null, TerminationDate: D(2026, 2, 28)))
            .Should().Be(178m / 360m);
    }

    [Fact]
    public void Business252_CountsBusinessDaysWhenACalendarIsGiven()
    {
        var withCalendar = new DayCountPeriodContext([], null, IsBusinessDay: BusinessDayCalendars.WeekendsOnly.IsBusinessDay);

        DayCountConventions.Fraction(DayCountConvention.Business252, D(2025, 1, 1), D(2025, 2, 1), withCalendar)
            .Should().Be(23m / 252m);
        DayCountConventions.TryFraction(DayCountConvention.Business252, D(2025, 1, 1), D(2025, 2, 1), NoContext, out _, out var refusal)
            .Should().BeFalse();
        refusal.Should().Be("Business/252 requires a business-day calendar.");
    }

    [Fact]
    public void Icma_RefusesInsteadOfDegrading()
    {
        var window = (D(2026, 8, 1), D(2026, 12, 15));
        var noFrequency = Icma(null, (D(2026, 6, 15), D(2026, 12, 15)));
        var notCovering = Icma(2, (D(2026, 8, 15), D(2026, 12, 15)));
        var gap = Icma(2, (D(2025, 12, 15), D(2026, 6, 14)), (D(2026, 6, 15), D(2026, 12, 15)));

        foreach (var context in new[] { noFrequency, notCovering, gap, NoContext })
        {
            DayCountConventions.TryFraction(DayCountConvention.ActualActualIcma, window.Item1, window.Item2, context, out var fraction, out var refusal)
                .Should().BeFalse();
            fraction.Should().Be(0m);
            refusal.Should().NotBeNullOrWhiteSpace();
            var act = () => DayCountConventions.Fraction(DayCountConvention.ActualActualIcma, window.Item1, window.Item2, context);
            act.Should().Throw<ArgumentException>().WithMessage($"{refusal}*");
        }
    }

    [Fact]
    public void EmptyWindow_IsZeroEvenWithoutContext()
    {
        DayCountConventions.TryFraction(DayCountConvention.ActualActualIcma, D(2026, 1, 1), D(2026, 1, 1), NoContext, out var fraction, out _)
            .Should().BeTrue();
        fraction.Should().Be(0m);
        DayCountConventions.TryFraction(DayCountConvention.Business252, D(2026, 2, 1), D(2026, 1, 1), NoContext, out fraction, out _)
            .Should().BeTrue();
        fraction.Should().Be(0m);
    }

    [Fact]
    public void NullContext_Throws()
    {
        var act = () => DayCountConventions.TryFraction(DayCountConvention.Actual360, D(2026, 1, 1), D(2026, 2, 1), null!, out _, out _);

        act.Should().Throw<ArgumentNullException>();
    }
}
