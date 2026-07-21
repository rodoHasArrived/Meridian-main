using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class EuropeanDistributionWaterfallTests
{
    [Fact]
    public void ReturnOfCapitalTier_PaidFirstToLp()
    {
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 6_000_000m,
            carryRate: 0.20m));

        result.ReturnOfCapital.Should().Be(6_000_000m);
        result.PreferredReturn.Should().Be(0m);
        result.GpTotal.Should().Be(0m);
        result.LpTotal.Should().Be(6_000_000m);
    }

    [Fact]
    public void PreferredReturnTier_PaidAfterCapital()
    {
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 10_500_000m,
            carryRate: 0.20m));

        result.ReturnOfCapital.Should().Be(10_000_000m);
        result.PreferredReturn.Should().Be(500_000m); // only 500k left after capital, pref not fully paid
        result.GpCatchUp.Should().Be(0m);
    }

    [Fact]
    public void CatchUp_BringsGpToCarryShareOfProfit()
    {
        // Full return of capital + full pref + catch-up. carry 20% => catch-up target =
        // 0.2/0.8 * pref(800k) = 200k. After catch-up, GP has 200k of the 1,000k profit = 20%.
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 11_000_000m,
            carryRate: 0.20m,
            catchUpRate: 1m));

        result.ReturnOfCapital.Should().Be(10_000_000m);
        result.PreferredReturn.Should().Be(800_000m);
        result.GpCatchUp.Should().Be(200_000m);
        // Profit distributed above capital = 1,000,000; GP share should be 20%.
        var profit = result.PreferredReturn + result.GpCatchUp + result.LpCarry + result.GpCarry;
        (result.GpCatchUp + result.GpCarry).Should().BeApproximately(0.20m * profit, 1m);
    }

    [Fact]
    public void ResidualSplit_AppliesCarryRate()
    {
        // Beyond catch-up, residual splits 80/20.
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 12_000_000m,
            carryRate: 0.20m,
            catchUpRate: 1m));

        result.ReturnOfCapital.Should().Be(10_000_000m);
        result.PreferredReturn.Should().Be(800_000m);
        result.GpCatchUp.Should().Be(200_000m);
        // Remaining 1,000,000 splits 80/20.
        result.LpCarry.Should().Be(800_000m);
        result.GpCarry.Should().Be(200_000m);
    }

    [Fact]
    public void PriorState_PreventsDoublePayingEarlierTiers()
    {
        // Capital and pref already fully paid in a prior distribution: this one is all carry.
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 1_000_000m,
            carryRate: 0.20m,
            catchUpRate: 1m,
            priorReturnOfCapital: 10_000_000m,
            priorPreferredPaid: 800_000m,
            priorGpCatchUp: 200_000m));

        result.ReturnOfCapital.Should().Be(0m);
        result.PreferredReturn.Should().Be(0m);
        result.GpCatchUp.Should().Be(0m);
        result.LpCarry.Should().Be(800_000m);
        result.GpCarry.Should().Be(200_000m);
    }

    [Fact]
    public void DistributedAmount_NeverExceedsInput()
    {
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 800_000m,
            amountToDistribute: 25_000_000m,
            carryRate: 0.20m));

        result.Distributed.Should().BeApproximately(25_000_000m, 0.01m);
    }
}
