using System.Collections.Concurrent;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Retention of finished orders: the bounded order table is trimmed of terminal entries once it
/// exceeds its configured size, together with the per-order sidecars that only a tracked order
/// can use.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private readonly ConcurrentDictionary<string, byte> _brokerageSubmittingOrderIds = new(StringComparer.Ordinal);

    private IDisposable? TrackBrokerageSubmissionForRetention(string orderId) =>
        _gateway is IBrokerageGateway || _recoveryStore is not null
            ? new BrokerageSubmissionRetentionLease(this, orderId)
            : null;

    private void TrimRetainedOrdersIfNeeded()
    {
        if (_orders.Count <= _options.ValidatedMaxRetainedOrders || !_brokerageRecoveryReportGate.Wait(0))
        {
            return;
        }

        try
        {
            TrimRetainedOrdersCore();
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    private void TrimRetainedOrdersCore()
    {

        // A fill that has left the order book but not yet reached the portfolio still needs
        // its tracked state and its sidecars. ProcessFillReportAsync reads the contract
        // multiplier from _orderContractMultipliers, so evicting an option order in that
        // window makes the fill fall back to 1 and books a standard contract at a hundredth
        // of its exposure; losing the order entirely can also leave the report untracked.
        var pendingFillOrderIds = _pendingFillReservations.Keys
            .Concat(_fillProcessing.Where(static entry => !entry.Value.IsComplete).Select(static entry => entry.Key))
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
            .Where(order => !_brokerageSubmittingOrderIds.ContainsKey(order.OrderId))
            .Where(order => !_inFlightDispatches.ContainsKey(order.OrderId))
            .Where(order => !_recoveryOrderIds.ContainsKey(order.OrderId))
            .Where(order => !pendingFillOrderIds.Contains(order.OrderId))
            .OrderBy(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .ToArray();

        foreach (var order in removableOrderIds)
        {
            if (_orders.Count <= _options.ValidatedMaxRetainedOrders)
                break;
            var removableOrderId = order.OrderId;
            lock (_brokerageRecoveryStateSync)
            {
                if (_recoveryOrderIds.ContainsKey(removableOrderId))
                    continue;
                if (_dispatchedOrderIds.ContainsKey(removableOrderId))
                {
                    // Durable identity and cumulative fills outlive the bounded display cache.
                    // Cancelled/expired orders still need their account and sizing for late fills;
                    // the store only compacts full fills or definitive pre-acceptance rejections.
                    try
                    {
                        if (_recoveryStore?.TryCompact(removableOrderId) != true)
                            continue;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(ex, "Cannot compact retained brokerage order {OrderId}; keeping recovery evidence",
                            LogSanitizer.Sanitize(removableOrderId));
                        continue;
                    }
                    _compactedBrokerageOrders[removableOrderId] = new BrokerageOrderTombstone(
                        removableOrderId, order.Status, order.Quantity, order.FilledQuantity);
                    _brokerageRecoveryVersions.Remove(removableOrderId);
                }
                if (!_orders.TryRemove(new KeyValuePair<string, OrderState>(removableOrderId, order)))
                    continue;
            }
            _orderBrokerIds.TryRemove(removableOrderId, out _);
            _orderSessionIds.TryRemove(removableOrderId, out _);
            _orderFinancialAccountIds.TryRemove(removableOrderId, out _);
            _orderContractMultipliers.TryRemove(removableOrderId, out _);
            _orderFaceValueSizing.TryRemove(removableOrderId, out _);
            _adoptedFillGaps.TryRemove(removableOrderId, out _);
        }
    }

    private bool IsCompactedBrokerageReport(ExecutionReport report)
    {
        if (!_compactedBrokerageOrders.TryGetValue(report.ClientOrderId ?? report.OrderId, out var tombstone))
            return false;
        if (report.FilledQuantity > tombstone.FilledQuantity)
        {
            // A compacted order has no legitimate remaining execution. Do not adopt a
            // contradictory callback as an unrelated external order with missing scope.
            var failure = new InvalidDataException("Broker execution contradicts a completed order recovery watermark.");
            MarkTerminalReportPumpFailure(failure, "Compacted brokerage order received inconsistent execution evidence.");
            throw failure;
        }
        return true;
    }

    private sealed class BrokerageSubmissionRetentionLease : IDisposable
    {
        private readonly OrderManagementSystem _owner;
        private readonly string _orderId;

        public BrokerageSubmissionRetentionLease(OrderManagementSystem owner, string orderId)
        {
            _owner = owner;
            _orderId = orderId;
            owner._brokerageSubmittingOrderIds[orderId] = 0;
        }

        public void Dispose()
        {
            _owner._brokerageSubmittingOrderIds.TryRemove(_orderId, out _);
            _owner.TrimRetainedOrdersIfNeeded();
        }
    }
}
