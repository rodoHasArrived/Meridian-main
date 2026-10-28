using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using static Meridian.Execution.OrderManagement.OrderAmendmentBook;

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
        GetRetainedBrokerageOrders(fundAccountId).Where(order => _recoveryJournal.RequiresRecovery(order.OrderId)).ToArray();

    /// <summary>Working and completed broker-dispatched identities retained for recovery.</summary>
    public IReadOnlyList<OrderState> GetRetainedBrokerageOrders(Guid? fundAccountId = null) =>
        _orders.Values.Where(order => _recoveryJournal.IsDispatched(order.OrderId)
            && (fundAccountId is null || order.FundAccountId == fundAccountId)).ToArray();

    /// <summary>Uncapped state snapshot, including registered orders awaiting dispatch and adopted fills.</summary>
    public IReadOnlyList<OrderState> GetAllRetainedOrderStates() => _orders.Values.ToArray();

    private void RestoreBrokerageOrders()
    {
        if (!_recoveryJournal.HasStore)
            return;
        var restored = _recoveryJournal.Restore(_gateway.GatewayId);
        foreach (var tombstone in restored.Tombstones)
            _brokerIdentities.Restore(tombstone);
        foreach (var retained in restored.Orders)
        {
            var state = retained.State;
            _orders[state.OrderId] = state;
            _amendmentBook.Restore(retained);
            _brokerIdentities.Restore(retained);
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
        _brokerIdentities.TryGetActiveId(state.OrderId, out var brokerOrderId);
        _recoveryJournal.RetainDispatch(state, brokerOrderId, GetBrokerOrderAliases(state.OrderId));
    }

    /// <summary>Refuses recovery/routing across a broker account or paper/live environment change.</summary>
    public void EnsureBrokerageRecoveryScope(IBrokerageGateway? gateway = null)
    {
        if (gateway is not null && !ReferenceEquals(_gateway, gateway))
            throw new InvalidOperationException("Order recovery must use the OMS active brokerage gateway.");
        if (_gateway is IBrokerageConnectionState connection && _recoveryJournal.HasStore)
        {
            if (string.IsNullOrWhiteSpace(connection.ScopeIdentity))
                throw new InvalidOperationException("The brokerage account and trading environment have not been verified for order recovery.");
            _recoveryJournal.BindScope(connection.ScopeIdentity);
        }
    }

    private void MarkBrokerageRecoveryRequired(string orderId) => _recoveryJournal.RequireRecovery(orderId);

    private long GetBrokerageRecoveryVersion(string orderId) => _recoveryJournal.CaptureVersion(orderId);

    private void RetainProcessedBrokerageOrder(string orderId, ExecutionReport report, long observedRecoveryVersion)
    {
        if (!_recoveryJournal.IsDispatched(orderId) || !_orders.TryGetValue(orderId, out var state))
            return;
        // Submit acknowledgments do not hold the shared report gate. Sample these owners at
        // the journal's lock boundary so a wait cannot overwrite a newer amendment or alias.
        var brokerIdentities = _brokerIdentities;
        var pendingAmendments = _amendmentBook;
        _recoveryJournal.CheckpointProcessed(state, report, observedRecoveryVersion, () =>
        {
            var hasPendingAmendment = pendingAmendments.Contains(orderId);
            brokerIdentities.TryGetActiveId(orderId, out var brokerOrderId);
            return new BrokerageOrderCheckpointEvidence(brokerOrderId, brokerIdentities.GetAliases(orderId),
                hasPendingAmendment, pendingAmendments.GetPending(orderId));
        });
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
            if (_dispatchRegistry.Contains(orderId))
                throw new InvalidOperationException("Order submission is still in progress; broker recovery must wait for its outcome.");
            if (report.ReportType is ExecutionReportType.ModificationPending)
            {
                // A pending outcome can still book fills, so it must prove the same identity and
                // fill economics as a resolved one; only its order quantity stays undetermined.
                var pendingTerms = GetPendingAmendmentForRecovery(orderId);
                // The live stream can resolve the amendment while its lookup is in flight. Checked
                // under the report gate, a pending answer for a resolved amendment is stale: it books
                // nothing and must not re-block an order no amendment recovery will revisit.
                if (pendingTerms is null)
                    return;
                var maximumQuantity = Math.Max(pendingTerms.Original.Quantity, pendingTerms.Proposed.Quantity);
                if (!_orders.TryGetValue(orderId, out var pendingState)
                    || !_recoveryJournal.IsDispatched(orderId)
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
            // A rejected successor echoes the refused proposal's quantity, not the order's: the
            // original terms stand, so only its fills are checked against them.
            var rejectedSuccessor = pending is not null && report.ReportType is ExecutionReportType.ModificationRejected
                && !string.Equals(report.GatewayOrderId ?? report.OrderId, pending.OriginalBrokerOrderId, StringComparison.Ordinal);
            if (!_orders.TryGetValue(orderId, out var state)
                || !_recoveryJournal.IsDispatched(orderId)
                || !string.Equals(state.Symbol, report.Symbol, StringComparison.OrdinalIgnoreCase)
                || state.Side != report.Side
                || report.FilledQuantity < state.FilledQuantity
                || report.FilledQuantity > confirmedQuantity
                || (report.FilledQuantity > state.FilledQuantity && report.FillPrice is not > 0m)
                || (report.OrderQuantity > 0m && report.OrderQuantity != confirmedQuantity && !rejectedSuccessor)
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
                    if (pending is not null)
                        _amendmentBook.RecordOutcome(pending, ReferenceEquals(selectedTerms, pending.Proposed));
                }
                if (report.GatewayOrderId is { Length: > 0 } confirmedBrokerId)
                {
                    _brokerIdentities.Confirm(orderId, confirmedBrokerId);
                }
            }
            await ProcessGatewayReportCoreAsync(report, ct).ConfigureAwait(false);
            if (canResolveAmendment && _amendmentBook.Remove(orderId))
                RetainProcessedBrokerageOrder(orderId, report, recoveryVersion);
        }
        finally
        {
            _brokerageRecoveryReportGate.Release();
            TrimRetainedOrdersIfNeeded();
        }
    }
}
