using System.Collections.Concurrent;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    private string GenerateOrderId()
    {
        return $"MDN-{Guid.NewGuid():N}";
    }

    private static OrderState ApplyReport(
        OrderState current,
        ExecutionReport report,
        decimal? locallyAuthorizedModifiedQuantity = null)
    {
        // Cancel/expiry can arrive ahead of an already executed fill. Preserve terminal
        // state unless authoritative cumulative execution advances; never reopen or resize.
        if (IsTerminal(current.Status))
        {
            if (current.Status == OrderStatus.Filled
                || report.FilledQuantity <= current.FilledQuantity
                || report.FillPrice is not > 0m
                || !string.Equals(current.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || current.Side != report.Side)
                return current;
            var filled = Math.Min(current.Quantity, report.FilledQuantity);
            return current with
            {
                FilledQuantity = filled,
                AverageFillPrice = ComputeAverageFillPrice(current, filled, report.FillPrice),
                Status = filled >= current.Quantity ? OrderStatus.Filled : current.Status,
                LastUpdatedAt = report.Timestamp
            };
        }

        // Gateway-streamed modifications are not trusted to authorize a quantity change.
        // A quantity may change only while applying the response to a locally initiated
        // modification, and is capped to the local request rather than report data.
        var authorizedQuantity = report.ReportType is ExecutionReportType.Modified
            && report.OrderStatus is OrderStatus.Accepted
            && locallyAuthorizedModifiedQuantity is > 0m
            ? Math.Max(locallyAuthorizedModifiedQuantity.Value, current.FilledQuantity)
            : current.Quantity;

        return current with
        {
            Status = report.OrderStatus,
            Quantity = authorizedQuantity,
            FilledQuantity = Math.Min(authorizedQuantity, Math.Max(report.FilledQuantity, current.FilledQuantity)),
            AverageFillPrice = ComputeAverageFillPrice(current,
                Math.Min(authorizedQuantity, Math.Max(report.FilledQuantity, current.FilledQuantity)), report.FillPrice),
            LastUpdatedAt = report.Timestamp
        };
    }

    private static decimal? ComputeAverageFillPrice(OrderState current, decimal filledQuantity, decimal? incrementPrice)
    {
        var increment = filledQuantity - current.FilledQuantity;
        if (increment <= 0m || incrementPrice is null)
            return current.AverageFillPrice;
        if (current.FilledQuantity == 0m || current.AverageFillPrice is null)
            return incrementPrice;
        return (current.FilledQuantity * current.AverageFillPrice.Value + increment * incrementPrice.Value) / filledQuantity;
    }

    private readonly FileBrokerageOrderRecoveryStore? _recoveryStore;
    private readonly ConcurrentDictionary<string, byte> _dispatchedOrderIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _recoveryOrderIds = new(StringComparer.Ordinal);
    // Recovery lookups and the live report stream share the same cumulative-fill watermark.
    // Serial admission keeps both from booking the same missing increment concurrently.
    private readonly SemaphoreSlim _brokerageRecoveryReportGate = new(1, 1);

    private async Task ProcessGatewayReportAsync(ExecutionReport report, CancellationToken ct)
    {
        await _brokerageRecoveryReportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ProcessGatewayReportCoreAsync(report, ct).ConfigureAwait(false);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }

    /// <summary>Retained dispatches requiring authoritative broker evidence before resuming.</summary>
    public IReadOnlyList<OrderState> GetRecoveryOrders(Guid? fundAccountId = null) =>
        GetRetainedBrokerageOrders(fundAccountId).Where(order => _recoveryOrderIds.ContainsKey(order.OrderId)).ToArray();

    /// <summary>Working and completed broker-dispatched identities retained for recovery.</summary>
    public IReadOnlyList<OrderState> GetRetainedBrokerageOrders(Guid? fundAccountId = null) =>
        _orders.Values.Where(order => _dispatchedOrderIds.ContainsKey(order.OrderId)
            && (fundAccountId is null || order.FundAccountId == fundAccountId)).ToArray();

    /// <summary>Uncapped state snapshot, including registered orders awaiting dispatch and adopted fills.</summary>
    public IReadOnlyList<OrderState> GetAllRetainedOrderStates() => _orders.Values.ToArray();

    private void RestoreBrokerageOrders()
    {
        if (_recoveryStore is null)
            return;
        if (!string.Equals(_recoveryStore.GatewayId, _gateway.GatewayId, StringComparison.Ordinal))
            throw new InvalidOperationException("Brokerage recovery store does not match the active gateway.");
        foreach (var retained in _recoveryStore.Load())
        {
            var state = retained.State;
            _orders[state.OrderId] = state;
            _dispatchedOrderIds[state.OrderId] = 0;
            if (retained.RequiresRecovery || state.Status is not (OrderStatus.Filled or OrderStatus.Rejected))
                _recoveryOrderIds[state.OrderId] = 0;
            if (!string.IsNullOrWhiteSpace(retained.BrokerOrderId))
                _orderBrokerIds[state.OrderId] = retained.BrokerOrderId;
            if (state.FundAccountId is { } accountId)
                _orderFinancialAccountIds[state.OrderId] = accountId.ToString("D");
            _orderContractMultipliers[state.OrderId] = state.ContractMultiplier;
            if (state.UsesFaceValuePercentageOfPar)
                _orderFaceValueSizing[state.OrderId] = true;
        }
    }

    private void RetainBrokerageDispatch(OrderState state)
    {
        EnsureBrokerageRecoveryScope();
        _orderBrokerIds.TryGetValue(state.OrderId, out var brokerOrderId);
        _recoveryStore?.Save(new RetainedBrokerageOrder(state, brokerOrderId, true));
        _dispatchedOrderIds[state.OrderId] = 0;
        _recoveryOrderIds[state.OrderId] = 0;
    }

    /// <summary>Refuses recovery/routing across a broker account or paper/live environment change.</summary>
    public void EnsureBrokerageRecoveryScope(IBrokerageGateway? gateway = null)
    {
        if (gateway is not null && !ReferenceEquals(_gateway, gateway))
            throw new InvalidOperationException("Order recovery must use the OMS active brokerage gateway.");
        if (_gateway is IBrokerageConnectionState connection && _recoveryStore is not null)
        {
            if (string.IsNullOrWhiteSpace(connection.ScopeIdentity))
                throw new InvalidOperationException("The brokerage account and trading environment have not been verified for order recovery.");
            _recoveryStore.BindScope(connection.ScopeIdentity);
        }
    }

    private void MarkBrokerageRecoveryRequired(string orderId)
    {
        if (!_dispatchedOrderIds.ContainsKey(orderId))
            return;
        _recoveryOrderIds[orderId] = 0;
        // Keep the last fully processed cumulative fill watermark; an in-flight report may
        // already have advanced the in-memory state without completing its durable handoff.
        _recoveryStore?.RequireRecovery(orderId);
    }

    private void RetainProcessedBrokerageOrder(string orderId, ExecutionReport report)
    {
        if (!_dispatchedOrderIds.ContainsKey(orderId) || !_orders.TryGetValue(orderId, out var state))
            return;
        // A delayed acknowledgement must not checkpoint a newer fill whose own side effects
        // are still running, or invalidate the recovery evidence of an already processed fill.
        if (report.FilledQuantity < state.FilledQuantity)
            return;
        _orderBrokerIds.TryGetValue(orderId, out var brokerOrderId);
        _recoveryStore?.Save(new RetainedBrokerageOrder(state, brokerOrderId, false));
        _recoveryOrderIds.TryRemove(orderId, out _);
    }

    /// <summary>Applies broker-proven recovery evidence through the ordinary idempotent fill path.</summary>
    public async Task ReconcileRecoveryOrderAsync(ExecutionReport report, CancellationToken ct = default)
    {
        using var operation = EnterOperation();
        EnsureBrokerageRecoveryScope();
        await _brokerageRecoveryReportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var orderId = report.ClientOrderId ?? report.OrderId;
            if (_inFlightDispatches.ContainsKey(orderId))
                throw new InvalidOperationException("Order submission is still in progress; broker recovery must wait for its outcome.");
            if (!_orders.TryGetValue(orderId, out var state)
                || !_dispatchedOrderIds.ContainsKey(orderId)
                || !string.Equals(state.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || state.Side != report.Side
                || report.FilledQuantity < state.FilledQuantity
                || report.FilledQuantity > state.Quantity
                || (report.FilledQuantity > state.FilledQuantity && report.FillPrice is not > 0m)
                || (report.OrderQuantity > 0m && report.OrderQuantity != state.Quantity)
                || (IsTerminalStatus(state.Status) && report.OrderStatus != state.Status
                    && !(report.FilledQuantity > state.FilledQuantity
                        && report.OrderStatus is OrderStatus.Filled or OrderStatus.PartiallyFilled)))
                throw new InvalidDataException("Broker recovery evidence does not match the retained order.");
            if (report.FilledQuantity > state.FilledQuantity && state.FilledQuantity > 0m)
            {
                if (state.AverageFillPrice is not > 0m)
                    throw new InvalidDataException("Retained fills lack cumulative economics required for broker recovery.");
                // Lookup snapshots quote the average across every fill, while the ordinary fill
                // path consumes only the missing increment. Recover its actual notional first.
                var increment = report.FilledQuantity - state.FilledQuantity;
                var incrementPrice = (report.FilledQuantity * report.FillPrice!.Value
                    - state.FilledQuantity * state.AverageFillPrice.Value) / increment;
                if (incrementPrice <= 0m)
                    throw new InvalidDataException("Broker recovery fill economics contradict retained executions.");
                report = report with { FillPrice = incrementPrice };
            }
            await ProcessGatewayReportCoreAsync(report, ct).ConfigureAwait(false);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
        }
    }
}
