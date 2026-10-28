using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns broker-side sweep snapshots, identity matching and residual cancellation evidence.
/// The facade retains tracked-order cancellation, concurrency and final outcome reconciliation.
/// </summary>
internal sealed class BrokerOrderSweep(
    IExecutionGateway gateway,
    OrderBrokerIdentityIndex brokerIdentities,
    OrderExecutionAudit executionAudit,
    ILogger logger)
{
    private readonly IExecutionGateway _gateway = gateway;
    private readonly OrderBrokerIdentityIndex _brokerIdentities = brokerIdentities;
    private readonly OrderExecutionAudit _executionAudit = executionAudit;
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Snapshots the broker's own open-order book and returns the orders the in-memory sweep will
    /// not cover, the broker-assigned cancellation ids for tracked orders, plus the enumeration
    /// error when the broker view could not be established.
    /// <para>
    /// An enumeration failure must not abort the in-memory sweep — a broker that cannot list its
    /// book may still accept cancellations — but it is returned rather than swallowed, because
    /// "we could not look" and "nothing was there" are different answers to the only question a
    /// kill switch is asked, and the sweep outcome has to carry which one this was.
    /// </para>
    /// </summary>
    internal async Task<(
        IReadOnlyList<BrokerOrder> ResidualOrders,
        IReadOnlyDictionary<string, string> BrokerCancellationIds,
        string? EnumerationError)> SnapshotResidualOrdersAsync(
        IReadOnlyList<OrderState> sweepTargets,
        CancellationToken ct)
    {
        if (_gateway is not IBrokerageGateway brokerage)
        {
            // The gateway keeps no broker-side book of its own (paper gateways execute
            // in-process), so the in-memory view is the whole book by construction rather than
            // by assumption.
            return (Array.Empty<BrokerOrder>(), new Dictionary<string, string>(StringComparer.Ordinal), null);
        }

        IReadOnlyList<BrokerOrder> brokerOpenOrders;
        try
        {
            brokerOpenOrders = await brokerage.GetOpenOrdersAsync(ct).ConfigureAwait(false)
                ?? Array.Empty<BrokerOrder>();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cancel-all could not enumerate the broker's open orders; the sweep covers only the in-memory book");
            return (
                Array.Empty<BrokerOrder>(),
                new Dictionary<string, string>(StringComparer.Ordinal),
                exception.Message);
        }

        if (brokerOpenOrders.Count == 0)
        {
            return (Array.Empty<BrokerOrder>(), new Dictionary<string, string>(StringComparer.Ordinal), null);
        }

        var trackedIds = new HashSet<string>(
            sweepTargets.Select(static order => order.OrderId),
            StringComparer.Ordinal);
        var trackedByBrokerId = sweepTargets
            .Select(order => _brokerIdentities.TryGetActiveId(order.OrderId, out var brokerOrderId)
                ? (order.OrderId, BrokerOrderId: brokerOrderId)
                : (order.OrderId, BrokerOrderId: (string?)null))
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.BrokerOrderId))
            .GroupBy(static mapping => mapping.BrokerOrderId!, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single().OrderId,
                StringComparer.Ordinal);

        var activeBrokerOrders = brokerOpenOrders
            // Defensive: GetOpenOrdersAsync should already return only working orders, but a
            // terminal row slipping through would make the sweep report a failure over an order
            // that cannot fill.
            .Where(static order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
            .ToList();

        var residualOrders = new List<BrokerOrder>();
        var brokerCancellationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var brokerOrder in activeBrokerOrders)
        {
            // The OMS keys tracked state by the client id it submitted, while Alpaca's DELETE
            // endpoint accepts only the broker UUID. Keep the broker row out of the residual
            // sweep, but carry its broker-assigned id into the tracked cancellation path instead
            // of silently falling back to the client id that matched it.
            var trackedOrderId = brokerOrder.ClientOrderId is { Length: > 0 } clientOrderId
                && trackedIds.Contains(clientOrderId)
                    ? clientOrderId
                    : trackedByBrokerId.TryGetValue(brokerOrder.OrderId, out var brokerMappedOrderId)
                        ? brokerMappedOrderId
                        : null;

            if (trackedOrderId is null)
            {
                residualOrders.Add(brokerOrder);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(brokerOrder.OrderId))
            {
                brokerCancellationIds.TryAdd(trackedOrderId, brokerOrder.OrderId);
            }
        }

        return (residualOrders, brokerCancellationIds, null);
    }

    /// <summary>
    /// Cancels one broker-known order the in-memory book does not track, returning the failure to
    /// report when the cancellation did not take. Goes straight to the gateway because there is no
    /// tracked <see cref="OrderState"/> for the ordinary cancel path to update.
    /// </summary>
    internal async Task<KillSwitchSweepFailure?> CancelResidualOrderAsync(BrokerOrder order, CancellationToken ct)
    {
        KillSwitchSweepFailure? failure;
        ExecutionReport? report = null;
        try
        {
            report = await CancelAtGatewayAsync(
                new OrderCancellationIdentifier(
                    order.OrderId,
                    OrderCancellationIdentifierKind.BrokerOrderId),
                ct).ConfigureAwait(false);
            failure = report.OrderStatus is OrderStatus.Cancelled
                ? null
                : new KillSwitchSweepFailure(
                    order.OrderId,
                    order.Symbol,
                    report.RejectReason ?? "The gateway did not confirm the cancellation.");
        }
        catch (Exception exception)
        {
            failure = new KillSwitchSweepFailure(order.OrderId, order.Symbol, exception.Message);
        }

        // Evidence, not control flow: an audit write that throws must not convert a confirmed
        // broker cancellation into a reported failure, or abandon the rest of the sweep.
        try
        {
            await _executionAudit.RecordLifecycleAsync(
                action: failure is null ? "OrderCancelled" : "OrderCancelRejected",
                outcome: failure is null ? nameof(OrderStatus.Cancelled) : "Rejected",
                orderId: order.OrderId,
                state: null,
                report: report,
                message: failure is null
                    ? "Cancel-all cancelled a broker-side order the in-memory book did not track."
                    : failure.Value.Reason,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception auditException)
        {
            _logger.LogError(
                auditException,
                "Cancel-all could not audit the broker-side cancellation outcome for order {OrderId}",
                LogSanitizer.Sanitize(order.OrderId));
        }

        return failure;
    }

    internal async Task<(IReadOnlyList<BrokerOrder> WorkingOrders, string? EnumerationError)>
        SnapshotWorkingOrdersAsync(CancellationToken ct)
    {
        if (_gateway is not IBrokerageGateway brokerage)
        {
            return (Array.Empty<BrokerOrder>(), null);
        }

        try
        {
            var brokerOrders = await brokerage.GetOpenOrdersAsync(ct).ConfigureAwait(false)
                ?? Array.Empty<BrokerOrder>();
            return (
                brokerOrders
                    .Where(static order => order.Status is OrderStatus.PendingNew
                        or OrderStatus.Accepted
                        or OrderStatus.PartiallyFilled
                        or OrderStatus.PendingCancel)
                    .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
                    .ToList(),
                null);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cancel-all could not verify the broker's open-order book after cancellation");
            return (Array.Empty<BrokerOrder>(), exception.Message);
        }
    }

    private Task<ExecutionReport> CancelAtGatewayAsync(
        OrderCancellationIdentifier identifier,
        CancellationToken ct) =>
        _gateway is IExplicitOrderCancellationGateway explicitCancellationGateway
            ? explicitCancellationGateway.CancelOrderAsync(identifier, ct)
            : _gateway.CancelOrderAsync(identifier.Value, ct);
}
