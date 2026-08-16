using FluentAssertions;
using Meridian.Execution.PaperMatching;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Commission, fee, slippage, and spread cost math applied to every paper fill.
/// </summary>
public sealed class PaperTradingCostModelTests
{
    [Fact]
    public void DefaultSchedule_ChargesPerShareCommissionWithMinimum()
    {
        var model = new PaperTradingCostModel();

        var costs = model.Compute(quantity: 10m, fillPrice: 100m, midPrice: null);

        costs.Commission.Should().Be(1.00m,
            "10 shares at 0.005/share is 0.05, floored by the 1.00 minimum");
        costs.CostModelVersion.Should().Be(PaperTradingCostModel.CostModelVersion);
    }

    [Fact]
    public void LargeOrder_ChargesPerShareCommissionAboveMinimum()
    {
        var model = new PaperTradingCostModel();

        var costs = model.Compute(quantity: 1_000m, fillPrice: 50m, midPrice: null);

        costs.Commission.Should().Be(5.00m, "1000 shares at 0.005/share");
    }

    [Fact]
    public void ZeroRates_MeanExplicitlyFreeExecution()
    {
        var model = new PaperTradingCostModel(new PaperTradingCostOptions
        {
            CommissionRate = 0m,
            CommissionMinimum = 1.00m
        });

        var costs = model.Compute(quantity: 100m, fillPrice: 100m, midPrice: null);

        costs.Commission.Should().Be(0m, "a zero commission rate is explicitly free and must not be floored");
        costs.ExplicitCost.Should().Be(0m);
    }

    [Fact]
    public void BasisPointCommission_UsesNotional()
    {
        var model = new PaperTradingCostModel(new PaperTradingCostOptions
        {
            CommissionKind = PaperCommissionKind.BasisPointsOfNotional,
            CommissionRate = 5m,
            CommissionMinimum = 0m
        });

        var costs = model.Compute(quantity: 100m, fillPrice: 200m, midPrice: null);

        costs.Commission.Should().Be(10.00m, "5 bps of the 20,000 notional");
    }

    [Fact]
    public void CommissionMaximum_CapsTheCharge()
    {
        var model = new PaperTradingCostModel(new PaperTradingCostOptions
        {
            CommissionRate = 0.01m,
            CommissionMaximum = 2.50m
        });

        var costs = model.Compute(quantity: 10_000m, fillPrice: 10m, midPrice: null);

        costs.Commission.Should().Be(2.50m);
    }

    [Fact]
    public void FeesAndSlippage_ChargeOnNotional()
    {
        var model = new PaperTradingCostModel(new PaperTradingCostOptions
        {
            CommissionRate = 0m,
            FeePerOrder = 0.50m,
            FeeBasisPoints = 1m,
            SlippageBasisPoints = 2m
        });

        var costs = model.Compute(quantity: 100m, fillPrice: 100m, midPrice: null);

        costs.Fees.Should().Be(1.50m, "0.50 flat plus 1 bp of the 10,000 notional");
        costs.SlippageCost.Should().Be(2.00m, "2 bps of the 10,000 notional");
        costs.ExplicitCost.Should().Be(3.50m);
    }

    [Fact]
    public void SpreadCost_ReportsDistanceFromMidpoint()
    {
        var model = new PaperTradingCostModel(new PaperTradingCostOptions { CommissionRate = 0m });

        var costs = model.Compute(quantity: 10m, fillPrice: 102m, midPrice: 101m);

        costs.SpreadCost.Should().Be(10.00m,
            "filling 10 shares one unit away from the midpoint is a 10.00 implicit spread cost");
        costs.ExplicitCost.Should().Be(0m, "spread cost is embedded in the fill price, not charged again");
    }

    [Fact]
    public void ZeroQuantityOrPrice_ProducesZeroCosts()
    {
        var model = new PaperTradingCostModel();

        model.Compute(0m, 100m, null).Should().Be(PaperFillCostBreakdown.Zero);
        model.Compute(10m, 0m, null).Should().Be(PaperFillCostBreakdown.Zero);
    }

    [Fact]
    public void NegativeQuantity_UsesAbsoluteValue()
    {
        var model = new PaperTradingCostModel();

        var costs = model.Compute(quantity: -1_000m, fillPrice: 50m, midPrice: null);

        costs.Commission.Should().Be(5.00m, "sells charge the same per-share commission as buys");
    }
}
