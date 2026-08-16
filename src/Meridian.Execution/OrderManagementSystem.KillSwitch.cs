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

        _logger.LogInformation(
            "Cancelling all {Count} working order(s), including any already pending cancellation",
            sweepTargets.Count);

        if (sweepTargets.Count == 0)
        {
            return failures.Count == 0
                ? KillSwitchSweepResult.Empty
                : KillSwitchSweepResult.From(withdrawalFailures, 0, failures);
        }

        // Collected under a lock rather than through Interlocked counters: the failures carry the
        // detail an operator acts on, and a concurrent List.Add corrupts the list rather than
        // merely losing a count.
        var cancelled = 0;
        var gate = new Lock();

        await Parallel.ForEachAsync(
            sweepTargets,
            new ParallelOptions
            {
                CancellationToken = sweepToken,
                MaxDegreeOfParallelism = _options.ValidatedCancelAllMaxConcurrency
            },
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

        var sweep = KillSwitchSweepResult.From(
            sweepTargets.Count + withdrawalFailures,
            cancelled,
            failures);

        if (sweep.RequiresOperatorAction)
        {
            _logger.LogError(
                "Kill-switch cancel-all did not empty the book: {Cancelled} of {Requested} cancelled, {StillWorking} still working",
                sweep.Cancelled,
                sweep.Requested,
                sweep.StillWorking.Count);
        }

        return sweep;
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
        var held = 0m;
        if (_portfolioState?.Positions.TryGetValue(symbol, out var position) == true)
        {
            held = position.ExactQuantity;
        }

        if (held == 0m)
        {
            return 0m;
        }

        // The side that moves this position toward flat. Orders on the other side increase it and
        // are not reductions to be counted against the closable quantity.
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
