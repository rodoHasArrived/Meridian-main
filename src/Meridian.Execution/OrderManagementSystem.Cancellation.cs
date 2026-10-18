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

        gatewayOrderId ??= _gateway is IExplicitOrderCancellationGateway
            && _orderBrokerIds.TryGetValue(orderId, out var knownBrokerId) ? knownBrokerId : null;
        var cancellationIdentifier = string.IsNullOrWhiteSpace(gatewayOrderId)
            ? new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.ClientOrderId)
            : new OrderCancellationIdentifier(gatewayOrderId, OrderCancellationIdentifierKind.BrokerOrderId);
        // After a restart the gateway's in-process replacement bookkeeping is gone, so only the
        // OMS still knows a retained amendment may have a live successor. Cancel it explicitly.
        var unresolvedCandidate = await CancelRetainedReplacementCandidateAsync(orderId, gatewayOrderId, ct)
            .ConfigureAwait(false);
        var report = await CancelAtGatewayAsync(cancellationIdentifier, ct).ConfigureAwait(false);
        if (unresolvedCandidate is not null
            && report.OrderStatus is OrderStatus.Cancelled && report.ReportType is not ExecutionReportType.CancelRejected)
        {
            // The predecessor is gone but its possible successor is not proven inactive (or has
            // fills only amendment recovery may book), so the order stays in cancellation sweeps.
            report = report with
            {
                ReportType = ExecutionReportType.CancelRejected,
                OrderStatus = OrderStatus.PendingCancel,
                RejectReason = unresolvedCandidate
            };
        }
        if (_gateway is not IExplicitOrderCancellationGateway
            && report.ReportType is ExecutionReportType.Rejected && report.OrderStatus is OrderStatus.Rejected)
        {
            // Legacy gateways reuse Rejected for the command result. Only their ordinary
            // execution stream can establish a separate terminal order rejection.
            report = report with
            {
                ReportType = ExecutionReportType.CancelRejected,
                OrderStatus = state.Status,
                Symbol = state.Symbol,
                Side = state.Side,
                OrderQuantity = state.Quantity,
                FilledQuantity = state.FilledQuantity,
                FillPrice = state.AverageFillPrice
            };
        }
        RememberBrokerOrderId(orderId, report);
        report = report with { ClientOrderId = orderId };
        if (report.OrderStatus is not OrderStatus.Cancelled || report.ReportType is ExecutionReportType.CancelRejected)
        {
            // A cancellation can lose a race to execution. Alpaca confirms that race by re-reading
            // the broker order after its DELETE acknowledgement and returns the cumulative fill.
            // Apply it through the same idempotent funnel as the gateway report pump before
            // returning the failed cancellation, so a synchronous result cannot be lost if the
            // adapter's observer stream is absent, delayed, or racing this call.
            if (report.ReportType is ExecutionReportType.CancelRejected
                || IsTerminalStatus(report.OrderStatus)
                || HasCumulativeFillEvidence(report))
            {
                await ProcessGatewayReportAsync(report, CancellationToken.None).ConfigureAwait(false);
            }

            var observedState = _orders.TryGetValue(orderId, out var currentState)
                ? currentState
                : state;
            await RecordOrderLifecycleAuditAsync(
                action: "OrderCancelRejected",
                outcome: report.ReportType is ExecutionReportType.CancelRejected ? "Rejected" : report.OrderStatus.ToString(),
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

    /// <returns>null when there is no retained candidate or it is proven inactive without fills;
    /// otherwise why the cancellation cannot complete yet.</returns>
    private async Task<string?> CancelRetainedReplacementCandidateAsync(
        string orderId, string? predecessorBrokerId, CancellationToken ct)
    {
        if (_gateway is not IExplicitOrderCancellationGateway
            || GetPendingAmendmentForRecovery(orderId) is not { CandidateBrokerOrderId: { Length: > 0 } candidateId }
            || string.Equals(candidateId, predecessorBrokerId, StringComparison.Ordinal))
            return null;
        ExecutionReport candidate;
        try
        {
            candidate = await CancelAtGatewayAsync(
                new OrderCancellationIdentifier(candidateId, OrderCancellationIdentifierKind.BrokerOrderId), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Replacement candidate {candidateId} could not be cancelled: {ex.Message}";
        }
        if (candidate.FilledQuantity > 0m)
        {
            MarkBrokerageRecoveryRequired(orderId);
            return $"Replacement candidate {candidateId} has fills that amendment recovery must book.";
        }
        return candidate.OrderStatus is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired
            ? null
            : $"Replacement candidate {candidateId} is not proven inactive.";
    }

    private Task<ExecutionReport> CancelAtGatewayAsync(
        OrderCancellationIdentifier identifier,
        CancellationToken ct) =>
        _gateway is IExplicitOrderCancellationGateway explicitCancellationGateway
            ? explicitCancellationGateway.CancelOrderAsync(identifier, ct)
            : _gateway.CancelOrderAsync(identifier.Value, ct);

    private void RememberBrokerOrderId(string localOrderId, ExecutionReport report)
    {
        if (report.ReportType is ExecutionReportType.ModificationPending)
            return;
        var brokerOrderId = report.GatewayOrderId;
        if (string.IsNullOrWhiteSpace(brokerOrderId)
            && !string.IsNullOrWhiteSpace(report.ClientOrderId)
            && !string.Equals(report.OrderId, report.ClientOrderId, StringComparison.Ordinal))
        {
            brokerOrderId = report.OrderId;
        }

        if (!string.IsNullOrWhiteSpace(localOrderId) && !string.IsNullOrWhiteSpace(brokerOrderId))
        {
            // A late report from an earlier chain member must keep the current routing ID.
            var rejectedReplacement = report.ReportType is ExecutionReportType.ModificationRejected or ExecutionReportType.Rejected
                && report.ReplacesOrderId is { Length: > 0 };
            if (!rejectedReplacement && (!_orderBrokerIds.TryGetValue(localOrderId, out var currentBrokerId)
                || !_brokerOrderAliases.ContainsKey(brokerOrderId)
                    && (string.IsNullOrWhiteSpace(report.ReplacesOrderId)
                        || string.Equals(report.ReplacesOrderId, currentBrokerId, StringComparison.Ordinal))))
                _orderBrokerIds[localOrderId] = brokerOrderId;
            _brokerOrderAliases.TryAdd(brokerOrderId, localOrderId);
            if (report.ReplacesOrderId is { Length: > 0 } replaced)
                _brokerOrderAliases.TryAdd(replaced, localOrderId);
        }
    }

    private static bool HasCumulativeFillEvidence(ExecutionReport report)
        => report.FilledQuantity > 0m && report.FillPrice is not null;
}
