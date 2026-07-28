using System.Globalization;
using Meridian.Execution.Events;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// Risk-outcome handling for the OMS pre-trade gate: the typed parked outcome for
/// governed-approval escalations and the durable retention of non-blocking risk warning
/// flags on both approved and rejected orders.
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Terminal handling for an order a risk escalation parked for governed approval: the
    /// order does not route, its tracked state mirrors a rejection (nothing is live at the
    /// broker), but the audit action and typed result distinguish "awaiting approval" from
    /// "rejected" so operators and downstream status surfaces do not count it as a breach.
    /// </summary>
    private async Task<OrderResult> ParkOrderForApprovalAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        RiskValidationResult riskResult,
        string? sessionId,
        CancellationToken ct)
    {
        var parkedState = CreateRejectedState(orderId, request, riskResult.RejectReason);

        // The tracked state is terminal (nothing is working at the broker), but the client
        // order id must not be reclaimable while the approval is live: another submission
        // could take the id and later collide with the released order. Reserve it against
        // the escalation so only that escalation's own release can reuse it.
        if (riskResult.EscalationId is { } escalationId)
        {
            _parkedOrderIds[orderId] = escalationId;
        }
        if (_orders.TryAdd(orderId, parkedState))
        {
            TrimRetainedOrdersIfNeeded();
        }

        // The escalation is already committed to the governed queue and an operator can
        // act on it, so post-park bookkeeping must never turn that committed outcome into
        // a failed submission — the submitter would then never learn the order is parked
        // while the queue entry stays releasable. Session persistence and the audit append
        // are best-effort here; both failures are logged, not propagated.
        try
        {
            await RecordSessionOrderUpdateAsync(sessionId, parkedState, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Order {OrderId} parked for approval, but its paper-session update could not be recorded",
                LogSanitizer.Sanitize(orderId));
        }

        _logger.LogWarning(
            "Order {OrderId} parked for governed risk approval ({EscalationId})",
            LogSanitizer.Sanitize(orderId),
            LogSanitizer.Sanitize(riskResult.EscalationId));

        if (_auditTrail is not null)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["escalationId"] = riskResult.EscalationId ?? string.Empty
            };
            AppendRiskWarningsMetadata(metadata, riskResult.Warnings);

            try
            {
                await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                    AuditId: Guid.NewGuid().ToString("N"),
                    Category: "Risk",
                    Action: "OrderParkedForApproval",
                    Outcome: "Parked",
                    OccurredAt: DateTimeOffset.UtcNow,
                    Actor: actor,
                    BrokerName: brokerName,
                    OrderId: orderId,
                    RunId: runId,
                    Symbol: request.Symbol,
                    CorrelationId: correlationId,
                    Message: riskResult.RejectReason,
                    Reason: "RISK_ESCALATION_PARKED",
                    Scope: BuildOrderAuditScope(request, runId),
                    Metadata: metadata), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Order {OrderId} parked for approval, but the parking audit entry could not be recorded",
                    LogSanitizer.Sanitize(orderId));
            }
        }

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = riskResult.RejectReason,
            OrderState = parkedState,
            RequiresApproval = true,
            EscalationId = riskResult.EscalationId,
            RiskWarnings = riskResult.Warnings.Count > 0 ? riskResult.Warnings : null
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetExposureReservingOrders()
    {
        var reserving = new List<OrderState>(GetOpenOrders());

        // A fill flips the tracked order terminal before ProcessFillReportAsync applies it
        // to the portfolio. During that window the exposure exists at the broker but sits
        // in neither book, so surface the un-applied increment as a reservation.
        foreach (var progress in _fillProcessing.Values)
        {
            if (progress.PortfolioApplied)
            {
                continue;
            }

            var increment = progress.FillIncrement;
            if (increment.FilledQuantity <= 0m)
            {
                continue;
            }

            reserving.Add(new OrderState
            {
                OrderId = increment.ClientOrderId ?? increment.OrderId,
                Symbol = increment.Symbol,
                Side = increment.Side,
                Type = OrderType.Market,
                Quantity = increment.FilledQuantity,
                LimitPrice = increment.FillPrice,
                Status = OrderStatus.PendingNew,
                CreatedAt = increment.Timestamp
            });
        }

        return reserving;
    }

    /// <summary>
    /// Rebuilds the client-order-id reservations held by escalations that survived a
    /// restart. The queue is durable but this map is not: without this, a resubmission
    /// under a parked order's client order id would find the id free, route, and reach a
    /// terminal state, after which the still-live escalation could be approved and route a
    /// second execution under the same id — two orders sharing one order's audit history.
    /// </summary>
    private void RehydrateParkedOrderReservations()
    {
        if (_escalationQueue is null)
        {
            return;
        }

        var reclaimed = 0;
        foreach (var entry in _escalationQueue.GetUnresolved())
        {
            if (entry.Request.ClientOrderId is not { Length: > 0 } clientOrderId)
            {
                continue;
            }

            if (_parkedOrderIds.TryAdd(clientOrderId, entry.EscalationId))
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

    /// <inheritdoc />
    public bool WasRiskApprovalDeclined(string orderId)
    {
        if (!_parkedOrderIds.TryGetValue(orderId, out var escalationId))
        {
            // Never parked, or the escalation's own release already reclaimed the id — in
            // which case the order routed and the report stream owns it from here.
            return false;
        }

        var entry = _escalationQueue?.TryGet(escalationId);
        if (entry is not null && entry.Status is not RiskEscalationStatus.Denied)
        {
            return false;
        }

        // A denied escalation can never be released, so the reservation on this client
        // order id is dead too; drop it here rather than waiting for the next placement
        // to clear it lazily.
        _parkedOrderIds.TryRemove(orderId, out _);
        return true;
    }

    /// <summary>
    /// Cancels an order awaiting governed approval: the escalation is withdrawn so no
    /// operator can later execute an order the submitter cancelled, and the cancellation
    /// completes locally because no broker order exists. Returns null when the order is
    /// not parked, leaving the ordinary gateway cancellation to run.
    /// </summary>
    private async Task<OrderResult?> TryCancelParkedOrderAsync(string orderId, CancellationToken ct)
    {
        if (TryWithdrawParkedEscalation(orderId) is not { } withdrawnState)
        {
            return null;
        }

        await RecordSessionOrderUpdateAsync(ResolveSessionId(orderId), withdrawnState, ct).ConfigureAwait(false);
        await RecordOrderLifecycleAuditAsync(
            action: "OrderCancelled",
            outcome: OrderStatus.Cancelled.ToString(),
            orderId: orderId,
            state: withdrawnState,
            report: null,
            message: "Cancelled while awaiting governed approval; the escalation was withdrawn.",
            ct: ct).ConfigureAwait(false);

        return new OrderResult { Success = true, OrderId = orderId, OrderState = withdrawnState };
    }

    /// <summary>
    /// Withdraws the governed-approval escalation holding <paramref name="orderId"/>, if
    /// any, and marks the tracked order cancelled. Returns the cancelled state when the
    /// order was parked (so the caller completes the cancellation locally), or null when
    /// the order is not awaiting approval.
    /// </summary>
    private OrderState? TryWithdrawParkedEscalation(string orderId)
    {
        if (!_parkedOrderIds.TryGetValue(orderId, out var escalationId) || _escalationQueue is null)
        {
            return null;
        }

        var entry = _escalationQueue.TryGet(escalationId);
        if (entry is null || entry.Status is not RiskEscalationStatus.PendingApproval and not RiskEscalationStatus.Approved)
        {
            // Already resolved: nothing to withdraw, and the id is free.
            _parkedOrderIds.TryRemove(orderId, out _);
            return null;
        }

        _escalationQueue.Deny(
            escalationId,
            actor: "order-management-system",
            reason: "The submitter cancelled the order while it awaited governed approval.");
        _parkedOrderIds.TryRemove(orderId, out _);

        if (!_orders.TryGetValue(orderId, out var state))
        {
            return null;
        }

        var cancelled = state with
        {
            Status = OrderStatus.Cancelled,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        _orders[orderId] = cancelled;
        _logger.LogInformation(
            "Order {OrderId} cancelled while parked; escalation {EscalationId} withdrawn",
            LogSanitizer.Sanitize(orderId),
            escalationId);
        return cancelled;
    }

    /// <summary>
    /// True when <paramref name="orderId"/> is reserved by a live escalation that
    /// <paramref name="request"/> is not the authorized release for. The release carries the
    /// escalation's approval token; anything else must not reclaim the id. A reservation
    /// whose escalation is no longer actionable (denied, or already released) is cleared.
    /// </summary>
    private bool IsReservedByLiveEscalation(string orderId, OrderRequest request)
    {
        if (!_parkedOrderIds.TryGetValue(orderId, out var escalationId))
        {
            return false;
        }

        var entry = _escalationQueue?.TryGet(escalationId);
        if (entry is null ||
            entry.Status is RiskEscalationStatus.Denied or RiskEscalationStatus.Released)
        {
            _parkedOrderIds.TryRemove(orderId, out _);
            return false;
        }

        if (request.Metadata is not null &&
            request.Metadata.TryGetValue(RiskEscalationQueueService.ApprovalMetadataKey, out var tokens) &&
            RiskEscalationQueueService.SplitTokens(tokens).Contains(escalationId, StringComparer.OrdinalIgnoreCase))
        {
            // This is the escalation's own release: it may reclaim the id.
            _parkedOrderIds.TryRemove(orderId, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Re-arms every governed approval consumed by a validation whose order never routed.
    /// The consumed value is a token set — an order can carry one approval per
    /// escalation-capable rule — so each id is restored individually.
    /// </summary>
    private void RestoreConsumedApprovals(string? consumedApprovalId, string cause)
    {
        if (consumedApprovalId is null || _escalationQueue is null)
        {
            return;
        }

        foreach (var escalationId in RiskEscalationQueueService.SplitTokens(consumedApprovalId))
        {
            if (_escalationQueue.TryRestoreApproval(escalationId))
            {
                _logger.LogInformation(
                    "Governed approval {EscalationId} re-armed after {Cause}",
                    escalationId,
                    cause);
            }
        }
    }

    /// <summary>
    /// Removes the internal evaluation-only probe flag from a caller-supplied request. The
    /// flag suppresses governed-approval parking, so honoring it from arbitrary order
    /// metadata would let a caller obtain a parked outcome with no queue entry behind it.
    /// </summary>
    private static OrderRequest StripEvaluationOnlyFlag(OrderRequest request)
    {
        if (request.Metadata is null ||
            !request.Metadata.ContainsKey(RiskEscalationQueueService.EvaluationOnlyMetadataKey))
        {
            return request;
        }

        var sanitized = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase);
        sanitized.Remove(RiskEscalationQueueService.EvaluationOnlyMetadataKey);
        return request with { Metadata = sanitized };
    }

    /// <summary>
    /// Outcome of revalidating a risk-increasing amendment: either a <paramref name="Refusal"/>
    /// to hand back to the caller, or the speculative <paramref name="Reservation"/> that
    /// publishes the amended size to concurrent placements while the amendment routes.
    /// </summary>
    private readonly record struct AmendmentGateResult(
        OrderState? Reservation,
        string? Refusal,
        IReadOnlyList<string>? Warnings);

    /// <summary>
    /// Revalidates a risk-increasing amendment and reserves the amended exposure under the
    /// same pre-trade gate a placement uses, so a concurrent order cannot size itself against
    /// the smaller pre-amendment order.
    /// </summary>
    private async Task<AmendmentGateResult> ReserveAmendedExposureAsync(
        string orderId,
        OrderState state,
        OrderModification modification,
        CancellationToken ct)
    {
        var amendmentProbe = BuildAmendmentProbe(state, modification);
        IReadOnlyList<string>? warnings = null;
        string? consumedApprovalId = null;

        await _preTradeReservationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_riskValidator is not null)
            {
                var amendedRisk = await _riskValidator.ValidateOrderAsync(amendmentProbe, ct).ConfigureAwait(false);
                warnings = amendedRisk.Warnings.Count > 0 ? amendedRisk.Warnings : null;
                consumedApprovalId = amendedRisk.ConsumedApprovalId;

                if (!amendedRisk.IsApproved)
                {
                    // An amendment is never parked: the original order stays live and the
                    // caller is told the increase was refused. Any approval token consumed
                    // by this evaluation is re-armed because nothing changed.
                    RestoreConsumedApprovals(consumedApprovalId, "an amendment refused by risk validation");
                    var refusal = amendedRisk.RequiresApproval
                        ? $"Modification requires governed approval and was not applied: {amendedRisk.RejectReason}"
                        : amendedRisk.RejectReason ?? "Modification rejected by risk validation.";
                    return new AmendmentGateResult(null, refusal, warnings);
                }
            }

            // Reserve the amended size before releasing the gate so a concurrent
            // placement measures against the larger order, then route the amendment.
            var reservation = state with
            {
                Quantity = modification.NewQuantity ?? state.Quantity,
                LimitPrice = modification.NewLimitPrice ?? state.LimitPrice,
                StopPrice = modification.NewStopPrice ?? state.StopPrice,
                RoutedNotional = null,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            if (_orders.TryUpdate(orderId, reservation, state))
            {
                return new AmendmentGateResult(reservation, null, warnings);
            }
        }
        finally
        {
            _preTradeReservationGate.Release();
        }

        // The order moved underneath the gate, so the amended exposure was never published.
        // Routing the amendment anyway would raise the broker-side size while every
        // concurrent placement still measured the smaller order, so refuse it instead.
        RestoreConsumedApprovals(consumedApprovalId, "an amendment whose exposure could not be reserved");
        _logger.LogWarning(
            "Order {OrderId} amendment was not applied: the order changed while its amended exposure was being reserved",
            LogSanitizer.Sanitize(orderId));

        return new AmendmentGateResult(
            null,
            "Modification not applied: the order changed while its amended exposure was being reserved.",
            warnings);
    }

    /// <summary>
    /// Undoes the speculative amended reservation when the gateway never accepted the
    /// modification, so the tracked order and every exposure snapshot fall back to the
    /// size the broker actually holds.
    /// </summary>
    private void RollBackSpeculativeReservation(string orderId, OrderState? speculative, OrderState original)
    {
        if (speculative is null)
        {
            return;
        }

        if (!_orders.TryUpdate(orderId, original, speculative))
        {
            // A report already advanced the order past the speculative state; the report
            // stream is authoritative from that point and must not be overwritten here.
            _logger.LogWarning(
                "Order {OrderId} amendment was refused, but its state had already advanced; leaving the tracked state to the report stream",
                LogSanitizer.Sanitize(orderId));
        }
    }

    /// <summary>
    /// Measured value of an order state under the same model the enforced rules use: the
    /// routed notional for dollar-sized orders, otherwise quantity times the order's own
    /// price. Returns null when the state carries no price of its own (a market order),
    /// where only the live mark — which the OMS does not hold — could measure it.
    /// </summary>
    private static decimal? MeasureOrderValue(decimal quantity, decimal? limitPrice, decimal? stopPrice, decimal? routedNotional)
    {
        if (routedNotional is { } notional && notional > 0m)
        {
            return notional;
        }

        var price = limitPrice ?? stopPrice;
        return price is { } resolved && resolved > 0m ? Math.Abs(quantity) * resolved : null;
    }

    /// <summary>
    /// True when a modification could increase the order's measured exposure. This mirrors
    /// the enforcement valuation (<c>OrderNotionalResolver</c>), which values an order at
    /// the larger of its own price and the live mark: a higher price raises the measured
    /// notional on either side, so a raised sell limit is risk-increasing too. Quantity
    /// increases always qualify. When neither the current nor the amended order carries a
    /// price, the amendment is treated as risk-increasing so the rules get to decide.
    /// </summary>
    private static bool IsRiskIncreasing(OrderState state, OrderModification modification)
    {
        if (modification.NewQuantity is { } newQuantity && Math.Abs(newQuantity) > Math.Abs(state.Quantity))
        {
            return true;
        }

        // Any price increase raises the measured notional under the enforcement model,
        // regardless of side. A price decrease can only lower it (a marketable order is
        // already valued at the mark), so it is never risk-increasing.
        if (modification.NewLimitPrice is { } newLimit &&
            newLimit > (state.LimitPrice ?? 0m))
        {
            return true;
        }

        return modification.NewStopPrice is { } newStop && newStop > (state.StopPrice ?? 0m);
    }

    /// <summary>
    /// Reconstructs the order the gateway would hold after <paramref name="modification"/>,
    /// so the pre-trade rules evaluate the proposed order rather than the original.
    /// </summary>
    private static OrderRequest BuildAmendedRequest(OrderState state, OrderModification modification) => new()
    {
        Symbol = state.Symbol,
        Side = state.Side,
        Type = state.Type,
        Quantity = modification.NewQuantity ?? state.Quantity,
        LimitPrice = modification.NewLimitPrice ?? state.LimitPrice,
        StopPrice = modification.NewStopPrice ?? state.StopPrice,
        ClientOrderId = state.OrderId,
        StrategyId = state.StrategyId,
        FundAccountId = state.FundAccountId
    };

    /// <summary>
    /// Builds the request the risk rules should evaluate for an amendment. The exposure
    /// snapshot already reserves the working order at its current size, so evaluating the
    /// full amended order would double-count it — raising a $1k working buy to $2k would
    /// project $3k. When both sizes are measurable, this returns a probe carrying only the
    /// incremental value, so snapshot + probe equals the post-amendment book. When the
    /// order cannot be measured from its own fields (a market order priced off the live
    /// mark), it falls back to the full amended request, which is conservative.
    /// </summary>
    private static OrderRequest BuildAmendmentProbe(OrderState state, OrderModification modification)
    {
        var amended = BuildAmendedRequest(state, modification);
        var probeMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [RiskEscalationQueueService.EvaluationOnlyMetadataKey] = "true"
        };

        var currentValue = MeasureOrderValue(state.Quantity, state.LimitPrice, state.StopPrice, state.RoutedNotional);
        var amendedValue = MeasureOrderValue(amended.Quantity, amended.LimitPrice, amended.StopPrice, routedNotional: null);
        if (currentValue is not { } current || amendedValue is not { } proposed || proposed <= current)
        {
            return amended with { Metadata = probeMetadata };
        }

        // Quantity stays the FULL amended quantity so the position-limit rule projects the
        // real post-amendment position; only the notional-based rules read the incremental
        // value, because the snapshot already reserves the working order's current size.
        probeMetadata[RiskEscalationQueueService.IncrementalNotionalMetadataKey] =
            (proposed - current).ToString("G29", CultureInfo.InvariantCulture);
        return amended with { Metadata = probeMetadata };
    }

    private static IReadOnlyDictionary<string, string>? BuildRiskWarningsAuditMetadata(
        IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AppendRiskWarningsMetadata(metadata, warnings);
        return metadata;
    }

    private static void AppendRiskWarningsMetadata(
        IDictionary<string, string> metadata,
        IReadOnlyList<string> warnings)
    {
        for (var i = 0; i < warnings.Count; i++)
        {
            metadata[$"warning{i + 1}"] = warnings[i];
        }
    }

    private async Task RecordRiskWarningsAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Order {OrderId} approved with {WarningCount} non-blocking risk flag(s)",
            LogSanitizer.Sanitize(orderId),
            warnings.Count);

        if (_auditTrail is null)
        {
            return;
        }

        try
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AppendRiskWarningsMetadata(metadata, warnings);

            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Risk",
                Action: "RiskWarningsFlagged",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: actor,
                BrokerName: brokerName,
                OrderId: orderId,
                RunId: runId,
                Symbol: request.Symbol,
                CorrelationId: correlationId,
                Message: $"Order approved with {warnings.Count} non-blocking risk flag(s).",
                Scope: BuildOrderAuditScope(request, runId),
                Metadata: metadata), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Order {OrderId} risk warnings could not be recorded to the audit trail",
                LogSanitizer.Sanitize(orderId));
        }
    }

    private sealed class FillProcessingProgress(
        ExecutionReport fillIncrement,
        decimal cumulativeFilledQuantity,
        bool isTrackedOrder)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ExecutionReport FillIncrement { get; } = fillIncrement;
        public decimal CumulativeFilledQuantity { get; } = cumulativeFilledQuantity;
        public bool IsTrackedOrder { get; } = isTrackedOrder;
        public TradeExecutedEvent? TradeEvent { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal NewCash { get; set; }
        public bool PortfolioApplied { get; set; }
        public bool TradeEventPublished { get; set; }
        public bool SessionRecorded { get; set; }
        public bool ExecutionReportPublished { get; set; }
        public volatile bool IsComplete;
    }
}
