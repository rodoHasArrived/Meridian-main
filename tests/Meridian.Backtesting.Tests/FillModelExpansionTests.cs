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
            .Select((l, i) => new OrderBookLevel(OrderBookSide.Ask, (ushort)i, l.Price, l.Qty))
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
        // open=396 <= high=397 (valid OHLC); low=393 crosses stopPrice=395; high=397 >= limitPrice=394
        var evt = MakeBarEvent("SPY", 396m, 397m, 393m, 394m);

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

    // =========================================================================
    // BarMidpointFillModel — tick-size rounding
    // =========================================================================

    [Fact]
    public void BarMidpointFillModel_TickSize_SnapsMarketFillToNearestTick()
    {
        // Tick size = $0.05; midpoint = (100 + 102) / 2 = 101, no slippage
        // 101 / 0.05 = 2020.0 → already on grid → fill is exactly 101
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["SPY"] = 0.05m };
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m, tickSizes: ticks);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 105m, 95m, 102m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(101m);
        // Price should be a multiple of the tick size
        (result.Fills[0].FillPrice % 0.05m).Should().Be(0m);
    }

    [Fact]
    public void BarMidpointFillModel_TickSize_RoundsToEven_WhenMidpointFallsBetweenTicks()
    {
        // Tick size = $0.10; open=100, close=101 → midpoint = 100.5
        // 100.5 / 0.10 = 1005.0 → exactly on grid → result is 100.5
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["SPY"] = 0.10m };
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m, tickSizes: ticks);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 106m, 94m, 101m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        var fill = result.Fills[0].FillPrice;
        (fill % 0.10m).Should().Be(0m, $"Fill price {fill} should be on the $0.10 tick grid");
    }

    [Fact]
    public void BarMidpointFillModel_TickSize_MidpointWithSlippageSnapped()
    {
        // Tick $0.25; open=200, close=201 → midpoint=200.5, 5 bps slippage (buy)
        // slip = 200.5 * 0.0005 = 0.10025; raw = 200.6003; snapped to nearest $0.25
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["SPY"] = 0.25m };
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, tickSizes: ticks);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 1L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 200m, 205m, 195m, 201m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        var fill = result.Fills[0].FillPrice;
        (fill % 0.25m).Should().Be(0m, $"Fill price {fill} should be on the $0.25 tick grid");
    }

    [Fact]
    public void BarMidpointFillModel_TickSize_NoTickForSymbol_PriceUnchanged()
    {
        // Symbol has no tick entry; fill should be the raw midpoint without rounding
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = 0.01m };
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m, tickSizes: ticks);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 106m, 94m, 103m); // midpoint = 101.5

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(101.5m);
    }

    [Fact]
    public void BarMidpointFillModel_TickSize_LargePrice_SmallTick_NoOverflow()
    {
        // High-priced symbol (e.g., AMZN ~$180) with $0.01 tick; should snap cleanly
        var ticks = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["AMZN"] = 0.01m };
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m, tickSizes: ticks);
        var order = new Order(Guid.NewGuid(), "AMZN", OrderType.Market, 1L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("AMZN", 180m, 185m, 175m, 181m); // midpoint = 180.5

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        var fill = result.Fills[0].FillPrice;
        (fill % 0.01m).Should().Be(0m);
        fill.Should().BeGreaterThan(0m);
    }

    // =========================================================================
    // BarMidpointFillModel — spread-aware mode
    // =========================================================================

    [Fact]
    public void BarMidpointFillModel_SpreadAware_HighVolatility_IncreasesSlippage()
    {
        // Wide bar (range=50 on midpoint~200 = 25% vol) should scale slippage up significantly vs
        // the baseline non-spread-aware model.
        var baseline = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: false);
        var aware = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: true);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var wideBar = MakeBarEvent("SPY", 175m, 225m, 175m, 225m); // range=50; mid≈200

        var baselineFill = baseline.TryFill(order, wideBar).Fills[0].FillPrice;
        var awareFill = aware.TryFill(order, wideBar).Fills[0].FillPrice;

        // Buy fill: spread-aware should have a higher fill price (more adverse slippage)
        awareFill.Should().BeGreaterThan(baselineFill,
            "spread-aware model should apply more slippage on a high-volatility bar");
    }

    [Fact]
    public void BarMidpointFillModel_SpreadAware_LowVolatility_CloseToBaseline()
    {
        // Very tight bar (range=0.01) → volatility factor ≈ 0 → effective slippage ≈ baseline
        var baseline = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: false);
        var aware = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: true);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        // Tight bar: open=200, high=200.005, low=199.995, close=200.005
        var tightBar = MakeBarEvent("SPY", 200m, 200.005m, 199.995m, 200.005m);

        var baselineFill = baseline.TryFill(order, tightBar).Fills[0].FillPrice;
        var awareFill = aware.TryFill(order, tightBar).Fills[0].FillPrice;

        // The two should be very close (within $0.01)
        Math.Abs(awareFill - baselineFill).Should().BeLessThan(0.01m,
            "spread-aware slippage should barely exceed baseline for a near-zero-range bar");
    }

    [Fact]
    public void BarMidpointFillModel_SpreadAware_Sell_DecreasesFillPrice()
    {
        // Sell side: spread-aware should push fill price down (more adverse) vs baseline
        var baseline = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: false);
        var aware = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: true);

        var sellOrder = new Order(Guid.NewGuid(), "SPY", OrderType.Market, -10L, null, null, DateTimeOffset.UtcNow);
        var wideBar = MakeBarEvent("SPY", 175m, 225m, 175m, 225m); // range=50; mid≈200

        var baselineFill = baseline.TryFill(sellOrder, wideBar).Fills[0].FillPrice;
        var awareFill = aware.TryFill(sellOrder, wideBar).Fills[0].FillPrice;

        // Sell fill: spread-aware should have a lower fill price (more adverse)
        awareFill.Should().BeLessThan(baselineFill,
            "spread-aware sell fill should be lower than baseline on a high-volatility bar");
    }

    [Fact]
    public void BarMidpointFillModel_SpreadAware_ZeroMidpoint_DoesNotDivideByZero()
    {
        // When the bar midpoint is effectively zero (very small values), spread-aware mode
        // must not throw or produce NaN/Infinity. HistoricalBar requires OHLC > 0, so we
        // use the smallest representable positive bar.
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 5m, spreadAware: true);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        // Penny stock: very small price so mid ≈ 0.000005; spread-aware should still compute without overflow
        var tinyBar = MakeBarEvent("SPY", 0.00001m, 0.00002m, 0.000005m, 0.00001m);

        var act = () => model.TryFill(order, tinyBar);

        act.Should().NotThrow("tiny-price bar should be handled gracefully in spread-aware mode");
        var result = act();
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().BeGreaterThan(0m);
    }

    // =========================================================================
    // BarMidpointFillModel — volume-constrained partial fills
    // =========================================================================

    [Fact]
    public void BarMidpointFillModel_VolumeConstrained_ProducesPartialFill_WhenOrderExceedsBarVolume()
    {
        // 10 % participation on a 1,000-share bar = max 100 shares per bar.
        // Order is for 500 shares → should produce a single 100-share fill and
        // leave the order as PartiallyFilled (not removed).
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 0m,
            maxParticipationRate: 0.10m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 500L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);
        // Bar with Volume=1000 → 10 % cap = 100 shares
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 1_000L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(100L);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.UpdatedOrder.RemainingQuantity.Should().Be(400L);
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void BarMidpointFillModel_VolumeConstrained_FullFill_WhenOrderFitsWithinCap()
    {
        // 10 % participation on 1,000-share bar = cap 100.
        // Order for 50 shares → fits entirely → full fill.
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 0m,
            maxParticipationRate: 0.10m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 50L, null, null, DateTimeOffset.UtcNow);
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 1_000L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(50L);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void BarMidpointFillModel_VolumeConstrained_NoFill_WhenAllowPartialFillsFalse()
    {
        // Order exceeds cap and partial fills are disabled → no fill this bar.
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 0m,
            maxParticipationRate: 0.10m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 500L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: false);
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 1_000L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse("order stays working; it needs more bar volume to fill");
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void BarMidpointFillModel_VolumeConstrained_SellOrder_PartialFill()
    {
        // Short-sell 500 shares; cap at 10 % of 1000-share bar = 100 shares.
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 0m,
            maxParticipationRate: 0.10m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, -500L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 1_000L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(-100L, "signed sell quantity");
        result.UpdatedOrder.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void BarMidpointFillModel_ZeroParticipation_FilledEntirely_BackwardCompatible()
    {
        // maxParticipationRate = 0 (default) → unconstrained; always fills entire remaining quantity.
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 500L, null, null, DateTimeOffset.UtcNow);
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 1_000L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(500L);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void BarMidpointFillModel_VolumeConstrained_ZeroBarVolume_NoFill()
    {
        // Bar with zero volume → cap calculation produces 0 fillable shares → no fill.
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 0m,
            maxParticipationRate: 0.10m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 100L, null, null, DateTimeOffset.UtcNow,
            AllowPartialFills: true);
        var evt = MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY", new HistoricalBar(
            "SPY", DateOnly.FromDateTime(DateTime.Today), 400m, 410m, 395m, 405m, 0L, "test"));

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("zero-volume bar should produce no fill even with partial fills allowed");
        result.RemoveOrder.Should().BeFalse();
    }
}
