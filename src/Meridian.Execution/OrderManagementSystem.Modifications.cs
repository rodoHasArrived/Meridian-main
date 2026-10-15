using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingOrderAmendment> _pendingModificationOrderIds = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
    {
        using var operation = EnterOperation();

        if (!_orders.TryGetValue(orderId, out var state))
        {
            await RecordOrderLifecycleAuditAsync(
                action: "OrderModifyRejected",
                outcome: "Rejected",
                orderId: orderId,
                state: null,
                report: null,
                message: "Order not found",
                ct: ct).ConfigureAwait(false);

            return new OrderResult { Success = false, OrderId = orderId, ErrorMessage = "Order not found" };
        }
        if (_pendingModificationOrderIds.ContainsKey(orderId) || IsTerminal(state.Status))
            return new OrderResult { Success = false, OrderId = orderId, OrderState = state,
                ErrorMessage = "The order is terminal or an earlier amendment requires broker reconciliation." };

        // A quantity increase or any supplied limit/stop price is a new risk decision. Notional
        // increases are not the only dangerous direction: lowering a sell limit or a buy stop can
        // move an accepted order straight through the market. Validation and the amended
        // reservation run under the same gate as placement so neither exposure nor price controls
        // can be bypassed through modification.
        OrderState? speculativeReservation = null;
        RiskValidationResult? amendmentDecision = null;
        var amendmentDispatchAttempted = false;
        IReadOnlyList<string>? amendmentWarnings = null;
        if (RequiresRiskRevalidation(state, modification))
        {
            var gate = await ReserveAmendedExposureAsync(orderId, state, modification, ct).ConfigureAwait(false);
            amendmentWarnings = gate.Warnings;
            if (gate.Refusal is not null)
            {
                await RecordOrderLifecycleAuditAsync(
                    action: "OrderModifyRejected",
                    outcome: "Rejected",
                    orderId: orderId,
                    state: state,
                    report: null,
                    message: gate.Refusal,
                    metadata: BuildOrderModificationAuditMetadata(modification, state, report: null, amendmentWarnings, gate.RiskDecision),
                    ct: ct).ConfigureAwait(false);

                return new OrderResult
                {
                    Success = false,
                    OrderId = orderId,
                    OrderState = state,
                    ErrorMessage = gate.Refusal,
                    RiskWarnings = amendmentWarnings
                };
            }

            speculativeReservation = gate.Reservation;
            amendmentDecision = gate.RiskDecision;
        }

        ExecutionReport report;
        PendingOrderAmendment? amendment = null;
        try
        {
            amendment = await BeginOrderAmendmentAsync(orderId, state, modification, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            amendmentDispatchAttempted = true;
            report = _gateway is IExplicitOrderModificationGateway explicitModification
                ? await explicitModification.ModifyOrderAsync(
                    _orderBrokerIds.TryGetValue(orderId, out var brokerOrderId)
                        ? new OrderCancellationIdentifier(brokerOrderId, OrderCancellationIdentifierKind.BrokerOrderId)
                        : new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.ClientOrderId),
                    modification, ct).ConfigureAwait(false)
                : await _gateway.ModifyOrderAsync(orderId, modification, ct).ConfigureAwait(false);
            // The operation itself proves which local order this replacement belongs to.
            report = report with { ClientOrderId = orderId };
        }
        catch
        {
            // The gateway never accepted the amendment: the speculative reservation must
            // not outlive the attempt, or the order table and every exposure snapshot
            // would keep reserving a size the broker does not hold.
            //
            // The rate slot follows the placement path's rule rather than the state
            // reservation's: a modify that threw after dispatch may still have reached the venue,
            // so it is committed. Over-counting a rate window is the safe direction.
            // Cancellation after the gateway call began is ambiguous, not pre-dispatch. Gateways
            // that send and then await acknowledgement on the same token — the brokerage adapter
            // does — cancel while the venue already holds the amended exposure. The only proof of
            // pre-dispatch is the check before the call, which throws before the flag is set.
            SettleRiskReservations(amendmentDecision, commit: amendmentDispatchAttempted, orderId);
            if (!amendmentDispatchAttempted)
            {
                if (amendment is not null)
                    await AbandonOrderAmendmentAsync(orderId, amendment).ConfigureAwait(false);
                RollBackSpeculativeReservation(orderId, speculativeReservation, state);
            }
            else
                MarkBrokerageRecoveryRequired(orderId);
            throw;
        }

        if (report.ReportType is ExecutionReportType.ModificationPending)
        {
            SettleRiskReservations(amendmentDecision, commit: true, orderId);
            if (_pendingModificationOrderIds.ContainsKey(orderId))
            {
                await ProcessGatewayReportAsync(report, CancellationToken.None).ConfigureAwait(false);
                MarkBrokerageRecoveryRequired(orderId);
            }
            var pending = _orders.TryGetValue(orderId, out var retained) ? retained : state;
            await RecordOrderLifecycleAuditAsync("OrderModifyPending", "RecoveryRequired", orderId,
                pending, report, report.RejectReason ?? "Amendment outcome requires broker reconciliation.",
                ct: CancellationToken.None).ConfigureAwait(false);
            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = pending,
                ErrorMessage = report.RejectReason ?? "Amendment outcome requires broker reconciliation.",
                RiskWarnings = amendmentWarnings
            };
        }

        if (report.OrderStatus is OrderStatus.Rejected || report.ReportType is ExecutionReportType.ModificationRejected)
        {
            // The broker refused the amendment outright, so nothing routed.
            SettleRiskReservations(amendmentDecision, commit: false, orderId);
            if (report.ReportType is not ExecutionReportType.ModificationRejected)
                report = report with { ReportType = ExecutionReportType.ModificationRejected, OrderStatus = state.Status,
                    Symbol = state.Symbol, Side = state.Side };
            await ProcessGatewayReportAsync(report, CancellationToken.None, amendment.Original.Quantity).ConfigureAwait(false);
            var observedState = _orders.TryGetValue(orderId, out var current) ? current : state;
            // Do not apply a rejected modify to order state: ApplyReport would let the terminal
            // Rejected overwrite a completed Filled/Cancelled order, and returning Success would
            // misreport that overwrite as a successful modify. Mirror the cancel path and fail.
            await RecordOrderLifecycleAuditAsync(
                action: "OrderModifyRejected",
                outcome: report.OrderStatus.ToString(),
                orderId: orderId,
                state: observedState,
                report: report,
                message: report.RejectReason ?? "Modify request rejected",
                metadata: BuildOrderModificationAuditMetadata(modification, state, report, amendmentWarnings),
                ct: ct).ConfigureAwait(false);

            return new OrderResult
            {
                Success = false,
                OrderId = orderId,
                OrderState = observedState,
                ErrorMessage = report.RejectReason ?? "Modify request rejected"
            };
        }

        // The amendment reached the venue and was accepted, so its capacity is spent.
        SettleRiskReservations(amendmentDecision, commit: true, orderId);

        // The gateway report stream is not an authorization channel. Only this locally
        // initiated modification may change the quantity cap, and it may do so only to
        // the quantity the caller requested; never to a gateway-supplied quantity.
        await ProcessGatewayReportAsync(report, CancellationToken.None, amendment.Proposed.Quantity).ConfigureAwait(false);
        var updated = _orders.TryGetValue(orderId, out var amendedState) ? amendedState : state;
        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), updated, ct).ConfigureAwait(false);
        await RecordOrderLifecycleAuditAsync(
            action: "OrderModified",
            outcome: updated.Status.ToString(),
            orderId: orderId,
            state: updated,
            report: report,
            message: report.RejectReason,
            metadata: BuildOrderModificationAuditMetadata(modification, updated, report, amendmentWarnings),
            ct: ct).ConfigureAwait(false);

        // Warnings raised while approving the amendment describe the exposure the caller
        // now holds, so they travel with the accepted result — not only with a refusal.
        return new OrderResult
        {
            Success = !_pendingModificationOrderIds.ContainsKey(orderId),
            OrderId = orderId,
            OrderState = updated,
            RiskWarnings = amendmentWarnings
        };
    }


    private ExecutionReport NormalizeBrokerOrderIdentity(ExecutionReport report)
    {
        var brokerId = report.GatewayOrderId ?? report.OrderId;
        string? localId = null;
        if (_brokerOrderAliases.TryGetValue(brokerId, out var known))
            localId = known;
        else if (report.ReplacesOrderId is { Length: > 0 } replaced
            && _brokerOrderAliases.TryGetValue(replaced, out var original))
            localId = original;
        if (localId is null)
            return report;
        var normalized = report with { ClientOrderId = localId };
        if (report.ReportType is ExecutionReportType.Rejected && report.OrderStatus is OrderStatus.Rejected
            && report.ReplacesOrderId is { Length: > 0 } rejectedPredecessor
            && _orderBrokerIds.TryGetValue(localId, out var workingBrokerId)
            && string.Equals(rejectedPredecessor, workingBrokerId, StringComparison.Ordinal)
            && !string.Equals(brokerId, workingBrokerId, StringComparison.Ordinal)
            && _orders.TryGetValue(localId, out var working) && !IsTerminal(working.Status))
        {
            // Rejecting a proposed replacement leaves its predecessor working. Keep the
            // rejected identity as an alias so a replay cannot adopt it as another order.
            normalized = normalized with
            {
                ReportType = ExecutionReportType.ModificationRejected,
                OrderStatus = working.Status,
                OrderQuantity = working.Quantity
            };
        }
        if (_brokerOrderAliases.ContainsKey(brokerId)
            && _orderBrokerIds.TryGetValue(localId, out var activeBrokerId)
            && !string.Equals(brokerId, activeBrokerId, StringComparison.Ordinal)
            && _orders.TryGetValue(localId, out var current))
        {
            // A superseded chain member can supply a late execution, but its cancellation
            // or expiry cannot terminate the replacement that is still working.
            var status = report.FilledQuantity >= current.Quantity ? OrderStatus.Filled
                : report.FilledQuantity > current.FilledQuantity
                    && current.Status is OrderStatus.Accepted or OrderStatus.PendingNew
                        ? OrderStatus.PartiallyFilled
                        : current.Status;
            normalized = normalized with { OrderStatus = status, OrderQuantity = current.Quantity };
        }
        return normalized;
    }

    private string[] GetBrokerOrderAliases(string localId) => _brokerOrderAliases
        .Where(pair => string.Equals(pair.Value, localId, StringComparison.Ordinal))
        .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
}
