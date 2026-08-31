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
            RiskWarnings = riskResult.Warnings.Count > 0 ? riskResult.Warnings : null,
            RiskDecision = riskResult.ToSummary()
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderState> GetExposureReservingOrders()
    {
        var reserving = new List<OrderState>(GetOpenOrders());

        // A fill flips the tracked order terminal before ProcessFillReportAsync applies it
        // to the portfolio. Reports whose precise increment is not tracked yet reserve
        // their whole reported fill; once tracked, the increment below supersedes them.
        foreach (var pending in _pendingFillReservations.Values)
        {
            if (_fillProcessing.ContainsKey(pending) || pending.FilledQuantity <= 0m)
            {
                continue;
            }

            reserving.Add(BuildHandoffReservation(
                pending.ClientOrderId ?? pending.OrderId,
                pending.Symbol,
                pending.Side,
                pending.FilledQuantity,
                pending.FillPrice,
                pending.Timestamp));
        }

        // During that window the exposure exists at the broker but sits in neither book,
        // so surface the un-applied increment as a reservation.
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

            reserving.Add(BuildHandoffReservation(
                increment.ClientOrderId ?? increment.OrderId,
                increment.Symbol,
                increment.Side,
                increment.FilledQuantity,
                increment.FillPrice,
                increment.Timestamp));
        }

        return reserving;
    }

    /// <summary>
    /// Reservation standing in for a fill that has left the order book but not yet reached
    /// the portfolio. It carries the tracked order's derivative sizing: without it a
    /// 100-contract fill at $5 reserves $500 instead of $50,000 during precisely the window
    /// this reservation exists to cover.
    /// </summary>
    /// <summary>
    /// Whether the order carries broker-native notional sizing metadata that this gateway
    /// will not route. Every rail that measures an order's economic size reads the routed
    /// notional from that metadata, so on a gateway that routes <c>Quantity</c> instead,
    /// <c>notional=1</c> on a 100,000-share order is measured as a one-dollar order by the
    /// order-notional, gross-exposure, and concentration rules while the broker fills all
    /// 100,000 shares. Refusing beats measuring one size and routing another.
    /// </summary>
    /// <summary>
    /// Strips server-owned routing keys a caller must not supply, then copies the metadata once,
    /// before anything reads it.
    /// <para>
    /// <see cref="ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys"/> hands back the
    /// original request when there is nothing to strip, so without the copy the sanitized request's
    /// metadata <em>is</em> the caller's dictionary — and an in-process caller can hold a mutable one
    /// across the awaits in placement. Sizing capability, risk validation, retained state, and
    /// gateway submission each read metadata at a different point, so a caller could otherwise flip
    /// <c>asset_class</c> after the sizing decision and have risk measure a treasury while the
    /// gateway routes equity shares, with the order state under-reserving it as face value for the
    /// rest of its life. One read, one copy, one order.
    /// </para>
    /// </summary>
    private OrderRequest SanitizeAndSnapshotRequest(OrderRequest request, string orderId)
    {
        var safeRequest = ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(request);
        if (!ReferenceEquals(safeRequest, request))
        {
            _logger.LogWarning(
                "Order {OrderId} for {Symbol} contained server-owned broker routing metadata; routing keys were removed before gateway submission.",
                LogSanitizer.Sanitize(orderId),
                LogSanitizer.Sanitize(request.Symbol));
        }

        // Metadata is not the only caller-owned collection on the request: Legs is a list the caller
        // may still hold, and the fat-finger quantity limb multiplies by the largest leg ratio, so a
        // ratio raised after validation routes more contracts than the ceiling approved. Both are
        // copied here for the same reason and at the same moment — snapshotting one and not the
        // other just moves the race.
        return safeRequest with
        {
            // Same construction the sizing stamp uses, so the request's key comparer and duplicate
            // handling survive: broker metadata readers have ordered alias rules of their own.
            Metadata = safeRequest.Metadata is { } callerMetadata
                ? new Dictionary<string, string>(callerMetadata)
                : safeRequest.Metadata,
            Legs = safeRequest.Legs is { } callerLegs ? [.. callerLegs] : safeRequest.Legs
        };
    }

    /// <summary>
    /// Whether the active gateway routes this order's quantity as face value priced as a percentage
    /// of par. Asset-class labels alone do not define quantity semantics: Alpaca routes fixed-income
    /// <c>Qty</c> as face value, while IB routes a count of $1,000 bonds under the generic "bond"
    /// class — so the gateway is asked rather than the label read.
    /// </summary>
    private bool ResolvesFaceValuePercentageOfPar(OrderRequest request) =>
        _faceValueOrderSizingGateway?.UsesFaceValuePercentageOfPar(request) is true;

    /// <summary>
    /// Carries the gateway-resolved sizing fact into the request the risk rules evaluate, and only
    /// there: the marker is server-owned, so it is stamped on a copy rather than on anything a
    /// caller supplied or the gateway will receive.
    /// </summary>
    private static OrderRequest StampResolvedOrderSizing(
        OrderRequest request,
        bool usesFaceValuePercentageOfPar) =>
        usesFaceValuePercentageOfPar
            ? request with { Metadata = OrderSizingMetadata.WithFaceValuePercentageOfPar(request.Metadata) }
            : request;

    private bool CarriesUnroutableNotionalMetadata(OrderRequest request) =>
        BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is not null &&
        _notionalSizingGateway?.RoutesNotionalMetadata(request) is not true;

    private static string UnroutableNotionalMetadataReason(string brokerName) =>
        $"Broker-native notional sizing metadata is not supported by {brokerName}; this gateway "
            + "routes order quantity. Remove the notional metadata and size the order in quantity.";

    private OrderState BuildHandoffReservation(
        string orderId,
        string symbol,
        OrderSide side,
        decimal filledQuantity,
        decimal? fillPrice,
        DateTimeOffset timestamp)
    {
        _orders.TryGetValue(orderId, out var tracked);
        return new OrderState
        {
            OrderId = orderId,
            Symbol = symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = filledQuantity,
            LimitPrice = fillPrice,
            Status = OrderStatus.PendingNew,
            CreatedAt = timestamp,
            ContractMultiplier = tracked?.ContractMultiplier ?? 1m,
            UsesFaceValuePercentageOfPar = tracked?.UsesFaceValuePercentageOfPar ?? false,
            OptionContract = tracked?.OptionContract,
            Legs = tracked?.Legs
        };
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

    /// <summary>
    /// Contract multiplier an order's fills carry: the notional one unit of quantity
    /// represents. Equity options are 100 across every venue Meridian routes to, so an
    /// option order with no adapter-stamped multiplier still measures as contracts rather
    /// than shares. A multi-leg order takes the largest of its legs, which cannot
    /// under-measure the position the fills open.
    /// </summary>
    private static decimal ResolveContractMultiplier(OrderRequest request)
    {
        const decimal defaultOptionMultiplier = 100m;

        static decimal FromContract(OptionContractIdentity? contract)
        {
            if (contract is null)
            {
                return 1m;
            }

            return decimal.TryParse(
                contract.Multiplier,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) && parsed > 0m
                ? parsed
                : defaultOptionMultiplier;
        }

        var multiplier = FromContract(request.OptionContract);
        if (request.Legs is { Count: > 0 } legs)
        {
            foreach (var leg in legs)
            {
                multiplier = Math.Max(multiplier, FromContract(leg.OptionContract));
            }
        }

        return multiplier;
    }

    /// <summary>
    /// Frees the client-order-id reservation an escalation held, once its released order is
    /// actually registered. From that point the tracked order guards the id itself, so the
    /// reservation has nothing left to protect — but until then it must stand, because every
    /// gate between the duplicate check and registration can still refuse the release while
    /// the approval remains armed.
    /// </summary>
    private void ReleaseParkedOrderReservation(string orderId) => _parkedOrderIds.TryRemove(orderId, out _);

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

        // Withdraw, not Deny: an operator may already have approved without releasing, and
        // a plain denial only resolves pending entries. Fail closed on the result — if the
        // entry survives, the escalation can still route, so this cancellation must not
        // report success or drop the reservation that keeps its client order id.
        if (_escalationQueue.Withdraw(
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
    {
        if (consumedApprovalId is null || _escalationQueue is null)
        {
            return;
        }

        foreach (var escalationId in RiskEscalationQueueService.SplitTokens(consumedApprovalId))
        {
            if (_escalationQueue.TryRestoreApproval(escalationId))
            {
                if (rearmedOrderId is not null)
                {
                    _parkedOrderIds[rearmedOrderId] = escalationId;
                }

                _logger.LogInformation(
                    "Governed approval {EscalationId} re-armed after {Cause}",
                    escalationId,
                    cause);
            }
        }
    }

    /// <summary>
    /// Metadata keys only this OMS may set. The evaluation-only flag suppresses governed-approval
    /// parking, the incremental notional tells portfolio rules to charge less than the order's
    /// full size, and the sizing marker records a decision made by the active gateway. A caller
    /// who could set them could bypass escalation, declare zero exposure, or opt out of the unit
    /// ceiling by pretending an ordinary quantity were fixed-income face value.
    /// </summary>
    private static readonly string[] InternalRiskMetadataKeys =
    [
        RiskEscalationQueueService.EvaluationOnlyMetadataKey,
        RiskEscalationQueueService.IncrementalNotionalMetadataKey,
        OrderSizingMetadata.FaceValuePercentageOfParKey
    ];

    /// <summary>
    /// Removes internal risk-probe metadata from a caller-supplied request, so only orders
    /// this OMS builds itself can carry it.
    /// </summary>
    private static OrderRequest StripInternalRiskMetadata(OrderRequest request)
    {
        if (request.Metadata is null
            || !request.Metadata.Keys.Any(candidate => InternalRiskMetadataKeys.Any(
                key => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))))
        {
            return request;
        }

        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (candidate, value) in request.Metadata)
        {
            if (!InternalRiskMetadataKeys.Any(
                    key => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)))
            {
                sanitized[candidate] = value;
            }
        }

        return request with { Metadata = sanitized };
    }

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

    /// <summary>
    /// Measured value of an order state under the same model the enforced rules use: the
    /// routed notional for dollar-sized orders, otherwise quantity times the order's own
    /// price. Returns null when the state carries no price of its own (a market order),
    /// where only the live mark — which the OMS does not hold — could measure it.
    /// </summary>
    private static decimal? MeasureOrderValue(
        decimal quantity,
        decimal? limitPrice,
        decimal? stopPrice,
        decimal? routedNotional,
        decimal contractMultiplier = 1m,
        bool usesFaceValuePercentageOfPar = false)
    {
        if (routedNotional is { } notional && notional > 0m)
        {
            return notional;
        }

        var price = limitPrice ?? stopPrice;
        if (price is not { } resolved || resolved <= 0m)
        {
            return null;
        }

        // A contract is not a share. Measuring an option amendment at quantity x premium
        // would assess an increase from 10 to 100 contracts at $5 as a $450 increment
        // instead of $45,000, and the per-order rule would see a $500 order.
        //
        // Scale percentage-of-par before multiplying, matching OrderNotionalResolver: dividing the
        // product instead lets the intermediate overflow on a notional that is perfectly
        // representable, and here that throws inside the amendment probe — before any risk decision
        // or dispatch — so ModifyOrderAsync raises instead of returning a structured refusal.
        var effectivePrice = usesFaceValuePercentageOfPar ? resolved / 100m : resolved;
        return Math.Abs(quantity) * effectivePrice
            * (contractMultiplier > 0m ? contractMultiplier : 1m);
    }

    /// <summary>
    /// True when a modification needs a fresh risk decision. Quantity increases qualify because
    /// they raise exposure. Every supplied limit or stop price also qualifies even when notional
    /// falls: the dangerous direction is side- and order-type-specific, so a side-neutral numeric
    /// increase test lets a sell limit or buy stop be amended through the market without the price
    /// controls seeing it. Presence, rather than equality with the initially read state, also
    /// prevents a concurrent amendment from turning an apparent no-op into an unvalidated price
    /// reversal. Quantity-only reductions still bypass the gate so de-risking is never blocked.
    /// </summary>
    private static bool RequiresRiskRevalidation(OrderState state, OrderModification modification)
    {
        if (modification.NewQuantity is { } newQuantity)
        {
            // A quantity amendment on a broker-notional order changes the sizing BASIS, so the two
            // numbers are not comparable and "smaller" does not mean smaller. The order's Quantity
            // is dollars (or a placeholder standing in for them), while the amended value is units
            // the gateway sends as qty with no notional field: $2,500 becoming 100 shares of a $100
            // symbol is $10,000 at the venue, and comparing 100 against 2,500 reads it as a
            // reduction and skips the gate on a fourfold increase. Any such amendment revalidates.
            if (state.RoutedNotional is > 0m)
            {
                return true;
            }

            if (Math.Abs(newQuantity) > Math.Abs(state.Quantity))
            {
                return true;
            }
        }

        return modification.NewLimitPrice is not null || modification.NewStopPrice is not null;
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
        FundAccountId = state.FundAccountId,
        Metadata = BuildAmendedSizingMetadata(state, modification),
        // Without the derivative identity the rules re-value the amended order as shares,
        // repeating the 1x mistake the multiplier above exists to prevent.
        OptionContract = state.OptionContract,
        Legs = state.Legs
    };

    /// <summary>
    /// The dollar sizing the broker still holds after an amendment. Nulling it instead makes the
    /// exposure provider fall back to <c>Quantity × price</c>, and for a notional order the quantity
    /// field is a placeholder rather than a size — a $2,500 buy carried as <c>Quantity = 1</c>
    /// repriced to a $90 limit would reserve $90, and later orders would clear portfolio ceilings
    /// against exposure understated by more than an order of magnitude. Under-reserving is the
    /// direction that admits a breach, so this errs the other way.
    /// <para>
    /// A price-only amendment cannot change the routed dollars, so they carry over. A quantity
    /// amendment ends the dollar sizing outright: <c>AlpacaBrokerageGateway.ModifyOrderAsync</c>
    /// serializes <c>NewQuantity</c> as <c>qty</c> and sends no notional field, so the replacement
    /// the venue holds is unit-sized. Keeping the old dollars past that point under-reserves rather
    /// than over-reserves — a $2,500 notional order amended to 100 shares of a $100 symbol leaves
    /// the broker holding $10,000 against a $2,500 reserve — so the classification is dropped and
    /// every unit-based rail, the fat-finger quantity ceiling included, applies to the new size.
    /// </para>
    /// </summary>
    private static decimal? ResolveAmendedRoutedNotional(OrderState state, OrderModification modification)
    {
        if (state.RoutedNotional is not { } routedNotional || routedNotional <= 0m)
        {
            return null;
        }

        return modification.NewQuantity is null ? routedNotional : null;
    }

    /// <summary>
    /// Restores the order's sizing classification onto the rebuilt amendment. An amended request is
    /// reconstructed from <see cref="OrderState"/> rather than carried over, so anything the rules
    /// need in order to read <c>Quantity</c> correctly has to be put back explicitly — a price-only
    /// amendment does not change how the venue sizes the order.
    /// <para>
    /// Both classifications matter and they are not the same. Face value says the quantity is par
    /// and the price is a percentage of it; broker-native notional says the quantity field carries
    /// <em>dollars</em> and the gateway discards it. Dropping the latter makes every rule that reads
    /// quantity treat a dollar amount as a share count, so a $2,500 notional order cannot amend its
    /// price under a 1,000-unit fat-finger ceiling — an order the venue would leave sized exactly as
    /// it was, refused for a size it does not have.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildAmendedSizingMetadata(
        OrderState state,
        OrderModification modification)
    {
        var metadata = state.UsesFaceValuePercentageOfPar
            ? new Dictionary<string, string>(OrderSizingMetadata.WithFaceValuePercentageOfPar(metadata: null))
            : null;

        // Dropped for a quantity amendment for the reason ResolveAmendedRoutedNotional gives: the
        // gateway sends the new quantity as qty and no notional field, so the replacement is
        // unit-sized. Carrying the marker would also make the fat-finger quantity limb read the new
        // share count as dollars and skip its ceiling on the one amendment that changed the size.
        if (modification.NewQuantity is not null
            || state.RoutedNotional is not { } routedNotional
            || routedNotional <= 0m)
        {
            return metadata;
        }

        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The canonical key, written as the decimal the reader treats as the notional itself
        // rather than as a boolean over a quantity this request may be amending.
        metadata[BrokerNotionalMetadata.Keys[0]] =
            routedNotional.ToString("G29", CultureInfo.InvariantCulture);
        return metadata;
    }

    /// <summary>
    /// Builds the request the risk rules should evaluate for an amendment. The exposure
    /// snapshot already reserves the working order at its current size, so evaluating the
    /// full amended order would double-count it — raising a $1k working buy to $2k would
    /// project $3k. When both sizes are measurable, this returns a probe carrying only the
    /// incremental value, so snapshot + probe equals the post-amendment book.
    /// <para>
    /// An amendment that adds no exposure stamps an increment of zero, whatever its side: it adds
    /// nothing to a book the snapshot already reserves. This is what keeps unchanged and reducing
    /// amendments routable — projecting the full order on top of the reservation it replaces reads
    /// an unchanged $10,000 sell as $20,000, and a gross ceiling between the two refuses it,
    /// tripping the breaker at Critical severity over an amendment that changed no exposure at all.
    /// </para>
    /// <para>
    /// Two kinds of order prove that differently, so they are decided by different evidence rather
    /// than by one test with exceptions attached.
    /// </para>
    /// <para>
    /// <b>Capped buy</b> — the limit is the price paid, so the measured value <em>is</em> exposure.
    /// The increment is the measured difference, floored at zero.
    /// </para>
    /// <para>
    /// <b>Everything else</b> (any sell, a stop-market trigger, an unpriced order) — the order's own
    /// price is not what it pays, so measured value is not exposure and the two can move in opposite
    /// directions: a sell amended from 10 shares at $100 to 100 at $1 measures downward while real
    /// exposure at a $100 mark grows tenfold. What both sides share is the mark, and exposure is
    /// quantity × mark, so exposure moves with <em>quantity</em> alone. A non-increasing quantity
    /// proves no added exposure however the entered price moved — a 100-share sell repriced $100 to
    /// $110 is the same shares against the same mark, and charging the full amended order for it
    /// reads $21,000 against a $10,000 book. An increasing quantity cannot be valued here at all,
    /// because the increase is Δquantity × mark and the OMS has no mark, so it carries the whole
    /// amended order for the rules to price. That double-counts the existing reservation, but
    /// over-reserving in this one direction can only refuse an amendment, never admit one that
    /// breaches a ceiling.
    /// </para>
    /// </summary>
    private static OrderRequest BuildAmendmentProbe(OrderState state, OrderModification modification)
    {
        var amended = BuildAmendedRequest(state, modification);
        var probeMetadata = amended.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(amended.Metadata, StringComparer.OrdinalIgnoreCase);
        probeMetadata[RiskEscalationQueueService.EvaluationOnlyMetadataKey] = "true";

        var pricePaidIsCapped = state.Side == OrderSide.Buy
            && state.LimitPrice is > 0m
            && amended.LimitPrice is > 0m;

        var currentValue = MeasureOrderValue(
            state.Quantity,
            state.LimitPrice,
            state.StopPrice,
            state.RoutedNotional,
            state.ContractMultiplier,
            state.UsesFaceValuePercentageOfPar);
        var amendedValue = MeasureOrderValue(
            amended.Quantity,
            amended.LimitPrice,
            amended.StopPrice,
            routedNotional: null,
            state.ContractMultiplier,
            state.UsesFaceValuePercentageOfPar);
        if (currentValue is not { } current || amendedValue is not { } proposed)
        {
            // Genuinely unmeasurable on both sides of the comparison: nothing to difference, so the
            // rules price the whole amended order at the mark and over-reserve.
            return amended with { Metadata = probeMetadata };
        }

        // Quantity stays the FULL amended quantity so the position-limit rule projects the
        // real post-amendment position; only the notional-based rules read the incremental
        // value, because the snapshot already reserves the working order's current size.
        //
        // An amendment that does not raise the order's own measured value adds nothing to the book,
        // whatever its side. Falling through to the full amended order projects it *on top of* a
        // snapshot that still reserves the original — an unchanged $10,000 sell reads as $20,000,
        // and a $10,000 buy cut to $9,000 reads as $19,000 — so a gross ceiling between the two
        // refuses the amendment, and at Critical severity trips the breaker, over an amendment that
        // lowered exposure or changed none of it.
        //
        // That over-reservation was harmless while only *increases* were revalidated, because
        // over-reserving an increase can refuse but never admit. Revalidating every supplied price
        // brought unchanged and reducing amendments down the same path, where the same arithmetic
        // blocks the directions a desk most needs to stay open.
        //
        // Which comparison proves "adds no exposure" depends on whether the order's own price is
        // what it pays, so the two cases are decided by different evidence rather than by one test
        // with exceptions bolted on:
        //
        //   Capped buy — the limit IS the price paid, so measured value is exact. The increment is
        //   simply the measured difference, floored at zero for a reduction or an unchanged price.
        //
        //   Everything else (any sell, a stop-market trigger, an unpriced order) — the order's own
        //   price is not what it pays, so measured value is not exposure. What both sides of the
        //   amendment share is the mark, and exposure is quantity x mark, so exposure moves with
        //   QUANTITY alone. A non-increasing quantity therefore proves no added exposure however
        //   the entered price moved: a 100-share sell repriced $100 -> $110 is the same 100 shares
        //   against the same mark, and charging the full amended order for it reads $21,000 against
        //   a $10,000 book. An increasing quantity cannot be valued here at all — the increase is
        //   Δquantity x mark and the OMS has no mark — so it goes to the rules whole.
        if (pricePaidIsCapped)
        {
            var increment = proposed > current ? proposed - current : 0m;
            probeMetadata[RiskEscalationQueueService.IncrementalNotionalMetadataKey] =
                increment.ToString("G29", CultureInfo.InvariantCulture);
            return amended with { Metadata = probeMetadata };
        }

        if (Math.Abs(amended.Quantity) > Math.Abs(state.Quantity))
        {
            // Priced at the mark by the rules, double-counting the working order's reservation.
            // Over-reserving is the conservative answer in this one direction: it can refuse an
            // amendment, never admit one that breaches a ceiling.
            return amended with { Metadata = probeMetadata };
        }

        probeMetadata[RiskEscalationQueueService.IncrementalNotionalMetadataKey] =
            0m.ToString("G29", CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Serializes the whole decision into the rejection audit: the headline names one breach, but
    /// every rule is evaluated before a decision is taken, so a rejection can carry several.
    /// Without these fields a breach behind the headline exists only on the transient
    /// <see cref="OrderResult"/> and never reaches rule status or history.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? BuildRiskDecisionAuditMetadata(
        RiskValidationResult decision)
    {
        if (decision.Violations.Count == 0 && decision.Warnings.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["decisionSource"] = "risk",
            ["decision"] = decision.Decision.ToString(),
        };

        AppendRiskWarningsMetadata(metadata, decision.Warnings);

        if (decision.Violations.Count == 0)
        {
            return metadata;
        }

        // Invariant culture on the numerics: a WAL written under one locale has to parse under
        // another, and these are read back by the status projection.
        metadata["violation.count"] = decision.Violations.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < decision.Violations.Count; i++)
        {
            var violation = decision.Violations[i];
            var prefix = string.Create(CultureInfo.InvariantCulture, $"violation.{i}");
            metadata[$"{prefix}.rule"] = violation.RuleName;
            metadata[$"{prefix}.code"] = violation.Code;
            metadata[$"{prefix}.message"] = violation.Message;
            metadata[$"{prefix}.severity"] = violation.Severity.ToString();

            if (violation.ObservedValue is { } observed)
            {
                metadata[$"{prefix}.observed"] = observed.ToString(CultureInfo.InvariantCulture);
            }

            if (violation.LimitValue is { } limit)
            {
                metadata[$"{prefix}.limit"] = limit.ToString(CultureInfo.InvariantCulture);
            }
        }

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
        public LosslessExecutionReportSubscriber[]? LosslessSubscriberTargets { get; set; }
        public HashSet<long> DeliveredLosslessSubscriberIds { get; } = [];
        public bool LosslessSubscribersPublished { get; set; }
        public bool ExecutionReportPublished { get; set; }
        public volatile bool IsComplete;

        public void ReleaseLosslessSubscriberProgress()
        {
            LosslessSubscriberTargets = null;
            DeliveredLosslessSubscriberIds.Clear();
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
