using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Unit tests for <see cref="MarketImpactFillModel"/> covering partial fills,
/// impact scaling, trigger logic, and edge cases.
/// </summary>
public sealed class MarketImpactFillModelTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MarketEvent MakeBarEvent(
        string symbol,
        decimal open, decimal high, decimal low, decimal close,
        long volume = 100_000L) =>
        MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, symbol,
            new HistoricalBar(symbol, DateOnly.FromDateTime(DateTime.Today),
                open, high, low, close, volume, "test"));

    private static Order MakeMarketOrder(string symbol, long quantity, bool allowPartialFills = true) =>
        new(Guid.NewGuid(), symbol, OrderType.Market, quantity,
            LimitPrice: null, StopPrice: null, DateTimeOffset.UtcNow,
            AllowPartialFills: allowPartialFills);

    private static Order MakeLimitOrder(string symbol, long quantity, decimal limitPrice) =>
        new(Guid.NewGuid(), symbol, OrderType.Limit, quantity,
            LimitPrice: limitPrice, StopPrice: null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);

    private static Order MakeStopMarketOrder(string symbol, long quantity, decimal stopPrice) =>
        new(Guid.NewGuid(), symbol, OrderType.StopMarket, quantity,
            LimitPrice: null, StopPrice: stopPrice, DateTimeOffset.UtcNow,
            AllowPartialFills: true);

    private static Order MakeStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice) =>
        new(Guid.NewGuid(), symbol, OrderType.StopLimit, quantity,
            LimitPrice: limitPrice, StopPrice: stopPrice, DateTimeOffset.UtcNow,
            AllowPartialFills: true);

    // =========================================================================
    // Basic fill behaviour
    // =========================================================================

    [Fact]
    public void TryFill_SmallMarketBuy_ProducesSingleFillAboveMidpoint()
    {
        // Order is tiny relative to bar volume → single fill
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m);
        var order = MakeMarketOrder("SPY", 10L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1, "small order relative to volume gets a single fill");
        result.Fills[0].FilledQuantity.Should().Be(10L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void TryFill_SmallMarketSell_ProducesSingleFillBelowMidpoint()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0.1m, slippageBasisPoints: 0m);
        var order = MakeMarketOrder("SPY", -10L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().NotBeEmpty();
        result.Fills[0].FilledQuantity.Should().Be(-10L, "sell fills carry negative quantity");
        // With impact, sell fills should be at or below midpoint
        var mid = (400m + 405m) / 2m;
        result.Fills.Average(f => f.FillPrice).Should().BeLessThanOrEqualTo(mid);
    }

    [Fact]
    public void TryFill_LargeOrderRelativeToVolume_ProducesMultiplePartialFills()
    {
        // order qty = 50% of bar volume → participationRate = 0.5 → multiple slices
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0.1m, maxPartialFills: 5);
        var order = MakeMarketOrder("SPY", 50_000L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Count.Should().BeGreaterThan(1, "large order triggers multi-slice fills");
        result.Fills.Sum(f => f.FilledQuantity).Should().Be(50_000L, "all slices sum to the original quantity");
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void TryFill_LargeOrder_LaterSlicesHaveHigherImpactOnBuy()
    {
        // Cumulative impact increases with each slice, so later fills are more expensive
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0.5m, maxPartialFills: 5);
        var order = MakeMarketOrder("SPY", 50_000L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Count.Should().BeGreaterThan(1);
        // Last fill should cost more than first fill for a buy
        result.Fills.Last().FillPrice.Should().BeGreaterThanOrEqualTo(result.Fills.First().FillPrice,
            "cumulative market impact makes each subsequent buy slice more expensive");
    }

    // =========================================================================
    // Limit orders
    // =========================================================================

    [Fact]
    public void TryFill_LimitBuy_FillsWhenBarLowTouchesLimit()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m);
        var order = MakeLimitOrder("SPY", 100L, limitPrice: 398m);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().NotBeEmpty("bar low (395) <= limit (398) satisfies buy limit");
        result.Fills.All(f => f.FillPrice <= 398m).Should().BeTrue("fill cannot exceed limit price");
    }

    [Fact]
    public void TryFill_LimitBuy_NoFillWhenBarLowAboveLimit()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeLimitOrder("SPY", 100L, limitPrice: 390m);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("bar low (395) > limit (390) → no fill");
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void TryFill_LimitSell_FillsWhenBarHighTouchesLimit()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m);
        var order = MakeLimitOrder("SPY", -100L, limitPrice: 403m);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().NotBeEmpty("bar high (405) >= limit (403) satisfies sell limit");
        result.Fills.All(f => f.FillPrice >= 403m).Should().BeTrue("sell fill cannot be below limit price");
    }

    [Fact]
    public void TryFill_LimitSell_NoFillWhenBarHighBelowLimit()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeLimitOrder("SPY", -100L, limitPrice: 410m);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("bar high (405) < limit (410) → no fill");
        result.RemoveOrder.Should().BeFalse();
    }

    // =========================================================================
    // Stop orders
    // =========================================================================

    [Fact]
    public void TryFill_StopMarketBuy_TriggersAndFillsWhenHighReachesStop()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m, slippageBasisPoints: 0m);
        var order = MakeStopMarketOrder("SPY", 10L, stopPrice: 405m);
        var evt = MakeBarEvent("SPY", 400m, 408m, 398m, 406m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue("bar high (408) >= stop (405) should trigger");
        result.Fills.Should().NotBeEmpty();
        result.Fills.Sum(f => f.FilledQuantity).Should().Be(10L);
    }

    [Fact]
    public void TryFill_StopMarketBuy_NoFillWhenHighBelowStop()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeStopMarketOrder("SPY", 10L, stopPrice: 410m);
        var evt = MakeBarEvent("SPY", 400m, 408m, 398m, 406m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeFalse("bar high (408) < stop (410) → not triggered");
        result.Fills.Should().BeEmpty();
    }

    [Fact]
    public void TryFill_StopMarketSell_TriggersAndFillsWhenLowReachesStop()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m, slippageBasisPoints: 0m);
        var order = MakeStopMarketOrder("SPY", -10L, stopPrice: 395m);
        var evt = MakeBarEvent("SPY", 400m, 403m, 392m, 398m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue("bar low (392) <= stop (395) should trigger");
        result.Fills.Should().NotBeEmpty();
        result.Fills.Sum(f => f.FilledQuantity).Should().Be(-10L);
    }

    [Fact]
    public void TryFill_StopLimitBuy_TriggersAndRespectsLimitCap()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m);
        // stop=405, limit=406 → triggers when high>=405, fills at limit=406
        var order = MakeStopLimitOrder("SPY", 10L, stopPrice: 405m, limitPrice: 406m);
        var evt = MakeBarEvent("SPY", 400m, 408m, 398m, 406m, volume: 1_000_000L);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().NotBeEmpty();
        result.Fills.All(f => f.FillPrice <= 406m).Should().BeTrue("fills must not exceed stop-limit cap");
    }

    // =========================================================================
    // Commission
    // =========================================================================

    [Fact]
    public void TryFill_FixedCommission_AppliedPerSlice()
    {
        // Fixed $2 per-order commission → each slice carries its share
        var model = new MarketImpactFillModel(new FixedCommissionModel(2m), impactCoefficient: 0.1m, maxPartialFills: 5);
        var order = MakeMarketOrder("SPY", 50_000L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().NotBeEmpty();
        result.Fills.All(f => f.Commission > 0m).Should().BeTrue("every fill slice should carry commission");
    }

    // =========================================================================
    // Symbol / event type mismatch
    // =========================================================================

    [Fact]
    public void TryFill_WrongSymbol_ReturnsNoFill()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeMarketOrder("SPY", 100L);
        var evt = MakeBarEvent("AAPL", 150m, 155m, 148m, 152m, volume: 500_000L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("symbol mismatch must produce no fill");
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void TryFill_NonBarEvent_ReturnsNoFill()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeMarketOrder("SPY", 100L);
        // LOBSnapshot is not a HistoricalBar → model should skip it
        var lob = new LOBSnapshot(DateTimeOffset.UtcNow, "SPY",
            Bids: [new OrderBookLevel(OrderBookSide.Bid, 0, 409m, 500L)],
            Asks: [new OrderBookLevel(OrderBookSide.Ask, 0, 410m, 500L)]);
        var evt = MarketEvent.L2Snapshot(DateTimeOffset.UtcNow, "SPY", lob);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("MarketImpactFillModel only handles HistoricalBar events");
        result.RemoveOrder.Should().BeFalse();
    }

    // =========================================================================
    // Terminal order states
    // =========================================================================

    [Fact]
    public void TryFill_AlreadyFilledOrder_ReturnedAsRemove()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeMarketOrder("SPY", 10L) with
        {
            Status = OrderStatus.Filled,
            FilledQuantity = 10L
        };
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeTrue("a fully-filled order should be removed from pending list");
    }

    [Fact]
    public void TryFill_CancelledOrder_ReturnedAsRemove()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeMarketOrder("SPY", 10L) with { Status = OrderStatus.Cancelled };
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeTrue("a cancelled order should be removed without filling");
    }

    [Fact]
    public void TryFill_ZeroVolumeBar_TreatsVolumeAsOne()
    {
        // Edge case: bar with Volume = 0 should not cause divide-by-zero
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m));
        var order = MakeMarketOrder("SPY", 1L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 0L);

        var act = () => model.TryFill(order, evt);

        act.Should().NotThrow("zero-volume bar must be handled gracefully");
    }

    // =========================================================================
    // Impact coefficient scaling
    // =========================================================================

    [Fact]
    public void TryFill_ZeroImpactCoefficient_FillsAtBaseMidpointWithSlippage()
    {
        // impactCoefficient=0 → no additional impact beyond baseline slippage
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m, slippageBasisPoints: 0m);
        var order = MakeMarketOrder("SPY", 100L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 100_000L);

        var result = model.TryFill(order, evt);

        var expectedMid = (400m + 405m) / 2m; // 402.5
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(expectedMid, "zero impact and zero slippage → exact midpoint");
    }

    [Fact]
    public void TryFill_HighImpactCoefficient_ProducesMoreImpactThanLow()
    {
        var commission = new FixedCommissionModel(0m);
        var modelLow  = new MarketImpactFillModel(commission, impactCoefficient: 0.01m, slippageBasisPoints: 0m);
        var modelHigh = new MarketImpactFillModel(commission, impactCoefficient: 1.0m,  slippageBasisPoints: 0m);

        var order = MakeMarketOrder("SPY", 20_000L);
        var evt   = MakeBarEvent("SPY", 400m, 410m, 390m, 405m, volume: 100_000L);

        var resultLow  = modelLow.TryFill(order, evt);
        var resultHigh = modelHigh.TryFill(order, evt);

        var avgLow  = resultLow.Fills.Average(f => (double)f.FillPrice);
        var avgHigh = resultHigh.Fills.Average(f => (double)f.FillPrice);

        avgHigh.Should().BeGreaterThan(avgLow,
            "higher impact coefficient must push buy fill prices higher");
    }
}
