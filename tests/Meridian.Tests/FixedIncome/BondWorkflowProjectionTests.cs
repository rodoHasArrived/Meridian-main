using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.FixedIncome;

public sealed class BondWorkflowProjectionTests
{
    [Fact]
    public void SignedFixedCouponAndAccruedRemainNegative()
    {
        var schedule = Schedule(new(2026, 1, 1), new(2027, 1, 1));
        var terms = Terms() with { CouponRate = -0.02m };
        var rows = BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 1));
        rows.Sum(r => r.Interest).Should().Be(-2m);
        rows.Sum(r => r.Principal).Should().Be(100m);
        BondWorkflowProjection.Accrued(schedule, terms, new(2026, 7, 1)).Should().Be(-1m);
    }

    [Fact]
    public void DeterministicPaydownSplitsInterestWithoutDoubleFactoring()
    {
        var schedule = Schedule(new(2026, 1, 1), new(2027, 1, 1));
        var terms = Terms() with
        {
            CouponRate = 0.10m,
            Principal = [new(new(2026, 7, 1), 40m)]
        };
        var rows = BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 1));
        // 100 * 10% * half year + 60 * 10% * half year.
        rows.Sum(r => r.Interest).Should().Be(8m);
        rows.Sum(r => r.Principal).Should().Be(100m);
    }

    [Fact]
    public void MidPeriodSettlementRestoresPaidPrincipalForAccrualWithoutRepeatingItsCash()
    {
        var schedule = Schedule(new(2026, 1, 1), new(2027, 1, 1));
        var terms = Terms() with
        {
            CouponRate = 0.10m,
            OpeningFactor = 0.6m,
            Principal = [new(new(2026, 7, 1), 40m)]
        };
        var rows = BondWorkflowProjection.Project(schedule, terms, new(2026, 8, 1));
        rows.Sum(r => r.Interest).Should().Be(8m);
        rows.Sum(r => r.Principal).Should().Be(60m);
        BondWorkflowProjection.Accrued(schedule, terms, new(2026, 8, 1)).Should().BeApproximately(5.5m, 0.000000000001m);
    }

    [Fact]
    public void OvernightWeekendUsesOneThreeDayFactorAndAddsMarginAfterCompounding()
    {
        var schedule = Schedule(new(2026, 1, 2), new(2026, 1, 6), term: true);
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            CouponCashBasis = "DayCount",
            Spread = 0.01m,
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 0)
        };
        var rows = BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 2),
            date => new(0.036m, true, "official"));
        var expected = 100m * ((1m + 0.036m * 3m / 360m) * (1m + 0.036m / 360m) - 1m + 0.01m * 4m / 360m);
        rows.Sum(r => r.Interest).Should().BeApproximately(expected, 0.00000000001m);
        rows.SelectMany(r => r.Observations).Select(o => o.Days).Should().Equal(3, 1);
    }

    [Fact]
    public void OvernightLockoutDoesNotApplyEarlyWhenComputingPartialAccrued()
    {
        var schedule = Schedule(new(2026, 1, 5), new(2026, 1, 16), term: true);
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            CouponCashBasis = "DayCount",
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 2)
        };
        var consumed = new List<DateOnly>();
        BondWorkflowProjection.Accrued(schedule, terms, new(2026, 1, 9), date =>
        { consumed.Add(date); return new(0.02m, true, "official"); });
        consumed.Should().Equal(new DateOnly(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7), new(2026, 1, 8));
    }

    [Fact]
    public void ObservationShiftDistinguishesObservationAndInterestPeriodWeights()
    {
        var schedule = Schedule(new(2026, 1, 5), new(2026, 1, 8), term: true);
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            CouponCashBasis = "DayCount",
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 1, true, false, 0)
        };
        var observationWeighted = BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 5), _ => new(0.036m, true, "SOFR"));
        var interestWeighted = BondWorkflowProjection.Project(schedule, terms with
        { Overnight = terms.Overnight! with { InterestPeriodWeighting = true } }, new(2026, 1, 5), _ => new(0.036m, true, "SOFR"));
        // Mon->Thu shifts to Fri->Wed: five observation days versus three accrual days.
        observationWeighted.Sum(r => r.Interest).Should().BeApproximately(interestWeighted.Sum(r => r.Interest) * 5m / 3m, 0.000000000001m);
        observationWeighted.SelectMany(r => r.Observations).First().Days.Should().Be(3);
        var observed = observationWeighted.SelectMany(r => r.Observations).ToArray();
        observed.Select(row => row.AccrualDate).Should().Equal(new DateOnly(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7));
        observed.Select(row => row.ObservationDate).Should().Equal(new DateOnly(2026, 1, 2), new(2026, 1, 5), new(2026, 1, 6));
    }

    [Fact]
    public void NegativeOvernightCouponIsAnObligationWithoutImplicitFloor()
    {
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            CouponCashBasis = "DayCount",
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 0)
        };
        var rows = BondWorkflowProjection.Project(Schedule(new(2026, 1, 2), new(2026, 1, 6), true),
            terms, new(2026, 1, 2), _ => new(-0.036m, true, "SOFR"));
        rows.Sum(r => r.Interest).Should().BeLessThan(0m);
        rows.SelectMany(r => r.Observations).Should().OnlyContain(o => o.Rate < 0m);
    }

    [Theory]
    [InlineData(0.036, 0.02, 2)]
    [InlineData(-0.01, -0.02, -2)]
    [InlineData(-0.03, -0.02, -3)]
    public void SimpleFloatingCapWithoutFloorBindsPositiveAndSignedCouponAndAccrual(double index, double cap, double annualCash)
    {
        var schedule = Schedule(new(2026, 1, 1), new(2027, 1, 1));
        var terms = Terms() with { CouponKind = "Floating", Cap = (decimal)cap, Floor = null };
        var rows = BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 1), _ => new((decimal)index, true, "observed"));
        rows.Sum(row => row.Interest).Should().Be((decimal)annualCash);
        BondWorkflowProjection.Accrued(schedule, terms, new(2026, 7, 1), _ => new((decimal)index, true, "observed"))
            .Should().Be((decimal)annualCash / 2m);
    }

    [Theory]
    [InlineData(0.036, 0.02, true)]
    [InlineData(-0.01, -0.02, true)]
    [InlineData(-0.03, -0.02, false)]
    public void OvernightCapWithoutFloorLimitsCompoundedCouponCashIncludingNegativeCap(double index, double cap, bool binds)
    {
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            CouponCashBasis = "DayCount",
            Cap = (decimal)cap,
            Floor = null,
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 0)
        };
        var rows = BondWorkflowProjection.Project(Schedule(new(2026, 1, 2), new(2026, 1, 6), true),
            terms, new(2026, 1, 2), _ => new((decimal)index, true, "observed"));
        var rawCash = 100m * ((1m + (decimal)index * 3m / 360m) * (1m + (decimal)index / 360m) - 1m);
        var expected = binds ? 100m * (decimal)cap * 4m / 360m : rawCash;
        rows.Sum(row => row.Interest).Should().BeApproximately(expected, 0.000000000001m);
    }

    [Fact]
    public void OvernightAmortizationInsideCouponPeriodIsExplicitlyRefused()
    {
        var schedule = Schedule(new(2026, 1, 5), new(2026, 2, 5), term: true);
        var terms = Terms() with
        {
            CouponKind = "Floating",
            DayCount = DayCountConvention.Actual360,
            Principal = [new(new(2026, 1, 20), 10m)],
            Overnight = new(BusinessDayCalendars.WeekendsOnly, 0, false, true, 0)
        };
        var act = () => BondWorkflowProjection.Project(schedule, terms, new(2026, 1, 5), _ => new(0.02m, true, "official"));
        act.Should().Throw<ArgumentException>().WithMessage("*coupon boundaries*");
    }

    [Fact]
    public void MonthlyInflationInterpolationUsesLaggedLevelsAndSeparateRedemptionFloor()
    {
        var convention = new BondWorkflowInflationConvention("CPI", 100m, 3, true, null, 1m);
        var ratio = BondWorkflowProjection.IndexRatio(new(2026, 7, 16), convention,
            date => new(date.Month == 4 ? 100m : 103.1m, true, "CPI"));
        ratio.Value.Should().Be(1.015m); // 15/31 of the 3.1-point monthly change.
        var terms = Terms() with { CouponKind = "InflationLinked", Inflation = convention, CouponRate = 0.02m };
        var rows = BondWorkflowProjection.Project(Schedule(new(2026, 1, 1), new(2027, 1, 1)), terms,
            new(2026, 1, 1), inflation: _ => new(90m, true, "CPI"));
        rows.Sum(r => r.Interest).Should().Be(1.8m);
        rows.Sum(r => r.Principal).Should().Be(100m);
    }

    private static BondWorkflowProjectionTerms Terms() => new("Fixed", 0.05m, 0m, null, null,
        DayCountConvention.Thirty360, "Level", 100m, 1m, []);
    private static ScheduleGenerationResult Schedule(DateOnly start, DateOnly end, bool term = false)
        => ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(start, end,
            term ? ScheduleTenor.Term : ScheduleTenor.Years(1)));
}
