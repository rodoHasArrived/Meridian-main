using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class CarriedInterestClawbackCalculatorTests
{
    [Fact]
    public void NoClawback_WhenGpTookExactlyItsEntitlement()
    {
        var result = CarriedInterestClawbackCalculator.Compute(
            cumulativeContributedCapital: 10_000_000m,
            cumulativeDistributions: 15_000_000m,
            gpCarryReceived: 1_000_000m, // 20% of 5,000,000 profit
            carryRate: 0.20m);

        result.CumulativeProfit.Should().Be(5_000_000m);
        result.GpCarryEntitled.Should().Be(1_000_000m);
        result.HasClawback.Should().BeFalse();
        result.ClawbackDue.Should().Be(0m);
    }

    [Fact]
    public void Clawback_WhenEarlyWinnersOverdistributedCarry()
    {
        // GP took 1,000,000 carry on early deals, but final cumulative profit is only 3,000,000,
        // entitling the GP to 600,000. Clawback = 400,000.
        var result = CarriedInterestClawbackCalculator.Compute(
            cumulativeContributedCapital: 10_000_000m,
            cumulativeDistributions: 13_000_000m,
            gpCarryReceived: 1_000_000m,
            carryRate: 0.20m);

        result.CumulativeProfit.Should().Be(3_000_000m);
        result.GpCarryEntitled.Should().Be(600_000m);
        result.HasClawback.Should().BeTrue();
        result.ClawbackDue.Should().Be(400_000m);
    }

    [Fact]
    public void NoProfit_FullCarryIsClawedBack()
    {
        var result = CarriedInterestClawbackCalculator.Compute(
            cumulativeContributedCapital: 10_000_000m,
            cumulativeDistributions: 9_000_000m, // fund lost money overall
            gpCarryReceived: 500_000m,
            carryRate: 0.20m);

        result.CumulativeProfit.Should().Be(0m);
        result.GpCarryEntitled.Should().Be(0m);
        result.ClawbackDue.Should().Be(500_000m);
    }
}
