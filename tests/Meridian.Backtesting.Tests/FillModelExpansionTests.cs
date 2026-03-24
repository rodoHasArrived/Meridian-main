using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

public sealed class FillModelExpansionTests
{
    // -------------------------------------------------------------------------
    // Helpers (same shape as FillModelTests)
    // -------------------------------------------------------------------------

    private static MarketEvent MakeBarEvent(string symbol, decimal open, decimal high, decimal low, decimal close) =>
        MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, symbol, new HistoricalBar(
            symbol, DateOnly.FromDateTime(DateTime.Today), open, high, low, close, 100_000L, "test"));

    private static MarketEvent MakeLobEvent(string symbol, decimal askPrice, long askQty, decimal? bidPrice = null, long bidQty = 1_000L) =>
        MarketEvent.L2Snapshot(DateTimeOffset.UtcNow, symbol, new LOBSnapshot(
            DateTimeOffset.UtcNow,
            symbol,
            Bids: [new OrderBookLevel(OrderBookSide.Bid, 0, bidPrice ?? askPrice - 0.01m, bidQty)],
            Asks: [new OrderBookLevel(OrderBookSide.Ask, 0, askPrice, askQty)]));

    private static MarketEvent MakeMultiLevelLobEvent(string symbol, IReadOnlyList<(decimal Price, long Qty)> askLevels, decimal bidPrice = 409m, long bidQty = 1_000L)
    {
        var asks = askLevels
            .Select((l, i) => new OrderBookLevel(OrderBookSide.Ask, i, l.Price, l.Qty))
            .ToArray();
        var bids = new[] { new OrderBookLevel(OrderBookSide.Bid, 0, bidPrice, bidQty) };
        return MarketEvent.L2Snapshot(DateTimeOffset.UtcNow, symbol, new LOBSnapshot(
            DateTimeOffset.UtcNow,
            symbol,
            Bids: bids,
            Asks: asks));
    }

    // =========================================================================
    // BarMidpointFillModel — additional cases
    // =========================================================================

    [Fact]
    public void BarMidpointFillModel_LimitSell_FillsIfHighTouched()
    {
        // sell limit=402, bar high=403 → high >= limit → fills at 402
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, -10L, LimitPrice: 402m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 403m, 398m, 401m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(402m);
        result.Fills[0].FilledQuantity.Should().Be(-10L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void BarMidpointFillModel_LimitSell_NoFillIfHighNotTouched()
    {
        // sell limit=408, bar high=405 → high < limit → no fill
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, -10L, LimitPrice: 408m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 403m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void BarMidpointFillModel_StopMarketBuy_TriggersWhenHighReachesStop()
    {
        // stop-market buy, stopPrice=405, bar high=406 → triggers and fills at midpoint
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m);
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.StopMarket, 10L,
            LimitPrice: null, StopPrice: 405m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 406m, 398m, 404m);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().HaveCount(1);
        // midpoint = (400 + 404) / 2 = 402, slippage = 0
        result.Fills[0].FillPrice.Should().Be(402m);
        result.Fills[0].FilledQuantity.Should().Be(10L);
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void BarMidpointFillModel_StopMarketSell_NoTriggerIfLowAboveStop()
    {
        // stop-market sell, stopPrice=395, bar low=396 → low > stop → no trigger, no fill
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.StopMarket, -10L,
            LimitPrice: null, StopPrice: 395m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 396m, 402m);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeFalse();
        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void BarMidpointFillModel_StopLimit_Sell_TriggersAndFillsAtLimit()
    {
        // stop-limit sell, stopPrice=395, limitPrice=394, bar low=393 → triggered, fills at 394
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopLimit,
            -10L,
            LimitPrice: 394m,
            StopPrice: 395m,
            SubmittedAt: DateTimeOffset.UtcNow);
        // high=397 >= limitPrice=394, so the limit condition is satisfied
        var evt = MakeBarEvent("SPY", 398m, 397m, 393m, 394m);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(394m);
        result.Fills[0].FilledQuantity.Should().Be(-10L);
    }

    [Fact]
    public void BarMidpointFillModel_Slippage_Buy_IncreasesPrice()
    {
        // market buy, 10 bps slippage; midpoint = (400 + 405) / 2 = 402.5
        // slip = 402.5 * 0.001 = 0.4025; fillPrice = 402.5 + 0.4025 = 402.9025
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 10m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(402.9025m);
    }

    [Fact]
    public void BarMidpointFillModel_Slippage_Sell_DecreasesPrice()
    {
        // market sell (-10 qty), 10 bps slippage; midpoint = (400 + 405) / 2 = 402.5
        // slip = 402.5 * 0.001 = 0.4025; fillPrice = 402.5 - 0.4025 = 402.0975
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 10m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, -10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(402.0975m);
    }

    [Fact]
    public void BarMidpointFillModel_AlreadyFilledOrder_ReturnedAsRemove()
    {
        // order already fully filled (IsComplete=true) → RemoveOrder=true, no new fills
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow,
            Status: OrderStatus.Filled,
            FilledQuantity: 10L);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void BarMidpointFillModel_CancelledOrder_ReturnedAsRemove()
    {
        // cancelled order → RemoveOrder=true, no new fills
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow,
            Status: OrderStatus.Cancelled);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeTrue();
    }

    // =========================================================================
    // OrderBookFillModel — additional cases
    // =========================================================================

    [Fact]
    public void OrderBookFillModel_LimitBuy_FillsAtAskWhenWithinLimit()
    {
        // limit buy at 411, ask is 410 → ask (410) <= limit (411) → fills at ask price
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 50L, LimitPrice: 411m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 410m, 200L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(410m);
        result.Fills[0].FilledQuantity.Should().Be(50L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void OrderBookFillModel_LimitBuy_NoFillWhenAskAboveLimit()
    {
        // limit buy at 405, ask is 410 → ask (410) > limit (405) → no fill
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 50L, LimitPrice: 405m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 410m, 200L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void OrderBookFillModel_LimitSell_FillsAtBidWhenWithinLimit()
    {
        // limit sell (-50) at 409, bid is 410 → bid (410) >= limit (409) → fills at bid price
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, -50L, LimitPrice: 409m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 411m, 100L, bidPrice: 410m, bidQty: 200L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(410m);
        result.Fills[0].FilledQuantity.Should().Be(-50L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void OrderBookFillModel_ImmediateOrCancel_CancelsRemainder()
    {
        // IOC market buy for 200, only 80 available at ask → fills 80, cancels remainder
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.Market, 200L, null, null, DateTimeOffset.UtcNow,
            TimeInForce: TimeInForce.ImmediateOrCancel,
            AllowPartialFills: true);
        var evt = MakeLobEvent("SPY", 410m, 80L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(80L);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void OrderBookFillModel_MultiLevel_ConsumesMultipleLevels()
    {
        // market buy for 300 shares; L2 book has 150@410 and 200@411
        // fills 150 from level 1 and 150 from level 2 → two fills totalling 300
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(), "SPY", OrderType.Market, 300L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);
        var evt = MakeMultiLevelLobEvent("SPY", [(410m, 150L), (411m, 200L)]);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(2);
        result.Fills[0].FillPrice.Should().Be(410m);
        result.Fills[0].FilledQuantity.Should().Be(150L);
        result.Fills[1].FillPrice.Should().Be(411m);
        result.Fills[1].FilledQuantity.Should().Be(150L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void OrderBookFillModel_WrongSymbol_ReturnsNoFill()
    {
        // buy SPY, LOBSnapshot for AAPL → symbol mismatch → no fill
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 100L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("AAPL", 410m, 200L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse();
    }

    // =========================================================================
    // Commission model tests
    // =========================================================================

    [Fact]
    public void PerShareCommissionModel_RespectsMinimum()
    {
        // 1 share * $0.005 = $0.005 < minimum $1.00 → commission is $1.00
        var model = new PerShareCommissionModel(perShare: 0.005m, minimumPerOrder: 1.00m, maximumPerOrder: decimal.MaxValue);

        var commission = model.Calculate("SPY", 1L, 200m);

        commission.Should().Be(1.00m);
    }

    [Fact]
    public void PerShareCommissionModel_RespectsMaximum()
    {
        // 1,000,000 shares * $0.005 = $5,000; maximum $100 → commission is $100
        var model = new PerShareCommissionModel(perShare: 0.005m, minimumPerOrder: 1.00m, maximumPerOrder: 100m);

        var commission = model.Calculate("SPY", 1_000_000L, 10m);

        commission.Should().Be(100m);
    }

    [Fact]
    public void PercentageCommissionModel_RespectsMinimum()
    {
        // 1 share * $10 price * 5 bps = 10 * 5/10000 = $0.005 < minimum $1.00 → commission is $1.00
        var model = new PercentageCommissionModel(basisPoints: 5m, minimumPerOrder: 1.00m);

        var commission = model.Calculate("SPY", 1L, 10m);

        commission.Should().Be(1.00m);
    }

    [Fact]
    public void PercentageCommissionModel_CalculatesCorrectly()
    {
        // 100 shares * $200 price * 5 bps = 20000 * 5/10000 = $10; minimum is $1.00 → commission is $10.00
        var model = new PercentageCommissionModel(basisPoints: 5m, minimumPerOrder: 1.00m);

        var commission = model.Calculate("SPY", 100L, 200m);

        commission.Should().Be(10.00m);
    }
}
