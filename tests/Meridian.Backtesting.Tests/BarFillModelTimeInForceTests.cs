using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// The bar-based fill models must honour all-or-nothing and immediate time-in-force rules:
/// an order that disallows partial fills never ends partially filled, and an IOC/FOK order is
/// resolved on the first bar it is executable instead of staying working across later bars.
/// </summary>
public sealed class BarFillModelTimeInForceTests
{
    private static MarketEvent Bar(decimal open, decimal high, decimal low, decimal close, long volume) =>
        MarketEvent.HistoricalBar(DateTimeOffset.UtcNow, "SPY",
            new HistoricalBar("SPY", new DateOnly(2024, 1, 2), open, high, low, close, volume, "test"), source: "TEST");

    private static Order LimitBuy(long quantity, decimal limit, TimeInForce tif, bool allowPartialFills = true) =>
        new(Guid.NewGuid(), "SPY", OrderType.Limit, quantity,
            LimitPrice: limit, StopPrice: null, DateTimeOffset.UtcNow,
            TimeInForce: tif, AllowPartialFills: allowPartialFills);

    private static Order MarketBuy(long quantity, TimeInForce tif, bool allowPartialFills = true) =>
        new(Guid.NewGuid(), "SPY", OrderType.Market, quantity,
            LimitPrice: null, StopPrice: null, DateTimeOffset.UtcNow,
            TimeInForce: tif, AllowPartialFills: allowPartialFills);

    public static TheoryData<string> BarModels => new() { "midpoint", "impact" };

    private static IFillModel Create(string model, decimal maxParticipationRate = 0m) => model switch
    {
        "midpoint" => new BarMidpointFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m,
            maxParticipationRate: maxParticipationRate),
        _ => new MarketImpactFillModel(new FixedCommissionModel(0m), slippageBasisPoints: 0m,
            maxParticipationRate: maxParticipationRate)
    };

    // Impact rises with each slice, so with a base of 100 and a 100.5 limit the first three of
    // five slices price inside the limit and the last two cross it and are skipped.
    private static MarketImpactFillModel LimitCrossingModel() =>
        new(new FixedCommissionModel(0m), impactCoefficient: 0.01m, slippageBasisPoints: 0m);

    [Fact]
    public void MarketImpact_NoPartialFills_LimitCrossingSlices_TakesNothingInsteadOfPartialFill()
    {
        var order = LimitBuy(50_000, 100.5m, TimeInForce.Day, allowPartialFills: false);

        var result = LimitCrossingModel().TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 100_000));

        result.Fills.Should().BeEmpty("an all-or-nothing order must not be partially executed");
        result.UpdatedOrder.FilledQuantity.Should().Be(0);
        result.UpdatedOrder.Status.Should().NotBe(OrderStatus.PartiallyFilled);
        result.RemoveOrder.Should().BeFalse("a resting Day order stays working for a later bar");
    }

    [Fact]
    public void MarketImpact_PartialFillsAllowed_LimitCrossingSlices_StillPartiallyFills()
    {
        var order = LimitBuy(50_000, 100.5m, TimeInForce.Day);

        var result = LimitCrossingModel().TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 100_000));

        result.Fills.Sum(fill => fill.FilledQuantity).Should().Be(30_000);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.RemoveOrder.Should().BeFalse();
    }

    [Fact]
    public void MarketImpact_ImmediateOrCancel_LimitCrossingSlices_KeepsFillAndCancelsRemainder()
    {
        var order = LimitBuy(50_000, 100.5m, TimeInForce.ImmediateOrCancel);

        var result = LimitCrossingModel().TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 100_000));

        result.Fills.Sum(fill => fill.FilledQuantity).Should().Be(30_000);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BarModels))]
    public void ImmediateOrCancel_LimitNotReached_IsCancelledInsteadOfLeftWorking(string model)
    {
        var order = LimitBuy(100, 90m, TimeInForce.ImmediateOrCancel);

        var result = Create(model).TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 100_000));

        result.Fills.Should().BeEmpty();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue("an IOC order must not stay working across later bars");
    }

    [Theory]
    [MemberData(nameof(BarModels))]
    public void DayOrder_LimitNotReached_StaysWorking(string model)
    {
        var order = LimitBuy(100, 90m, TimeInForce.Day);

        var result = Create(model).TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 100_000));

        result.Fills.Should().BeEmpty();
        result.UpdatedOrder.Status.Should().Be(order.Status);
        result.RemoveOrder.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BarModels))]
    public void ImmediateOrCancel_VolumeCappedFill_CancelsRemainder(string model)
    {
        var order = MarketBuy(1_000, TimeInForce.ImmediateOrCancel);

        // 10% of 4,000 shares caps the bar at 400.
        var result = Create(model, maxParticipationRate: 0.1m).TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 4_000));

        result.Fills.Sum(fill => fill.FilledQuantity).Should().Be(400);
        result.UpdatedOrder.FilledQuantity.Should().Be(400);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BarModels))]
    public void FillOrKill_VolumeCappedFill_IsKilledWithoutAnyFill(string model)
    {
        // AllowPartialFills is left at its default (true): fill-or-kill overrides it.
        var order = MarketBuy(1_000, TimeInForce.FillOrKill);

        var result = Create(model, maxParticipationRate: 0.1m).TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 4_000));

        result.Fills.Should().BeEmpty();
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BarModels))]
    public void FillOrKill_FullyFillable_Fills(string model)
    {
        var order = MarketBuy(100, TimeInForce.FillOrKill);

        var result = Create(model).TryFill(order, Bar(100m, 101m, 99m, 100m, volume: 1_000_000));

        result.Fills.Sum(fill => fill.FilledQuantity).Should().Be(100);
        result.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
        result.RemoveOrder.Should().BeTrue();
    }
}
