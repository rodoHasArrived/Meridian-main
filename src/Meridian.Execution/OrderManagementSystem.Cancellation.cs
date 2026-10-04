using Meridian.Execution.Sdk;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    /// <inheritdoc />
    public async Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        return await CancelOrderCoreAsync(orderId, ct).ConfigureAwait(false);
    }

    private async Task<OrderResult> CancelOrderCoreAsync(
        string orderId,
        CancellationToken ct,
        string? gatewayOrderId = null)
    {
        if (!_orders.TryGetValue(orderId, out var state))
        {
            await RecordOrderLifecycleAuditAsync(
                action: "OrderCancelRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: null,
                report: null,
                message: "Order not found",
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }

        // A parked order has no broker order to cancel: withdraw the escalation and
        // complete the cancellation locally instead of failing at the gateway.
        if (await TryCancelParkedOrderAsync(orderId, ct).ConfigureAwait(false) is { } parkedCancellation)
        {
            return parkedCancellation;
        }

        var cancellationIdentifier = string.IsNullOrWhiteSpace(gatewayOrderId)
            ? new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.ClientOrderId)
            : new OrderCancellationIdentifier(gatewayOrderId, OrderCancellationIdentifierKind.BrokerOrderId);
        var report = await CancelAtGatewayAsync(cancellationIdentifier, ct).ConfigureAwait(false);
        RememberBrokerOrderId(orderId, report);
        if (report.OrderStatus is not OrderStatus.Cancelled)
        {
            // A cancellation can lose a race to execution. Alpaca confirms that race by re-reading
            // the broker order after its DELETE acknowledgement and returns the cumulative fill.
            // Apply it through the same idempotent funnel as the gateway report pump before
            // returning the failed cancellation, so a synchronous result cannot be lost if the
            // adapter's observer stream is absent, delayed, or racing this call.
            if (IsTerminalStatus(report.OrderStatus)
                || HasCumulativeFillEvidence(report))
            {
                await ProcessGatewayReportAsync(report, CancellationToken.None).ConfigureAwait(false);
            }

            var observedState = _orders.TryGetValue(orderId, out var currentState)
                ? currentState
                : state;
            await RecordOrderLifecycleAuditAsync(
                action: "OrderCancelRejected",
                outcome: report.OrderStatus.ToString(),
                orderId: orderId,
                state: observedState,
                report: report,
                message: report.RejectReason ?? "Cancel request failed",
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = observedState,
                ErrorMessage = report.RejectReason ?? "Cancel request failed"
            };
        }

        // A broker can confirm cancellation with a non-zero cumulative fill (the last partial
        // execution raced the cancel). Route the same authoritative report through the normal
        // fill funnel so portfolio, accounting, session, and report subscribers all receive the
        // execution before the locally tracked order becomes a completed cancellation result.
        await ProcessGatewayReportAsync(report, CancellationToken.None).ConfigureAwait(false);
        var updated = _orders.TryGetValue(orderId, out var current) ? current : state;
        await RecordOrderLifecycleAuditAsync(
            action: "OrderCancelled",
            outcome: updated.Status.ToString(),
            orderId: orderId,
            state: updated,
            report: report,
            message: report.RejectReason,
            ct: ct).ConfigureAwait(false);

        return new OrderResult
        {
            Success = true,
            OrderId = orderId,
            OrderState = updated
        };
    }

    private Task<ExecutionReport> CancelAtGatewayAsync(
        OrderCancellationIdentifier identifier,
        CancellationToken ct) =>
        _gateway is IExplicitOrderCancellationGateway explicitCancellationGateway
            ? explicitCancellationGateway.CancelOrderAsync(identifier, ct)
            : _gateway.CancelOrderAsync(identifier.Value, ct);

    private void RememberBrokerOrderId(string localOrderId, ExecutionReport report)
    {
        var brokerOrderId = report.GatewayOrderId;
        if (string.IsNullOrWhiteSpace(brokerOrderId)
            && !string.IsNullOrWhiteSpace(report.ClientOrderId)
            && !string.Equals(report.OrderId, report.ClientOrderId, StringComparison.Ordinal))
        {
            brokerOrderId = report.OrderId;
        }

        if (!string.IsNullOrWhiteSpace(localOrderId) && !string.IsNullOrWhiteSpace(brokerOrderId))
        {
            _orderBrokerIds[localOrderId] = brokerOrderId;
        }
    }

    private static bool HasCumulativeFillEvidence(ExecutionReport report)
        => report.FilledQuantity > 0m && report.FillPrice is not null;
}
