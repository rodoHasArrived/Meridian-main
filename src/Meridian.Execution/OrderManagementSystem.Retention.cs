using System.Collections.Concurrent;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
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
        _gateway is IBrokerageGateway || _recoveryJournal.HasStore
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
        // its tracked state and its sidecars. OrderFillProcessor.ProcessAsync reads the contract
        // multiplier from _orderContractMultipliers, so evicting an option order in that
        // window makes the fill fall back to 1 and books a standard contract at a hundredth
        // of its exposure; losing the order entirely can also leave the report untracked.
        var pendingFillOrderIds = _fillProcessor.GetIncompleteOrderIds();

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
            .Where(order => !_approvalReservations.Contains(order.OrderId))
            .Where(order => !_brokerageSubmittingOrderIds.ContainsKey(order.OrderId))
            .Where(order => !_dispatchRegistry.Contains(order.OrderId))
            .Where(order => !_recoveryJournal.RequiresRecovery(order.OrderId))
            .Where(order => !pendingFillOrderIds.Contains(order.OrderId))
            .OrderBy(static order => order.LastUpdatedAt ?? order.CreatedAt)
            .ToArray();

        foreach (var order in removableOrderIds)
        {
            if (_orders.Count <= _options.ValidatedMaxRetainedOrders)
                break;
            var removableOrderId = order.OrderId;
            try
            {
                // Durable identity and cumulative fills outlive the bounded display cache.
                // The journal rechecks recovery and compacts only safe terminal evidence
                // while holding the same lock as this expected-state removal.
                if (!_recoveryJournal.TryCompactAndRemove(order,
                    () => _orders.TryRemove(new KeyValuePair<string, OrderState>(removableOrderId, order))))
                    continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Cannot compact retained brokerage order {OrderId}; keeping recovery evidence",
                    LogSanitizer.Sanitize(removableOrderId));
                continue;
            }
            _brokerIdentities.ForgetActiveId(removableOrderId);
            _orderSessionIds.TryRemove(removableOrderId, out _);
            _orderFinancialAccountIds.TryRemove(removableOrderId, out _);
            _orderContractMultipliers.TryRemove(removableOrderId, out _);
            _orderFaceValueSizing.TryRemove(removableOrderId, out _);
            _adoptedFillGaps.TryRemove(removableOrderId, out _);
        }
    }

    private bool IsCompactedBrokerageReport(ExecutionReport report)
    {
        try
        {
            return _recoveryJournal.IsCompactedReport(report);
        }
        catch (InvalidDataException failure)
        {
            // A compacted order has no legitimate remaining execution. Do not adopt a
            // contradictory callback as an unrelated external order with missing scope.
            MarkTerminalReportPumpFailure(failure, "Compacted brokerage order received inconsistent execution evidence.");
            throw;
        }
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
