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

        await WithdrawAllParkedEscalationsAsync(ct).ConfigureAwait(false);

        var openOrders = GetOpenOrders();
        _logger.LogInformation("Cancelling all {Count} open orders", openOrders.Count);

        if (openOrders.Count == 0)
        {
            return KillSwitchSweepResult.Empty;
        }

        // Collected under a lock rather than through Interlocked counters: the failures carry the
        // detail an operator acts on, and a concurrent List.Add corrupts the list rather than
        // merely losing a count.
        var failures = new List<KillSwitchSweepFailure>();
        var cancelled = 0;
        var gate = new Lock();

        await Parallel.ForEachAsync(
            openOrders,
            new ParallelOptions
            {
                CancellationToken = ct,
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
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // The caller gave up on the sweep. This order's fate is genuinely unknown, so
                    // it is reported as still working rather than quietly dropped.
                    failure = new KillSwitchSweepFailure(
                        order.OrderId,
                        order.Symbol,
                        "The cancel-all sweep was cancelled before this order was confirmed.");
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

        var sweep = KillSwitchSweepResult.From(openOrders.Count, cancelled, failures);

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
}
