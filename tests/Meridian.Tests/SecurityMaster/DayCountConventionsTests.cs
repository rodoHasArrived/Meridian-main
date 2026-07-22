using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the single canonical day-count engine that every fixed-income accrual, coupon,
/// amortization, and cost-basis-relief path now routes through. The edge cases below are the exact
/// dates on which the previously-duplicated GL implementations disagreed with the cost-basis /
/// read-model implementations, which is why GL postings and cost-basis relief could fail to tie for
/// the same bond.
/// </summary>
public sealed class DayCountConventionsTests
{
    [Theory]
    [InlineData("30/360", DayCountConvention.Thirty360)]
    [InlineData("30/360 US", DayCountConvention.Thirty360)]
    [InlineData("Thirty360", DayCountConvention.Thirty360)]
    [InlineData("bond-basis", DayCountConvention.Thirty360)]
    [InlineData("30E/360", DayCountConvention.ThirtyE360)]
    [InlineData("Eurobond", DayCountConvention.ThirtyE360)]
    [InlineData("30E/360 ISDA", DayCountConvention.ThirtyE360Isda)]
    [InlineData("30/360 German", DayCountConvention.ThirtyE360Isda)]
    [InlineData("ACT/360", DayCountConvention.Actual360)]
    [InlineData("ACT360", DayCountConvention.Actual360)]
    [InlineData("Actual/360", DayCountConvention.Actual360)]
    [InlineData("ACT/365", DayCountConvention.Actual365)]
    [InlineData("Actual/365", DayCountConvention.Actual365)]
    [InlineData("Act365F", DayCountConvention.Actual365)]
    [InlineData("ACT/365.25", DayCountConvention.Actual36525)]
    [InlineData("NL/365", DayCountConvention.Nl365)]
    [InlineData("ACT/365 No-Leap", DayCountConvention.Nl365)]
    [InlineData("ACT/ACT", DayCountConvention.ActualActualIsda)]
    [InlineData("Actual/Actual", DayCountConvention.ActualActualIsda)]
    [InlineData("ActualActualISDA", DayCountConvention.ActualActualIsda)]
    [InlineData("ACT/ACT ICMA", DayCountConvention.ActualActualIcma)]
    [InlineData("Act/Act ISMA", DayCountConvention.ActualActualIcma)]
    [InlineData("Business252", DayCountConvention.Business252)]
    [InlineData("BUS/252", DayCountConvention.Business252)]
    public void Parse_RecognizesEverySpellingTheLegacyCopiesUsed(string raw, DayCountConvention expected)
        => DayCountConventions.Parse(raw).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Fixed")]
    public void Parse_FallsBackToUnknownInsteadOfThrowing(string? raw)
        => DayCountConventions.Parse(raw).Should().Be(DayCountConvention.Unknown);

    [Fact]
    public void Unknown_EvaluatesThroughActual365FallbackWithoutThrowing()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);

        DayCountConventions.Fraction("totally-unmapped", start, end)
            .Should().Be(30m / 365m);
    }

    // The two divergence cases that broke the GL-vs-cost-basis tie-out.
    // Jan 31 -> Mar 31 under 30/360: the correct US Bond Basis count is 60 days. The GL "expected
    // accounting events" copy computed 61 because it only capped D2 when D1 was *exactly* 30.
    [Fact]
    public void Thirty360_StartOn31_TiesToTheCostBasisRule_Not61()
    {
        DayCountConventions.Thirty360Days(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 31))
            .Should().Be(60);
    }

    // Jan 15 -> Mar 31 under 30/360: the correct count is 76 days. The cash-flow / amortization GL
    // copy computed 75 because it capped D2 at 30 even when D1 was below 30.
    [Fact]
    public void Thirty360_EndOn31_TiesToTheCostBasisRule_Not75()
    {
        DayCountConventions.Thirty360Days(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 31))
            .Should().Be(76);
    }

    [Theory]
    [InlineData(2026, 1, 1, 2026, 7, 1, 180)]   // exact half year
    [InlineData(2026, 1, 30, 2026, 3, 31, 60)]  // D1 = 30 caps D2 of 31 to 30
    [InlineData(2026, 3, 31, 2026, 9, 30, 180)] // D1 of 31 -> 30
    [InlineData(2026, 1, 31, 2026, 1, 31, 0)]   // zero-length window
    public void Thirty360Days_MatchesUsBondBasis(int y1, int m1, int d1, int y2, int m2, int d2, int expected)
        => DayCountConventions.Thirty360Days(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2))
            .Should().Be(expected);

    [Fact]
    public void Fraction_ReturnsZeroWhenEndDoesNotFollowStart()
    {
        DayCountConventions.Fraction(DayCountConvention.Thirty360, new DateOnly(2026, 7, 1), new DateOnly(2026, 1, 1))
            .Should().Be(0m);
    }

    [Fact]
    public void Fraction_Actual360_DividesActualDaysBy360()
    {
        DayCountConventions.Fraction(DayCountConvention.Actual360, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))
            .Should().Be(30m / 360m);
    }

    [Fact]
    public void Fraction_Actual365_DividesActualDaysBy365()
    {
        DayCountConventions.Fraction(DayCountConvention.Actual365, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))
            .Should().Be(30m / 365m);
    }

    [Fact]
    public void Fraction_Thirty360_TreatsAHalfYearAsExactlyOneHalf()
    {
        DayCountConventions.Fraction(DayCountConvention.Thirty360, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1))
            .Should().Be(0.5m);
    }

    [Fact]
    public void Fraction_ActualActualIsda_WithinNonLeapYear_DividesActualDaysBy365()
    {
        // 2026 is not a leap year; Jan 1 -> Jul 1 is 181 actual days. Decimal arithmetic end-to-end,
        // so the result is exact — no double round-trip tolerance needed.
        DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1))
            .Should().Be(181m / 365m);
    }

    [Fact]
    public void Fraction_ActualActualIsda_AcrossYears_WeighsEachCalendarYearByItsLength()
    {
        // 2027-10-01 -> 2028-03-01: 92 days left of 2027 (non-leap) + 60 days into 2028 (leap).
        DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, new DateOnly(2027, 10, 1), new DateOnly(2028, 3, 1))
            .Should().Be((92m / 365m) + (60m / 366m));
    }

    // ── Extended conventions ────────────────────────────────────────────────────────────────────

    // Same dates, different 30/360 family member: US Bond Basis leaves D2=31 untouched when D1<30
    // (76 days), Eurobond caps it unconditionally (75 days). Both must stay distinct.
    [Fact]
    public void ThirtyE360_CapsEndOf31Unconditionally_WhereUsBondBasisDoesNot()
    {
        var start = new DateOnly(2026, 1, 15);
        var end = new DateOnly(2026, 3, 31);

        DayCountConventions.Thirty360Days(start, end).Should().Be(76);
        DayCountConventions.ThirtyE360Days(start, end).Should().Be(75);
    }

    [Fact]
    public void ThirtyE360Isda_SnapsFebruaryEndOfMonthTo30()
    {
        // Feb 28 2026 is the last day of February (non-leap) -> D1 = 30; Apr 30 -> D2 = 30.
        DayCountConventions.ThirtyE360IsdaDays(new DateOnly(2026, 2, 28), new DateOnly(2026, 4, 30))
            .Should().Be(60);
    }

    [Fact]
    public void ThirtyE360Isda_MaturityCarveOut_KeepsFebruaryTrueDayForTheTerminationDate()
    {
        // §4.16(h): when the end date is the maturity date and falls on February's last day, D2 stays
        // at February's true day instead of snapping to 30.
        var start = new DateOnly(2026, 1, 30);
        var maturity = new DateOnly(2026, 2, 28);

        DayCountConventions.ThirtyE360IsdaDays(start, maturity).Should().Be(30);
        DayCountConventions.ThirtyE360IsdaDays(start, maturity, endIsMaturityDate: true).Should().Be(28);
    }

    [Fact]
    public void Nl365_ExcludesLeapDaysFromTheNumerator()
    {
        // 2028 is a leap year: a full Jan 1 -> Jan 1 span is 366 actual days, 365 excluding Feb 29,
        // so NL/365 treats the year as exactly 1.0.
        DayCountConventions.Nl365Days(new DateOnly(2028, 1, 1), new DateOnly(2029, 1, 1)).Should().Be(365);
        DayCountConventions.Fraction(DayCountConvention.Nl365, new DateOnly(2028, 1, 1), new DateOnly(2029, 1, 1))
            .Should().Be(1m);

        // A span that never touches Feb 29 counts plain actual days.
        DayCountConventions.Nl365Days(new DateOnly(2028, 3, 1), new DateOnly(2028, 4, 1)).Should().Be(31);
    }

    [Fact]
    public void Actual36525_DividesActualDaysByTheMeanJulianYear()
    {
        DayCountConventions.Fraction(DayCountConvention.Actual36525, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))
            .Should().Be(30m / 365.25m);
    }

    [Fact]
    public void ActualActualIcma_UsesTheCouponPeriodAndFrequency()
    {
        // Semiannual coupon period Jan 1 -> Jul 1 2026 (181 days); accrued Jan 1 -> Mar 1 (59 days).
        var fraction = DayCountConventions.ActualActualIcmaFraction(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1),
            couponsPerYear: 2);

        fraction.Should().Be(59m / (2m * 181m));
    }

    [Fact]
    public void ActualActualIcma_WithoutPeriodContext_DegradesToIsdaInsteadOfGuessing()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 1);

        DayCountConventions.Fraction(DayCountConvention.ActualActualIcma, start, end)
            .Should().Be(DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, start, end));
    }

    [Fact]
    public void Business252_CountsBusinessDaysAgainstTheSuppliedCalendar()
    {
        // Mon Jan 5 2026 -> Mon Jan 12 2026: [start, end) holds exactly one Mon-Fri week.
        static bool IsWeekday(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        var start = new DateOnly(2026, 1, 5);
        var end = new DateOnly(2026, 1, 12);

        DayCountConventions.Business252Days(start, end, IsWeekday).Should().Be(5);
        DayCountConventions.Fraction(DayCountConvention.Business252, start, end, IsWeekday)
            .Should().Be(5m / 252m);
    }

    [Fact]
    public void Business252_WithoutACalendar_DegradesToActual365()
    {
        var start = new DateOnly(2026, 1, 5);
        var end = new DateOnly(2026, 1, 12);

        DayCountConventions.Fraction(DayCountConvention.Business252, start, end)
            .Should().Be(7m / 365m);
    }

    // The whole point of the reconciliation: a coupon accrual computed for a bond produces the same
    // day-count fraction no matter which posting path asks for it, so GL and cost-basis tie.
    [Theory]
    [InlineData("30/360")]
    [InlineData("ACT/360")]
    [InlineData("ACT/365")]
    [InlineData("unmapped-convention")]
    public void Fraction_IsIdenticalRegardlessOfCaller(string convention)
    {
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 7, 31);

        var glSide = DayCountConventions.Fraction(convention, start, end);
        var costBasisSide = DayCountConventions.Fraction(DayCountConventions.Parse(convention), start, end);

        glSide.Should().Be(costBasisSide);
    }
}
