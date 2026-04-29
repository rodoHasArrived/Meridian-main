using System.Threading.Channels;
using Meridian.Ledger;

namespace Meridian.Execution.Events;

/// <summary>
/// Background consumer that listens to <see cref="TradeExecutedEvent"/> instances and
/// posts corresponding double-entry journal entries to an attached <see cref="Ledger"/>.
/// </summary>
/// <remarks>
/// This class implements the event-driven decoupling pattern described in the architectural
/// enhancement plan. The portfolio layer publishes events; this consumer writes to the ledger
/// asynchronously, removing the synchronous ledger dependency from hot execution paths.
///
/// The channel is bounded (capacity configurable via constructor) and uses
/// <see cref="BoundedChannelFullMode.DropOldest"/> as a safety valve; callers should size
/// the capacity appropriately for expected fill throughput.
/// </remarks>
public sealed class LedgerPostingConsumer : ITradeEventPublisher, IAsyncDisposable
{
    private readonly Ledger.Ledger _ledger;
    private readonly Channel<TradeExecutedEvent> _channel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<LedgerPostingConsumer> _logger;

    /// <summary>
    /// Initialises a new <see cref="LedgerPostingConsumer"/> bound to <paramref name="ledger"/>.
    /// </summary>
    /// <param name="ledger">The double-entry ledger that journal entries will be posted to.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="channelCapacity">
    ///     Maximum number of un-processed events to buffer before oldest events are dropped.
    ///     Defaults to 10 000.
    /// </param>
    public LedgerPostingConsumer(
        Ledger.Ledger ledger,
        ILogger<LedgerPostingConsumer> logger,
        int channelCapacity = 10_000)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(logger);
        if (channelCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCapacity));

        _ledger = ledger;
        _logger = logger;

        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<TradeExecutedEvent>(options);
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
    }

    /// <summary>
    /// Enqueues a <see cref="TradeExecutedEvent"/> for asynchronous ledger posting.
    /// This method returns immediately and never blocks the caller.
    /// </summary>
    public void Publish(TradeExecutedEvent tradeEvent)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);

        if (!_channel.Writer.TryWrite(tradeEvent))
        {
            _logger.LogWarning(
                "LedgerPostingConsumer channel is full; dropping event for fill {FillId} on {Symbol}",
                tradeEvent.FillId, tradeEvent.Symbol);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Complete the channel writer so the background loop exits naturally after
        // processing all queued events.  Cancel only as a hard fallback (5 s timeout).
        _channel.Writer.TryComplete();

        using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _processingTask.WaitAsync(drainTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain timed out — force-cancel the background task.
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            { await _processingTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ProcessAsync(CancellationToken ct)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                PostEvent(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to post ledger entries for fill {FillId} ({Symbol})",
                    evt.FillId, evt.Symbol);
            }
        }
    }

    private void PostEvent(TradeExecutedEvent evt)
    {
        var accountId = evt.FinancialAccountId;
        var cashAccount = accountId is null
            ? LedgerAccounts.Cash
            : LedgerAccounts.CashAccount(accountId);

        switch (evt.Side)
        {
            case Sdk.OrderSide.Buy:
                PostBuy(evt, cashAccount, accountId);
                break;

            case Sdk.OrderSide.Sell:
                PostSell(evt, cashAccount, accountId);
                break;

            default:
                _logger.LogWarning("Unhandled order side {Side} for fill {FillId}", evt.Side, evt.FillId);
                break;
        }

        if (evt.Commission > 0m)
        {
            PostCommission(evt, cashAccount, accountId);
        }

        _logger.LogDebug(
            "Posted ledger entries for fill {FillId}: {Side} {Quantity} {Symbol} @ {Price}",
            evt.FillId, evt.Side, evt.FilledQuantity, evt.Symbol, evt.FillPrice);
    }

    private void PostBuy(TradeExecutedEvent evt, LedgerAccount cashAccount, string? accountId)
    {
        var securitiesAccount = LedgerAccounts.Securities(evt.Symbol, accountId);
        _ledger.PostLines(
            evt.OccurredAt,
            $"Buy {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
            [
                (securitiesAccount, evt.GrossValue, 0m),
                (cashAccount, 0m, evt.GrossValue)
            ]);
    }

    private void PostSell(TradeExecutedEvent evt, LedgerAccount cashAccount, string? accountId)
    {
        var securitiesAccount = LedgerAccounts.Securities(evt.Symbol, accountId);

        if (evt.RealizedPnl > 0m)
        {
            // Gain: proceeds = cost + gain
            // Dr Cash (proceeds), Dr Securities (cost basis, balancing debit is 0 so we reduce the Cr)
            // Cr Securities (cost basis removed), Cr RealizedGain
            var gainAccount = accountId is null
                ? LedgerAccounts.RealizedGain
                : LedgerAccounts.RealizedGainFor(accountId);
            var costBasis = evt.GrossValue - evt.RealizedPnl;
            _ledger.PostLines(
                evt.OccurredAt,
                $"Sell {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
                [
                    (cashAccount, evt.GrossValue, 0m),
                    (securitiesAccount, 0m, costBasis),
                    (gainAccount, 0m, evt.RealizedPnl)
                ]);
        }
        else if (evt.RealizedPnl < 0m)
        {
            var lossAccount = accountId is null
                ? LedgerAccounts.RealizedLoss
                : LedgerAccounts.RealizedLossFor(accountId);
            var costBasis = evt.GrossValue - evt.RealizedPnl; // grossValue + abs(loss)
            _ledger.PostLines(
                evt.OccurredAt,
                $"Sell {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
                [
                    (cashAccount, evt.GrossValue, 0m),
                    (lossAccount, -evt.RealizedPnl, 0m),
                    (securitiesAccount, 0m, costBasis)
                ]);
        }
        else
        {
            _ledger.PostLines(
                evt.OccurredAt,
                $"Sell {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
                [
                    (cashAccount, evt.GrossValue, 0m),
                    (securitiesAccount, 0m, evt.GrossValue)
                ]);
        }
    }

    private void PostCommission(TradeExecutedEvent evt, LedgerAccount cashAccount, string? accountId)
    {
        var commissionAccount = accountId is null
            ? LedgerAccounts.CommissionExpense
            : LedgerAccounts.CommissionExpenseFor(accountId);

        _ledger.PostLines(
            evt.OccurredAt,
            $"Commission on {evt.Symbol} fill {evt.FillId}",
            [
                (commissionAccount, evt.Commission, 0m),
                (cashAccount, 0m, evt.Commission)
            ]);
    }
}
