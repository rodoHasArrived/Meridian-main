using System.Collections.Concurrent;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns permanent dispatch identities, recovery uncertainty and durable processed-order
/// checkpoints. It neither owns the live order book nor processes execution reports.
/// </summary>
internal sealed class BrokerageOrderRecoveryJournal(FileBrokerageOrderRecoveryStore? store)
{
    private readonly ConcurrentDictionary<string, byte> _dispatchedIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _recoveryIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BrokerageOrderTombstone> _tombstones = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _versions = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    internal bool HasStore => store is not null;
    internal bool IsDispatched(string orderId) => _dispatchedIds.ContainsKey(orderId);
    internal bool RequiresRecovery(string orderId) => _recoveryIds.ContainsKey(orderId);

    internal BrokerageOrderRecoveryRestore Restore(string gatewayId)
    {
        if (store is null)
            return new([], []);
        if (!string.Equals(store.GatewayId, gatewayId, StringComparison.Ordinal))
            throw new InvalidOperationException("Brokerage recovery store does not match the active gateway.");

        var tombstones = store.LoadTombstones();
        var orders = store.Load();
        foreach (var tombstone in tombstones)
        {
            _tombstones[tombstone.OrderId] = tombstone;
            _dispatchedIds[tombstone.OrderId] = 0;
        }
        foreach (var retained in orders)
        {
            var state = retained.State;
            _dispatchedIds[state.OrderId] = 0;
            if (retained.RequiresRecovery || state.Status is not (OrderStatus.Filled or OrderStatus.Rejected))
                _recoveryIds[state.OrderId] = 0;
        }
        return new(orders, tombstones);
    }

    internal void BindScope(string scopeIdentity) => store?.BindScope(scopeIdentity);

    internal void RetainDispatch(OrderState state, string? brokerOrderId, string[] aliases)
    {
        store?.Save(new RetainedBrokerageOrder(state, brokerOrderId, true) { BrokerOrderAliases = aliases });
        _dispatchedIds[state.OrderId] = 0;
        _recoveryIds[state.OrderId] = 0;
    }

    internal void RequireRecovery(string orderId)
    {
        if (!IsDispatched(orderId))
            return;
        lock (_sync)
        {
            // Failure to persist uncertainty must not reopen admission in this process.
            // RequireRecovery retains the last fully processed cumulative fill watermark.
            _versions[orderId] = _versions.GetValueOrDefault(orderId) + 1;
            _recoveryIds[orderId] = 0;
            store?.RequireRecovery(orderId);
        }
    }

    internal long CaptureVersion(string orderId)
    {
        lock (_sync)
            return _versions.GetValueOrDefault(orderId);
    }

    /// <summary>
    /// Reads current broker/amendment evidence only after acquiring the recovery-state lock.
    /// A submit acknowledgment can wait here while another operation persists newer evidence.
    /// The reader must synchronously inspect those owners only; it must not mutate, await, or
    /// enter another journal operation. It is invoked during this call and never retained.
    /// </summary>
    internal void CheckpointProcessed(
        OrderState state,
        ExecutionReport report,
        long observedRecoveryVersion,
        Func<BrokerageOrderCheckpointEvidence> readEvidence)
    {
        if (!IsDispatched(state.OrderId)
            || report.FilledQuantity < state.FilledQuantity
                && report.ReportType is not ExecutionReportType.ModificationPending)
            return;
        lock (_sync)
        {
            var evidence = readEvidence();
            var requiresAmendmentRecovery = evidence.HasPendingAmendment
                || report.ReportType is ExecutionReportType.ModificationPending;
            var requiresRecovery = requiresAmendmentRecovery
                || observedRecoveryVersion != _versions.GetValueOrDefault(state.OrderId);
            store?.Save(new RetainedBrokerageOrder(state, evidence.BrokerOrderId, requiresRecovery)
            {
                BrokerOrderAliases = evidence.BrokerOrderAliases,
                RequiresAmendmentRecovery = requiresAmendmentRecovery,
                PendingAmendment = evidence.PendingAmendment
            });
            SetRecoveryRequired(state.OrderId, requiresRecovery);
        }
    }

    internal void SavePendingAmendment(
        OrderState state, string? brokerOrderId, string[] aliases, PendingOrderAmendment? pendingAmendment)
    {
        if (!IsDispatched(state.OrderId))
            return;
        store?.Save(new RetainedBrokerageOrder(state, brokerOrderId, pendingAmendment is not null)
        {
            BrokerOrderAliases = aliases,
            RequiresAmendmentRecovery = pendingAmendment is not null,
            PendingAmendment = pendingAmendment
        });
        SetRecoveryRequired(state.OrderId, pendingAmendment is not null);
    }

    private void SetRecoveryRequired(string orderId, bool required)
    {
        if (required)
            _recoveryIds[orderId] = 0;
        else
            _recoveryIds.TryRemove(orderId, out _);
    }

    /// <summary>
    /// Persists safe compaction and attempts expected-state removal under the same recovery lock.
    /// The sole mutation callback must synchronously compare/remove the selected order, never
    /// await, re-enter recovery, or perform sidecar cleanup. The caller still holds the report
    /// gate and cleans sidecars only when this returns true. A lost removal race does not undo
    /// a durable tombstone or make its dispatched client identity reusable.
    /// </summary>
    internal bool TryCompactAndRemove(OrderState candidate, Func<bool> tryRemoveExpectedOrder)
    {
        lock (_sync)
        {
            if (RequiresRecovery(candidate.OrderId))
                return false;
            if (IsDispatched(candidate.OrderId))
            {
                if (store?.TryCompact(candidate.OrderId) != true)
                    return false;
                _tombstones[candidate.OrderId] = new BrokerageOrderTombstone(
                    candidate.OrderId, candidate.Status, candidate.Quantity, candidate.FilledQuantity);
                _versions.Remove(candidate.OrderId);
            }
            return tryRemoveExpectedOrder();
        }
    }

    /// <summary>
    /// Returns true for a completed identity's duplicate callback. Contradictory execution is
    /// refused; the lifecycle owner decides how to close admission after that fatal evidence.
    /// </summary>
    internal bool IsCompactedReport(ExecutionReport report)
    {
        if (!_tombstones.TryGetValue(report.ClientOrderId ?? report.OrderId, out var tombstone))
            return false;
        if (report.FilledQuantity > tombstone.FilledQuantity)
            throw new InvalidDataException("Broker execution contradicts a completed order recovery watermark.");
        return true;
    }
}

internal sealed record BrokerageOrderRecoveryRestore(
    IReadOnlyList<RetainedBrokerageOrder> Orders,
    IReadOnlyList<BrokerageOrderTombstone> Tombstones);
