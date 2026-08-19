using Meridian.Execution.Events;

namespace Meridian.Execution;

/// <summary>
/// Accounting handoff for executed fills: replaying the durable backlog of fills the
/// accounting layer never accepted, and retaining new handoff failures so the order path can
/// fail closed on them. Split out of the main partial so the delivery-boundary logic has a
/// dedicated home rather than growing the order-lifecycle file.
/// </summary>
public sealed partial class OrderManagementSystem
{
    private async Task ReplayRetainedAccountingHandoffsAsync(CancellationToken ct)
    {
        if (_tradeEventPublisher is null || _tradeFillHandoffFailureStore is null)
            return;

        // Retained handoffs are fills the accounting layer never accepted. Abandoning the load
        // on the first error would leave them undelivered with nothing scheduled to retry, and
        // the OMS would keep trading as though the accounting backlog were empty. Retry with
        // backoff so an unavailable store delays replay rather than cancelling it.
        IReadOnlyList<RetainedTradeFillHandoffFailure> retained;
        var loadAttempt = 0;
        while (true)
        {
            try
            {
                retained = await _tradeFillHandoffFailureStore.LoadPendingAsync(ct).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                loadAttempt++;
                _logger.LogCritical(
                    ex,
                    "Could not load retained accounting handoff failures (attempt {Attempt}); retrying — " +
                    "retained fills stay undelivered until this succeeds",
                    loadAttempt);

                // 1s, 2s, 4s ... capped at 30s: fast enough that a transient store blip barely
                // delays replay, bounded so a durable outage does not spin.
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(loadAttempt - 1, 5))));
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        foreach (var failure in retained)
        {
            if (ct.IsCancellationRequested)
                return;
            try
            {
                // Awaited for the same reason as the live fill path: replay runs on a pool
                // thread and acceptance can wait for the posting consumer to free capacity.
                await _tradeEventPublisher.PublishAsync(failure.TradeEvent).ConfigureAwait(false);
                await _tradeFillHandoffFailureStore
                    .MarkReplayedAsync(failure.TradeEvent.FillId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(
                    ex,
                    "Retained accounting handoff replay failed for fill {FillId}; the durable failure record remains pending",
                    failure.TradeEvent.FillId);
                try
                {
                    await _tradeFillHandoffFailureStore
                        .RetainAsync(failure.TradeEvent, ex.Message, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception retentionException)
                {
                    _logger.LogCritical(
                        retentionException,
                        "Could not update retained accounting handoff failure for fill {FillId}",
                        failure.TradeEvent.FillId);
                }
            }
        }
    }

    private async Task<bool> RetainAccountingHandoffFailureAsync(
        TradeExecutedEvent tradeEvent,
        Exception publisherFailure,
        CancellationToken ct)
    {
        if (_tradeFillHandoffFailureStore is null)
        {
            _logger.LogCritical(
                publisherFailure,
                "Accounting publisher rejected fill {FillId} and no durable OMS handoff-failure store is configured",
                tradeEvent.FillId);
            return false;
        }

        try
        {
            await _tradeFillHandoffFailureStore
                .RetainAsync(tradeEvent, publisherFailure.Message, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception retentionFailure)
        {
            _logger.LogCritical(
                retentionFailure,
                "Accounting publisher and OMS failure-store retention both failed for fill {FillId}; the order path will fail closed",
                tradeEvent.FillId);
            return false;
        }
    }
}
