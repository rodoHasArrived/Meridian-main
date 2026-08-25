using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

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
public sealed class LedgerPostingConsumer : IScopedTradeEventPublisher, IAsyncDisposable
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCancellationTimeout = TimeSpan.FromSeconds(1);

    private const string TradeFillActivityType = "trade-fill";

    // Opt-in trade-identity tags the statement-reconciliation projection
    // (LedgerJournalInternalTransactionSource) reads to exact-match a posted trade journal
    // against a custodian statement row through the matcher's FITID rule.
    private const string ReconciliationQuantityTag = "quantity";
    private const string ReconciliationExternalTransactionIdTag = "externalTransactionId";

    private readonly ITradeFillLedgerPostingTarget _postingTarget;
    private readonly Ledger.Ledger? _projectionLedger;
    private readonly Channel<PendingTradeFillPosting> _channel;
    private readonly ITradeFillPostingStore _postingStore;
    private readonly TradeFillLedgerPostingContext _postingContext;
    private readonly string _postingScope;
    private readonly TradeFillPostingScopeIdentity _scopeIdentity;
    private readonly TaskCompletionSource _recoveryLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<LedgerPostingConsumer> _logger;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly bool _requireSecurityMasterPostingGate;
    private readonly TimeSpan _drainTimeout;
    private readonly TimeSpan _cancellationTimeout;
    private readonly object _disposeSync = new();
    private readonly object _postingBoundarySync = new();
    private Task? _disposeTask;
    private bool _postingDisabled;
    private int _disposeStarted;
    private int _cancellationSourceDisposed;

    // Set when the posting loop stops for any reason other than shutdown. Nothing drains the
    // channel after that, so acceptance must fail closed instead of waiting on capacity that
    // will never free — a blocked publisher is silent: the ledger simply stops receiving
    // postings while trading continues.
    private Exception? _processingFailure;

    internal Task ProcessingCompletion => _processingTask;

    /// <inheritdoc />
    public string PostingScope => _postingScope;

    /// <inheritdoc />
    public TradeFillPostingScopeIdentity ScopeIdentity => _scopeIdentity;

    /// <summary>
    /// Initialises a new <see cref="LedgerPostingConsumer"/> bound to one durable accounting scope.
    /// </summary>
    /// <param name="postingTarget">Caller-owned durable posting target for the authoritative journal store.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="postingStore">Durable accepted-fill handoff owned by the same accounting scope.</param>
    /// <param name="postingContext">Exact ledger aggregate, book, and period scope represented by the store.</param>
    /// <param name="projectionLedger">Optional rebuildable in-memory projection updated after durable persistence.</param>
    /// <param name="channelCapacity">
    ///     Maximum number of un-processed events to buffer before additional publishes block
    ///     until the consumer drains capacity (backpressure).
    ///     Defaults to 10 000.
    /// </param>
    /// <param name="drainTimeout">
    ///     Maximum time to drain queued fills during disposal before cancellation begins.
    ///     Defaults to five seconds.
    /// </param>
    /// <param name="cancellationTimeout">
    ///     Maximum time allowed for each cancellation shutdown phase before disposal returns and
    ///     observes the non-cooperative worker through deferred cleanup. Defaults to one second.
    /// </param>
    public LedgerPostingConsumer(
        ITradeFillLedgerPostingTarget postingTarget,
        ILogger<LedgerPostingConsumer> logger,
        ITradeFillPostingStore postingStore,
        TradeFillLedgerPostingContext postingContext,
        Ledger.Ledger? projectionLedger = null,
        int channelCapacity = 10_000,
        ISecurityValidationGateService? securityValidationGate = null,
        bool requireSecurityMasterPostingGate = true,
        TimeSpan? drainTimeout = null,
        TimeSpan? cancellationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(postingTarget);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(postingStore);
        ArgumentNullException.ThrowIfNull(postingContext);
        if (channelCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCapacity));
        postingContext = postingContext.Validate();
        var expectedScopeIdentity = TradeFillPostingScopeIdentity.FromContext(postingContext);
        var postingStoreScopeIdentity = postingStore.ScopeIdentity.Validate();
        if (!string.Equals(
                postingStoreScopeIdentity.PostingScope,
                postingStore.PostingScope,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Posting store scope identity label does not match its configured posting scope.",
                nameof(postingStore));
        }
        if (!string.Equals(postingStore.PostingScope, postingContext.PostingScope, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Posting store scope '{postingStore.PostingScope}' does not match ledger scope '{postingContext.PostingScope}'.",
                nameof(postingContext));
        }
        if (postingStoreScopeIdentity.IsExact && postingStoreScopeIdentity != expectedScopeIdentity)
        {
            throw new ArgumentException(
                $"Posting store scope identity '{postingStoreScopeIdentity}' does not match ledger scope identity '{expectedScopeIdentity}'.",
                nameof(postingContext));
        }

        _postingTarget = postingTarget;
        _projectionLedger = projectionLedger;
        _logger = logger;
        _postingStore = postingStore;
        _postingContext = postingContext;
        _postingScope = postingContext.PostingScope;
        _scopeIdentity = postingStoreScopeIdentity.IsExact
            ? expectedScopeIdentity
            : postingStoreScopeIdentity;
        _securityValidationGate = securityValidationGate;
        _requireSecurityMasterPostingGate = requireSecurityMasterPostingGate;
        _drainTimeout = RequirePositiveTimeout(drainTimeout, DefaultDrainTimeout, nameof(drainTimeout));
        _cancellationTimeout = RequirePositiveTimeout(
            cancellationTimeout,
            DefaultCancellationTimeout,
            nameof(cancellationTimeout));

        var options = new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<PendingTradeFillPosting>(options);
        _processingTask = Task.Run(() => ProcessAsync(_cts.Token));
    }

    /// <summary>
    /// Durably accepts a <see cref="TradeExecutedEvent"/> for asynchronous ledger posting.
    /// Returning means the fill can replay after restart. While the channel has capacity the
    /// call returns after the WAL append; when full it blocks until the consumer frees space.
    /// </summary>
    /// <exception cref="ChannelClosedException">
    ///     Disposal prevented acceptance, or the fill was durably accepted but the live channel
    ///     closed before enqueue; in the latter case the exception message confirms restart replay.
    /// </exception>
    public void Publish(TradeExecutedEvent tradeEvent)
        // Sync bridge for legacy publishers; the fill-processing path awaits PublishAsync so
        // storage backpressure never blocks a thread there.
        => PublishAsync(tradeEvent).GetAwaiter().GetResult();

    /// <summary>
    /// Durably accepts a <see cref="TradeExecutedEvent"/> for asynchronous ledger posting.
    /// Returning means the fill can replay after restart. While the channel has capacity the
    /// call returns after the WAL append; when full it awaits until the consumer frees space.
    /// Intentionally not cancellable: once called, the fill must reach the durable store.
    /// </summary>
    /// <exception cref="ChannelClosedException">
    ///     Disposal prevented acceptance, or the fill was durably accepted but the live channel
    ///     closed before enqueue; in the latter case the exception message confirms restart replay.
    /// </exception>
    public async Task PublishAsync(TradeExecutedEvent tradeEvent)
    {
        ArgumentNullException.ThrowIfNull(tradeEvent);
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ChannelClosedException(
                $"LedgerPostingConsumer is disposed; fill {tradeEvent.FillId} for {tradeEvent.Symbol} was not accepted.");
        }

        // Establish a strict cut between restart replay and live acceptance so a fill cannot
        // appear in both the recovered snapshot and the channel during consumer startup.
        await _recoveryLoaded.Task.ConfigureAwait(false);
        if (Volatile.Read(ref _disposeStarted) != 0)
        {
            throw new ChannelClosedException(
                $"LedgerPostingConsumer is disposed; fill {tradeEvent.FillId} for {tradeEvent.Symbol} was not accepted.");
        }

        // A stopped posting loop means nothing will drain what is accepted here, so refuse
        // before taking the fill rather than stranding it behind a reader that is gone.
        ThrowIfProcessingFailed(tradeEvent);

        // The publisher contract intentionally applies storage backpressure here: returning
        // means the executed fill is durably replayable even if this process stops.
        var acceptance = await _postingStore
            .AcceptAsync(tradeEvent, CancellationToken.None)
            .ConfigureAwait(false);
        if (!acceptance.ShouldEnqueue)
            return;

        var posting = acceptance.Posting
            ?? throw new InvalidOperationException("A newly accepted trade fill is missing its durable posting envelope.");

        // Fast path: capacity available.
        if (_channel.Writer.TryWrite(posting))
            return;

        // Slow path: channel full. Hold the publisher until the consumer drains capacity
        // rather than dropping the fill — a dropped fill silently corrupts the books.
        _logger.LogWarning(
            "LedgerPostingConsumer channel is full; applying backpressure for fill {FillId} on {Symbol}",
            tradeEvent.FillId, tradeEvent.Symbol);

        while (!_channel.Writer.TryWrite(posting))
        {
            // Re-checked every iteration: the reader can stop while this publisher is already
            // waiting, and capacity would then never free. The fill is durable either way, so
            // surfacing the stall beats blocking here indefinitely.
            ThrowIfProcessingFailed(tradeEvent);

            var channelOpen = await _channel.Writer.WaitToWriteAsync().ConfigureAwait(false);
            if (!channelOpen)
            {
                throw new ChannelClosedException(
                    $"LedgerPostingConsumer is disposed; durable fill {tradeEvent.FillId} for {tradeEvent.Symbol} will replay on restart.");
            }
        }
    }

    /// <summary>
    /// Stops accepting fills, gives accepted fills one bounded drain window, and then applies a
    /// bounded cancellation fallback. Repeated calls await the same shutdown operation. Once the
    /// method returns, no in-flight validation can cross the posting boundary into the ledger.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            Interlocked.Exchange(ref _disposeStarted, 1);
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Complete the writer first: blocked and future publishers now fail deterministically,
        // while the reader gets one bounded opportunity to drain fills already accepted.
        _channel.Writer.TryComplete();

        if (await WaitForProcessingAsync(_drainTimeout).ConfigureAwait(false))
        {
            ClosePostingBoundary();
            DisposeCancellationSource();
            return;
        }

        // Prevent a non-cooperative in-flight dependency from returning after disposal and
        // mutating the ledger. The guarded posting section is synchronous and contains no I/O.
        ClosePostingBoundary();
        var pendingEventCount = _channel.Reader.CanCount ? _channel.Reader.Count : -1;
        _logger.LogWarning(
            "LedgerPostingConsumer did not drain within {DrainTimeout}; cancelling the worker with {PendingEventCount} accepted fills still queued",
            _drainTimeout,
            pendingEventCount);

        var cancellationTask = _cts.CancelAsync();
        if (!await WaitForTaskAsync(cancellationTask, _cancellationTimeout).ConfigureAwait(false))
        {
            _logger.LogError(
                "LedgerPostingConsumer cancellation callbacks did not finish within {CancellationTimeout}; deferred cleanup will observe completion",
                _cancellationTimeout);
            ObserveDeferredShutdown(cancellationTask);
            return;
        }

        if (!await WaitForProcessingAsync(_cancellationTimeout).ConfigureAwait(false))
        {
            _logger.LogError(
                "LedgerPostingConsumer worker did not stop within {CancellationTimeout} after cancellation; deferred cleanup will observe completion",
                _cancellationTimeout);
            ObserveDeferredShutdown(Task.CompletedTask);
            return;
        }

        DisposeCancellationSource();
    }

    private async Task<bool> WaitForProcessingAsync(TimeSpan timeout)
    {
        try
        {
            await _processingTask.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return true;
        }
    }

    private static async Task<bool> WaitForTaskAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void ClosePostingBoundary()
    {
        lock (_postingBoundarySync)
        {
            _postingDisabled = true;
        }
    }

    private void ObserveDeferredShutdown(Task cancellationTask)
        => _ = ObserveDeferredShutdownAsync(cancellationTask);

    private async Task ObserveDeferredShutdownAsync(Task cancellationTask)
    {
        try
        {
            await cancellationTask.ConfigureAwait(false);
            await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Expected after the bounded drain window expires.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LedgerPostingConsumer deferred shutdown failed");
        }
        finally
        {
            DisposeCancellationSource();
        }
    }

    private void DisposeCancellationSource()
    {
        if (Interlocked.Exchange(ref _cancellationSourceDisposed, 1) == 0)
        {
            _cts.Dispose();
        }
    }

    private static TimeSpan RequirePositiveTimeout(
        TimeSpan? configured,
        TimeSpan fallback,
        string parameterName)
    {
        var timeout = configured ?? fallback;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Shutdown timeout must be positive and finite.");
        }

        return timeout;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            var recovered = await _postingStore.LoadPendingAsync(ct).ConfigureAwait(false);
            _recoveryLoaded.TrySetResult();
            foreach (var posting in recovered)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessPostingAsync(posting, ct).ConfigureAwait(false);
            }

            await foreach (var posting in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await ProcessPostingAsync(posting, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Publish the fault before completing the recovery gate: a publisher released by
            // that gate must observe the failure rather than proceed into a channel that no
            // longer has a reader.
            if (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Volatile.Write(ref _processingFailure, ex);

                // Completing the writer wakes publishers already parked in WaitToWriteAsync;
                // setting the field alone would leave them waiting on capacity that no reader
                // will ever free.
                _channel.Writer.TryComplete(ex);

                _logger.LogCritical(
                    ex,
                    "Ledger posting consumer stopped for scope {PostingScope}; accepted fills remain durable and " +
                    "replay on restart, but no further fills can be accepted until it is restarted",
                    _postingScope);
            }

            _recoveryLoaded.TrySetException(ex);
            throw;
        }
    }

    /// <summary>
    /// Throws when the posting loop has stopped outside shutdown. Acceptance must fail closed
    /// there: the channel has no reader, so waiting on capacity would block the caller forever
    /// and hide the fact that the ledger is no longer being posted to.
    /// </summary>
    private void ThrowIfProcessingFailed(TradeExecutedEvent tradeEvent)
    {
        var failure = Volatile.Read(ref _processingFailure);
        if (failure is null)
            return;

        throw new ChannelClosedException(
            $"LedgerPostingConsumer for scope '{_postingScope}' stopped processing; fill {tradeEvent.FillId} " +
            $"for {tradeEvent.Symbol} was not accepted.",
            failure);
    }

    private async Task ProcessPostingAsync(PendingTradeFillPosting posting, CancellationToken ct)
    {
        var evt = posting.TradeEvent;
        try
        {
            var result = await PostEventAsync(evt, ct).ConfigureAwait(false);
            if (!result.Posted)
            {
                await RecordFailureSafelyAsync(evt, result.Failure!, null).ConfigureAwait(false);
                return;
            }

            // Acknowledgement is intentionally not cancellable after ledger mutation. If it
            // fails, the pending record survives and replay detects the existing fill journals.
            await _postingStore.MarkPostedAsync(evt.FillId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordFailureSafelyAsync(evt, ex.Message, ex).ConfigureAwait(false);
        }
    }

    private async Task RecordFailureSafelyAsync(
        TradeExecutedEvent evt,
        string failure,
        Exception? exception)
    {
        try
        {
            await _postingStore.RecordFailureAsync(evt.FillId, failure, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception persistenceException)
        {
            _logger.LogError(
                persistenceException,
                "Failed to append reconciliation handoff for fill {FillId} ({Symbol}); the original pending WAL record remains unacknowledged",
                evt.FillId,
                evt.Symbol);
        }

        _logger.LogError(
            exception,
            "Failed to post ledger entries for fill {FillId} ({Symbol}); the fill remains pending for replay",
            evt.FillId,
            evt.Symbol);
    }

    private async Task<LedgerPostingAttempt> PostEventAsync(TradeExecutedEvent evt, CancellationToken ct)
    {
        lock (_postingBoundarySync)
        {
            if (_postingDisabled || ct.IsCancellationRequested)
                throw new OperationCanceledException("Ledger posting consumer is shutting down.", ct);
        }

        var securityGate = await EvaluateSecurityMasterPostingGateAsync(evt, ct).ConfigureAwait(false);
        if (!securityGate.CanPost)
        {
            _logger.LogError(
                "Blocked ledger posting for fill {FillId} on {Symbol}: {Reason}",
                evt.FillId,
                evt.Symbol,
                securityGate.Reason);
            return LedgerPostingAttempt.Failed(securityGate.Reason);
        }

        var expectedJournals = BuildCanonicalJournals(evt, securityGate);
        foreach (var expectedJournal in expectedJournals)
        {
            lock (_postingBoundarySync)
            {
                if (_postingDisabled || ct.IsCancellationRequested)
                    throw new OperationCanceledException("Ledger posting consumer is shutting down.", ct);
            }

            var write = BuildDurableWrite(evt, expectedJournal);
            var confirmation = await _postingTarget
                .PostAndConfirmAsync(write, ct)
                .ConfigureAwait(false);
            EnsureCanonicalJournalMatches(
                expectedJournal,
                confirmation.RetainedRecord.Entry,
                evt.FillId,
                expectedJournal.Metadata.ActivityType ?? "unknown");

            // The authoritative append/read-back is complete. The in-memory ledger is only a
            // rebuildable projection and is deliberately updated afterward.
            lock (_postingBoundarySync)
            {
                if (_postingDisabled || ct.IsCancellationRequested)
                    throw new OperationCanceledException("Ledger posting consumer is shutting down.", ct);
                UpdateProjection(expectedJournal, evt.FillId);
            }
        }

        _logger.LogDebug(
            "Posted ledger entries for fill {FillId}: {Side} {Quantity} {Symbol} @ {Price}",
            evt.FillId, evt.Side, evt.FilledQuantity, evt.Symbol, evt.FillPrice);
        return LedgerPostingAttempt.Success;
    }

    private IReadOnlyList<JournalEntry> BuildCanonicalJournals(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate)
    {
        if (evt.FilledQuantity <= 0m)
            throw new InvalidDataException($"Fill '{evt.FillId:D}' has non-positive quantity {evt.FilledQuantity}.");
        if (evt.FillPrice <= 0m)
            throw new InvalidDataException($"Fill '{evt.FillId:D}' has non-positive price {evt.FillPrice}.");
        if (evt.Commission < 0m)
            throw new InvalidDataException($"Fill '{evt.FillId:D}' has negative commission {evt.Commission}.");

        var journals = new List<JournalEntry>(evt.Commission > 0m ? 2 : 1)
        {
            BuildTradeJournal(evt, securityGate)
        };
        if (evt.Commission > 0m)
            journals.Add(BuildCommissionJournal(evt, securityGate));

        return journals;
    }

    private JournalEntry BuildTradeJournal(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate)
    {
        const string activityType = TradeFillActivityType;
        var journalId = CreateDeterministicGuid(evt.FillId, activityType, "journal");
        var description = evt.Side switch
        {
            Sdk.OrderSide.Buy => FormattableString.Invariant(
                $"Buy {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}"),
            Sdk.OrderSide.Sell => FormattableString.Invariant(
                $"Sell {evt.FilledQuantity} {evt.Symbol} @ {evt.FillPrice:F4}"),
            _ => throw new InvalidDataException(
                $"Order side '{evt.Side}' is not supported for fill '{evt.FillId:D}'.")
        };
        var accountId = NormalizeOptional(evt.FinancialAccountId);
        var cashAccount = accountId is null ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(accountId);
        var securitiesAccount = LedgerAccounts.Securities(evt.Symbol, accountId);
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>();

        if (evt.Side == Sdk.OrderSide.Buy)
        {
            lines.Add((securitiesAccount, evt.GrossValue, 0m));
            lines.Add((cashAccount, 0m, evt.GrossValue));
        }
        else if (evt.RealizedPnl > 0m)
        {
            var gainAccount = accountId is null
                ? LedgerAccounts.RealizedGain
                : LedgerAccounts.RealizedGainFor(accountId);
            var costBasis = evt.GrossValue - evt.RealizedPnl;
            if (costBasis <= 0m)
                throw new InvalidDataException($"Fill '{evt.FillId:D}' has non-positive sell cost basis {costBasis}.");
            lines.Add((cashAccount, evt.GrossValue, 0m));
            lines.Add((securitiesAccount, 0m, costBasis));
            lines.Add((gainAccount, 0m, evt.RealizedPnl));
        }
        else if (evt.RealizedPnl < 0m)
        {
            var lossAccount = accountId is null
                ? LedgerAccounts.RealizedLoss
                : LedgerAccounts.RealizedLossFor(accountId);
            var costBasis = evt.GrossValue - evt.RealizedPnl;
            lines.Add((cashAccount, evt.GrossValue, 0m));
            lines.Add((lossAccount, -evt.RealizedPnl, 0m));
            lines.Add((securitiesAccount, 0m, costBasis));
        }
        else
        {
            lines.Add((cashAccount, evt.GrossValue, 0m));
            lines.Add((securitiesAccount, 0m, evt.GrossValue));
        }

        return CreateJournal(evt, securityGate, activityType, journalId, description, lines);
    }

    private JournalEntry BuildCommissionJournal(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate)
    {
        const string activityType = "trade-commission";
        var journalId = CreateDeterministicGuid(evt.FillId, activityType, "journal");
        var description = $"Commission on {evt.Symbol} fill {evt.FillId}";
        var accountId = NormalizeOptional(evt.FinancialAccountId);
        var cashAccount = accountId is null ? LedgerAccounts.Cash : LedgerAccounts.CashAccount(accountId);
        var commissionAccount = accountId is null
            ? LedgerAccounts.CommissionExpense
            : LedgerAccounts.CommissionExpenseFor(accountId);
        return CreateJournal(
            evt,
            securityGate,
            activityType,
            journalId,
            description,
            [
                (commissionAccount, evt.Commission, 0m),
                (cashAccount, 0m, evt.Commission)
            ]);
    }

    private JournalEntry CreateJournal(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate,
        string activityType,
        Guid journalId,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> amounts)
    {
        var lines = amounts
            .Select((line, index) => new LedgerEntry(
                CreateDeterministicGuid(evt.FillId, activityType, $"line:{index}"),
                journalId,
                evt.OccurredAt,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        var metadata = BuildPostingMetadata(evt, securityGate, activityType, lines);
        return new JournalEntry(journalId, evt.OccurredAt, description, lines, metadata);
    }

    private LedgerJournalEntryWrite BuildDurableWrite(
        TradeExecutedEvent evt,
        JournalEntry entry)
    {
        var activityType = entry.Metadata.ActivityType
            ?? throw new InvalidDataException("Trade-fill journal activity type is required.");
        var commandId = CreateDeterministicGuid(evt.FillId, activityType, "command");
        var sourceEventId = CreateDeterministicGuid(evt.FillId, activityType, "source-event");
        return new LedgerJournalEntryWrite(
            entry,
            _postingContext.AggregateId,
            _postingContext.PeriodId,
            CommandId: commandId,
            CorrelationId: evt.FillId,
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingPolicyId: _postingContext.AccountingPolicyId,
            AccountingPolicyVersion: _postingContext.AccountingPolicyVersion,
            RuleId: $"execution.{activityType}",
            RuleVersion: "1",
            SourceEventId: sourceEventId,
            PostingKind: LedgerPostingKindDto.Originating,
            PostingCommand: new AccountingPostingCommandDto(
                commandId,
                _postingContext.AggregateId,
                _postingContext.PeriodId,
                DateOnly.FromDateTime(evt.OccurredAt.UtcDateTime),
                evt.OccurredAt,
                entry.Metadata.IdempotencyKey!,
                AccountingPostingIntentDto.Originating,
                SourceEventId: sourceEventId,
                CorrelationId: evt.FillId,
                CausationId: commandId,
                ExpectedVersion: _postingContext.ExpectedPeriodVersion,
                SourceEventType: activityType,
                ApprovalState: AccountingPostingApprovalStateDto.NotRequired,
                OperatorRationale: "Governed OMS execution report accepted for ledger posting.",
                Evidence:
                [
                    new AccountingPostingEvidenceReferenceDto(
                        $"execution-fill:{evt.FillId:D}:{activityType}",
                        $"evidence://execution/fills/{evt.FillId:D}/{activityType}",
                        AccountingPostingEvidenceKindDto.Source,
                        "OrderManagementSystem",
                        evt.OccurredAt,
                        "execution-ledger-consumer",
                        SubjectId: evt.OrderId,
                        Description: "Authoritative execution report and Security Master gate result.")
                ],
                ActionOrigin: OperationsActionOriginDto.HumanOperator,
                LedgerBookId: _postingContext.LedgerBookId),
            LedgerBookId: _postingContext.LedgerBookId);
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

    private JournalEntryMetadata BuildPostingMetadata(
        TradeExecutedEvent evt,
        LedgerPostingSecurityGateResult securityGate,
        string activityType,
        IReadOnlyList<LedgerEntry> lines)
    {
        var securityId = securityGate.SecurityId
            ?? throw new InvalidDataException($"Fill '{evt.FillId:D}' has no resolved Security Master identity.");
        Guid? orderId = Guid.TryParse(evt.OrderId, out var parsedOrderId) ? parsedOrderId : null;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["securityMaster.workflow"] = SecurityValidationWorkflowDto.LedgerPosting.ToString(),
            ["securityMaster.scope"] = securityGate.ValidationScope,
            ["securityMaster.gate"] = "resolved-approved-mapped",
            ["securityMaster.issueCodes"] = string.Join(",", securityGate.IssueCodes),
            ["source.orderId"] = evt.OrderId,
            ["ledgerPosting.scope"] = _postingScope,
            ["tradeFill.side"] = evt.Side.ToString(),
            ["tradeFill.quantity"] = FormatDecimal(evt.FilledQuantity),
            ["tradeFill.price"] = FormatDecimal(evt.FillPrice),
            ["tradeFill.commission"] = FormatDecimal(evt.Commission),
            ["tradeFill.realizedPnl"] = FormatDecimal(evt.RealizedPnl),
            ["tradeFill.newCash"] = FormatDecimal(evt.NewCash),
            ["tradeFill.fingerprint"] = BuildEconomicFingerprint(
                evt,
                securityId,
                activityType,
                lines)
        };

        if (string.Equals(activityType, TradeFillActivityType, StringComparison.Ordinal))
        {
            // Trade-identity tags for statement reconciliation: the journal→custodian-transaction
            // projection reads these opt-in tags so the posted trade exact-matches a statement row
            // instead of degrading to the candidate stage. The canonical fill id is the only
            // transaction-granular identity the execution report carries (order ids repeat across
            // partial fills) and it is content-derived, so replay stamps the same value. No
            // `settlementDate` is stamped — the execution report carries no settlement date, and
            // estimating T+n conventions is a policy decision this consumer must not make; the
            // projection then defaults settlement to the trade date. The commission journal is
            // deliberately left untagged: a share quantity does not apply to a fee movement, and a
            // fabricated fee FITID could never equal a custodian's.
            tags[ReconciliationQuantityTag] = FormatDecimal(evt.FilledQuantity);
            tags[ReconciliationExternalTransactionIdTag] = evt.FillId.ToString("D");
        }

        return new JournalEntryMetadata(
            ActivityType: activityType,
            Symbol: evt.Symbol,
            SecurityId: securityId,
            OrderId: orderId,
            FillId: evt.FillId,
            LedgerBook: _postingContext.LedgerBookId.ToString("D"),
            FinancialAccountId: NormalizeOptional(evt.FinancialAccountId),
            EffectiveDate: DateOnly.FromDateTime(evt.OccurredAt.UtcDateTime),
            IdempotencyKey: $"execution:{_postingScope}:{evt.FillId:D}:{activityType}",
            Tags: tags);
    }

    private string BuildEconomicFingerprint(
        TradeExecutedEvent evt,
        Guid securityId,
        string activityType,
        IReadOnlyList<LedgerEntry> lines)
    {
        var canonical = new StringBuilder(512);
        AppendCanonical(canonical, "v1");
        AppendCanonical(canonical, _postingScope);
        AppendCanonical(canonical, _postingContext.AggregateId.ToString("D"));
        AppendCanonical(canonical, _postingContext.PeriodId.ToString("D"));
        AppendCanonical(canonical, _postingContext.LedgerBookId.ToString("D"));
        AppendCanonical(canonical, evt.FillId.ToString("D"));
        AppendCanonical(canonical, evt.OrderId);
        AppendCanonical(canonical, evt.Symbol.Trim().ToUpperInvariant());
        AppendCanonical(canonical, evt.Side.ToString());
        AppendCanonical(canonical, FormatDecimal(evt.FilledQuantity));
        AppendCanonical(canonical, FormatDecimal(evt.FillPrice));
        AppendCanonical(canonical, FormatDecimal(evt.Commission));
        AppendCanonical(canonical, FormatDecimal(evt.RealizedPnl));
        AppendCanonical(canonical, FormatDecimal(evt.NewCash));
        AppendCanonical(canonical, evt.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, NormalizeOptional(evt.FinancialAccountId));
        AppendCanonical(canonical, securityId.ToString("D"));
        AppendCanonical(canonical, activityType);
        foreach (var line in lines)
        {
            AppendCanonical(canonical, ((int)line.Account.AccountType).ToString(CultureInfo.InvariantCulture));
            AppendCanonical(canonical, line.Account.Name);
            AppendCanonical(canonical, line.Account.Symbol?.Trim().ToUpperInvariant());
            AppendCanonical(canonical, NormalizeOptional(line.Account.FinancialAccountId));
            AppendCanonical(canonical, FormatDecimal(line.Debit));
            AppendCanonical(canonical, FormatDecimal(line.Credit));
        }

        // Deliberately NOT routed through Sha256Digest (which lowercases): this canonical fill
        // hash participates in posting dedupe/verification against retained records, so changing
        // its casing would break recognition of previously posted fills (#2691).
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private Guid CreateDeterministicGuid(Guid fillId, string activityType, string purpose)
    {
        var canonical = string.Join("|", _postingScope, fillId.ToString("D"), activityType, purpose);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical), hash);
        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private void UpdateProjection(JournalEntry expected, Guid fillId)
    {
        if (_projectionLedger is null)
            return;

        var activityType = expected.Metadata.ActivityType ?? "unknown";
        var retained = _projectionLedger
            .GetJournalEntries(new LedgerQuery(FillId: fillId, ActivityType: activityType))
            .Where(entry => entry.Metadata.Tags is not null
                && entry.Metadata.Tags.TryGetValue("ledgerPosting.scope", out var scope)
                && string.Equals(scope, _postingScope, StringComparison.Ordinal))
            .ToArray();
        if (retained.Length > 1)
        {
            throw new InvalidDataException(
                $"Projection contains multiple '{activityType}' journals for fill '{fillId:D}' in scope '{_postingScope}'.");
        }

        if (retained.Length == 1)
        {
            EnsureCanonicalJournalMatches(expected, retained[0], fillId, activityType);
            return;
        }

        _projectionLedger.Post(expected);
    }

    private static void EnsureCanonicalJournalMatches(
        JournalEntry expected,
        JournalEntry retained,
        Guid fillId,
        string activityType)
    {
        var matches = expected.JournalEntryId == retained.JournalEntryId
            && expected.Timestamp == retained.Timestamp
            && string.Equals(expected.Description, retained.Description, StringComparison.Ordinal)
            && MetadataMatches(expected.Metadata, retained.Metadata)
            && expected.Lines.Count == retained.Lines.Count;
        if (matches)
        {
            for (var index = 0; index < expected.Lines.Count; index++)
            {
                var expectedLine = expected.Lines[index];
                var retainedLine = retained.Lines[index];
                if (expectedLine.EntryId != retainedLine.EntryId
                    || expectedLine.JournalEntryId != retainedLine.JournalEntryId
                    || expectedLine.Timestamp != retainedLine.Timestamp
                    || expectedLine.Account != retainedLine.Account
                    || expectedLine.Debit != retainedLine.Debit
                    || expectedLine.Credit != retainedLine.Credit
                    || !string.Equals(expectedLine.Description, retainedLine.Description, StringComparison.Ordinal)
                    || expectedLine.Dimensions != retainedLine.Dimensions)
                {
                    matches = false;
                    break;
                }
            }
        }

        if (!matches)
        {
            throw new InvalidDataException(
                $"Retained '{activityType}' journal for fill '{fillId:D}' does not match the canonical economic fingerprint and ordered lines.");
        }
    }

    private static bool MetadataMatches(JournalEntryMetadata expected, JournalEntryMetadata retained)
    {
        expected = expected.Normalize();
        retained = retained.Normalize();
        return string.Equals(expected.ActivityType, retained.ActivityType, StringComparison.Ordinal)
            && string.Equals(expected.Symbol, retained.Symbol, StringComparison.Ordinal)
            && expected.SecurityId == retained.SecurityId
            && expected.OrderId == retained.OrderId
            && expected.FillId == retained.FillId
            && string.Equals(expected.ProjectId, retained.ProjectId, StringComparison.Ordinal)
            && string.Equals(expected.LedgerBook, retained.LedgerBook, StringComparison.Ordinal)
            && expected.LedgerView == retained.LedgerView
            && string.Equals(expected.ScenarioId, retained.ScenarioId, StringComparison.Ordinal)
            && string.Equals(expected.StrategyId, retained.StrategyId, StringComparison.Ordinal)
            && string.Equals(expected.FinancialAccountId, retained.FinancialAccountId, StringComparison.Ordinal)
            && string.Equals(expected.CounterpartyAccountId, retained.CounterpartyAccountId, StringComparison.Ordinal)
            && string.Equals(expected.Institution, retained.Institution, StringComparison.Ordinal)
            && expected.EffectiveDate == retained.EffectiveDate
            && string.Equals(expected.IdempotencyKey, retained.IdempotencyKey, StringComparison.Ordinal)
            && string.Equals(expected.FundEventId, retained.FundEventId, StringComparison.Ordinal)
            && string.Equals(expected.FundEventType, retained.FundEventType, StringComparison.Ordinal)
            && string.Equals(expected.CapitalAccountId, retained.CapitalAccountId, StringComparison.Ordinal)
            && string.Equals(expected.InvestorId, retained.InvestorId, StringComparison.Ordinal)
            && string.Equals(expected.PaymentIntentId, retained.PaymentIntentId, StringComparison.Ordinal)
            && string.Equals(expected.SettlementReference, retained.SettlementReference, StringComparison.Ordinal)
            && TagsMatch(expected.Tags, retained.Tags)
            && expected.EvidenceReferences.SequenceEqual(retained.EvidenceReferences);
    }

    private static bool TagsMatch(
        IReadOnlyDictionary<string, string>? expected,
        IReadOnlyDictionary<string, string>? retained)
    {
        var expectedTags = expected ?? new Dictionary<string, string>();
        var retainedTags = retained ?? new Dictionary<string, string>();
        if (expectedTags.Count != retainedTags.Count)
            return false;

        return expectedTags.All(pair => retainedTags.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static void AppendCanonical(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static string FormatDecimal(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture);

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

    private sealed record LedgerPostingAttempt(bool Posted, string? Failure)
    {
        public static LedgerPostingAttempt Success { get; } = new(true, null);

        public static LedgerPostingAttempt Failed(string failure) => new(false, failure);
    }
}
