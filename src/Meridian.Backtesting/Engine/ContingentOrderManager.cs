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

    /// <summary>
    /// Adjusts the outstanding quantity of any OCO sibling of <paramref name="filledOrder"/> after
    /// a (partial) fill. When one leg of an OCO pair executes, the other leg's remaining quantity is
    /// reduced by the same absolute number of shares that were filled. If the sibling's remaining
    /// quantity reaches zero, it is removed immediately.
    ///
    /// <para>
    /// Reduction semantics: the sibling is reduced proportionally to the fill size rather than
    /// cancelled outright on any partial fill. This allows OCO orders that are themselves partially
    /// filled (e.g. from an <see cref="ExecutionModel.OrderBook"/> fill model) to wind down
    /// gracefully. A sibling with <c>RemainingQuantity == 0</c> after the reduction is removed
    /// from the authoritative working-order collection.
    /// </para>
    /// </summary>
    public static void ReconcileOcoSiblings(BacktestContext context, Order filledOrder, FillEvent fill)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (filledOrder.OcoGroupId is not { } ocoGroupId)
            return;

        var reduction = Math.Abs(fill.FilledQuantity);
        if (reduction == 0)
            return;

        foreach (var sibling in context.GetWorkingOrdersSnapshot())
        {
            if (sibling.OrderId == filledOrder.OrderId || sibling.OcoGroupId != ocoGroupId)
                continue;

            var siblingRemaining = sibling.RemainingQuantity;
            var nextRemaining = Math.Max(0L, siblingRemaining - reduction);
            if (nextRemaining == 0)
            {
                context.RemoveWorkingOrder(sibling.OrderId);
                continue;
            }

            // Quantity is the order's total target, while FilledQuantity is cumulative. Preserve
            // already executed shares and reduce only the still-working target; otherwise a
            // previously partial-filled sibling can become spuriously complete after this update.
            var newAbsoluteQuantity = Math.Abs(sibling.FilledQuantity) + nextRemaining;
            var newSignedQuantity = Math.Sign(sibling.Quantity) * newAbsoluteQuantity;
            context.UpdateWorkingOrder(sibling with { Quantity = newSignedQuantity });
        }
    }
}
