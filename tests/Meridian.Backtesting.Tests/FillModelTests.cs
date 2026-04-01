using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

public sealed class FillModelTests
{
    private static MarketEvent MakeBarEvent(string symbol, decimal open, decimal high, decimal low, decimal close) =>
        MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, symbol, new HistoricalBar(
            symbol, DateOnly.FromDateTime(DateTime.Today), open, high, low, close, 100_000L, "test"));

    private static MarketEvent MakeLobEvent(string symbol, decimal askPrice, long askQty, decimal? bidPrice = null, long bidQty = 1_000L) =>
        MarketEvent.L2Snapshot(DateTimeOffset.UtcNow, symbol, new LOBSnapshot(
            DateTimeOffset.UtcNow,
            symbol,
            Bids: [new OrderBookLevel(OrderBookSide.Bid, 0, bidPrice ?? askPrice - 0.01m, bidQty)],
            Asks: [new OrderBookLevel(OrderBookSide.Ask, 0, askPrice, askQty)]));

    [Fact]
    public void BarMidpointFillModel_FillsMarketOrder_AtMidpoint()
    {
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(402.5m);
        result.Fills[0].FilledQuantity.Should().Be(10L);
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void BarMidpointFillModel_LimitBuy_FillsIfLowTouched()
    {
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, 397m, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(397m);
    }

    [Fact]
    public void BarMidpointFillModel_StopLimit_Buy_TriggersAndFillsAtLimit()
    {
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopLimit,
            10L,
            LimitPrice: 404m,
            StopPrice: 403m,
            SubmittedAt: DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 399m, 404m);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(404m);
    }

    [Fact]
    public void BarMidpointFillModel_LimitBuy_NoFillIfNotTouched()
    {
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, 390m, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void OrderBookFillModel_FillsBuyAtAsk()
    {
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 100L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 410m, 200L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(410m);
        result.Fills[0].FilledQuantity.Should().Be(100L);
        result.RemoveOrder.Should().BeTrue();
    }

    [Fact]
    public void OrderBookFillModel_PartialFill_KeepsWorkingOrder()
    {
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 100L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 410m, 40L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(40L);
        result.RemoveOrder.Should().BeFalse();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.UpdatedOrder.RemainingQuantity.Should().Be(60L);
    }

    [Fact]
    public void OrderBookFillModel_FillOrKill_CancelsWhenLiquidityInsufficient()
    {
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Market,
            100L,
            null,
            null,
            DateTimeOffset.UtcNow,
            TimeInForce.FillOrKill);
        var evt = MakeLobEvent("SPY", 410m, 50L);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
        result.RemoveOrder.Should().BeTrue();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void OrderBookFillModel_StopMarketSell_TriggersFromBid()
    {
        var model = new OrderBookFillModel(new FixedCommissionModel(0m));
        var order = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopMarket,
            -25L,
            null,
            StopPrice: 399m,
            SubmittedAt: DateTimeOffset.UtcNow);
        var evt = MakeLobEvent("SPY", 401m, 100L, bidPrice: 398.5m, bidQty: 100L);

        var result = model.TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().HaveCount(1);
        result.Fills[0].FilledQuantity.Should().Be(-25L);
        result.Fills[0].FillPrice.Should().Be(398.5m);
    }

    [Fact]
    public void BarMidpointFillModel_WrongSymbol_ReturnsEmpty()
    {
        var model = new BarMidpointFillModel(new FixedCommissionModel(0m));
        var order = new Order(Guid.NewGuid(), "AAPL", OrderType.Market, 10L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 410m, 395m, 405m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty();
    }

    // Bar: open=100, high=110, low=90, close=100 → mid=100, range=20, volatilityFactor=0.2

    [Fact]
    public void BarMidpointFillModel_SpreadAware_DefaultMultiplier_ProducesExpectedSlippage()
    {
        // effectiveSlippage = 10 * (1 + 0.2 * 50) = 110 bps; slip = 100 * 0.011 = 1.1; fillPrice = 101.1
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 10m,
            spreadAware: true);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 1L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 110m, 90m, 100m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(101.1m);
    }

    [Fact]
    public void BarMidpointFillModel_SpreadAware_CustomMultiplier100_IncreasesVolatilityScaling()
    {
        // effectiveSlippage = 10 * (1 + 0.2 * 100) = 210 bps; slip = 100 * 0.021 = 2.1; fillPrice = 102.1
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 10m,
            spreadAware: true,
            volatilityMultiplier: 100m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 1L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 110m, 90m, 100m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(102.1m);
    }

    [Fact]
    public void BarMidpointFillModel_SpreadAware_ZeroMultiplier_ProducesBaseSlippageOnly()
    {
        // effectiveSlippage = 10 * (1 + 0.2 * 0) = 10 bps (same as non-spread-aware); fillPrice = 100.1
        var model = new BarMidpointFillModel(
            new FixedCommissionModel(0m),
            slippageBasisPoints: 10m,
            spreadAware: true,
            volatilityMultiplier: 0m);

        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Market, 1L, null, null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 100m, 110m, 90m, 100m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().HaveCount(1);
        result.Fills[0].FillPrice.Should().Be(100.1m);
    }
}
