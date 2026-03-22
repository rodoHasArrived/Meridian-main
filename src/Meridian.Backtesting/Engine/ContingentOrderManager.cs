namespace Meridian.Backtesting.Engine;

internal static class ContingentOrderManager
{
    public static IReadOnlyList<Order> CreateContingentOrders(Order parentOrder, FillEvent fill)
    {
        if (parentOrder.ParentOrderId is not null)
            return [];

        if (!parentOrder.TakeProfitPrice.HasValue && !parentOrder.StopLossPrice.HasValue)
            return [];

        var exitQuantity = -fill.FilledQuantity;
        if (exitQuantity == 0)
            return [];

        var ocoGroupId = parentOrder.TakeProfitPrice.HasValue && parentOrder.StopLossPrice.HasValue
            ? Guid.NewGuid()
            : (Guid?)null;

        var orders = new List<Order>(capacity: 2);

        if (parentOrder.TakeProfitPrice is { } takeProfitPrice)
        {
            orders.Add(new Order(
                Guid.NewGuid(),
                parentOrder.Symbol,
                OrderType.Limit,
                exitQuantity,
                takeProfitPrice,
                null,
                fill.FilledAt,
                TimeInForce: TimeInForce.GoodTilCancelled,
                ExecutionModel: parentOrder.ExecutionModel,
                AllowPartialFills: parentOrder.AllowPartialFills,
                ProviderParameters: parentOrder.ProviderParameters,
                AccountId: fill.AccountId ?? parentOrder.AccountId,
                ParentOrderId: parentOrder.OrderId,
                OcoGroupId: ocoGroupId));
        }

        if (parentOrder.StopLossPrice is { } stopLossPrice)
        {
            orders.Add(new Order(
                Guid.NewGuid(),
                parentOrder.Symbol,
                OrderType.StopMarket,
                exitQuantity,
                null,
                stopLossPrice,
                fill.FilledAt,
                TimeInForce: TimeInForce.GoodTilCancelled,
                ExecutionModel: parentOrder.ExecutionModel,
                AllowPartialFills: parentOrder.AllowPartialFills,
                ProviderParameters: parentOrder.ProviderParameters,
                AccountId: fill.AccountId ?? parentOrder.AccountId,
                ParentOrderId: parentOrder.OrderId,
                OcoGroupId: ocoGroupId));
        }

        return orders;
    }

    public static void ReconcileOcoSiblings(List<Order> pendingOrders, Order filledOrder, FillEvent fill)
    {
        if (filledOrder.OcoGroupId is not { } ocoGroupId)
            return;

        var reduction = Math.Abs(fill.FilledQuantity);
        if (reduction == 0)
            return;

        for (var i = 0; i < pendingOrders.Count; i++)
        {
            var sibling = pendingOrders[i];
            if (sibling.OrderId == filledOrder.OrderId || sibling.OcoGroupId != ocoGroupId)
                continue;

            var siblingRemaining = sibling.RemainingQuantity;
            var nextRemaining = Math.Max(0L, siblingRemaining - reduction);
            if (nextRemaining == 0)
            {
                pendingOrders[i] = sibling with { Status = OrderStatus.Cancelled };
                continue;
            }

            var newSignedQuantity = Math.Sign(sibling.Quantity) * nextRemaining;
            pendingOrders[i] = sibling with { Quantity = newSignedQuantity };
        }
    }
}
