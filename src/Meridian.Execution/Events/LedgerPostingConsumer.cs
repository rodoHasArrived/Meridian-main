using System.Threading.Channels;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
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
/// The channel is bounded (capacity configurable via constructor) and applies backpressure
/// when full so trade-fill events are never silently discarded.
/// </remarks>
public sealed class LedgerPostingConsumer : ITradeEventPublisher, IAsyncDisposable
{
    private readonly Ledger.Ledger _ledger;
    private readonly Channel<TradeExecutedEvent> _channel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<LedgerPostingConsumer> _logger;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly bool _requireSecurityMasterPostingGate;

    /// <summary>
    /// Initialises a new <see cref="LedgerPostingConsumer"/> bound to <paramref name="ledger"/>.
    /// </summary>
    /// <param name="ledger">The double-entry ledger that journal entries will be posted to.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="channelCapacity">
    ///     Maximum number of un-processed events to buffer before additional publishes block
    ///     until the consumer drains capacity (backpressure).
    ///     Defaults to 10 000.
    /// </param>
    public LedgerPostingConsumer(
        Ledger.Ledger ledger,
        ILogger<LedgerPostingConsumer> logger,
        int channelCapacity = 10_000,
        ISecurityValidationGateService? securityValidationGate = null,
        bool requireSecurityMasterPostingGate = true)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(logger);
        if (channelCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCapacity));

        _ledger = ledger;
        _logger = logger;
        _securityValidationGate = securityValidationGate;
        _requireSecurityMasterPostingGate = requireSecurityMasterPostingGate;

        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<TradeExecutedEvent>(options);
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
    }

    /// <summary>
    /// Enqueues a <see cref="TradeExecutedEvent"/> for asynchronous ledger posting.
    /// Returns immediately while the channel has capacity; when the channel is full the call
    /// blocks until the background consumer frees space, so fills are never dropped.
    /// </summary>
    /// <exception cref="ChannelClosedException">
    ///     The consumer has been disposed and the event could not be enqueued.
    /// </exception>
    public void Publish(TradeExecutedEvent tradeEvent)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);

        // Fast path: capacity available.
        if (_channel.Writer.TryWrite(tradeEvent))
            return;

        // Slow path: channel full. Block the publisher until the consumer drains capacity
        // rather than dropping the fill — a dropped fill silently corrupts the books.
        _logger.LogWarning(
            "LedgerPostingConsumer channel is full; applying backpressure for fill {FillId} on {Symbol}",
            tradeEvent.FillId, tradeEvent.Symbol);

        while (!_channel.Writer.TryWrite(tradeEvent))
        {
            var channelOpen = _channel.Writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult();
            if (!channelOpen)
            {
                throw new ChannelClosedException(
                    $"LedgerPostingConsumer is disposed; fill {tradeEvent.FillId} for {tradeEvent.Symbol} was not enqueued.");
            }
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
                await PostEventAsync(evt, ct).ConfigureAwait(false);
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

    private async Task PostEventAsync(TradeExecutedEvent evt, CancellationToken ct)
    {
        var securityGate = await EvaluateSecurityMasterPostingGateAsync(evt, ct).ConfigureAwait(false);
        if (!securityGate.CanPost)
        {
            _logger.LogError(
                "Blocked ledger posting for fill {FillId} on {Symbol}: {Reason}",
                evt.FillId,
                evt.Symbol,
                securityGate.Reason);
            return;
        }

        var accountId = evt.FinancialAccountId;
        var cashAccount = accountId is null
            ? LedgerAccounts.Cash
            : LedgerAccounts.CashAccount(accountId);
        var metadata = BuildPostingMetadata(evt, securityGate);

        switch (evt.Side)
        {
            case Sdk.OrderSide.Buy:
                PostBuy(evt, cashAccount, accountId, metadata);
                break;

            case Sdk.OrderSide.Sell:
                PostSell(evt, cashAccount, accountId, metadata);
                break;

            default:
                _logger.LogWarning("Unhandled order side {Side} for fill {FillId}", evt.Side, evt.FillId);
                break;
        }

        if (evt.Commission > 0m)
        {
            PostCommission(evt, cashAccount, accountId, metadata);
        }

        _logger.LogDebug(
            "Posted ledger entries for fill {FillId}: {Side} {Quantity} {Symbol} @ {Price}",
            evt.FillId, evt.Side, evt.FilledQuantity, evt.Symbol, evt.FillPrice);
    }

    private async Task<LedgerPostingSecurityGateResult> EvaluateSecurityMasterPostingGateAsync(
        TradeExecutedEvent evt,
        CancellationToken ct)
    {
        if (_securityValidationGate is null)
        {
            return _requireSecurityMasterPostingGate
                ? LedgerPostingSecurityGateResult.Blocked("Security Master validation gate is not configured.")
                : LedgerPostingSecurityGateResult.Allowed(null, "not-configured", []);
        }

        var validation = await _securityValidationGate
            .ValidateSymbolAsync(
                evt.Symbol,
                SecurityValidationWorkflowDto.LedgerPosting,
                workflowReference: evt.FillId.ToString("N"),
                actor: "ledger-posting-consumer",
                persistSnapshot: false,
                ct)
            .ConfigureAwait(false);

        var issueCodes = validation.Report.Issues.Select(static issue => issue.Code).ToArray();
        if (!validation.IsResolved || validation.SecurityId is null)
        {
            return LedgerPostingSecurityGateResult.Blocked(
                $"Security Master identity is unresolved for symbol '{evt.Symbol}'. Issues={string.Join(",", issueCodes)}");
        }

        if (validation.IsBlocked || validation.Report.HasBlockingIssues)
        {
            return LedgerPostingSecurityGateResult.Blocked(
                $"Security Master validation blocked ledger posting. Issues={string.Join(",", issueCodes)}");
        }

        return LedgerPostingSecurityGateResult.Allowed(
            validation.SecurityId,
            validation.Report.Scope,
            issueCodes);
    }

    private static JournalEntryMetadata BuildPostingMetadata(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate)
    {
        Guid? orderId = Guid.TryParse(evt.OrderId, out var parsedOrderId) ? parsedOrderId : null;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["securityMaster.workflow"] = SecurityValidationWorkflowDto.LedgerPosting.ToString(),
            ["securityMaster.scope"] = securityGate.ValidationScope,
            ["securityMaster.gate"] = "resolved-approved-mapped",
            ["securityMaster.issueCodes"] = string.Join(",", securityGate.IssueCodes),
            ["source.orderId"] = evt.OrderId
        };

        return new JournalEntryMetadata(
            ActivityType: "trade-fill",
            Symbol: evt.Symbol,
            SecurityId: securityGate.SecurityId,
            OrderId: orderId,
            FillId: evt.FillId,
            FinancialAccountId: evt.FinancialAccountId,
            Tags: tags);
    }

    private void PostBuy(
        TradeExecutedEvent evt,
        LedgerAccount cashAccount,
        string? accountId,
        JournalEntryMetadata metadata)
    {
        var securitiesAccount = LedgerAccounts.Securities(evt.Symbol, accountId);
        _ledger.PostLines(
            evt.OccurredAt,
            $"Buy {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
            [
                (securitiesAccount, evt.GrossValue, 0m),
                (cashAccount, 0m, evt.GrossValue)
            ],
            metadata);
    }

    private void PostSell(
        TradeExecutedEvent evt,
        LedgerAccount cashAccount,
        string? accountId,
        JournalEntryMetadata metadata)
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
                ],
                metadata);
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
                ],
                metadata);
        }
        else
        {
            _ledger.PostLines(
                evt.OccurredAt,
                $"Sell {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}",
                [
                    (cashAccount, evt.GrossValue, 0m),
                    (securitiesAccount, 0m, evt.GrossValue)
                ],
                metadata);
        }
    }

    private void PostCommission(
        TradeExecutedEvent evt,
        LedgerAccount cashAccount,
        string? accountId,
        JournalEntryMetadata metadata)
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
            ],
            metadata with { ActivityType = "trade-commission" });
    }

    private sealed record LedgerPostingSecurityGateResult(
        bool CanPost,
        Guid? SecurityId,
        string ValidationScope,
        IReadOnlyList<string> IssueCodes,
        string Reason)
    {
        public static LedgerPostingSecurityGateResult Allowed(
            Guid? securityId,
            string validationScope,
            IReadOnlyList<string> issueCodes)
            => new(true, securityId, validationScope, issueCodes, string.Empty);

        public static LedgerPostingSecurityGateResult Blocked(string reason)
            => new(false, null, "SecurityMasterResolution", [], reason);
    }
}
