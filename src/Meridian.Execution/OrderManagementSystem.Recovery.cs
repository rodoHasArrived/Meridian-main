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
        decimal? locallyAuthorizedModifiedQuantity = null,
        decimal? pendingExecutionLimit = null)
    {
        var authorizedQuantity = report.ReportType is ExecutionReportType.Modified
            && report.OrderStatus is not OrderStatus.Rejected
            && locallyAuthorizedModifiedQuantity is > 0m
            ? Math.Max(locallyAuthorizedModifiedQuantity.Value, current.FilledQuantity)
            : current.Quantity;

        // Cancel/expiry can arrive ahead of an already executed fill. Preserve terminal
        // state unless authoritative cumulative execution advances; never reopen or resize.
        if (IsTerminal(current.Status))
        {
            if (report.FilledQuantity <= current.FilledQuantity
                || report.FillPrice is not > 0m
                || !string.Equals(current.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || current.Side != report.Side)
                return current;
            var terminalQuantity = pendingExecutionLimit is > 0m
                ? Math.Max(current.Quantity, Math.Min(pendingExecutionLimit.Value, report.FilledQuantity)) : current.Quantity;
            var filled = Math.Min(terminalQuantity, report.FilledQuantity);
            return current with
            {
                Quantity = terminalQuantity,
                FilledQuantity = filled,
                AverageFillPrice = ComputeAverageFillPrice(current, filled, report.FillPrice),
                Status = filled >= terminalQuantity ? OrderStatus.Filled : current.Status,
                LastUpdatedAt = report.Timestamp
            };
        }

        // Gateway-streamed modifications are not trusted to authorize a quantity change.
        // A quantity may change only while applying the response to a locally initiated
        // modification, and is capped to the local request rather than report data.
        if (pendingExecutionLimit is > 0m && report.FillPrice is > 0m)
            authorizedQuantity = Math.Max(authorizedQuantity, Math.Min(pendingExecutionLimit.Value, report.FilledQuantity));
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
    private readonly ConcurrentDictionary<string, BrokerageOrderTombstone> _compactedBrokerageOrders = new(StringComparer.Ordinal);
    private readonly object _brokerageRecoveryStateSync = new();
    private readonly Dictionary<string, long> _brokerageRecoveryVersions = new(StringComparer.Ordinal);
    // Recovery lookups and the live report stream share the same cumulative-fill watermark.
    // Serial admission keeps both from booking the same missing increment concurrently.
    private readonly SemaphoreSlim _brokerageRecoveryReportGate = new(1, 1);

    private async Task ProcessGatewayReportAsync(ExecutionReport report, CancellationToken ct, decimal? authorizedQuantity = null)
    {
        await _brokerageRecoveryReportGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ProcessGatewayReportCoreAsync(report, ct, authorizedQuantity).ConfigureAwait(false);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
            TrimRetainedOrdersIfNeeded();
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
        foreach (var tombstone in _recoveryStore.LoadTombstones())
        {
            _compactedBrokerageOrders[tombstone.OrderId] = tombstone;
            _dispatchedOrderIds[tombstone.OrderId] = 0;
            foreach (var alias in tombstone.BrokerOrderAliases ?? [])
            {
                if (string.IsNullOrWhiteSpace(alias)
                    || !_brokerOrderAliases.TryAdd(alias, tombstone.OrderId))
                    throw new InvalidDataException("Retired broker aliases conflict with another order identity.");
            }
        }
        foreach (var retained in _recoveryStore.Load())
        {
            var state = retained.State;
            _orders[state.OrderId] = state;
            _dispatchedOrderIds[state.OrderId] = 0;
            if (retained.RequiresRecovery || state.Status is not (OrderStatus.Filled or OrderStatus.Rejected))
                _recoveryOrderIds[state.OrderId] = 0;
            if (retained.PendingAmendment is { } pending)
                _pendingModificationOrderIds[state.OrderId] = pending;
            else if (retained.RequiresAmendmentRecovery)
                _pendingModificationOrderIds[state.OrderId] = new PendingOrderAmendment(state, state, retained.BrokerOrderId);
            if (!string.IsNullOrWhiteSpace(retained.BrokerOrderId))
            {
                _orderBrokerIds[state.OrderId] = retained.BrokerOrderId;
                _brokerOrderAliases[retained.BrokerOrderId] = state.OrderId;
            }
            foreach (var alias in retained.BrokerOrderAliases ?? [])
            {
                if (string.IsNullOrWhiteSpace(alias)
                    || _brokerOrderAliases.TryGetValue(alias, out var owner) && owner != state.OrderId)
                    throw new InvalidDataException("Retained broker aliases conflict with another order identity.");
                _brokerOrderAliases[alias] = state.OrderId;
            }
            if (state.FundAccountId is { } accountId)
                _orderFinancialAccountIds[state.OrderId] = accountId.ToString("D");
            _orderContractMultipliers[state.OrderId] = state.ContractMultiplier;
            if (state.UsesFaceValuePercentageOfPar)
                _orderFaceValueSizing[state.OrderId] = true;
        }
        TrimRetainedOrdersIfNeeded();
    }

    private void RetainBrokerageDispatch(OrderState state)
    {
        EnsureBrokerageRecoveryScope();
        _orderBrokerIds.TryGetValue(state.OrderId, out var brokerOrderId);
        _recoveryStore?.Save(new RetainedBrokerageOrder(state, brokerOrderId, true)
        { BrokerOrderAliases = GetBrokerOrderAliases(state.OrderId) });
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
        lock (_brokerageRecoveryStateSync)
        {
            _brokerageRecoveryVersions[orderId] = _brokerageRecoveryVersions.GetValueOrDefault(orderId) + 1;
            _recoveryOrderIds[orderId] = 0;
            // Keep the last fully processed cumulative fill watermark; an in-flight report may
            // already have advanced the in-memory state without completing its durable handoff.
            _recoveryStore?.RequireRecovery(orderId);
        }
    }

    private long GetBrokerageRecoveryVersion(string orderId)
    {
        lock (_brokerageRecoveryStateSync)
            return _brokerageRecoveryVersions.GetValueOrDefault(orderId);
    }

    private void RetainProcessedBrokerageOrder(string orderId, ExecutionReport report, long observedRecoveryVersion)
    {
        if (!_dispatchedOrderIds.ContainsKey(orderId) || !_orders.TryGetValue(orderId, out var state))
            return;
        // A delayed acknowledgement must not checkpoint a newer fill whose own side effects
        // are still running, or invalidate the recovery evidence of an already processed fill.
        if (report.FilledQuantity < state.FilledQuantity && report.ReportType is not ExecutionReportType.ModificationPending)
            return;
        lock (_brokerageRecoveryStateSync)
        {
            // Completing a report that began before a later disconnect cannot reconcile that
            // newer uncertainty. Persist its completed fills while retaining the newer block.
            var requiresAmendmentRecovery = _pendingModificationOrderIds.ContainsKey(orderId)
                || report.ReportType is ExecutionReportType.ModificationPending;
            var requiresRecovery = requiresAmendmentRecovery
                || observedRecoveryVersion != _brokerageRecoveryVersions.GetValueOrDefault(orderId);
            _orderBrokerIds.TryGetValue(orderId, out var brokerOrderId);
            _recoveryStore?.Save(new RetainedBrokerageOrder(state, brokerOrderId, requiresRecovery)
            {
                BrokerOrderAliases = GetBrokerOrderAliases(orderId),
                RequiresAmendmentRecovery = requiresAmendmentRecovery,
                PendingAmendment = GetPendingAmendmentForRecovery(orderId)
            });
            if (requiresRecovery)
                _recoveryOrderIds[orderId] = 0;
            else
                _recoveryOrderIds.TryRemove(orderId, out _);
        }
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
            if (report.ReportType is ExecutionReportType.ModificationPending)
            {
                // A pending outcome can still book fills, so it must prove the same identity and
                // fill economics as a resolved one; only its order quantity stays undetermined.
                var pendingTerms = GetPendingAmendmentForRecovery(orderId);
                var maximumQuantity = pendingTerms is null
                    ? _orders.GetValueOrDefault(orderId)?.Quantity ?? 0m
                    : Math.Max(pendingTerms.Original.Quantity, pendingTerms.Proposed.Quantity);
                if (!_orders.TryGetValue(orderId, out var pendingState)
                    || !_dispatchedOrderIds.ContainsKey(orderId)
                    || !string.Equals(pendingState.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                    || pendingState.Side != report.Side
                    || report.FilledQuantity < pendingState.FilledQuantity
                    || report.FilledQuantity > maximumQuantity
                    || (report.FilledQuantity > pendingState.FilledQuantity && report.FillPrice is not > 0m))
                    throw new InvalidDataException("Broker recovery evidence does not match the retained order.");
                await ProcessGatewayReportCoreAsync(report, ct).ConfigureAwait(false);
                MarkBrokerageRecoveryRequired(orderId);
                return;
            }
            var pending = GetPendingAmendmentForRecovery(orderId);
            var selectedTerms = pending is null ? null : SelectAmendmentTerms(report, pending);
            var confirmedQuantity = selectedTerms?.Quantity ?? _orders.GetValueOrDefault(orderId)?.Quantity ?? 0m;
            if (!_orders.TryGetValue(orderId, out var state)
                || !_dispatchedOrderIds.ContainsKey(orderId)
                || !string.Equals(state.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || state.Side != report.Side
                || report.FilledQuantity < state.FilledQuantity
                || report.FilledQuantity > confirmedQuantity
                || (report.FilledQuantity > state.FilledQuantity && report.FillPrice is not > 0m)
                || (report.OrderQuantity > 0m && report.OrderQuantity != confirmedQuantity)
                || (IsTerminalStatus(state.Status) && report.OrderStatus != state.Status
                    && !(pending is not null && IsTerminalStatus(report.OrderStatus))
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
                report = report with { FillPrice = incrementPrice, FillPriceIsCumulativeAverage = false };
            }
            var recoveryVersion = GetBrokerageRecoveryVersion(orderId);
            var canResolveAmendment = !IsTerminal(state.Status) || IsTerminal(report.OrderStatus);
            if (selectedTerms is not null)
            {
                if (canResolveAmendment)
                {
                    var confirmed = ApplyAmendmentTerms(state, selectedTerms);
                    if (IsTerminal(state.Status) && IsTerminal(report.OrderStatus)
                        && selectedTerms == pending?.Proposed
                        && !(report.ReportType is ExecutionReportType.ModificationRejected
                            && report.GatewayOrderId == pending?.OriginalBrokerOrderId))
                        confirmed = confirmed with { Status = report.OrderStatus };
                    _orders[orderId] = confirmed;
                }
                if (report.GatewayOrderId is { Length: > 0 } confirmedBrokerId)
                {
                    _orderBrokerIds[orderId] = confirmedBrokerId;
                    _brokerOrderAliases.TryAdd(confirmedBrokerId, orderId);
                }
            }
            await ProcessGatewayReportCoreAsync(report, ct).ConfigureAwait(false);
            if (canResolveAmendment && _pendingModificationOrderIds.TryRemove(orderId, out _))
                RetainProcessedBrokerageOrder(orderId, report, recoveryVersion);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
            TrimRetainedOrdersIfNeeded();
        }
    }
}
