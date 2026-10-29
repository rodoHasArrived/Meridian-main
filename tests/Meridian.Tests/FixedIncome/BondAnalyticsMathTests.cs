using FluentAssertions;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondAnalyticsMathTests
{
    [Theory]
    [InlineData(BondYieldConvention.NominalPeriodic)]
    [InlineData(BondYieldConvention.AnnualEffective)]
    [InlineData(BondYieldConvention.Simple)]
    public void NegativeYield_RoundTripsWithValidatedPriceResidual(BondYieldConvention convention)
    {
        BondPricingCashFlow[] flows = [new(2.5m, 0.5m), new(102.5m, 1m)];
        BondPricingMath.TryPrice(flows, -0.015m, convention, 2, out var price, out var reason).Should().BeTrue(reason);
        BondPricingMath.TrySolveYield(flows, price!.DirtyPrice, convention, 2, out var yield, out reason).Should().BeTrue(reason);
        yield.Should().BeApproximately(-0.015m, 0.000000001m);
    }

    [Fact]
    public void DurationConvexityAndDv01_MatchIndependentFiniteDifferences()
    {
        BondPricingCashFlow[] flows = [new(3m, 0.4m), new(3m, 0.9m), new(103m, 1.4m)];
        var mid = Price(flows, 0.04m);
        const decimal bump = 0.00001m;
        var up = Price(flows, 0.04m + bump);
        var down = Price(flows, 0.04m - bump);
        var modified = (down.DirtyPrice - up.DirtyPrice) / (2m * bump * mid.DirtyPrice);
        var convexity = (up.DirtyPrice + down.DirtyPrice - 2m * mid.DirtyPrice) / (bump * bump * mid.DirtyPrice);
        mid.ModifiedDuration.Should().BeApproximately(modified, 0.000001m);
        mid.Convexity.Should().BeApproximately(convexity, 0.0002m);
        mid.Dv01.Should().BeApproximately(mid.DirtyPrice * mid.ModifiedDuration * 0.0001m, 0.00000001m);
    }

    [Fact]
    public void ClosedFormZeroCoupon_AnnualEffectiveMeasures()
    {
        BondPricingMath.TryPrice([new(100m, 2m)], 0.05m, BondYieldConvention.AnnualEffective, 1,
            out var price, out var reason).Should().BeTrue(reason);
        price!.DirtyPrice.Should().BeApproximately(100m / 1.1025m, 0.00000001m);
        price.MacaulayDuration.Should().Be(2m);
        price.ModifiedDuration.Should().BeApproximately(2m / 1.05m, 0.000000001m);
        price.Convexity.Should().BeApproximately(6m / 1.1025m, 0.000000001m);
    }

    [Fact]
    public void InvalidDiscountBaseAndCashflows_AreTypedFailures()
    {
        BondPricingMath.TryPrice([new(100m, 1m)], -2m, BondYieldConvention.NominalPeriodic, 2,
            out var price, out var reason).Should().BeFalse();
        price.Should().BeNull();
        reason.Should().Contain("nonpositive");
        BondPricingMath.TrySolveYield([], 100m, BondYieldConvention.NominalPeriodic, 2,
            out _, out _).Should().BeFalse();
        BondPricingMath.TryPrice([new(-100m, 1m)], 0.05m, BondYieldConvention.AnnualEffective, 1,
            out _, out _).Should().BeFalse();
    }

    private static BondPriceMeasures Price(IReadOnlyList<BondPricingCashFlow> flows, decimal yield)
    {
        BondPricingMath.TryPrice(flows, yield, BondYieldConvention.NominalPeriodic, 2,
            out var result, out var reason).Should().BeTrue(reason);
        return result!;
    }
}
