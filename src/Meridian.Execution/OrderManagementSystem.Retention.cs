using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Retention of finished orders: the bounded order table is trimmed of terminal entries once it
/// exceeds its configured size, together with the per-order sidecars that only a tracked order
/// can use.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private void TrimRetainedOrdersIfNeeded()
    {
        if (_orders.Count <= _options.ValidatedMaxRetainedOrders)
        {
            return;
        }

        // A fill that has left the order book but not yet reached the portfolio still needs
        // its tracked state and its sidecars. ProcessFillReportAsync reads the contract
        // multiplier from _orderContractMultipliers, so evicting an option order in that
        // window makes the fill fall back to 1 and books a standard contract at a hundredth
        // of its exposure; losing the order entirely can also leave the report untracked.
        var pendingFillOrderIds = _pendingFillReservations.Keys
            .Select(static report => report.ClientOrderId ?? report.OrderId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removableOrderIds = _orders.Values
            .Where(order => order.Status is
                OrderStatus.Filled or
                OrderStatus.Cancelled or
                OrderStatus.Rejected or
                OrderStatus.Expired)
            // A parked order is recorded Rejected but is not finished: its escalation is
            // still live in the durable queue and can still route. Evicting its tracked
            // state makes CancelOrderAsync answer "order not found", stranding an approval
            // the submitter can no longer withdraw. Retain it until the escalation
            // resolves, which is exactly when its reservation is dropped.
            .Where(order => !_parkedOrderIds.ContainsKey(order.OrderId))
            .Where(order => !pendingFillOrderIds.Contains(order.OrderId))
            .OrderBy(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .Take(_orders.Count - _options.ValidatedMaxRetainedOrders)
            .Select(static order => order.OrderId)
            .ToArray();

        foreach (var removableOrderId in removableOrderIds)
        {
            _orders.TryRemove(removableOrderId, out _);
            _orderBrokerIds.TryRemove(removableOrderId, out _);
            _orderSessionIds.TryRemove(removableOrderId, out _);
            _orderFinancialAccountIds.TryRemove(removableOrderId, out _);
            _orderContractMultipliers.TryRemove(removableOrderId, out _);
            _orderFaceValueSizing.TryRemove(removableOrderId, out _);
            _adoptedFillGaps.TryRemove(removableOrderId, out _);
        }
    }
}
