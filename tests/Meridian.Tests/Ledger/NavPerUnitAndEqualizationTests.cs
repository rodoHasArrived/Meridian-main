using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class NavPerUnitAndEqualizationTests
{
    [Fact]
    public void NavPerUnit_UsesInceptionPriceWhenNoUnitsOutstanding()
    {
        NavPerUnitCalculator.ComputeNavPerUnit(0m, 0m, inceptionNavPerUnit: 100m).Should().Be(100m);
    }

    [Fact]
    public void UnitsForSubscription_DividesAmountByPrice()
    {
        NavPerUnitCalculator.UnitsForSubscription(1_050_000m, 105m).Should().Be(10_000m);
    }

    [Fact]
    public void CashForRedemption_MultipliesUnitsByPrice()
    {
        NavPerUnitCalculator.CashForRedemption(2_500m, 118.50m).Should().Be(296_250m);
    }

    [Fact]
    public void Equalisation_CreditAboveHighWater()
    {
        // 10,000 units, NAV 120 vs HW 100, 20% perf fee => 0.20 * 20 * 10,000 = 40,000.
        var adjustment = EqualizationCalculator.Compute(120m, 100m, 10_000m, 0.20m);

        adjustment.EqualisationCredit.Should().Be(40_000m);
        adjustment.ContingentRedemption.Should().Be(0m);
        adjustment.HasEqualisationCredit.Should().BeTrue();
    }

    [Fact]
    public void Equalisation_ContingentBelowHighWater()
    {
        // NAV 90 vs HW 100 => 0.20 * 10 * 10,000 = 20,000 contingent redemption.
        var adjustment = EqualizationCalculator.Compute(90m, 100m, 10_000m, 0.20m);

        adjustment.ContingentRedemption.Should().Be(20_000m);
        adjustment.EqualisationCredit.Should().Be(0m);
    }

    [Fact]
    public void Equalisation_NoAdjustmentAtHighWaterOrZeroFee()
    {
        EqualizationCalculator.Compute(100m, 100m, 10_000m, 0.20m).Should().Be(new EqualizationAdjustment(0m, 0m));
        EqualizationCalculator.Compute(120m, 100m, 10_000m, 0m).Should().Be(new EqualizationAdjustment(0m, 0m));
    }
}
