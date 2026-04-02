using FluentAssertions;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Margin, Financing, and Leverage models (Phase 4).
/// Validates <see cref="RegTMarginModel"/> and <see cref="PortfolioMarginModel"/>.
/// </summary>
public sealed class MarginModelTests
{
    private static ExecutionPosition MakePosition(string symbol, long qty, decimal avgCost) =>
        new(symbol, qty, avgCost, UnrealisedPnl: 0m, RealisedPnl: 0m);

    // -------------------------------------------------------------------------
    // RegTMarginModel — long positions
    // -------------------------------------------------------------------------

    [Fact]
    public void RegT_LongPosition_InitialMarginIsFiftyPercent()
    {
        var model = new RegTMarginModel();
        var position = MakePosition("AAPL", 100, 150m);

        var req = model.CalculateForPosition(position, lastPrice: 200m, portfolioEquity: 100_000m);

        req.NotionalValue.Should().Be(100 * 200m);
        req.InitialMargin.Should().Be(100 * 200m * 0.50m);
        req.MaintenanceMargin.Should().Be(100 * 200m * 0.25m);
    }

    [Fact]
    public void RegT_LongPosition_MarginCall_WhenEquityBelowMaintenance()
    {
        var model = new RegTMarginModel();
        var position = MakePosition("AAPL", 100, 200m);

        // Portfolio equity = $1000; notional = $20,000; maintenance = $5,000
        var req = model.CalculateForPosition(position, lastPrice: 200m, portfolioEquity: 1_000m);

        req.IsMarginCall.Should().BeTrue();
        req.ExcessLiquidity.Should().BeNegative();
    }

    [Fact]
    public void RegT_LongPosition_NoMarginCall_WhenEquityAboveMaintenance()
    {
        var model = new RegTMarginModel();
        var position = MakePosition("AAPL", 100, 200m);

        // notional = 20,000; maintenance = 5,000; equity = 20,000 ≥ 5,000
        var req = model.CalculateForPosition(position, lastPrice: 200m, portfolioEquity: 20_000m);

        req.IsMarginCall.Should().BeFalse();
        req.ExcessLiquidity.Should().BePositive();
    }

    // -------------------------------------------------------------------------
    // RegTMarginModel — short positions
    // -------------------------------------------------------------------------

    [Fact]
    public void RegT_ShortPosition_InitialMarginIsOneFiftyPercent()
    {
        var model = new RegTMarginModel();
        var position = MakePosition("TSLA", -50, 600m);

        var req = model.CalculateForPosition(position, lastPrice: 600m, portfolioEquity: 100_000m);

        req.NotionalValue.Should().Be(-50 * 600m);
        req.InitialMargin.Should().Be(50 * 600m * 1.50m);
    }

    // -------------------------------------------------------------------------
    // RegTMarginModel — portfolio aggregation
    // -------------------------------------------------------------------------

    [Fact]
    public void RegT_Portfolio_SumsRequirementsAcrossPositions()
    {
        var model = new RegTMarginModel();
        var positions = new Dictionary<string, ExecutionPosition>
        {
            ["AAPL"] = MakePosition("AAPL", 100, 150m),
            ["MSFT"] = MakePosition("MSFT", 50, 300m),
        };
        var prices = new Dictionary<string, decimal>
        {
            ["AAPL"] = 160m,
            ["MSFT"] = 320m,
        };

        var req = model.CalculatePortfolioRequirement(positions, prices, cash: 50_000m);

        req.Symbol.Should().BeNull();
        req.InitialMargin.Should().Be((100 * 160m * 0.50m) + (50 * 320m * 0.50m));
        req.MaintenanceMargin.Should().Be((100 * 160m * 0.25m) + (50 * 320m * 0.25m));
    }

    // -------------------------------------------------------------------------
    // PortfolioMarginModel
    // -------------------------------------------------------------------------

    [Fact]
    public void PortfolioMargin_LongPosition_RequirementReflectsStressLoss()
    {
        var model = new PortfolioMarginModel(stressDownPercent: 0.15m);
        var position = MakePosition("AAPL", 100, 200m);

        var req = model.CalculateForPosition(position, lastPrice: 200m, portfolioEquity: 100_000m);

        // Max loss = stress down: 100 * 200 * 0.15 = 3000
        req.InitialMargin.Should().BeGreaterThan(0m);
        req.MaintenanceMargin.Should().Be(req.InitialMargin * 0.80m);
    }

    [Fact]
    public void PortfolioMargin_Constructor_ThrowsOnZeroStressPercent()
    {
        var act = () => new PortfolioMarginModel(stressDownPercent: 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // MarginRequirement
    // -------------------------------------------------------------------------

    [Fact]
    public void MarginRequirement_Rates_AreComputedFromNotional()
    {
        var req = new MarginRequirement(
            Symbol: "AAPL",
            NotionalValue: 10_000m,
            InitialMargin: 5_000m,
            MaintenanceMargin: 2_500m,
            ExcessLiquidity: 7_500m);

        req.InitialMarginRate.Should().Be(0.50m);
        req.MaintenanceMarginRate.Should().Be(0.25m);
        req.IsMarginCall.Should().BeFalse();
    }
}
