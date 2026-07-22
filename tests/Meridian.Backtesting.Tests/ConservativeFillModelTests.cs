using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Covers the conservative limit/stop semantics that are now the default for the bar-based fill
/// models: trade-through limits (a bare touch does not fill), gap-aware limit pricing, and stop
/// fills that can never beat the stop price.
/// </summary>
public sealed class ConservativeFillModelTests
{
    private static MarketEvent MakeBarEvent(string symbol, decimal open, decimal high, decimal low, decimal close, long volume = 100_000L) =>
        MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, symbol, new HistoricalBar(
            symbol, DateOnly.FromDateTime(DateTime.Today), open, high, low, close, volume, "test"));

    private static BarMidpointFillModel CreateModel(FillConservatism conservatism = FillConservatism.Conservative) =>
        new(new FixedCommissionModel(0m), slippageBasisPoints: 0m, conservatism: conservatism);

    // ────────────────────────────────────────────────────────────────────────
    // Limit orders: touch vs trade-through
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LimitBuy_BareTouchAtLow_DoesNotFill()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, LimitPrice: 395m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().BeEmpty("bar low exactly equals the limit — queue position means a touch is not a fill");
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void LimitBuy_BareTouch_OptimisticModeStillFills()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, LimitPrice: 395m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m);

        var result = CreateModel(FillConservatism.Optimistic).TryFill(order, evt);

        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(395m,
            "legacy optimistic mode preserves touch-fill behaviour");
    }

    [Fact]
    public void LimitBuy_TradedThrough_FillsAtLimit()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, LimitPrice: 397m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(397m,
            "the bar traded strictly through the limit (low 395 < 397)");
    }

    [Fact]
    public void LimitBuy_GapOpenBelowLimit_FillsAtOpen()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 10L, LimitPrice: 400m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 394m, 401m, 393m, 399m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(394m,
            "a bar that opens through a buy limit fills at the opening price, not the (worse) limit");
    }

    [Fact]
    public void LimitSell_BareTouchAtHigh_DoesNotFill()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, -10L, LimitPrice: 405m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().BeEmpty("bar high exactly equals the sell limit — a touch is not a fill");
    }

    [Fact]
    public void LimitSell_GapOpenAboveLimit_FillsAtOpen()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, -10L, LimitPrice: 400m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 406m, 408m, 399m, 401m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(406m,
            "a bar that opens through a sell limit fills at the opening price");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Stop-market orders: fills can never beat the stop
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StopMarketBuy_GapOpenAboveStop_FillsAtOpenNotStop()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopMarket, 10L, LimitPrice: null, StopPrice: 405m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 410m, 412m, 408m, 411m);

        var result = CreateModel().TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(410m,
            "the market gapped over the stop — the fill happens at the (worse) opening price");
    }

    [Fact]
    public void StopMarketSell_TriggeredInBar_FillsAtStopNotMidpoint()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopMarket, -10L, LimitPrice: null, StopPrice: 395m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 401m, 393m, 399m);

        var result = CreateModel().TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(395m,
            "a sell stop hit inside the bar fills at the stop, never at the better midpoint (399.5)");
    }

    [Fact]
    public void StopMarketSell_GapOpenBelowStop_FillsAtOpen()
    {
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopMarket, -10L, LimitPrice: null, StopPrice: 395m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 388m, 392m, 386m, 390m);

        var result = CreateModel().TryFill(order, evt);

        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(388m,
            "a gap below the sell stop fills at the opening price, modelling real gap risk");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Stop-limit orders: trigger-bar semantics
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StopLimitBuy_TriggerBar_FillsAtWorstInRangePriceUnderLimit()
    {
        // stop 403, limit 406, bar open 400 high 405: trigger = max(403, 400) = 403;
        // limit is marketable, conservative price = min(limit 406, high 405) = 405.
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopLimit, 10L, LimitPrice: 406m, StopPrice: 403m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 399m, 404m);

        var result = CreateModel().TryFill(order, evt);

        result.WasTriggered.Should().BeTrue();
        result.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(405m);
    }

    [Fact]
    public void StopLimitBuy_GapOpensBeyondLimit_NoFillOnTriggerBar_ThenFillsAsRestingLimit()
    {
        // stop 400, limit 402, bar gaps open at 405 → triggered but the limit is not marketable.
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopLimit, 10L, LimitPrice: 402m, StopPrice: 400m, DateTimeOffset.UtcNow);
        var triggerBar = MakeBarEvent("SPY", 405m, 407m, 403m, 406m);

        var model = CreateModel();
        var triggerResult = model.TryFill(order, triggerBar);

        triggerResult.WasTriggered.Should().BeTrue();
        triggerResult.Fills.Should().BeEmpty("the bar opened beyond the buy limit — no realistic fill exists");
        triggerResult.UpdatedOrder.IsTriggered.Should().BeTrue();

        // Next bar pulls back strictly through the limit → conservative resting-limit fill.
        var pullbackBar = MakeBarEvent("SPY", 403m, 404m, 401m, 402m);
        var pullbackResult = model.TryFill(triggerResult.UpdatedOrder, pullbackBar);

        pullbackResult.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(402m);
    }

    // ────────────────────────────────────────────────────────────────────────
    // MarketImpactFillModel parity
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MarketImpact_LimitBuy_BareTouch_DoesNotFill()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m, slippageBasisPoints: 0m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.Limit, 100L, LimitPrice: 395m, StopPrice: null, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 405m, 395m, 402m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().BeEmpty("the market-impact model applies the same trade-through rule");
    }

    [Fact]
    public void MarketImpact_StopMarketBuy_AnchorsToStopNotMidpoint()
    {
        var model = new MarketImpactFillModel(new FixedCommissionModel(0m), impactCoefficient: 0m, slippageBasisPoints: 0m);
        var order = new Order(Guid.NewGuid(), "SPY", OrderType.StopMarket, 100L, LimitPrice: null, StopPrice: 405m, DateTimeOffset.UtcNow);
        var evt = MakeBarEvent("SPY", 400m, 406m, 398m, 404m);

        var result = model.TryFill(order, evt);

        result.Fills.Should().NotBeEmpty();
        result.Fills.Should().OnlyContain(fill => fill.FillPrice >= 405m,
            "no slice of a triggered buy stop may execute below the stop price");
    }
}
