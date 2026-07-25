using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Describes the terminal state reached by a dual-path pipeline shutdown.
/// </summary>
public enum DualPathPipelineStopStatus
{
    Succeeded,
    Failed,
    Cancelled,
    TimedOut
}

/// <summary>
/// Reports whether shutdown quiesced the hot-path consumers and accounts for every
/// event that had already been accepted by a ring buffer.
/// </summary>
public sealed record DualPathPipelineStopOutcome(
    DualPathPipelineStopStatus Status,
    bool Quiesced,
    long TradeConsumed,
    long TradeDropped,
    long TradePending,
    long QuoteConsumed,
    long QuoteDropped,
    long QuotePending,
    Exception? Failure)
{
    /// <summary>Gets whether shutdown completed without cancellation, timeout, or publication failure.</summary>
    public bool Succeeded => Status == DualPathPipelineStopStatus.Succeeded;
}

/// <summary>
/// A dual-path event pipeline that routes high-volume <see cref="MarketEventType.Trade"/>
/// and <see cref="MarketEventType.BboQuote"/> events through a zero-allocation hot path
/// (struct ring buffer), while routing all other event types through the standard
/// record-based <see cref="EventPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hot path (Trade / BboQuote):</b>
/// The producer extracts the essential fields into a <see cref="RawTradeEvent"/> or
/// <see cref="RawQuoteEvent"/> struct and writes it into a pre-allocated
/// <see cref="SpscRingBuffer{T}"/>.  No heap allocation occurs on the producer thread.
/// A dedicated background consumer drains the ring buffer in batches, reconstructs
/// <see cref="MarketEvent"/> objects, and forwards them to the <see cref="EventPipeline"/>
/// slow path for storage.
/// </para>
/// <para>
/// <b>Slow path (all other event types):</b>
/// Events are passed directly to the underlying <see cref="EventPipeline"/>, preserving
/// all existing WAL, deduplication, and validation behaviour.
/// </para>
/// <para>
/// <b>Zero-allocation producer API:</b>
/// Callers that already construct structs without a <see cref="MarketEvent"/> intermediary
/// can call <see cref="TryPublishTrade"/> or <see cref="TryPublishQuote"/> directly.
/// These overloads bypass <see cref="MarketEvent"/> allocation entirely. Concurrent callers
/// are serialized at each SPSC buffer's producer boundary so every successful publication
/// owns one distinct slot.
/// </para>
/// <para>
/// <b>Ring buffer full behaviour:</b>
/// When a ring buffer is full the event falls back to the slow path to prevent data loss.
/// </para>
/// </remarks>
public sealed class DualPathEventPipeline : IMarketEventPublisher, IBackpressureSignal, IAsyncDisposable
{
    private readonly EventPipeline _slowPath;
    private readonly SpscRingBuffer<RawTradeEvent> _tradeBuffer;
    private readonly SpscRingBuffer<RawQuoteEvent> _quoteBuffer;
    private readonly object _tradeProducerSync = new();
    private readonly object _quoteProducerSync = new();
    private readonly SymbolTable _symbolTable;
    private readonly ILogger<DualPathEventPipeline> _logger;

    // Background consumer tasks that drain the ring buffers into the slow path.
    private readonly Task _tradeConsumer;
    private readonly Task _quoteConsumer;
    private readonly CancellationTokenSource _cts = new();

    // Pre-allocated batch arrays — drained on the consumer thread, never shared.
    private readonly RawTradeEvent[] _tradeBatch;
    private readonly RawQuoteEvent[] _quoteBatch;
    private readonly int _batchDrainSize;

    // Hot-path counters (updated on the producer thread or consumer thread only
    // via Interlocked to stay thread-safe without locks).
    private long _hotTradePublished;
    private long _hotTradeDropped;
    private long _hotTradeConsumed;
    private long _hotTradeFallbacks;
    private long _hotTradeInFlight;
    private long _hotQuotePublished;
    private long _hotQuoteDropped;
    private long _hotQuoteConsumed;
    private long _hotQuoteFallbacks;
    private long _hotQuoteInFlight;

    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private Task<DualPathPipelineStopOutcome>? _stopTask;
    private Task? _lateCleanupTask;
    private int _lifecycleState;
    private int _activePublishers;
    private int _shutdownCancellationReason;
    private readonly TimeSpan _shutdownTimeout;

    private const int Accepting = 0;
    private const int Stopping = 1;
    private const int Disposed = 2;
    private const int ShutdownNotCancelled = 0;
    private const int ShutdownCancelledByCaller = 1;
    private const int ShutdownTimedOut = 2;

    // A real delay is required here: Task.Yield() and Task.Delay(0) keep the
    // elevated-priority consumers runnable and can starve small CI/desktop hosts.
    private const int EmptyPollDelayMilliseconds = 1;
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CancellationQuiescenceGrace = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Creates a <see cref="DualPathEventPipeline"/> that wraps an existing
    /// <see cref="EventPipeline"/> slow path.
    /// </summary>
    /// <param name="slowPath">
    /// The underlying <see cref="EventPipeline"/> used for non-hot-path event types
    /// and as a fallback when ring buffers are full.
    /// The caller retains ownership and must dispose it separately after this pipeline.
    /// </param>
    /// <param name="symbolTable">
    /// Shared symbol intern table.  Must be the same instance used by any callers
    /// that invoke <see cref="TryPublishTrade"/> / <see cref="TryPublishQuote"/> directly.
    /// </param>
    /// <param name="ringBufferCapacity">
    /// Capacity of each ring buffer (one for trades, one for quotes).
    /// Rounded up to the next power of two. Default is 4 096 slots.
    /// </param>
    /// <param name="batchDrainSize">
    /// Maximum events drained per consumer iteration.  Default is 256.
    /// </param>
    /// <param name="startConsumers">
    /// When <see langword="false"/> the background consumer tasks are not started.
    /// Intended for unit tests that need to inspect ring buffer state without a
    /// concurrent consumer draining it.  Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="shutdownTimeout">
    /// Maximum time to drain accepted hot-path events before shutdown cancels the consumers
    /// and reports a timed-out outcome. Defaults to 30 seconds.
    /// </param>
    public DualPathEventPipeline(
        EventPipeline slowPath,
        SymbolTable symbolTable,
        int ringBufferCapacity = 4_096,
        int batchDrainSize = 256,
        bool startConsumers = true,
        ILogger<DualPathEventPipeline>? logger = null,
        TimeSpan? shutdownTimeout = null)
    {
        _slowPath = slowPath ?? throw new ArgumentNullException(nameof(slowPath));
        _symbolTable = symbolTable ?? throw new ArgumentNullException(nameof(symbolTable));
        _logger = logger ?? NullLogger<DualPathEventPipeline>.Instance;
        _shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;

        if (ringBufferCapacity < 2)
            throw new ArgumentOutOfRangeException(nameof(ringBufferCapacity), "Ring buffer capacity must be at least 2.");

        if (batchDrainSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchDrainSize), "Batch drain size must be at least 1.");

        if (_shutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout), "Shutdown timeout must be greater than zero.");

        _batchDrainSize = batchDrainSize;
        _tradeBuffer = new SpscRingBuffer<RawTradeEvent>(ringBufferCapacity);
        _quoteBuffer = new SpscRingBuffer<RawQuoteEvent>(ringBufferCapacity);

        // Pre-allocate batch drain arrays (consumer thread only).
        _tradeBatch = new RawTradeEvent[batchDrainSize];
        _quoteBatch = new RawQuoteEvent[batchDrainSize];

        if (startConsumers)
        {
            // Start one long-running consumer per ring buffer.
            _tradeConsumer = Task.Factory.StartNew(
                () => ConsumeTradesAsync(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();

            _quoteConsumer = Task.Factory.StartNew(
                () => ConsumeQuotesAsync(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }
        else
        {
            // Task.CompletedTask is safe to await in DisposeAsync — Task.WhenAll
            // on already-completed tasks returns immediately.
            _tradeConsumer = Task.CompletedTask;
            _quoteConsumer = Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // IMarketEventPublisher
    // -------------------------------------------------------------------------

    /// <summary>
    /// Routes the event to the hot path (ring buffer) for trades and quotes,
    /// or to the slow path for all other event types.
    /// </summary>
    /// <remarks>
    /// When the trade or quote ring buffer is full the event is forwarded to the
    /// slow-path <see cref="EventPipeline"/> to prevent data loss.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublish(in MarketEvent evt)
    {
        if (!TryEnterPublish())
            return false;

        try
        {
            return evt.Type switch
            {
                MarketEventType.Trade => TryRouteTrade(in evt),
                MarketEventType.BboQuote => TryRouteQuote(in evt),
                _ => _slowPath.TryPublish(in evt)
            };
        }
        finally
        {
            ExitPublish();
        }
    }

    // -------------------------------------------------------------------------
    // Zero-allocation producer API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a <see cref="RawTradeEvent"/> struct directly into the trade ring buffer
    /// without any heap allocation on the calling (producer) thread.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the event was written;
    /// <see langword="false"/> only when both the ring buffer and the slow-path fallback reject the event.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublishTrade(in RawTradeEvent trade)
    {
        if (!TryEnterPublish())
            return false;

        try
        {
            if (TryWriteTrade(in trade))
                return true;

            Interlocked.Increment(ref _hotTradeFallbacks);
            return TryPublishTradeFallback(in trade);
        }
        finally
        {
            ExitPublish();
        }
    }

    /// <summary>
    /// Writes a <see cref="RawQuoteEvent"/> struct directly into the quote ring buffer
    /// without any heap allocation on the calling (producer) thread.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the event was written;
    /// <see langword="false"/> only when both the ring buffer and the slow-path fallback reject the event.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublishQuote(in RawQuoteEvent quote)
    {
        if (!TryEnterPublish())
            return false;

        try
        {
            if (TryWriteQuote(in quote))
                return true;

            Interlocked.Increment(ref _hotQuoteFallbacks);
            return TryPublishQuoteFallback(in quote);
        }
        finally
        {
            ExitPublish();
        }
    }

    // -------------------------------------------------------------------------
    // IBackpressureSignal — delegates to the slow-path pipeline
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool IsUnderPressure => _slowPath.IsUnderPressure;

    /// <inheritdoc/>
    public double QueueUtilization => _slowPath.QueueUtilization;

    // -------------------------------------------------------------------------
    // Statistics
    // -------------------------------------------------------------------------

    /// <summary>Gets the total number of trade events written to the hot-path ring buffer.</summary>
    public long HotTradePublished => Interlocked.Read(ref _hotTradePublished);

    /// <summary>Gets the total number of trade events that were rejected or could not be handed off after hot-path acceptance.</summary>
    public long HotTradeDropped => Interlocked.Read(ref _hotTradeDropped);

    /// <summary>Gets the total number of trade events drained from the ring buffer and accepted by the slow-path pipeline.</summary>
    public long HotTradeConsumed => Interlocked.Read(ref _hotTradeConsumed);

    /// <summary>Gets the number of trade events that bypassed the ring buffer and fell back to the slow path.</summary>
    public long HotTradeFallbacks => Interlocked.Read(ref _hotTradeFallbacks);

    /// <summary>Gets the number of trade events currently owned by a consumer batch.</summary>
    public long HotTradeInFlight => Interlocked.Read(ref _hotTradeInFlight);

    /// <summary>Gets the total number of quote events written to the hot-path ring buffer.</summary>
    public long HotQuotePublished => Interlocked.Read(ref _hotQuotePublished);

    /// <summary>Gets the total number of quote events that were rejected or could not be handed off after hot-path acceptance.</summary>
    public long HotQuoteDropped => Interlocked.Read(ref _hotQuoteDropped);

    /// <summary>Gets the total number of quote events drained from the ring buffer and accepted by the slow-path pipeline.</summary>
    public long HotQuoteConsumed => Interlocked.Read(ref _hotQuoteConsumed);

    /// <summary>Gets the number of quote events that bypassed the ring buffer and fell back to the slow path.</summary>
    public long HotQuoteFallbacks => Interlocked.Read(ref _hotQuoteFallbacks);

    /// <summary>Gets the number of quote events currently owned by a consumer batch.</summary>
    public long HotQuoteInFlight => Interlocked.Read(ref _hotQuoteInFlight);

    /// <summary>Gets the current number of trade events waiting in the ring buffer.</summary>
    public int TradeBufferCount => _tradeBuffer.Count;

    /// <summary>Gets the current number of quote events waiting in the ring buffer.</summary>
    public int QuoteBufferCount => _quoteBuffer.Count;

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Closes producer admission and drains all events accepted by the hot path.
    /// </summary>
    /// <remarks>
    /// The first stop request starts one shared shutdown operation. Cancellation from any
    /// concurrent caller cancels that operation, while the configured shutdown deadline
    /// bounds callers that do not supply a token. The returned outcome distinguishes a
    /// clean drain from a publication failure, caller cancellation, or timeout and reports
    /// any events still pending if an uncooperative dependency prevents quiescence.
    /// </remarks>
    public Task<DualPathPipelineStopOutcome> StopAsync(CancellationToken ct = default)
    {
        Task<DualPathPipelineStopOutcome> stopTask;
        lock (_disposeSync)
        {
            _stopTask ??= StopCoreAsync();
            stopTask = _stopTask;
        }

        if (!ct.CanBeCanceled)
            return stopTask;

        return AwaitStopWithCallerCancellationAsync(stopTask, ct);
    }

    /// <summary>
    /// Waits until all pipeline-owned consumers and any publishers admitted before shutdown
    /// have stopped using the caller-owned slow path.
    /// </summary>
    /// <remarks>
    /// A bounded <see cref="StopAsync(CancellationToken)"/> can report
    /// <see cref="DualPathPipelineStopOutcome.Quiesced"/> as <see langword="false"/> while a
    /// late cleanup continues. Call this method before disposing the slow path in that case.
    /// Cancellation cancels only this wait; it does not alter the shared shutdown outcome.
    /// </remarks>
    public async Task AwaitTerminalCleanupAsync(CancellationToken ct = default)
    {
        var outcome = await StopAsync().WaitAsync(ct).ConfigureAwait(false);
        if (outcome.Quiesced)
            return;

        Task cleanupTask;
        lock (_disposeSync)
        {
            cleanupTask = _lateCleanupTask
                ?? throw new InvalidOperationException(
                    "A non-quiesced shutdown did not schedule terminal cleanup.");
        }

        await cleanupTask.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        var outcome = await StopAsync().ConfigureAwait(false);
        if (outcome.Succeeded)
            return;

        if (outcome.Failure is not null)
        {
            ExceptionDispatchInfo.Capture(outcome.Failure).Throw();
        }

        if (outcome.Status == DualPathPipelineStopStatus.TimedOut)
        {
            throw new TimeoutException(
                $"Dual-path pipeline shutdown timed out with {outcome.TradePending} trade event(s) " +
                $"and {outcome.QuotePending} quote event(s) still pending.");
        }

        throw new OperationCanceledException(
            $"Dual-path pipeline shutdown was cancelled with {outcome.TradePending} trade event(s) " +
            $"and {outcome.QuotePending} quote event(s) still pending.");
    }

    private async Task<DualPathPipelineStopOutcome> AwaitStopWithCallerCancellationAsync(
        Task<DualPathPipelineStopOutcome> stopTask,
        CancellationToken ct)
    {
        using var registration = ct.Register(
            static state => ((DualPathEventPipeline)state!).RequestStopCancellation(ShutdownCancelledByCaller),
            this);

        return await stopTask.ConfigureAwait(false);
    }

    private async Task<DualPathPipelineStopOutcome> StopCoreAsync()
    {
        Interlocked.CompareExchange(ref _lifecycleState, Stopping, Accepting);

        using var timeoutCts = new CancellationTokenSource(_shutdownTimeout);
        using var timeoutRegistration = timeoutCts.Token.Register(
            static state => ((DualPathEventPipeline)state!).RequestStopCancellation(ShutdownTimedOut),
            this);

        var failures = new List<Exception>();

        if (!await WaitForActivePublishersAsync().ConfigureAwait(false))
        {
            ScheduleLateCleanup();
            return CreateStopOutcome(
                GetCancellationStatus(),
                quiesced: false,
                failure: null);
        }

        var consumers = Task.WhenAll(_tradeConsumer, _quoteConsumer);
        if (!await WaitForConsumerQuiescenceAsync(consumers).ConfigureAwait(false))
        {
            ScheduleLateCleanup();
            return CreateStopOutcome(
                GetCancellationStatus(),
                quiesced: false,
                failure: null);
        }

        if (consumers.IsFaulted && consumers.Exception is not null)
        {
            failures.AddRange(consumers.Exception.Flatten().InnerExceptions);
        }

        if (_cts.IsCancellationRequested)
        {
            DropRemainingTrades();
            DropRemainingQuotes();
        }
        else
        {
            var tradeDrainFailure = await DrainRemainingTradesAsync(_cts.Token).ConfigureAwait(false);
            if (tradeDrainFailure is not null &&
                !(tradeDrainFailure is OperationCanceledException && _cts.IsCancellationRequested))
            {
                failures.Add(tradeDrainFailure);
            }

            var quoteDrainFailure = await DrainRemainingQuotesAsync(_cts.Token).ConfigureAwait(false);
            if (quoteDrainFailure is not null &&
                !(quoteDrainFailure is OperationCanceledException && _cts.IsCancellationRequested))
            {
                failures.Add(quoteDrainFailure);
            }
        }

        timeoutRegistration.Dispose();
        timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
        Volatile.Write(ref _lifecycleState, Disposed);

        var failure = failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException("Multiple hot-path consumers failed during shutdown.", failures)
        };

        var status = failure is not null
            ? DualPathPipelineStopStatus.Failed
            : GetCancellationStatus();

        var outcome = CreateStopOutcome(status, quiesced: true, failure);
        if (!outcome.Succeeded)
        {
            _logger.LogError(
                failure,
                "Dual-path pipeline shutdown completed with status {Status}. " +
                "Trades consumed/dropped/pending: {TradeConsumed}/{TradeDropped}/{TradePending}; " +
                "quotes consumed/dropped/pending: {QuoteConsumed}/{QuoteDropped}/{QuotePending}",
                outcome.Status,
                outcome.TradeConsumed,
                outcome.TradeDropped,
                outcome.TradePending,
                outcome.QuoteConsumed,
                outcome.QuoteDropped,
                outcome.QuotePending);
        }

        return outcome;
    }

    private void ScheduleLateCleanup()
    {
        lock (_disposeSync)
        {
            _lateCleanupTask ??= CompleteLateCleanupAsync();
        }
    }

    private async Task CompleteLateCleanupAsync()
    {
        while (Volatile.Read(ref _activePublishers) != 0)
            await Task.Delay(EmptyPollDelayMilliseconds).ConfigureAwait(false);

        try
        {
            await Task.WhenAll(_tradeConsumer, _quoteConsumer).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // StopAsync already returned a non-quiesced outcome. Observe and report
            // a later consumer fault so it cannot become an unobserved task failure.
            _logger.LogError(ex, "Dual-path consumer faulted while completing late shutdown cleanup");
        }

        DropRemainingTrades();
        DropRemainingQuotes();

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // A terminal cleanup path won the race.
        }

        Volatile.Write(ref _lifecycleState, Disposed);
    }

    private async Task<bool> WaitForActivePublishersAsync()
    {
        while (Volatile.Read(ref _activePublishers) != 0 && !_cts.IsCancellationRequested)
            await Task.Delay(EmptyPollDelayMilliseconds).ConfigureAwait(false);

        if (Volatile.Read(ref _activePublishers) == 0)
            return true;

        var deadline = Stopwatch.GetTimestamp() + (long)(CancellationQuiescenceGrace.TotalSeconds * Stopwatch.Frequency);
        while (Volatile.Read(ref _activePublishers) != 0 && Stopwatch.GetTimestamp() < deadline)
            await Task.Delay(EmptyPollDelayMilliseconds).ConfigureAwait(false);

        return Volatile.Read(ref _activePublishers) == 0;
    }

    private async Task<bool> WaitForConsumerQuiescenceAsync(Task consumers)
    {
        if (consumers.IsCompleted)
            return true;

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = _cts.Token.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancelled);

        if (await Task.WhenAny(consumers, cancelled.Task).ConfigureAwait(false) == consumers)
            return true;

        var graceDelay = Task.Delay(CancellationQuiescenceGrace);
        return await Task.WhenAny(consumers, graceDelay).ConfigureAwait(false) == consumers;
    }

    private void RequestStopCancellation(int reason)
    {
        Interlocked.CompareExchange(ref _shutdownCancellationReason, reason, ShutdownNotCancelled);

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A caller token may race with the completion path that disposes the
            // consumer CTS. The terminal outcome has already been established.
        }
    }

    private DualPathPipelineStopStatus GetCancellationStatus()
        => Volatile.Read(ref _shutdownCancellationReason) switch
        {
            ShutdownTimedOut => DualPathPipelineStopStatus.TimedOut,
            ShutdownCancelledByCaller => DualPathPipelineStopStatus.Cancelled,
            _ => DualPathPipelineStopStatus.Succeeded
        };

    private DualPathPipelineStopOutcome CreateStopOutcome(
        DualPathPipelineStopStatus status,
        bool quiesced,
        Exception? failure)
        => new(
            status,
            quiesced,
            HotTradeConsumed,
            HotTradeDropped,
            TradeBufferCount + HotTradeInFlight,
            HotQuoteConsumed,
            HotQuoteDropped,
            QuoteBufferCount + HotQuoteInFlight,
            failure);

    // -------------------------------------------------------------------------
    // Private routing helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteTrade(in RawTradeEvent trade)
    {
        lock (_tradeProducerSync)
        {
            if (!_tradeBuffer.TryWrite(in trade))
                return false;

            Interlocked.Increment(ref _hotTradePublished);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteQuote(in RawQuoteEvent quote)
    {
        lock (_quoteProducerSync)
        {
            if (!_quoteBuffer.TryWrite(in quote))
                return false;

            Interlocked.Increment(ref _hotQuotePublished);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRouteTrade(in MarketEvent evt)
    {
        if (evt.Payload is Trade trade)
        {
            var symbolHash = _symbolTable.GetOrAdd(evt.Symbol);
            var raw = new RawTradeEvent(
                evt.Timestamp.UtcTicks,
                symbolHash,
                trade.Price,
                trade.Size,
                (byte)trade.Aggressor,
                evt.Sequence);

            if (TryWriteTrade(in raw))
                return true;
        }

        Interlocked.Increment(ref _hotTradeFallbacks);
        return _slowPath.TryPublish(in evt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRouteQuote(in MarketEvent evt)
    {
        if (evt.Payload is BboQuotePayload quote)
        {
            var symbolHash = _symbolTable.GetOrAdd(evt.Symbol);
            var raw = new RawQuoteEvent(
                evt.Timestamp.UtcTicks,
                symbolHash,
                quote.BidPrice,
                quote.BidSize,
                quote.AskPrice,
                quote.AskSize,
                evt.Sequence);

            if (TryWriteQuote(in raw))
                return true;
        }

        Interlocked.Increment(ref _hotQuoteFallbacks);
        return _slowPath.TryPublish(in evt);
    }


    // -------------------------------------------------------------------------
    // Consumer loops
    // -------------------------------------------------------------------------

    private async Task ConsumeTradesAsync(CancellationToken ct = default)
    {
        ThreadingUtilities.SetAboveNormalPriority();

        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                    break;

                var drained = _tradeBuffer.DrainTo(_tradeBatch, _batchDrainSize);

                if (drained == 0)
                {
                    if (Volatile.Read(ref _lifecycleState) != Accepting &&
                        Volatile.Read(ref _activePublishers) == 0)
                    {
                        break;
                    }

                    await Task.Delay(EmptyPollDelayMilliseconds, ct).ConfigureAwait(false);
                    continue;
                }

                Interlocked.Add(ref _hotTradeInFlight, drained);
                var handedOff = 0;

                try
                {
                    for (var i = 0; i < drained; i++)
                    {
                        ref readonly var raw = ref _tradeBatch[i];
                        var evt = ReconstituteTrade(in raw);

                        // Wait for slow-path capacity instead of retrying TryPublish().
                        // Repeated TryPublish() calls in Wait mode distort drop metrics and
                        // audit trails because the event is eventually persisted, not lost.
                        await _slowPath.PublishAsync(evt, ct).ConfigureAwait(false);
                        handedOff++;
                        Interlocked.Increment(ref _hotTradeConsumed);
                        Interlocked.Decrement(ref _hotTradeInFlight);
                    }
                }
                catch
                {
                    RecordUnpublishedTradeBatch(drained - handedOff);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // StopAsync owns cancellation reporting and accounts for any items
            // that remain in the ring buffer after this consumer quiesces.
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _lifecycleState, Stopping, Accepting);
            _logger.LogError(ex, "Hot-path trade consumer encountered an unexpected error after consuming {Count} events", _hotTradeConsumed);
            throw;
        }
    }

    private async Task ConsumeQuotesAsync(CancellationToken ct = default)
    {
        ThreadingUtilities.SetAboveNormalPriority();

        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                    break;

                var drained = _quoteBuffer.DrainTo(_quoteBatch, _batchDrainSize);

                if (drained == 0)
                {
                    if (Volatile.Read(ref _lifecycleState) != Accepting &&
                        Volatile.Read(ref _activePublishers) == 0)
                    {
                        break;
                    }

                    await Task.Delay(EmptyPollDelayMilliseconds, ct).ConfigureAwait(false);
                    continue;
                }

                Interlocked.Add(ref _hotQuoteInFlight, drained);
                var handedOff = 0;

                try
                {
                    for (var i = 0; i < drained; i++)
                    {
                        ref readonly var raw = ref _quoteBatch[i];
                        var evt = ReconstituteQuote(in raw);

                        await _slowPath.PublishAsync(evt, ct).ConfigureAwait(false);
                        handedOff++;
                        Interlocked.Increment(ref _hotQuoteConsumed);
                        Interlocked.Decrement(ref _hotQuoteInFlight);
                    }
                }
                catch
                {
                    RecordUnpublishedQuoteBatch(drained - handedOff);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // StopAsync records the cancellation outcome after both consumers quiesce.
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _lifecycleState, Stopping, Accepting);
            _logger.LogError(ex, "Hot-path quote consumer encountered an unexpected error after consuming {Count} events", _hotQuoteConsumed);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Struct → MarketEvent reconstruction helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MarketEvent ReconstituteTrade(in RawTradeEvent raw)
    {
        var symbol = _symbolTable.TryGetSymbol(raw.SymbolHash) ?? string.Empty;
        var ts = new DateTimeOffset(raw.TimestampTicks, TimeSpan.Zero);

        var trade = new Trade(
            Timestamp: ts,
            Symbol: symbol,
            Price: raw.Price,
            Size: raw.Size,
            Aggressor: (AggressorSide)raw.Aggressor,
            SequenceNumber: raw.Sequence);

        return MarketEvent.Trade(ts, symbol, trade, raw.Sequence);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MarketEvent ReconstituteQuote(in RawQuoteEvent raw)
    {
        var symbol = _symbolTable.TryGetSymbol(raw.SymbolHash) ?? string.Empty;
        var ts = new DateTimeOffset(raw.TimestampTicks, TimeSpan.Zero);

        var quote = BboQuotePayload.FromUpdate(
            timestamp: ts,
            symbol: symbol,
            bidPrice: raw.BidPrice,
            bidSize: raw.BidSize,
            askPrice: raw.AskPrice,
            askSize: raw.AskSize,
            sequenceNumber: raw.Sequence);

        return MarketEvent.BboQuote(ts, symbol, quote, raw.Sequence);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryEnterPublish()
    {
        if (Volatile.Read(ref _lifecycleState) != Accepting)
            return false;

        Interlocked.Increment(ref _activePublishers);
        if (Volatile.Read(ref _lifecycleState) == Accepting)
            return true;

        Interlocked.Decrement(ref _activePublishers);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitPublish()
        => Interlocked.Decrement(ref _activePublishers);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPublishTradeFallback(in RawTradeEvent raw)
    {
        if (_symbolTable.TryGetSymbol(raw.SymbolHash) is not { Length: > 0 })
        {
            Interlocked.Increment(ref _hotTradeDropped);
            return false;
        }

        var evt = ReconstituteTrade(in raw);
        var accepted = _slowPath.TryPublish(in evt);
        if (!accepted)
            Interlocked.Increment(ref _hotTradeDropped);

        return accepted;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPublishQuoteFallback(in RawQuoteEvent raw)
    {
        if (_symbolTable.TryGetSymbol(raw.SymbolHash) is not { Length: > 0 })
        {
            Interlocked.Increment(ref _hotQuoteDropped);
            return false;
        }

        var evt = ReconstituteQuote(in raw);
        var accepted = _slowPath.TryPublish(in evt);
        if (!accepted)
            Interlocked.Increment(ref _hotQuoteDropped);

        return accepted;
    }

    private void RecordUnpublishedTradeBatch(int count)
    {
        if (count <= 0)
            return;

        Interlocked.Add(ref _hotTradeDropped, count);
        Interlocked.Add(ref _hotTradeInFlight, -count);
    }

    private void RecordUnpublishedQuoteBatch(int count)
    {
        if (count <= 0)
            return;

        Interlocked.Add(ref _hotQuoteDropped, count);
        Interlocked.Add(ref _hotQuoteInFlight, -count);
    }

    // Drains remaining items from the trade ring buffer into the slow path
    // during disposal (after consumers have stopped).
    private async Task<Exception?> DrainRemainingTradesAsync(CancellationToken ct = default)
    {
        while (_tradeBuffer.TryRead(out var raw))
        {
            Interlocked.Increment(ref _hotTradeInFlight);
            try
            {
                var evt = ReconstituteTrade(in raw);
                await _slowPath.PublishAsync(evt, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _hotTradeConsumed);
                Interlocked.Decrement(ref _hotTradeInFlight);
            }
            catch (Exception ex)
            {
                RecordUnpublishedTradeBatch(1);
                DropRemainingTrades();
                return ex;
            }
        }

        return null;
    }

    private async Task<Exception?> DrainRemainingQuotesAsync(CancellationToken ct = default)
    {
        while (_quoteBuffer.TryRead(out var raw))
        {
            Interlocked.Increment(ref _hotQuoteInFlight);
            try
            {
                var evt = ReconstituteQuote(in raw);
                await _slowPath.PublishAsync(evt, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _hotQuoteConsumed);
                Interlocked.Decrement(ref _hotQuoteInFlight);
            }
            catch (Exception ex)
            {
                RecordUnpublishedQuoteBatch(1);
                DropRemainingQuotes();
                return ex;
            }
        }

        return null;
    }

    private void DropRemainingTrades()
    {
        var dropped = 0;
        while (_tradeBuffer.TryRead(out _))
            dropped++;

        if (dropped > 0)
            Interlocked.Add(ref _hotTradeDropped, dropped);
    }

    private void DropRemainingQuotes()
    {
        var dropped = 0;
        while (_quoteBuffer.TryRead(out _))
            dropped++;

        if (dropped > 0)
            Interlocked.Add(ref _hotQuoteDropped, dropped);
    }
}
