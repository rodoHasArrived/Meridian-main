using Meridian.Execution.Events;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns accounting-publisher acceptance and the independent durable fallback for accepted fills.
/// The caller owns replay startup and shutdown; construction never starts background work.
/// </summary>
internal sealed class AccountingHandoffCoordinator
{
    private readonly ITradeEventPublisher? _tradeEventPublisher;
    private readonly ITradeFillHandoffFailureStore? _tradeFillHandoffFailureStore;
    private readonly ILogger _logger;

    public AccountingHandoffCoordinator(
        ITradeEventPublisher? tradeEventPublisher,
        ITradeFillHandoffFailureStore? tradeFillHandoffFailureStore,
        ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tradeEventPublisher = tradeEventPublisher;
        _tradeFillHandoffFailureStore = tradeFillHandoffFailureStore;
        if (tradeFillHandoffFailureStore is not null
            && tradeEventPublisher is not IScopedTradeEventPublisher)
        {
            throw new ArgumentException(
                "A handoff-failure store requires a scope-bound accounting publisher.",
                nameof(tradeEventPublisher));
        }
        if (tradeEventPublisher is IScopedTradeEventPublisher scopedPublisher
            && tradeFillHandoffFailureStore is not null)
        {
            var publisherScopeIdentity = scopedPublisher.ScopeIdentity.Validate();
            var failureStoreScopeIdentity = tradeFillHandoffFailureStore.ScopeIdentity.Validate();
            var scopeIdentityMatches = publisherScopeIdentity.IsExact && failureStoreScopeIdentity.IsExact
                ? publisherScopeIdentity == failureStoreScopeIdentity
                : !publisherScopeIdentity.IsExact
                  && !failureStoreScopeIdentity.IsExact
                  && string.Equals(
                      publisherScopeIdentity.PostingScope,
                      failureStoreScopeIdentity.PostingScope,
                      StringComparison.Ordinal);
            if (!scopeIdentityMatches)
            {
                throw new ArgumentException(
                    $"Accounting publisher scope identity '{publisherScopeIdentity}' does not match handoff-failure store scope identity '{failureStoreScopeIdentity}'.",
                    nameof(tradeFillHandoffFailureStore));
            }
        }
    }

    public bool HasPublisher => _tradeEventPublisher is not null;

    public bool CanReplay => _tradeEventPublisher is not null && _tradeFillHandoffFailureStore is not null;

    /// <summary>
    /// Awaits durable publisher acceptance. A rejected or ambiguous acceptance is retained when
    /// possible and still reported as a failure; the caller must not advance fill completion.
    /// Accepted broker fills cannot be abandoned because an operation token was cancelled.
    /// </summary>
    public async Task PublishAsync(TradeExecutedEvent tradeEvent)
    {
        if (_tradeEventPublisher is null)
            return;

        try
        {
            await _tradeEventPublisher.PublishAsync(tradeEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var wasRetained = await RetainFailureAsync(tradeEvent, ex).ConfigureAwait(false);
            throw new AccountingHandoffException(tradeEvent, wasRetained, ex);
        }
    }

    public Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> GetPendingAsync(CancellationToken ct = default)
        => _tradeFillHandoffFailureStore is null
            ? Task.FromResult<IReadOnlyList<RetainedTradeFillHandoffFailure>>([])
            : _tradeFillHandoffFailureStore.LoadPendingAsync(ct);

    public async Task ReplayPendingAsync(CancellationToken ct)
    {
        if (_tradeEventPublisher is null || _tradeFillHandoffFailureStore is null)
            return;

        // Retained handoffs are fills whose accounting acceptance was not confirmed. Abandoning the load
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
                // Acceptance and acknowledgment finish even when shutdown cancels replay after
                // this record was selected. Later records remain pending for the next start.
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

    private async Task<bool> RetainFailureAsync(TradeExecutedEvent tradeEvent, Exception publisherFailure)
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
                .RetainAsync(tradeEvent, publisherFailure.Message, CancellationToken.None)
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

/// <summary>An accepted broker fill has not completed its authoritative accounting handoff.</summary>
internal sealed class AccountingHandoffException : Exception
{
    public AccountingHandoffException(
        TradeExecutedEvent tradeEvent,
        bool wasRetained,
        Exception innerException)
        : base(
            $"Execution fill '{tradeEvent.FillId:D}' was accepted by the broker but its accounting handoff failed"
            + (wasRetained
                ? "; the event is durably retained for restart replay."
                : "; no durable fallback accepted the event and the order result is fail-closed."),
            innerException)
    {
        WasRetained = wasRetained;
    }

    public AccountingHandoffException(
        string orderId,
        bool evidenceRetained,
        Exception innerException)
        : base(
            $"Execution fill for order '{orderId}' was accepted by the broker but could not form a safe accounting event"
            + (evidenceRetained
                ? "; durable execution-audit evidence requires operator reconciliation."
                : "; no durable reconciliation evidence was available and the order result is fail-closed."),
            innerException)
    {
        WasRetained = evidenceRetained;
    }

    public bool WasRetained { get; }
}
