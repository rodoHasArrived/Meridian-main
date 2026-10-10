using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Constant-yield (effective-interest) amortization for acquisitions that settle inside a coupon
/// period: the schedule runs backward from maturity, the first period is a short stub priced with
/// a fractional discount exponent, and the clean acquisition price excludes purchased accrued
/// interest.
/// </summary>
public sealed class FaceValueLotOddPeriodTests
{
    private static readonly Guid SecurityId = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000dd");

    // Hand-checked case: 5% semiannual bond, 30/360, acquired 2026-04-01 with maturity 2028-07-01
    // (2.25 years). Coupons fall on Jan 1 / Jul 1, so the stub runs 90 of 180 days: f = 0.5 and
    // purchased accrued interest is 2.5 × 0.5 = 1.25 per 100. At a 6% yield (3% per period):
    //   dirty = 1.03^0.5 × [Σ 2.5 / 1.03^k (k = 1..5) + 100 / 1.03^5] = 99.16496806878665…
    //   clean = dirty − 1.25 = 97.91496806878665…
    private static readonly DateOnly HandAcquired = new(2026, 4, 1);
    private static readonly DateOnly HandMaturity = new(2028, 7, 1);
    private const decimal HandCleanPrice = 97.914968068786653766911087391m;

    [Fact]
    public void HandCheckedStub_BasisAfterFirstCouponEqualsPresentValueOfRemainingFlows()
    {
        var lot = new FaceValueLot("odd-hand", SecurityId, HandAcquired, originalFace: 100m,
            pricePercentOfPar: decimal.Round(HandCleanPrice, 12));

        var afterFirstCoupon = lot.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2026, 7, 1), 5m);
        var afterSecondCoupon = lot.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2027, 1, 1), 5m);

        // Independently: 4 and 3 remaining semiannual flows at 3% per period.
        afterFirstCoupon.Should().BeApproximately(PresentValue(2.5m, 0.03m, 4), 0.000000001m);
        afterFirstCoupon.Should().BeApproximately(98.141450798594815086m, 0.000000001m);
        afterSecondCoupon.Should().BeApproximately(PresentValue(2.5m, 0.03m, 3), 0.000000001m);
        afterSecondCoupon.Should().BeApproximately(98.585694322552659539m, 0.000000001m);
    }

    [Fact]
    public void HandCheckedStub_InterpolatesLinearlyByDayCountInsideTheStub()
    {
        var lot = new FaceValueLot("odd-hand", SecurityId, HandAcquired, 100m, HandCleanPrice);

        // 45 of the 90 stub days (30/360) have elapsed on May 16.
        var midStub = lot.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2026, 5, 16), 5m, 2, 0.06m);

        midStub.Should().BeApproximately(98.028209433690734427m, 0.000000001m);
    }

    [Fact]
    public void RetainedYield_ReconcilesAgainstTheOddFirstPeriodPricing()
    {
        var lot = new FaceValueLot("odd-hand", SecurityId, HandAcquired, 100m, HandCleanPrice);

        var basis = lot.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2026, 7, 1), 5m, 2, 0.06m);

        basis.Should().BeApproximately(PresentValue(2.5m, 0.03m, 4), 0.000000001m);
        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, HandMaturity, HandMaturity, 5m, 2, 0.06m)
            .Should().Be(100m);
    }

    [Theory]
    [InlineData(0.0601)]
    [InlineData(0.05)]
    public void RetainedYield_ThatDoesNotPriceTheOddSchedule_IsRefused(double wrongYield)
    {
        var lot = new FaceValueLot("odd-hand", SecurityId, HandAcquired, 100m, HandCleanPrice);

        var act = () => lot.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2026, 7, 1), 5m, 2, (decimal)wrongYield);

        act.Should().Throw<ArgumentException>().WithMessage("*yield does not reconcile*");
    }

    [Fact]
    public void RetainedYield_ThatIgnoresAccruedInterest_IsRefused()
    {
        // Treating the clean price as if it bought the full first coupon overstates the yield.
        var dirtyAsClean = new FaceValueLot("odd-hand", SecurityId, HandAcquired, 100m, HandCleanPrice + 1.25m);

        var act = () => dirtyAsClean.ConstantYieldAmortizedBasisAsOf(
            DayCountConvention.Thirty360, HandMaturity, new DateOnly(2026, 7, 1), 5m, 2, 0.06m);

        act.Should().Throw<ArgumentException>().WithMessage("*yield does not reconcile*");
    }

    [Theory]
    [InlineData(103.5, 5.0)]
    [InlineData(95.25, 4.0)]
    [InlineData(82.0, 0.0)]
    public void MidPeriodAcquisition_RunsMonotonicallyFromCostToPar(double price, double coupon)
    {
        var acquired = new DateOnly(2026, 4, 15);
        var maturity = new DateOnly(2031, 1, 1);
        var lot = new FaceValueLot("odd-mono", SecurityId, acquired, originalFace: 1000m, pricePercentOfPar: (decimal)price);
        var premium = lot.PremiumDiscount > 0m;

        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, acquired, (decimal)coupon)
            .Should().Be(lot.CostBasis, "basis at acquisition is the clean cost");

        var previous = lot.CostBasis;
        for (var asOf = acquired.AddDays(1); asOf < maturity; asOf = asOf.AddDays(7))
        {
            var basis = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, asOf, (decimal)coupon);
            if (premium)
            {
                basis.Should().BeLessThanOrEqualTo(previous, "a premium amortizes down on {0}", asOf);
                basis.Should().BeGreaterThan(1000m, "a premium cannot cross par before maturity");
            }
            else
            {
                basis.Should().BeGreaterThanOrEqualTo(previous, "a discount accretes up on {0}", asOf);
                basis.Should().BeLessThan(1000m, "a discount cannot cross par before maturity");
            }

            previous = basis;
        }

        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, maturity.AddDays(-1), (decimal)coupon)
            .Should().BeApproximately(1000m, 0.5m);
        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, maturity, (decimal)coupon)
            .Should().Be(1000m);
    }

    [Fact]
    public void LongMonthlyZeroCouponAtAPremium_SolvesANegativeYieldWithoutOverflow()
    {
        // ~1,197 monthly coupons priced above par imply a negative yield. The bisection's early
        // probes discount that many periods at a deeply negative rate, which overflows decimal;
        // the solver must treat that as "price above target" rather than throwing.
        var maturity = new DateOnly(2126, 1, 1);
        var acquired = new DateOnly(2026, 4, 15);
        var lot = new FaceValueLot("odd-long", SecurityId, acquired, 1000m, 100.5m);

        var atAcquisition = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, acquired, 0m, 12);
        var midway = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, new DateOnly(2076, 1, 1), 0m, 12);
        var atMaturity = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, maturity, 0m, 12);

        atAcquisition.Should().Be(lot.CostBasis);
        midway.Should().BeLessThan(lot.CostBasis).And.BeGreaterThan(1000m);
        atMaturity.Should().Be(1000m);
    }

    [Fact]
    public void MidPeriodPremium_LastCouponPeriodConvergesExactlyToPar()
    {
        // The solved yield prices the odd schedule, so rolling every coupon forward lands on par
        // at the final coupon boundary rather than jumping there at maturity.
        var maturity = new DateOnly(2031, 1, 1);
        var lot = new FaceValueLot("odd-par", SecurityId, new DateOnly(2026, 4, 15), 1000m, 103.5m);
        var lastPeriodStart = new DateOnly(2030, 7, 1);
        var startBasis = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, lastPeriodStart, 5m);
        var nearEnd = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, new DateOnly(2030, 12, 30), 5m);

        // 179 of 180 30/360 days: linear interpolation toward the par endpoint.
        var expected = startBasis + ((1000m - startBasis) * 179m / 180m);
        nearEnd.Should().BeApproximately(expected, 0.0000001m);
    }

    [Fact]
    public void ZeroCouponStub_AccretesWithFractionalExponent()
    {
        // No coupon and no accrued interest: the basis at each coupon date is 1 / (1 + y)^(remaining).
        var acquired = new DateOnly(2026, 4, 1);
        var maturity = new DateOnly(2028, 7, 1);
        var cleanPrice = 100m / (decimal)Math.Pow(1.03, 4.5);
        var lot = new FaceValueLot("odd-zero", SecurityId, acquired, 100m, cleanPrice);

        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, new DateOnly(2026, 7, 1), 0m)
            .Should().BeApproximately(PresentValue(0m, 0.03m, 4), 0.000000001m);
    }

    [Fact]
    public void EndOfMonthMaturity_GeneratesMonthEndCouponsBackward()
    {
        // Maturity on Feb 29 implies an end-of-month schedule: Aug 31 / Feb 28-29 coupon dates.
        var acquired = new DateOnly(2026, 10, 15);
        var maturity = new DateOnly(2028, 2, 29);
        // Actual/365: stub Oct 15 2026 → Feb 28 2027 is 136 of 181 days (Aug 31 → Feb 28).
        var stub = 136m / 181m;
        var y = 0.025m;
        var dirty = (decimal)Math.Pow(1.025, 1d - (double)stub) * PresentValue(3m, y, 3);
        var clean = dirty - (3m * (1m - stub));
        var lot = new FaceValueLot("odd-eom", SecurityId, acquired, 100m, clean);

        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Actual365, maturity, new DateOnly(2027, 2, 28), 6m, 2, 0.05m)
            .Should().BeApproximately(PresentValue(3m, y, 2), 0.000000001m);
        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Actual365, maturity, new DateOnly(2027, 8, 31), 6m, 2, 0.05m)
            .Should().BeApproximately(PresentValue(3m, y, 1), 0.000000001m);
    }

    [Fact]
    public void StubOnlySchedule_AcquiredInsideTheFinalPeriod_AccretesToPar()
    {
        var maturity = new DateOnly(2027, 1, 1);
        var lot = new FaceValueLot("odd-final", SecurityId, new DateOnly(2026, 10, 1), 1000m, 99m);

        var mid = lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, new DateOnly(2026, 11, 16), 4m);

        mid.Should().BeApproximately(995m, 0.0000001m, "a single stub interpolates linearly from cost to par");
        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, maturity, 4m).Should().Be(1000m);
    }

    [Fact]
    public void OddSchedule_StillRefusesMoreThan1200Coupons()
    {
        var acquired = new DateOnly(2026, 1, 15);
        var maturity = new DateOnly(2126, 2, 1);
        var lot = new FaceValueLot("odd-long", SecurityId, acquired, 1000m, 101m);

        var act = () => lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, maturity, 1m, 12);

        act.Should().Throw<ArgumentException>().WithMessage("*one to 1200*");
    }

    [Fact]
    public void RegularSchedule_IsUnchangedByTheOddPeriodBranch()
    {
        // Acquisition on a coupon date keeps the whole-period roll-forward: after each coupon the
        // basis is the plain annuity present value at the retained yield.
        var acquired = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2028, 7, 1);
        var lot = new FaceValueLot("regular", SecurityId, acquired, 100m, PresentValue(2.5m, 0.03m, 5));

        lot.ConstantYieldAmortizedBasisAsOf(DayCountConvention.Thirty360, maturity, new DateOnly(2026, 7, 1), 5m, 2, 0.06m)
            .Should().BeApproximately(PresentValue(2.5m, 0.03m, 4), 0.0000000001m);
    }

    // Independent price per 100 of `remaining` level flows at a per-period yield.
    private static decimal PresentValue(decimal couponPer100, decimal yieldPerPeriod, int remaining)
    {
        var discount = 1m;
        var price = 0m;
        for (var period = 0; period < remaining; period++)
        {
            discount /= 1m + yieldPerPeriod;
            price += couponPer100 * discount;
        }

        return price + (100m * discount);
    }
}
