using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Tests;

public sealed class BracketOrderTests
{
    [Fact]
    public void PlaceBracketOrder_QueuesEntryWithAttachedExitTargets()
    {
        var ctx = CreateContext();
        ctx.CurrentTime = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);

        var orderId = ctx.PlaceBracketOrder(new BracketOrderRequest(
            Symbol: "SPY",
            Quantity: 10L,
            EntryType: OrderType.Limit,
            TakeProfitPrice: 110m,
            StopLossPrice: 95m,
            LimitPrice: 100m,
            AccountId: "broker-1"));

        var queuedOrder = ctx.GetWorkingOrdersSnapshot().Should().ContainSingle().Subject;
        queuedOrder.OrderId.Should().Be(orderId);
        queuedOrder.Type.Should().Be(OrderType.Limit);
        queuedOrder.LimitPrice.Should().Be(100m);
        queuedOrder.TakeProfitPrice.Should().Be(110m);
        queuedOrder.StopLossPrice.Should().Be(95m);
        queuedOrder.AccountId.Should().Be("broker-1");
    }

    [Fact]
    public void CancelContingentOrders_RemovesChildrenWithoutTouchingUnrelatedOrders()
    {
        var parentOrderId = Guid.NewGuid();
        var ctx = CreateContext();
        ctx.CurrentTime = DateTimeOffset.UtcNow;

        var unrelatedOrderId = ctx.PlaceMarketOrder("QQQ", 5L);
        var parentChildOne = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Limit,
            -10L,
            110m,
            null,
            DateTimeOffset.UtcNow,
            ParentOrderId: parentOrderId,
            OcoGroupId: Guid.NewGuid());
        var parentChildTwo = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopMarket,
            -10L,
            null,
            95m,
            DateTimeOffset.UtcNow,
            ParentOrderId: parentOrderId,
            OcoGroupId: parentChildOne.OcoGroupId);

        InjectPendingOrders(ctx, parentChildOne, parentChildTwo);

        ctx.CancelContingentOrders(parentOrderId);

        var remaining = ctx.GetWorkingOrdersSnapshot();
        remaining.Should().ContainSingle(order => order.OrderId == unrelatedOrderId);
    }

    [Fact]
    public void CreateContingentOrders_CreatesOcoTakeProfitAndStopLoss()
    {
        var parent = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Market,
            10L,
            null,
            null,
            DateTimeOffset.UtcNow,
            TakeProfitPrice: 110m,
            StopLossPrice: 95m,
            AccountId: "broker-1");

        var fill = new FillEvent(Guid.NewGuid(), parent.OrderId, "SPY", 10L, 100m, 0m, DateTimeOffset.UtcNow, "broker-1");

        var contingentOrders = ContingentOrderManager.CreateContingentOrders(parent, fill);

        contingentOrders.Should().HaveCount(2);
        contingentOrders.Select(static order => order.Type).Should().BeEquivalentTo([OrderType.Limit, OrderType.StopMarket]);
        contingentOrders.Should().OnlyContain(order => order.Quantity == -10L);
        contingentOrders.Should().OnlyContain(order => order.ParentOrderId == parent.OrderId);
        contingentOrders.Select(static order => order.OcoGroupId).Distinct().Should().ContainSingle();
        contingentOrders.Should().OnlyContain(order => order.AccountId == "broker-1");
    }

    [Fact]
    public void CreateContingentOrders_ForShortEntry_CreatesBuyToCoverExits()
    {
        var parent = new Order(
            Guid.NewGuid(),
            "QQQ",
            OrderType.Market,
            -5L,
            null,
            null,
            DateTimeOffset.UtcNow,
            TakeProfitPrice: 380m,
            StopLossPrice: 405m);

        var fill = new FillEvent(Guid.NewGuid(), parent.OrderId, "QQQ", -5L, 400m, 0m, DateTimeOffset.UtcNow);

        var contingentOrders = ContingentOrderManager.CreateContingentOrders(parent, fill);

        contingentOrders.Should().HaveCount(2);
        contingentOrders.Should().OnlyContain(order => order.Quantity == 5L);
        contingentOrders.Should().ContainSingle(order => order.Type == OrderType.Limit && order.LimitPrice == 380m);
        contingentOrders.Should().ContainSingle(order => order.Type == OrderType.StopMarket && order.StopPrice == 405m);
    }

    [Fact]
    public void ReconcileOcoSiblings_PartialFill_ReducesSiblingQuantity()
    {
        var ocoGroupId = Guid.NewGuid();
        var filledOrder = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Limit,
            -10L,
            110m,
            null,
            DateTimeOffset.UtcNow,
            OcoGroupId: ocoGroupId,
            ParentOrderId: Guid.NewGuid());
        var siblingOrder = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopMarket,
            -10L,
            null,
            95m,
            DateTimeOffset.UtcNow,
            OcoGroupId: ocoGroupId,
            ParentOrderId: filledOrder.ParentOrderId);

        var context = CreateContext();
        context.AddWorkingOrders([filledOrder, siblingOrder]);
        var partialFill = new FillEvent(Guid.NewGuid(), filledOrder.OrderId, "SPY", -4L, 110m, 0m, DateTimeOffset.UtcNow);

        ContingentOrderManager.ReconcileOcoSiblings(context, filledOrder, partialFill);

        var sibling = context.GetWorkingOrdersSnapshot().Single(order => order.OrderId == siblingOrder.OrderId);
        sibling.Quantity.Should().Be(-6L);
        sibling.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void ReconcileOcoSiblings_WhenSiblingWasPartiallyFilled_PreservesItsOpenRemainder()
    {
        var ocoGroupId = Guid.NewGuid();
        var parentOrderId = Guid.NewGuid();
        var filledOrder = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Limit,
            -10L,
            110m,
            null,
            DateTimeOffset.UtcNow,
            OcoGroupId: ocoGroupId,
            ParentOrderId: parentOrderId);
        var partiallyFilledSibling = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopMarket,
            -10L,
            null,
            95m,
            DateTimeOffset.UtcNow,
            Status: OrderStatus.PartiallyFilled,
            FilledQuantity: -4L,
            OcoGroupId: ocoGroupId,
            ParentOrderId: parentOrderId);

        var context = CreateContext();
        context.AddWorkingOrders([filledOrder, partiallyFilledSibling]);
        var newFill = new FillEvent(
            Guid.NewGuid(),
            filledOrder.OrderId,
            "SPY",
            -2L,
            110m,
            0m,
            DateTimeOffset.UtcNow);

        ContingentOrderManager.ReconcileOcoSiblings(context, filledOrder, newFill);

        var sibling = context.GetWorkingOrdersSnapshot()
            .Single(order => order.OrderId == partiallyFilledSibling.OrderId);
        sibling.Quantity.Should().Be(-8L, "the total target includes its four already-filled shares");
        sibling.FilledQuantity.Should().Be(-4L);
        sibling.RemainingQuantity.Should().Be(4L, "only the newly filled two shares reduce its open exposure");
    }

    [Fact]
    public void ReconcileOcoSiblings_FullFill_CancelsSibling()
    {
        var ocoGroupId = Guid.NewGuid();
        var parentOrderId = Guid.NewGuid();
        var filledOrder = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.Limit,
            -10L,
            110m,
            null,
            DateTimeOffset.UtcNow,
            OcoGroupId: ocoGroupId,
            ParentOrderId: parentOrderId);
        var siblingOrder = new Order(
            Guid.NewGuid(),
            "SPY",
            OrderType.StopMarket,
            -10L,
            null,
            95m,
            DateTimeOffset.UtcNow,
            OcoGroupId: ocoGroupId,
            ParentOrderId: parentOrderId);

        var context = CreateContext();
        context.AddWorkingOrders([filledOrder, siblingOrder]);
        var fullFill = new FillEvent(Guid.NewGuid(), filledOrder.OrderId, "SPY", -10L, 110m, 0m, DateTimeOffset.UtcNow);

        ContingentOrderManager.ReconcileOcoSiblings(context, filledOrder, fullFill);

        context.GetWorkingOrdersSnapshot().Should().NotContain(order => order.OrderId == siblingOrder.OrderId);
    }

    private static BacktestContext CreateContext()
    {
        var portfolio = new Meridian.Backtesting.Portfolio.SimulatedPortfolio(
            10_000m,
            new Meridian.Backtesting.Portfolio.FixedCommissionModel(0m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);

        return new BacktestContext(
            portfolio,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY", "QQQ" },
            new BacktestLedger(),
            "broker-1");
    }

    private static void InjectPendingOrders(BacktestContext ctx, params Order[] orders)
    {
        ctx.AddWorkingOrders(orders);
    }
}
