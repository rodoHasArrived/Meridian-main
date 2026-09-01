using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class EuropeanDistributionWaterfallTests
{
    [Fact]
    public void Constructor_CatchUpRateDoesNotExceedCarryRate_RejectsInvalidWaterfall()
    {
        // A 10% catch-up cannot achieve a 20% carried-interest target. Rejecting this configuration
        // avoids silently skipping the catch-up tier and paying the higher residual carry rate.
        var act = () => new EuropeanWaterfallInput(
            contributedCapital: 100m,
            preferredReturnAccrued: 10m,
            amountToDistribute: 120m,
            carryRate: 0.20m,
            catchUpRate: 0.10m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("catchUpRate");
    }

    [Fact]
    public void CatchUpRateEqualToCarryRate_ExpressesNoCatchUpWithoutOverpayingTheGp()
    {
        // Fund terms with a preferred return and no special GP catch-up are represented by a
        // catch-up rate equal to the carry rate: the catch-up tier would split every dollar
        // exactly like the residual carry tier, so skipping it changes nothing and the GP cannot
        // be overpaid. The equality boundary must therefore stay constructible.
        var input = new EuropeanWaterfallInput(
            contributedCapital: 100m,
            preferredReturnAccrued: 10m,
            amountToDistribute: 120m,
            carryRate: 0.20m,
            catchUpRate: 0.20m);

        var result = EuropeanDistributionWaterfall.Distribute(input);

        result.GpCatchUp.Should().Be(0m);
        result.Tiers.Should().NotContain(tier => tier.Tier == "GpCatchUp");
        result.ReturnOfCapital.Should().Be(100m);
        result.PreferredReturn.Should().Be(10m);
        result.GpCarry.Should().Be(2m);
        result.LpCarry.Should().Be(8m);
    }

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
    public void PartialCatchUpRate_StillBringsGpToCarryShare()
    {
        // 20% carry with an 80% catch-up: the catch-up must account for the 20% LP leakage so the GP
        // still reaches 20% of profit above return of capital (not the 19.05% the full-catch-up
        // formula would give).
        var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
            contributedCapital: 10_000_000m,
            preferredReturnAccrued: 100_000m,
            amountToDistribute: 10_133_333.33m,
            carryRate: 0.20m,
            catchUpRate: 0.80m));

        result.ReturnOfCapital.Should().Be(10_000_000m);
        result.PreferredReturn.Should().Be(100_000m);
        var profitAboveReturnOfCapital = result.Distributed - result.ReturnOfCapital;
        (result.GpTotal / profitAboveReturnOfCapital).Should().BeApproximately(0.20m, 0.001m);
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
    public void ExplicitPriorCatchUpPool_AvoidsRoundingDriftFromDerivedPool()
    {
        // Return of capital and preferred are already fully paid, so this distribution is pure
        // catch-up. A tiny prior GP catch-up of 0.02 at a 75% rate reconstructs to a 0.03 pool by
        // reverse division, understating the remaining catch-up; threading the real 0.02 pool
        // restores the correct (slightly larger) allocation.
        EuropeanWaterfallInput Build(decimal priorCatchUpPool) => new(
            contributedCapital: 100m,
            preferredReturnAccrued: 0m,
            amountToDistribute: 100m,
            carryRate: 0.20m,
            catchUpRate: 0.75m,
            priorReturnOfCapital: 100m,
            priorPreferredPaid: 100m,
            priorGpCatchUp: 0.02m,
            priorCatchUpPool: priorCatchUpPool);

        var derived = EuropeanDistributionWaterfall.Distribute(Build(0m));
        var threaded = EuropeanDistributionWaterfall.Distribute(Build(0.02m));

        threaded.GpCatchUp.Should().BeGreaterThan(derived.GpCatchUp);
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
