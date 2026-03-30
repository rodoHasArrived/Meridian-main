using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Tests for the new fill-model and commission-model configuration properties
/// on <see cref="BacktestRequest"/> (Track D roadmap item).
/// </summary>
public sealed class BacktestRequestConfigTests
{
    // -------------------------------------------------------------------------
    // Default values
    // -------------------------------------------------------------------------

    [Fact]
    public void BacktestRequest_Defaults_AreBackwardCompatible()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31));

        request.DefaultExecutionModel.Should().Be(ExecutionModel.Auto);
        request.SlippageBasisPoints.Should().Be(5m);
        request.CommissionKind.Should().Be(BacktestCommissionKind.PerShare);
        request.CommissionRate.Should().Be(0.005m);
        request.CommissionMinimum.Should().Be(1.00m);
        request.CommissionMaximum.Should().Be(decimal.MaxValue);
        request.MarketImpactCoefficient.Should().Be(0.1m);
    }

    // -------------------------------------------------------------------------
    // Commission model selection
    // -------------------------------------------------------------------------

    [Fact]
    public void PerShareCommission_CalculatesCorrectly()
    {
        var model = new PerShareCommissionModel(
            perShare: 0.005m,
            minimumPerOrder: 1.00m,
            maximumPerOrder: decimal.MaxValue);

        // 100 shares * $0.005 = $0.50, below minimum → capped at $1.00
        model.Calculate("SPY", 100L, 450m).Should().Be(1.00m);

        // 500 shares * $0.005 = $2.50, above minimum
        model.Calculate("SPY", 500L, 450m).Should().Be(2.50m);
    }

    [Fact]
    public void PerShareCommission_RespectsMaximum_WhenConfigured()
    {
        var model = new PerShareCommissionModel(
            perShare: 0.005m,
            minimumPerOrder: 1.00m,
            maximumPerOrder: 10m);

        // 5,000 shares * $0.005 = $25, capped at $10
        model.Calculate("SPY", 5_000L, 100m).Should().Be(10m);
    }

    [Fact]
    public void PercentageCommission_CalculatesCorrectly()
    {
        var model = new PercentageCommissionModel(basisPoints: 5m, minimumPerOrder: 1.00m);

        // 100 shares * $400 * 5bps = $400,000 * 0.0005 = $20
        model.Calculate("SPY", 100L, 400m).Should().Be(20m);
    }

    [Fact]
    public void FreeCommission_AlwaysReturnsZero()
    {
        var model = new FixedCommissionModel(0m);

        model.Calculate("SPY", 10_000L, 500m).Should().Be(0m);
        model.Calculate("AAPL", 1L, 1m).Should().Be(0m);
    }

    // -------------------------------------------------------------------------
    // BacktestCommissionKind enum coverage
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BacktestCommissionKind.PerShare)]
    [InlineData(BacktestCommissionKind.Percentage)]
    [InlineData(BacktestCommissionKind.Free)]
    public void BacktestCommissionKind_AllValuesAreDistinct(BacktestCommissionKind kind)
    {
        // Ensures enum serialization round-trips correctly
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31),
            CommissionKind: kind);

        request.CommissionKind.Should().Be(kind);
    }

    // -------------------------------------------------------------------------
    // DefaultExecutionModel
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ExecutionModel.Auto)]
    [InlineData(ExecutionModel.BarMidpoint)]
    [InlineData(ExecutionModel.OrderBook)]
    [InlineData(ExecutionModel.MarketImpact)]
    public void DefaultExecutionModel_CanBeSetToAnyValue(ExecutionModel model)
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31),
            DefaultExecutionModel: model);

        request.DefaultExecutionModel.Should().Be(model);
    }

    // -------------------------------------------------------------------------
    // Slippage and market impact coefficient
    // -------------------------------------------------------------------------

    [Fact]
    public void SlippageBasisPoints_CanBeSetToZero()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31),
            SlippageBasisPoints: 0m);

        request.SlippageBasisPoints.Should().Be(0m);
    }

    [Fact]
    public void MarketImpactCoefficient_CanBeSetToHighValue()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31),
            MarketImpactCoefficient: 0.5m);

        request.MarketImpactCoefficient.Should().Be(0.5m);
    }

    // -------------------------------------------------------------------------
    // BarMidpointFillModel respects custom slippage from request
    // -------------------------------------------------------------------------

    [Fact]
    public void BarMidpointFillModel_RespectsRequestSlippage_ZeroBps_NoSlippage()
    {
        var commission = new FixedCommissionModel(0m);
        var model = new Meridian.Backtesting.FillModels.BarMidpointFillModel(commission, slippageBasisPoints: 0m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);

        var bar = new Meridian.Contracts.Domain.Models.HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 390m, 405m, 100_000L, "test");
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", bar);

        var result = model.TryFill(order, evt);

        // mid = (400 + 405) / 2 = 402.5, slippage = 0 → fill at exactly 402.5
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(402.5m);
    }

    [Fact]
    public void BarMidpointFillModel_RespectsRequestSlippage_HighBps_IncreasedCost()
    {
        var commission = new FixedCommissionModel(0m);
        var modelLow = new Meridian.Backtesting.FillModels.BarMidpointFillModel(commission, slippageBasisPoints: 0m);
        var modelHigh = new Meridian.Backtesting.FillModels.BarMidpointFillModel(commission, slippageBasisPoints: 100m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var bar = new Meridian.Contracts.Domain.Models.HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 390m, 405m, 100_000L, "test");
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", bar);

        var lowResult = modelLow.TryFill(order, evt);
        var highResult = modelHigh.TryFill(order, evt);

        // Higher slippage → higher fill price on a buy
        highResult.Fills[0].FillPrice.Should().BeGreaterThan(lowResult.Fills[0].FillPrice);
    }

    // -------------------------------------------------------------------------
    // MarketImpactFillModel respects custom coefficient from request
    // -------------------------------------------------------------------------

    [Fact]
    public void MarketImpactFillModel_HigherCoefficient_IncreasesImpact()
    {
        var commission = new FixedCommissionModel(0m);
        var modelLow = new Meridian.Backtesting.FillModels.MarketImpactFillModel(commission, impactCoefficient: 0.01m);
        var modelHigh = new Meridian.Backtesting.FillModels.MarketImpactFillModel(commission, impactCoefficient: 0.5m);

        // Large order relative to bar volume to trigger impact
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10_000L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);
        var bar = new Meridian.Contracts.Domain.Models.HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 390m, 405m, 50_000L, "test");
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", bar);

        var lowResult = modelLow.TryFill(order, evt);
        var highResult = modelHigh.TryFill(order, evt);

        lowResult.Fills.Should().NotBeEmpty();
        highResult.Fills.Should().NotBeEmpty();

        // Higher coefficient → higher average fill price on a buy
        var avgLow = lowResult.Fills.Average(f => (double)f.FillPrice);
        var avgHigh = highResult.Fills.Average(f => (double)f.FillPrice);
        avgHigh.Should().BeGreaterThan(avgLow);
    }
}
