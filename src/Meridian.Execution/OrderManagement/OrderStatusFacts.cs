using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>Shared order-status classification for the OMS and its order-management helpers.</summary>
internal static class OrderStatusFacts
{
    /// <summary>
    /// Filled, cancelled, rejected or expired: the order can no longer trade and its id may be
    /// reclaimed by a later submission. Excludes <see cref="OrderStatus.PendingCancel"/>: an order
    /// awaiting cancel confirmation is still working at the broker and its id must not be reused.
    /// </summary>
    public static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;
}
