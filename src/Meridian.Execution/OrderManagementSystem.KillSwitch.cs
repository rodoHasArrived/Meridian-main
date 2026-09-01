using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

/// <summary>
/// The kill-switch sweep: cancelling the open book and reporting what actually happened to it.
/// <para>
/// Separated from the main partial because the sweep's correctness argument is about
/// <em>evidence</em> rather than about order flow. The exit criterion says activation cancels open
/// orders; a sweep that reports success by completing establishes only that it ran.
/// </para>
/// </summary>
public sealed partial class OrderManagementSystem
{
    /// <inheritdoc />
    public async Task<KillSwitchSweepResult> CancelAllAsync(CancellationToken ct = default)
    {
        using var operation = EnterOperation();

        // The sweep deliberately ignores the caller's token from here on, and the parameter is kept
        // only so the interface reads the same as every other async member. A kill switch is owed
        // to the desk, not to the request that asked for it: threading an HTTP token through meant
        // a browser tab closing mid-sweep stopped scheduling the remaining orders and threw out of
        // the loop, leaving the durable breaker open over a book nobody finished cancelling. The
        // same reasoning the risk validator applies to a confirmed breaker trip -- cancellation
        // must not veto a halt already established.
        ct.ThrowIfCancellationRequested();
        var sweepToken = CancellationToken.None;

        // Before the book is read, let the submissions already past the operator-control gate
        // reach their acknowledgement. A breaker trip and a submission race: PlaceOrderAsync
        // consults the controls, then validates, reserves, and dispatches, and an order that
        // passed the gate a moment before the trip could otherwise land at the broker after
        // this sweep had looked and found nothing. Waiting here -- bounded, and only for the
        // dispatches in flight at this instant -- means the snapshot below sees each of them
        // as either acknowledged (and swept) or rejected at the dispatch recheck.
        var unsettledDispatches = await WaitForInFlightDispatchesToSettleAsync().ConfigureAwait(false);

        // Withdrawal failures are part of the outcome, not a log line. A parked order is absent
        // from the order book below, so an escalation that could not be withdrawn would otherwise
        // leave the sweep reporting an empty book while the escalation stayed releasable -- an
        // order able to route after the halt.
        var failures = new List<KillSwitchSweepFailure>(
            await WithdrawAllParkedEscalationsAsync(sweepToken).ConfigureAwait(false));

        // Not GetOpenOrders(): that excludes PendingCancel, and an order whose earlier cancellation
        // is still unconfirmed can absolutely still fill. Skipping it would let the kill switch
        // report Completed over an order the broker has not agreed to cancel.
        var sweepTargets = _orders.Values
            .Where(static order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .ToList();

        // A submission the settle window did not resolve is working as far as this sweep can
        // tell: its dispatch may reach the broker after every cancellation below has been sent.
        // One already registered in the book is swept like any other order and its cancellation
        // result speaks for it; one not yet visible (or already terminal) is named here, because
        // the alternative is a Completed verdict over an order nobody cancelled.
        foreach (var unsettledOrderId in unsettledDispatches)
        {
            if (sweepTargets.Any(target => string.Equals(target.OrderId, unsettledOrderId, StringComparison.Ordinal)))
            {
                continue;
            }

            _orders.TryGetValue(unsettledOrderId, out var unsettledState);
            if (unsettledState is not null && IsTerminalStatus(unsettledState.Status))
            {
                continue;
            }

            failures.Add(new KillSwitchSweepFailure(
                unsettledOrderId,
                unsettledState?.Symbol,
                "The submission was still awaiting the gateway's acknowledgement when the sweep began; verify it at the broker."));
        }

        var preSweepFailures = failures.Count;

        // The in-memory dictionary is a claim about the book, not the book. After an OMS restart,
        // for bracket child legs that were never registered, or for orders placed out of band,
        // the broker can hold working orders this process has never heard of — so the sweep also
        // asks the broker for its own open-order book and cancels whatever the in-memory view
        // does not cover. The snapshot is taken before anything is cancelled so it reflects the
        // pre-sweep book, and deduplication is by order id, so an order present in both views is
        // cancelled exactly once, through the tracked path that owns its state.
        var (brokerResidualOrders, brokerCancellationIds, brokerViewError) =
            await SnapshotBrokerResidualOrdersAsync(sweepTargets, sweepToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Cancelling all {Count} working order(s), including any already pending cancellation",
            sweepTargets.Count);

        if (brokerResidualOrders.Count > 0)
        {
            _logger.LogWarning(
                "Cancel-all found {BrokerOnlyCount} broker-side open order(s) the in-memory book does not track; cancelling them directly at the gateway",
                brokerResidualOrders.Count);
        }

        if (sweepTargets.Count == 0 && brokerResidualOrders.Count == 0)
        {
            var emptySweep = failures.Count == 0
                ? KillSwitchSweepResult.Empty
                : KillSwitchSweepResult.From(preSweepFailures, 0, failures);
            return brokerViewError is null
                ? emptySweep
                : emptySweep with { BrokerViewUnavailable = true, BrokerViewError = brokerViewError };
        }

        // Collected under a lock rather than through Interlocked counters: the failures carry the
        // detail an operator acts on, and a concurrent List.Add corrupts the list rather than
        // merely losing a count.
        var cancelled = 0;
        var gate = new Lock();
        var confirmedCancellations = new List<ConfirmedCancellation>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = sweepToken,
            MaxDegreeOfParallelism = _options.ValidatedCancelAllMaxConcurrency
        };

        await Parallel.ForEachAsync(
            sweepTargets,
            parallelOptions,
            async (order, token) =>
            {
                // A cancellation that throws is a cancellation that did not happen, and the sweep
                // has to say so per order. Letting it escape would abandon the remaining orders
                // mid-sweep and report the whole kill switch as failed on one broker's fault.
                KillSwitchSweepFailure? failure;
                var gatewayOrderId = brokerCancellationIds.TryGetValue(order.OrderId, out var brokerOrderId)
                    ? brokerOrderId
                    : null;
                try
                {
                    var result = await CancelOrderCoreAsync(order.OrderId, token, gatewayOrderId).ConfigureAwait(false);
                    failure = result.Success
                        ? null
                        : new KillSwitchSweepFailure(
                            order.OrderId,
                            order.Symbol,
                            result.ErrorMessage ?? "The gateway did not confirm the cancellation.");
                }
                catch (Exception exception)
                {
                    failure = new KillSwitchSweepFailure(order.OrderId, order.Symbol, exception.Message);
                }

                lock (gate)
                {
                    if (failure is { } stillWorking)
                    {
                        failures.Add(stillWorking);
                    }
                    else
                    {
                        cancelled++;
                        var confirmedBrokerOrderId = gatewayOrderId;
                        if (string.IsNullOrWhiteSpace(confirmedBrokerOrderId))
                        {
                            _orderBrokerIds.TryGetValue(order.OrderId, out confirmedBrokerOrderId);
                        }
                        confirmedCancellations.Add(new ConfirmedCancellation(
                            order.OrderId,
                            confirmedBrokerOrderId));
                    }
                }
            }).ConfigureAwait(false);

        // The broker-side residue is cancelled directly at the gateway: these orders have no
        // tracked state for CancelOrderCoreAsync to find, and skipping them is exactly the
        // dishonesty this sweep exists to prevent — a Completed verdict over a broker book that
        // still has working orders.
        if (brokerResidualOrders.Count > 0)
        {
            await Parallel.ForEachAsync(
                brokerResidualOrders,
                parallelOptions,
                async (order, token) =>
                {
                    var failure = await CancelBrokerResidualOrderAsync(order, token).ConfigureAwait(false);

                    lock (gate)
                    {
                        if (failure is { } stillWorking)
                        {
                            failures.Add(stillWorking);
                        }
                        else
                        {
                            cancelled++;
                            confirmedCancellations.Add(new ConfirmedCancellation(
                                order.ClientOrderId ?? order.OrderId,
                                order.OrderId));
                        }
                    }
                }).ConfigureAwait(false);
        }

        // A cancellation response is not proof that the broker book converged. Re-enumerate the
        // fully paginated working book after every request has settled: rows that survived (or
        // appeared during the sweep) make Completed impossible, and a failed verification makes
        // the broker view explicitly unavailable.
        var (survivingBrokerOrders, convergenceError) =
            await SnapshotWorkingBrokerOrdersAsync(sweepToken).ConfigureAwait(false);
        if (convergenceError is not null)
        {
            brokerViewError = convergenceError;
        }
        else
        {
            // A successful final enumeration supersedes an earlier transient listing failure: it
            // establishes the broker's current open book, which is the kill switch's exit criterion.
            brokerViewError = null;
            foreach (var survivor in survivingBrokerOrders)
            {
                var confirmedIndex = confirmedCancellations.FindIndex(confirmed =>
                    (confirmed.BrokerOrderId is { Length: > 0 } brokerOrderId
                     && string.Equals(brokerOrderId, survivor.OrderId, StringComparison.Ordinal))
                    || string.Equals(confirmed.LocalOrderId, survivor.ClientOrderId, StringComparison.Ordinal));
                if (confirmedIndex >= 0)
                {
                    confirmedCancellations.RemoveAt(confirmedIndex);
                    cancelled = Math.Max(0, cancelled - 1);
                }

                var failureOrderId = survivor.ClientOrderId is { Length: > 0 } clientOrderId
                    && sweepTargets.Any(target => string.Equals(
                        target.OrderId,
                        clientOrderId,
                        StringComparison.Ordinal))
                        ? clientOrderId
                        : survivor.OrderId;
                if (failures.Any(failure =>
                    string.Equals(failure.OrderId, failureOrderId, StringComparison.Ordinal)
                    || string.Equals(failure.OrderId, survivor.OrderId, StringComparison.Ordinal)
                    || string.Equals(failure.OrderId, survivor.ClientOrderId, StringComparison.Ordinal)))
                {
                    continue;
                }

                failures.Add(new KillSwitchSweepFailure(
                    failureOrderId,
                    survivor.Symbol,
                    $"Broker verification still reports the order as {survivor.Status}."));
            }
        }

        var sweep = KillSwitchSweepResult.From(
            sweepTargets.Count + brokerResidualOrders.Count + preSweepFailures,
            cancelled,
            failures);

        if (brokerViewError is not null)
        {
            sweep = sweep with { BrokerViewUnavailable = true, BrokerViewError = brokerViewError };
        }

        if (sweep.RequiresOperatorAction)
        {
            _logger.LogError(
                "Kill-switch cancel-all did not empty the book: {Cancelled} of {Requested} cancelled, {StillWorking} still working, broker view unavailable: {BrokerViewUnavailable}",
                sweep.Cancelled,
                sweep.Requested,
                sweep.StillWorking.Count,
                sweep.BrokerViewUnavailable);
        }

        return sweep;
    }

    /// <summary>
    /// Waits, bounded by <see cref="OrderManagementSystemOptions.CancelAllInFlightSettleTimeout"/>,
    /// for the submissions in flight at the moment of the call to be acknowledged or rejected, and
    /// returns the order ids of those that were not.
    /// <para>
    /// Only the dispatches present when the wait starts are awaited. Submissions that begin later
    /// are the breaker's concern -- with it open they are refused at the dispatch recheck -- and
    /// under a plain cancel-all with no halt they are legitimately new orders; waiting for them
    /// would let a steady submitter starve the sweep.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> WaitForInFlightDispatchesToSettleAsync()
    {
        var inFlight = _inFlightDispatches.ToArray();
        if (inFlight.Length == 0)
        {
            return [];
        }

        var settleTimeout = _options.ValidatedCancelAllInFlightSettleTimeout;
        _logger.LogInformation(
            "Cancel-all is waiting up to {SettleTimeout} for {InFlightCount} in-flight submission(s) to be acknowledged before sweeping the book",
            settleTimeout,
            inFlight.Length);

        try
        {
            await Task.WhenAll(inFlight.Select(static entry => entry.Value.Task))
                .WaitAsync(settleTimeout)
                .ConfigureAwait(false);
            return [];
        }
        catch (TimeoutException)
        {
            var unsettled = inFlight
                .Where(static entry => !entry.Value.Task.IsCompleted)
                .Select(static entry => entry.Key)
                .ToList();
            _logger.LogWarning(
                "Cancel-all stopped waiting after {SettleTimeout}: {UnsettledCount} submission(s) still await gateway acknowledgement and will be reported as working unless their cancellation is confirmed",
                settleTimeout,
                unsettled.Count);
            return unsettled;
        }
    }

    /// <summary>
    /// Marks one submission as in flight between registration and gateway acknowledgement, for the
    /// kill-switch sweep to wait on. Disposal settles the lease on every exit path of
    /// <see cref="PlaceOrderAsync"/>, including rejection and gateway failure.
    /// </summary>
    private sealed class DispatchLease(OrderManagementSystem owner) : IDisposable
    {
        private string? _orderId;
        private TaskCompletionSource? _settled;

        public void Begin(string orderId)
        {
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            while (true)
            {
                if (owner._inFlightDispatches.TryAdd(orderId, settled))
                {
                    _orderId = orderId;
                    _settled = settled;
                    return;
                }

                if (!owner._inFlightDispatches.TryGetValue(orderId, out var existing))
                {
                    continue;
                }

                if (!existing.Task.IsCompleted)
                {
                    // Another attempt is in flight under this client order id. It owns the
                    // lease and the sweep is already waiting on it; TryRegisterOrder will
                    // reject this attempt as a duplicate. Overwriting here would let this
                    // attempt's disposal strip the winner's lease and hide a live broker
                    // submission from the kill switch.
                    return;
                }

                // A settled lease still under the id is a leak from a terminal order whose id
                // is being reused; clear it so the new submission is visible to the sweep.
                owner._inFlightDispatches.TryRemove(new KeyValuePair<string, TaskCompletionSource>(orderId, existing));
            }
        }

        public void Dispose()
        {
            if (_orderId is null || _settled is null)
            {
                return;
            }

            owner._inFlightDispatches.TryRemove(new KeyValuePair<string, TaskCompletionSource>(_orderId, _settled));
            _settled.TrySetResult();
        }
    }

    /// <summary>
    /// Rejects an order that is already registered in the tracked table but has not been sent to
    /// the gateway, because the operator controls closed between the gate and dispatch.
    /// <para>
    /// Unlike <see cref="RejectOrderAsync"/>, which runs before registration and must not disturb
    /// an entry that belongs to another order, this path owns the entry under
    /// <paramref name="orderId"/> and replaces its <c>PendingNew</c> state with the rejection --
    /// the same terminal transition the gateway-failure path applies -- so a sweep or a status
    /// read never sees an order that was never routed reported as working.
    /// </para>
    /// </summary>
    private async Task<OrderResult> RejectRegisteredOrderBeforeDispatchAsync(
        string orderId,
        OrderState registeredState,
        OrderRequest request,
        string? actor,
        string brokerName,
        string? runId,
        string? correlationId,
        ExecutionControlDecision decision,
        string? sessionId,
        IReadOnlyList<string>? riskWarnings,
        RiskValidationResult? riskDecision,
        CancellationToken ct)
    {
        var rejectedState = registeredState with
        {
            Status = OrderStatus.Rejected,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        _orders[orderId] = rejectedState;
        TrimRetainedOrdersIfNeeded();

        await RecordSessionOrderUpdateAsync(sessionId, rejectedState, ct).ConfigureAwait(false);
        await RecordOrderRejectionAsync(
            orderId,
            request,
            actor,
            brokerName,
            runId,
            correlationId,
            decision.RejectReason,
            ct,
            rejectionSource: "operator controls at dispatch",
            decision.RejectCode ?? "OPERATOR_CONTROL_REJECTED",
            BuildOrderRejectedByControlAuditMetadata(decision)).ConfigureAwait(false);

        return new OrderResult
        {
            Success = false,
            OrderId = orderId,
            ErrorMessage = decision.RejectReason,
            OrderState = rejectedState,
            RiskWarnings = riskWarnings,
            RiskDecision = riskDecision?.ToSummary()
        };
    }

    /// <summary>
    /// Snapshots the broker's own open-order book and returns the orders the in-memory sweep will
    /// not cover, the broker-assigned cancellation ids for tracked orders, plus the enumeration
    /// error when the broker view could not be established.
    /// <para>
    /// An enumeration failure must not abort the in-memory sweep — a broker that cannot list its
    /// book may still accept cancellations — but it is returned rather than swallowed, because
    /// "we could not look" and "nothing was there" are different answers to the only question a
    /// kill switch is asked, and the sweep outcome has to carry which one this was.
    /// </para>
    /// </summary>
    private async Task<(
        IReadOnlyList<BrokerOrder> ResidualOrders,
        IReadOnlyDictionary<string, string> BrokerCancellationIds,
        string? EnumerationError)> SnapshotBrokerResidualOrdersAsync(
        IReadOnlyList<OrderState> sweepTargets,
        CancellationToken ct)
    {
        if (_gateway is not IBrokerageGateway brokerage)
        {
            // The gateway keeps no broker-side book of its own (paper gateways execute
            // in-process), so the in-memory view is the whole book by construction rather than
            // by assumption.
            return (Array.Empty<BrokerOrder>(), new Dictionary<string, string>(StringComparer.Ordinal), null);
        }

        IReadOnlyList<BrokerOrder> brokerOpenOrders;
        try
        {
            brokerOpenOrders = await brokerage.GetOpenOrdersAsync(ct).ConfigureAwait(false)
                ?? Array.Empty<BrokerOrder>();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cancel-all could not enumerate the broker's open orders; the sweep covers only the in-memory book");
            return (
                Array.Empty<BrokerOrder>(),
                new Dictionary<string, string>(StringComparer.Ordinal),
                exception.Message);
        }

        if (brokerOpenOrders.Count == 0)
        {
            return (Array.Empty<BrokerOrder>(), new Dictionary<string, string>(StringComparer.Ordinal), null);
        }

        var trackedIds = new HashSet<string>(
            sweepTargets.Select(static order => order.OrderId),
            StringComparer.Ordinal);
        var trackedByBrokerId = sweepTargets
            .Select(order => _orderBrokerIds.TryGetValue(order.OrderId, out var brokerOrderId)
                ? (order.OrderId, BrokerOrderId: brokerOrderId)
                : (order.OrderId, BrokerOrderId: (string?)null))
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.BrokerOrderId))
            .GroupBy(static mapping => mapping.BrokerOrderId!, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single().OrderId,
                StringComparer.Ordinal);

        var activeBrokerOrders = brokerOpenOrders
            // Defensive: GetOpenOrdersAsync should already return only working orders, but a
            // terminal row slipping through would make the sweep report a failure over an order
            // that cannot fill.
            .Where(static order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
            .ToList();

        var residualOrders = new List<BrokerOrder>();
        var brokerCancellationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var brokerOrder in activeBrokerOrders)
        {
            // The OMS keys tracked state by the client id it submitted, while Alpaca's DELETE
            // endpoint accepts only the broker UUID. Keep the broker row out of the residual
            // sweep, but carry its broker-assigned id into the tracked cancellation path instead
            // of silently falling back to the client id that matched it.
            var trackedOrderId = brokerOrder.ClientOrderId is { Length: > 0 } clientOrderId
                && trackedIds.Contains(clientOrderId)
                    ? clientOrderId
                    : trackedByBrokerId.TryGetValue(brokerOrder.OrderId, out var brokerMappedOrderId)
                        ? brokerMappedOrderId
                        : null;

            if (trackedOrderId is null)
            {
                residualOrders.Add(brokerOrder);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(brokerOrder.OrderId))
            {
                brokerCancellationIds.TryAdd(trackedOrderId, brokerOrder.OrderId);
            }
        }

        return (residualOrders, brokerCancellationIds, null);
    }

    /// <summary>
    /// Cancels one broker-known order the in-memory book does not track, returning the failure to
    /// report when the cancellation did not take. Goes straight to the gateway because there is no
    /// tracked <see cref="OrderState"/> for the ordinary cancel path to update.
    /// </summary>
    private async Task<KillSwitchSweepFailure?> CancelBrokerResidualOrderAsync(BrokerOrder order, CancellationToken ct)
    {
        KillSwitchSweepFailure? failure;
        ExecutionReport? report = null;
        try
        {
            report = await CancelAtGatewayAsync(
                new OrderCancellationIdentifier(
                    order.OrderId,
                    OrderCancellationIdentifierKind.BrokerOrderId),
                ct).ConfigureAwait(false);
            failure = report.OrderStatus is OrderStatus.Cancelled
                ? null
                : new KillSwitchSweepFailure(
                    order.OrderId,
                    order.Symbol,
                    report.RejectReason ?? "The gateway did not confirm the cancellation.");
        }
        catch (Exception exception)
        {
            failure = new KillSwitchSweepFailure(order.OrderId, order.Symbol, exception.Message);
        }

        // Evidence, not control flow: an audit write that throws must not convert a confirmed
        // broker cancellation into a reported failure, or abandon the rest of the sweep.
        try
        {
            await RecordOrderLifecycleAuditAsync(
                action: failure is null ? "OrderCancelled" : "OrderCancelRejected",
                outcome: failure is null ? nameof(OrderStatus.Cancelled) : "Rejected",
                orderId: order.OrderId,
                state: null,
                report: report,
                message: failure is null
                    ? "Cancel-all cancelled a broker-side order the in-memory book did not track."
                    : failure.Value.Reason,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception auditException)
        {
            _logger.LogError(
                auditException,
                "Cancel-all could not audit the broker-side cancellation outcome for order {OrderId}",
                LogSanitizer.Sanitize(order.OrderId));
        }

        return failure;
    }

    private async Task<(IReadOnlyList<BrokerOrder> WorkingOrders, string? EnumerationError)>
        SnapshotWorkingBrokerOrdersAsync(CancellationToken ct)
    {
        if (_gateway is not IBrokerageGateway brokerage)
        {
            return (Array.Empty<BrokerOrder>(), null);
        }

        try
        {
            var brokerOrders = await brokerage.GetOpenOrdersAsync(ct).ConfigureAwait(false)
                ?? Array.Empty<BrokerOrder>();
            return (
                brokerOrders
                    .Where(static order => order.Status is OrderStatus.PendingNew
                        or OrderStatus.Accepted
                        or OrderStatus.PartiallyFilled
                        or OrderStatus.PendingCancel)
                    .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
                    .ToList(),
                null);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cancel-all could not verify the broker's open-order book after cancellation");
            return (Array.Empty<BrokerOrder>(), exception.Message);
        }
    }

    private readonly record struct ConfirmedCancellation(string LocalOrderId, string? BrokerOrderId);

    /// <summary>
    /// Withdraws every parked governed escalation, returning the ones that could not be withdrawn.
    /// <para>
    /// A parked order holds no broker order, so it never appears in the swept book — but its
    /// escalation can still be released, which would route an order after the halt. The failures
    /// are returned rather than logged so the sweep's outcome can carry them.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<KillSwitchSweepFailure>> WithdrawAllParkedEscalationsAsync(CancellationToken ct)
    {
        var parkedOrderIds = _parkedOrderIds.Keys.ToArray();
        if (parkedOrderIds.Length == 0)
        {
            return [];
        }

        _logger.LogInformation(
            "Cancel-all is withdrawing {ParkedCount} parked escalation(s)",
            parkedOrderIds.Length);

        List<KillSwitchSweepFailure>? failures = null;
        foreach (var parkedOrderId in parkedOrderIds)
        {
            OrderResult? withdrawn;
            try
            {
                withdrawn = await TryCancelParkedOrderAsync(parkedOrderId, ct).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(new KillSwitchSweepFailure(
                    parkedOrderId,
                    _orders.TryGetValue(parkedOrderId, out var faulted) ? faulted.Symbol : null,
                    $"The governed escalation could not be withdrawn: {exception.Message}"));
                continue;
            }

            if (withdrawn is null or { Success: false })
            {
                _logger.LogError(
                    "Cancel-all could not withdraw the governed escalation holding order {OrderId}; it remains releasable",
                    LogSanitizer.Sanitize(parkedOrderId));

                (failures ??= []).Add(new KillSwitchSweepFailure(
                    parkedOrderId,
                    _orders.TryGetValue(parkedOrderId, out var parked) ? parked.Symbol : null,
                    withdrawn?.ErrorMessage
                        ?? "The governed escalation remains releasable and can still route this order."));
            }
        }

        return failures ?? (IReadOnlyList<KillSwitchSweepFailure>)[];
    }

    /// <summary>
    /// Absolute quantity already working in orders that would reduce this symbol's position for
    /// this fund account.
    /// <para>
    /// Feeds the close-only exception's admission arithmetic, so it counts what is <em>committed</em>
    /// rather than what has settled: an order resting at the broker will reduce the position
    /// whether or not it has filled yet, and ignoring it lets two closes that each fit the position
    /// together cross through it.
    /// </para>
    /// </summary>
    private decimal ResolveWorkingReductionQuantity(string symbol, Guid? fundAccountId)
    {
        if (_portfolioState?.Positions.TryGetValue(symbol, out var position) != true)
        {
            return 0m;
        }

        // The same fund-owned quantity the admission check measures, not the netted aggregate. A
        // fund can hold the opposite sign to the book: with fund A long 100 and fund B short 10 the
        // aggregate is long 90, so taking the side from it would look for B's reductions among
        // sells while B reduces by buying — leaving its working buy-to-close uncounted and letting
        // a second buy cross B through flat into a long.
        var held = Services.ExecutionOperatorControlService.ResolveOwnedQuantity(position!, fundAccountId);
        if (held == 0m)
        {
            return 0m;
        }

        var reducingSide = held > 0m ? OrderSide.Sell : OrderSide.Buy;

        return _orders.Values
            .Where(order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .Where(order => string.Equals(order.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Where(order => order.Side == reducingSide)
            .Where(order => fundAccountId is null || order.FundAccountId == fundAccountId)
            // Remaining, not requested: a partially filled close has already moved the
            // position by its filled part, and that part is in the settled quantity above.
            // Counting the whole order would double-count the fill and refuse a legitimate
            // close of what is left.
            .Sum(order => Math.Abs(order.Quantity - order.FilledQuantity));
    }
}
