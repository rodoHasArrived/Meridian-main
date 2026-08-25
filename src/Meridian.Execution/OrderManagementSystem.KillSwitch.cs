using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
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

        // Withdrawal failures are part of the outcome, not a log line. A parked order is absent
        // from the order book below, so an escalation that could not be withdrawn would otherwise
        // leave the sweep reporting an empty book while the escalation stayed releasable -- an
        // order able to route after the halt.
        var failures = new List<KillSwitchSweepFailure>(
            await WithdrawAllParkedEscalationsAsync(sweepToken).ConfigureAwait(false));
        var withdrawalFailures = failures.Count;

        // Not GetOpenOrders(): that excludes PendingCancel, and an order whose earlier cancellation
        // is still unconfirmed can absolutely still fill. Skipping it would let the kill switch
        // report Completed over an order the broker has not agreed to cancel.
        var sweepTargets = _orders.Values
            .Where(static order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .ToList();

        // The in-memory dictionary is a claim about the book, not the book. After an OMS restart,
        // for bracket child legs that were never registered, or for orders placed out of band,
        // the broker can hold working orders this process has never heard of — so the sweep also
        // asks the broker for its own open-order book and cancels whatever the in-memory view
        // does not cover. The snapshot is taken before anything is cancelled so it reflects the
        // pre-sweep book, and deduplication is by order id, so an order present in both views is
        // cancelled exactly once, through the tracked path that owns its state.
        var (brokerResidualOrders, brokerViewError) =
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
                : KillSwitchSweepResult.From(withdrawalFailures, 0, failures);
            return brokerViewError is null
                ? emptySweep
                : emptySweep with { BrokerViewUnavailable = true, BrokerViewError = brokerViewError };
        }

        // Collected under a lock rather than through Interlocked counters: the failures carry the
        // detail an operator acts on, and a concurrent List.Add corrupts the list rather than
        // merely losing a count.
        var cancelled = 0;
        var gate = new Lock();
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
                try
                {
                    var result = await CancelOrderCoreAsync(order.OrderId, token).ConfigureAwait(false);
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
                        }
                    }
                }).ConfigureAwait(false);
        }

        var sweep = KillSwitchSweepResult.From(
            sweepTargets.Count + brokerResidualOrders.Count + withdrawalFailures,
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
    /// Snapshots the broker's own open-order book and returns the orders the in-memory sweep will
    /// not cover, plus the enumeration error when the broker view could not be established.
    /// <para>
    /// An enumeration failure must not abort the in-memory sweep — a broker that cannot list its
    /// book may still accept cancellations — but it is returned rather than swallowed, because
    /// "we could not look" and "nothing was there" are different answers to the only question a
    /// kill switch is asked, and the sweep outcome has to carry which one this was.
    /// </para>
    /// </summary>
    private async Task<(IReadOnlyList<BrokerOrder> ResidualOrders, string? EnumerationError)> SnapshotBrokerResidualOrdersAsync(
        IReadOnlyList<OrderState> sweepTargets,
        CancellationToken ct)
    {
        if (_gateway is not IBrokerageGateway brokerage)
        {
            // The gateway keeps no broker-side book of its own (paper gateways execute
            // in-process), so the in-memory view is the whole book by construction rather than
            // by assumption.
            return (Array.Empty<BrokerOrder>(), null);
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
            return (Array.Empty<BrokerOrder>(), exception.Message);
        }

        if (brokerOpenOrders.Count == 0)
        {
            return (Array.Empty<BrokerOrder>(), null);
        }

        var trackedIds = new HashSet<string>(
            sweepTargets.Select(static order => order.OrderId),
            StringComparer.Ordinal);

        return (brokerOpenOrders
            // Defensive: GetOpenOrdersAsync should already return only working orders, but a
            // terminal row slipping through would make the sweep report a failure over an order
            // that cannot fill.
            .Where(static order => order.Status is OrderStatus.PendingNew
                or OrderStatus.Accepted
                or OrderStatus.PartiallyFilled
                or OrderStatus.PendingCancel)
            .DistinctBy(static order => order.OrderId, StringComparer.Ordinal)
            // Deduped on both ids: the OMS registers orders under the client order id it handed
            // the broker, while some gateways key their book by their own order id. An order the
            // in-memory sweep already targets is cancelled there, once.
            .Where(order => !(order.ClientOrderId is { Length: > 0 } clientOrderId && trackedIds.Contains(clientOrderId))
                && !trackedIds.Contains(order.OrderId))
            .ToList(), null);
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
            report = await _gateway.CancelOrderAsync(order.OrderId, ct).ConfigureAwait(false);
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
