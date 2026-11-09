using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Time-in-force rules shared by the bar-based fill models. A bar model sees an order once per
/// bar, so "immediate" means the first bar on which the order is executable: an
/// <see cref="TimeInForce.ImmediateOrCancel"/> or <see cref="TimeInForce.FillOrKill"/> order
/// takes what that bar offers and is then cancelled rather than left working on later bars.
/// </summary>
internal static class BarTimeInForce
{
    /// <summary>The order must fill (or cancel) on the first bar it is executable.</summary>
    public static bool IsImmediate(Order order)
        => order.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill;

    /// <summary>Fill-or-kill is all-or-nothing regardless of <see cref="Order.AllowPartialFills"/>.</summary>
    public static bool AllowsPartialFill(Order order)
        => order.AllowPartialFills && order.TimeInForce != TimeInForce.FillOrKill;

    /// <summary>
    /// Result for an executable order that receives no fill on this bar: a resting order stays
    /// working, an immediate order is cancelled and removed.
    /// </summary>
    public static OrderFillResult NoFillThisBar(Order order, bool triggered)
    {
        var cancel = IsImmediate(order);
        return new OrderFillResult(
            order with
            {
                Status = cancel ? OrderStatus.Cancelled : order.Status,
                IsTriggered = triggered
            },
            [],
            RemoveOrder: cancel,
            WasTriggered: triggered && !order.IsTriggered);
    }
}
