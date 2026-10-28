using System.Collections.Concurrent;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns the case-insensitive client identities held by governed approvals. Queue transitions
/// must succeed before a reservation is released; request tokens alone never authorize reuse.
/// Live order state and its session/audit effects remain with the command coordinator.
/// </summary>
internal sealed class OrderApprovalReservations(RiskEscalationQueueService? queue, ILogger logger)
{
    private readonly ConcurrentDictionary<string, string> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly RiskEscalationQueueService? _queue = queue;
    private readonly ILogger _logger = logger;

    internal void Reserve(string orderId, string escalationId) => _reservations[orderId] = escalationId;
    internal void Release(string orderId) => _reservations.TryRemove(orderId, out _);
    internal bool Contains(string orderId) => _reservations.ContainsKey(orderId);
    internal string[] SnapshotOrderIds() => _reservations.Keys.ToArray();

    internal void RehydrateParkedOrderReservations()
    {
        if (_queue is null)
        {
            return;
        }

        var reclaimed = 0;
        foreach (var entry in _queue.GetUnresolved())
        {
            if (entry.Request.ClientOrderId is not { Length: > 0 } clientOrderId)
            {
                continue;
            }

            if (_reservations.TryAdd(clientOrderId, entry.EscalationId))
            {
                reclaimed++;
            }
        }

        if (reclaimed > 0)
        {
            _logger.LogInformation(
                "Reserved {Count} client order id(s) held by governed approvals that outlived the previous host",
                reclaimed);
        }
    }

    internal bool WasRiskApprovalDeclined(string orderId)
    {
        if (!_reservations.TryGetValue(orderId, out var escalationId))
        {
            // Never parked, or the escalation's own release already reclaimed the id — in
            // which case the order routed and the report stream owns it from here.
            return false;
        }

        var entry = _queue?.TryGet(escalationId);
        if (entry is not null && entry.Status is not RiskEscalationStatus.Denied)
        {
            return false;
        }

        // A denied escalation can never be released, so the reservation on this client
        // order id is dead too; drop it here rather than waiting for the next placement
        // to clear it lazily.
        _reservations.TryRemove(orderId, out _);
        return true;
    }

    internal bool IsReservedByLiveEscalation(string orderId, OrderRequest request)
    {
        if (!_reservations.TryGetValue(orderId, out var escalationId))
        {
            return false;
        }

        var entry = _queue?.TryGet(escalationId);
        if (entry is null ||
            entry.Status is RiskEscalationStatus.Denied or RiskEscalationStatus.Released)
        {
            _reservations.TryRemove(orderId, out _);
            return false;
        }

        // Only an APPROVED escalation's release may pass. The token is just the escalation
        // id, which the submitter already received in their own parked response; honouring
        // it while the entry is still pending would let them resubmit, and if the market or
        // the configured threshold had since moved so the rule no longer escalates, the
        // order would route with no operator decision behind it at all.
        //
        // The reservation is NOT dropped here. Several gates still stand between this check
        // and the approval actually being consumed — placement, live readiness, operator
        // controls, security master — and any of them can refuse the release while the
        // approval stays armed. Freeing the id now would let a tokenless retry take it and
        // execute, after which the original approval could still release a second order
        // under the same id. ReleaseParkedOrderReservation clears it once the order is
        // registered, and the live order itself guards the id from then on.
        return entry.Status != RiskEscalationStatus.Approved ||
            request.Metadata is null ||
            !request.Metadata.TryGetValue(RiskEscalationQueueService.ApprovalMetadataKey, out var tokens) ||
            !RiskEscalationQueueService.SplitTokens(tokens).Contains(escalationId, StringComparer.OrdinalIgnoreCase);
    }

    internal void RestoreConsumedApprovals(string? consumedApprovalId, string cause, string? rearmedOrderId = null)
    {
        if (consumedApprovalId is null || _queue is null)
        {
            return;
        }

        foreach (var escalationId in RiskEscalationQueueService.SplitTokens(consumedApprovalId))
        {
            if (_queue.TryRestoreApproval(escalationId))
            {
                if (rearmedOrderId is not null)
                {
                    _reservations[rearmedOrderId] = escalationId;
                }

                _logger.LogInformation(
                    "Governed approval {EscalationId} re-armed after {Cause}",
                    escalationId,
                    cause);
            }
        }
    }

    internal string? TryWithdrawEscalation(string orderId)
    {
        if (!_reservations.TryGetValue(orderId, out var escalationId) || _queue is null)
        {
            return null;
        }

        var entry = _queue.TryGet(escalationId);
        if (entry is null || entry.Status is not RiskEscalationStatus.PendingApproval and not RiskEscalationStatus.Approved)
        {
            // Already resolved: nothing to withdraw, and the id is free.
            _reservations.TryRemove(orderId, out _);
            return null;
        }

        // Withdraw, not Deny: an operator may already have approved without releasing, and
        // a plain denial only resolves pending entries. Fail closed on the result — if the
        // entry survives, the escalation can still route, so this cancellation must not
        // report success or drop the reservation that keeps its client order id.
        if (_queue.Withdraw(
                escalationId,
                actor: "order-management-system",
                reason: "The submitter cancelled the order while it awaited governed approval.") is null)
        {
            _logger.LogWarning(
                "Order {OrderId} could not be cancelled: escalation {EscalationId} could not be withdrawn",
                LogSanitizer.Sanitize(orderId),
                escalationId);
            return null;
        }

        _reservations.TryRemove(orderId, out _);

        return escalationId;
    }
}
