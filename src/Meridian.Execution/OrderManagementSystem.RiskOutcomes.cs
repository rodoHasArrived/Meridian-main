using Meridian.Execution.Events;
using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

using static Meridian.Execution.OrderManagement.OrderAuditMetadata;
using static Meridian.Execution.OrderManagement.OrderRiskSizingPolicy;

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
            _approvalReservations.Reserve(orderId, escalationId);
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
            RiskWarnings = riskResult.Warnings.Count > 0 ? riskResult.Warnings : null,
            RiskDecision = riskResult.ToSummary()
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetExposureReservingOrders()
    {
        var reserving = new List<OrderState>(GetOpenOrders());
        var amendments = GetAmendmentExposureReservations();
        reserving.RemoveAll(order => _amendmentBook.Contains(order.OrderId));
        reserving.AddRange(amendments);

        reserving.AddRange(_fillProcessor.GetExposureReservations());

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
        => _approvalReservations.RehydrateParkedOrderReservations();

    /// <summary>
    /// Frees the client-order-id reservation an escalation held, once its released order is
    /// actually registered. From that point the tracked order guards the id itself, so the
    /// reservation has nothing left to protect — but until then it must stand, because every
    /// gate between the duplicate check and registration can still refuse the release while
    /// the approval remains armed.
    /// </summary>
    private void ReleaseParkedOrderReservation(string orderId) => _approvalReservations.Release(orderId);

    /// <inheritdoc />
    public bool WasRiskApprovalDeclined(string orderId)
        => _approvalReservations.WasRiskApprovalDeclined(orderId);

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
        await _executionAudit.RecordLifecycleAsync(
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
        if (_approvalReservations.TryWithdrawEscalation(orderId) is not { } escalationId)
            return null;

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
        => _approvalReservations.IsReservedByLiveEscalation(orderId, request);

    /// <summary>
    /// Re-arms every governed approval consumed by a validation whose order never routed.
    /// The consumed value is a token set — an order can carry one approval per
    /// escalation-capable rule — so each id is restored individually.
    /// </summary>
    /// <param name="rearmedOrderId">
    /// Client order id to re-reserve alongside the approval, for a release submission that
    /// did not route. Registration drops the parked reservation as soon as the order claims
    /// its own id, so re-arming the approval without it leaves the approval releasable while
    /// the submitter can no longer cancel it — the parked-order path cannot find the id —
    /// and the now-terminal tracked state lets an unrelated submission reclaim that id.
    /// Omitted for amendments: there the order is working rather than parked, so the
    /// ordinary cancel path owns it and a parked reservation would wrongly intercept.
    /// </param>
    private void RestoreConsumedApprovals(string? consumedApprovalId, string cause, string? rearmedOrderId = null)
        => _approvalReservations.RestoreConsumedApprovals(consumedApprovalId, cause, rearmedOrderId);

    /// <summary>
    /// Outcome of revalidating a risk-increasing amendment: either a <paramref name="Refusal"/>
    /// to hand back to the caller, or the speculative <paramref name="Reservation"/> that
    /// publishes the amended size to concurrent placements while the amendment routes.
    /// </summary>
    /// <param name="RiskDecision">
    /// The approved amendment's risk decision, carrying any capacity a stateful rule reserved to
    /// admit it. Null when no validator ran or the amendment was refused — a refusal releases its
    /// own capacity before returning, so only an approved amendment hands anything to the caller,
    /// which settles it once the gateway has answered.
    /// </param>
    private readonly record struct AmendmentGateResult(
        OrderState? Reservation,
        string? Refusal,
        IReadOnlyList<string>? Warnings,
        RiskValidationResult? RiskDecision = null);

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

        // An amendment revalidation goes through the same reserving rules a placement does, so it
        // takes real capacity. Discarding the result would hold that slot for the process's
        // lifetime and eventually block every later order.
        RiskValidationResult? amendmentDecision = null;

        await _preTradeReservationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var brokerageLimitRefusal = _admissionPolicy.EvaluateBrokerageNotionalLimit(amendmentProbe);
            if (brokerageLimitRefusal is not null)
            {
                return new AmendmentGateResult(null, brokerageLimitRefusal, null, null);
            }

            if (_riskValidator is not null)
            {
                var amendedRisk = await _riskValidator.ValidateOrderAsync(amendmentProbe, ct).ConfigureAwait(false);
                warnings = amendedRisk.Warnings.Count > 0 ? amendedRisk.Warnings : null;
                consumedApprovalId = amendedRisk.ConsumedApprovalId;
                amendmentDecision = amendedRisk;

                if (!amendedRisk.IsApproved)
                {
                    // An amendment is never parked: the original order stays live and the
                    // caller is told the increase was refused. Any approval token consumed
                    // by this evaluation is re-armed because nothing changed.
                    // Refused: nothing routes, so release the capacity now rather than handing an
                    // unsettleable handle to a caller whose only outcome is a refusal.
                    SettleRiskReservations(amendmentDecision, commit: false, orderId);
                    RestoreConsumedApprovals(consumedApprovalId, "an amendment refused by risk validation");
                    var refusal = amendedRisk.RequiresApproval
                        ? $"Modification requires governed approval and was not applied: {amendedRisk.RejectReason}"
                        : amendedRisk.RejectReason ?? "Modification rejected by risk validation.";
                    // Carry the decision itself, not just its headline. A composite rejection takes
                    // its message from the highest-severity rule and keeps the rest in Violations,
                    // so a fat-finger breach behind a gross-exposure headline exists only here —
                    // dropping it leaves the amendment audit unable to show it, and rule status
                    // reporting Healthy for a rule that is actively refusing amendments.
                    return new AmendmentGateResult(null, refusal, warnings, amendedRisk);
                }
            }

            // Reserve the amended size before releasing the gate so a concurrent
            // placement measures against the larger order, then route the amendment.
            var reservation = state with
            {
                Quantity = modification.NewQuantity ?? state.Quantity,
                LimitPrice = modification.NewLimitPrice ?? state.LimitPrice,
                StopPrice = modification.NewStopPrice ?? state.StopPrice,
                RoutedNotional = ResolveAmendedRoutedNotional(state, modification),
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            if (_orders.TryUpdate(orderId, reservation, state))
            {
                return new AmendmentGateResult(reservation, null, warnings, amendmentDecision);
            }
        }
        catch
        {
            // Cancelled or faulted before the amendment could be published: nothing routed.
            SettleRiskReservations(amendmentDecision, commit: false, orderId);
            throw;
        }
        finally
        {
            _preTradeReservationGate.Release();
        }

        // The order moved underneath the gate, so the amended exposure was never published.
        // Routing the amendment anyway would raise the broker-side size while every
        // concurrent placement still measured the smaller order, so refuse it instead.
        // The amendment lost the race and never published its exposure, so nothing can route.
        SettleRiskReservations(amendmentDecision, commit: false, orderId);
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

    /// <summary>
    /// Settles the capacity the risk gate reserved for this order, exactly once, at the routing
    /// boundary.
    /// </summary>
    /// <param name="decision">
    /// The approved decision holding the reservations, or <see langword="null"/> when no validator
    /// ran or the order never passed the gate. A non-approved decision has already released its own
    /// capacity inside the validator, so there is nothing here to settle.
    /// </param>
    /// <param name="commit">
    /// <see langword="true"/> when the order reached — or may have reached — a venue.
    /// </param>
    /// <remarks>
    /// Never throws. Settlement runs on failure paths that are already unwinding, and a reservation
    /// whose commit faults must not replace the outcome the caller is about to receive, nor stop the
    /// remaining reservations from being settled.
    /// </remarks>
    private void SettleRiskReservations(RiskValidationResult? decision, bool commit, string orderId)
    {
        if (decision is null || decision.Reservations.Count == 0)
        {
            return;
        }

        try
        {
            if (commit)
            {
                decision.CommitReservations();
            }
            else
            {
                decision.RollbackReservations();
            }
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError(
                    ex,
                    "Risk reservations for order {OrderId} could not be settled ({Settlement})",
                    LogSanitizer.Sanitize(orderId),
                    commit ? "commit" : "rollback");
            }
            catch
            {
                // The logging provider is the thing that failed. Nothing left to report with,
                // and the settlement outcome must not become the caller's exception.
            }
        }
    }

    private async Task<OrderResult> RejectDuplicateClientOrderIdAsync(
        string orderId,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        CancellationToken ct)
    {
        var message = $"Duplicate client order id '{orderId}': an order with this id is already being tracked and is not in a terminal state.";

        await RecordOrderRejectionAsync(
            orderId,
            request,
            actor,
            brokerName,
            runId,
            correlationId,
            message,
            ct,
            rejectionSource: "duplicate client order id guard",
            reasonCode: "DUPLICATE_CLIENT_ORDER_ID").ConfigureAwait(false);

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = message
        };
    }

    private static OrderState CreateRejectedState(
        string orderId,
        OrderRequest request,
        string? reason)
    {
        return new OrderState
        {
            OrderId = orderId,
            Symbol = request.Symbol,
            Side = request.Side,
            Type = request.Type,
            Quantity = request.Quantity,
            LimitPrice = request.LimitPrice,
            StopPrice = request.StopPrice,
            Status = OrderStatus.Rejected,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            StrategyId = request.StrategyId,
            // Fund scope survives a rejection: a parked order's state is built here, and
            // cancelling one withdraws its approval — authorized against this field.
            FundAccountId = request.FundAccountId,
            AverageFillPrice = null,
            FilledQuantity = 0m
        };
    }
}
