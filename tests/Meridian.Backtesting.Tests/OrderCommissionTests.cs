using FluentAssertions;
using Meridian.Backtesting.FillModels;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Tests;

public sealed class OrderCommissionTests
{
    private static readonly DateTimeOffset Timestamp = new(2024, 1, 2, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("bar", false)]
    [InlineData("bar", true)]
    [InlineData("impact", false)]
    [InlineData("impact", true)]
    [InlineData("book", false)]
    [InlineData("book", true)]
    public void PartialFills_ApplyMinimumAndMaximumToOrderRatherThanEachExecution(string modelKind, bool capMaximum)
    {
        var commission = capMaximum
            ? new PerShareCommissionModel(0.03m, 1m, 2m)
            : new PerShareCommissionModel(0.005m, 1m);
        var model = CreateModel(modelKind, commission);
        var order = MarketOrder(100);
        var first = model.TryFill(order, Event(modelKind, 100m));
        var second = model.TryFill(first.UpdatedOrder, Event(modelKind, 101m));
        var third = model.TryFill(second.UpdatedOrder, Event(modelKind, 102m));
        var fills = first.Fills.Concat(second.Fills).Concat(third.Fills).ToArray();

        first.UpdatedOrder.Status.Should().Be(OrderStatus.PartiallyFilled);
        third.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
        fills.Sum(fill => fill.FilledQuantity).Should().Be(100);
        fills.Sum(fill => fill.Commission).Should().Be(capMaximum ? 2m : 1m);
        fills.Should().OnlyContain(fill => fill.Commission >= 0);
        third.UpdatedOrder.CommissionCharged.Should().Be(capMaximum ? 2m : 1m);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("impact")]
    [InlineData("book")]
    public void PartialFills_PercentageUsesCumulativeNotionalAtActualExecutionPrices(string modelKind)
    {
        var model = CreateModel(modelKind, new PercentageCommissionModel(10m, 1m));
        var order = MarketOrder(-100);
        var first = model.TryFill(order, Event(modelKind, 100m));
        var second = model.TryFill(first.UpdatedOrder, Event(modelKind, 200m));
        var third = model.TryFill(second.UpdatedOrder, Event(modelKind, 50m));
        var fills = first.Fills.Concat(second.Fills).Concat(third.Fills).ToArray();

        third.UpdatedOrder.Status.Should().Be(OrderStatus.Filled);
        fills.Sum(fill => fill.FilledQuantity).Should().Be(-100);
        var notional = fills.Sum(fill => Math.Abs(fill.FilledQuantity) * fill.FillPrice);
        fills.Sum(fill => fill.Commission).Should().BeApproximately(notional * 0.001m, 0.00000000000001m);
        third.UpdatedOrder.FilledNotional.Should().Be(notional);
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("impact")]
    public void ImmediateOrder_PartialFillChargesOneMinimumAndCancelsTheRemainder(string modelKind)
    {
        var model = CreateModel(modelKind, new PerShareCommissionModel(0.005m, 1m));
        var order = MarketOrder(100) with { TimeInForce = TimeInForce.ImmediateOrCancel };

        var result = model.TryFill(order, Event(modelKind, 100m));
        var afterCancellation = model.TryFill(result.UpdatedOrder, Event(modelKind, 101m));

        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
        result.Fills.Sum(fill => fill.FilledQuantity).Should().Be(40);
        result.Fills.Sum(fill => fill.Commission).Should().Be(1m);
        result.UpdatedOrder.CommissionCharged.Should().Be(1m);
        result.UpdatedOrder.FilledNotional.Should().Be(4_000m);
        afterCancellation.Fills.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bar")]
    [InlineData("impact")]
    public void FillOrKill_InsufficientLiquidityDoesNotConsumeCommissionMinimum(string modelKind)
    {
        var model = CreateModel(modelKind, new PerShareCommissionModel(0.005m, 1m));
        var order = MarketOrder(100) with { TimeInForce = TimeInForce.FillOrKill };

        var result = model.TryFill(order, Event(modelKind, 100m));

        result.UpdatedOrder.Status.Should().Be(OrderStatus.Cancelled);
        result.RemoveOrder.Should().BeTrue();
        result.Fills.Should().BeEmpty();
        result.UpdatedOrder.CommissionCharged.Should().Be(0m);
        result.UpdatedOrder.FilledNotional.Should().Be(0m);
    }

    private static IFillModel CreateModel(string kind, ICommissionModel commission) => kind switch
    {
        "bar" => new BarMidpointFillModel(commission, slippageBasisPoints: 0m, maxParticipationRate: 1m),
        "impact" => new MarketImpactFillModel(commission, impactCoefficient: 0m,
            slippageBasisPoints: 0m, maxParticipationRate: 1m),
        "book" => new OrderBookFillModel(commission),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Order MarketOrder(long quantity) => new(Guid.NewGuid(), "SPY", OrderType.Market,
        quantity, null, null, Timestamp, AllowPartialFills: true);

    private static MarketEvent Event(string kind, decimal price) => kind == "book"
        ? MarketEvent.L2Snapshot(Timestamp, "SPY", new LOBSnapshot(Timestamp, "SPY",
            Bids: [new OrderBookLevel(OrderBookSide.Bid, 0, price, 20),
                new OrderBookLevel(OrderBookSide.Bid, 1, price, 20)],
            Asks: [new OrderBookLevel(OrderBookSide.Ask, 0, price, 20),
                new OrderBookLevel(OrderBookSide.Ask, 1, price, 20)]), source: "TEST")
        : MarketEvent.HistoricalBar(Timestamp, "SPY", new HistoricalBar("SPY", new DateOnly(2024, 1, 2),
            price, price, price, price, 40, "TEST"), source: "TEST");
}
