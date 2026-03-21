using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

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
/// These overloads bypass <see cref="MarketEvent"/> allocation entirely.
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
    private long _hotQuotePublished;
    private long _hotQuoteDropped;
    private long _hotQuoteConsumed;

    private int _disposed;

    // Spin-wait microseconds between empty drain cycles.
    private const int EmptySpinUs = 100;

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
    public DualPathEventPipeline(
        EventPipeline slowPath,
        SymbolTable symbolTable,
        int ringBufferCapacity = 4_096,
        int batchDrainSize = 256,
        bool startConsumers = true,
        ILogger<DualPathEventPipeline>? logger = null)
    {
        _slowPath = slowPath ?? throw new ArgumentNullException(nameof(slowPath));
        _symbolTable = symbolTable ?? throw new ArgumentNullException(nameof(symbolTable));
        _logger = logger ?? NullLogger<DualPathEventPipeline>.Instance;

        if (ringBufferCapacity < 2)
            throw new ArgumentOutOfRangeException(nameof(ringBufferCapacity), "Ring buffer capacity must be at least 2.");

        if (batchDrainSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchDrainSize), "Batch drain size must be at least 1.");

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
                () => ConsumeTradesAsync(),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();

            _quoteConsumer = Task.Factory.StartNew(
                () => ConsumeQuotesAsync(),
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
        return evt.Type switch
        {
            MarketEventType.Trade => TryRouteTrade(in evt),
            MarketEventType.BboQuote => TryRouteQuote(in evt),
            _ => _slowPath.TryPublish(in evt)
        };
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
    /// <see langword="false"/> when the ring buffer was full and the event was dropped.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublishTrade(in RawTradeEvent trade)
    {
        if (_tradeBuffer.TryWrite(in trade))
        {
            Interlocked.Increment(ref _hotTradePublished);
            return true;
        }

        Interlocked.Increment(ref _hotTradeDropped);
        return false;
    }

    /// <summary>
    /// Writes a <see cref="RawQuoteEvent"/> struct directly into the quote ring buffer
    /// without any heap allocation on the calling (producer) thread.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the event was written;
    /// <see langword="false"/> when the ring buffer was full and the event was dropped.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublishQuote(in RawQuoteEvent quote)
    {
        if (_quoteBuffer.TryWrite(in quote))
        {
            Interlocked.Increment(ref _hotQuotePublished);
            return true;
        }

        Interlocked.Increment(ref _hotQuoteDropped);
        return false;
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

    /// <summary>Gets the total number of trade events dropped because the ring buffer was full.</summary>
    public long HotTradeDropped => Interlocked.Read(ref _hotTradeDropped);

    /// <summary>Gets the total number of trade events drained from the ring buffer and forwarded to storage.</summary>
    public long HotTradeConsumed => Interlocked.Read(ref _hotTradeConsumed);

    /// <summary>Gets the total number of quote events written to the hot-path ring buffer.</summary>
    public long HotQuotePublished => Interlocked.Read(ref _hotQuotePublished);

    /// <summary>Gets the total number of quote events dropped because the ring buffer was full.</summary>
    public long HotQuoteDropped => Interlocked.Read(ref _hotQuoteDropped);

    /// <summary>Gets the total number of quote events drained from the ring buffer and forwarded to storage.</summary>
    public long HotQuoteConsumed => Interlocked.Read(ref _hotQuoteConsumed);

    /// <summary>Gets the current number of trade events waiting in the ring buffer.</summary>
    public int TradeBufferCount => _tradeBuffer.Count;

    /// <summary>Gets the current number of quote events waiting in the ring buffer.</summary>
    public int QuoteBufferCount => _quoteBuffer.Count;

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _cts.CancelAsync().ConfigureAwait(false);

        // Wait for consumer tasks to finish draining
        var consumerTimeout = Task.Delay(TimeSpan.FromSeconds(10));
        await Task.WhenAny(Task.WhenAll(_tradeConsumer, _quoteConsumer), consumerTimeout)
            .ConfigureAwait(false);

        _cts.Dispose();

        // Drain any remaining events from ring buffers into the slow path before
        // the slow path itself is disposed by the caller.
        await DrainRemainingTradesAsync().ConfigureAwait(false);
        await DrainRemainingQuotesAsync().ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private routing helpers
    // -------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRouteTrade(in MarketEvent evt)
    {
        if (evt.Payload is not Trade trade)
            return _slowPath.TryPublish(in evt); // Unknown payload — fall back

        var symbolId = _symbolTable.GetOrAdd(evt.EffectiveSymbol);
        var raw = new RawTradeEvent(
            timestampTicks: evt.Timestamp.UtcTicks,
            symbolHash: symbolId,
            price: trade.Price,
            size: trade.Size,
            aggressor: (byte)trade.Aggressor,
            sequence: evt.Sequence);

        if (_tradeBuffer.TryWrite(in raw))
        {
            Interlocked.Increment(ref _hotTradePublished);
            return true;
        }

        // Ring buffer full — fall back to slow path to avoid data loss.
        Interlocked.Increment(ref _hotTradeDropped);
        return _slowPath.TryPublish(in evt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRouteQuote(in MarketEvent evt)
    {
        if (evt.Payload is not BboQuotePayload quote)
            return _slowPath.TryPublish(in evt); // Unknown payload — fall back

        var symbolId = _symbolTable.GetOrAdd(evt.EffectiveSymbol);
        var raw = new RawQuoteEvent(
            timestampTicks: evt.Timestamp.UtcTicks,
            symbolHash: symbolId,
            bidPrice: quote.BidPrice,
            bidSize: quote.BidSize,
            askPrice: quote.AskPrice,
            askSize: quote.AskSize,
            sequence: evt.Sequence);

        if (_quoteBuffer.TryWrite(in raw))
        {
            Interlocked.Increment(ref _hotQuotePublished);
            return true;
        }

        // Ring buffer full — fall back to slow path to avoid data loss.
        Interlocked.Increment(ref _hotQuoteDropped);
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
            while (!_cts.Token.IsCancellationRequested)
            {
                var drained = _tradeBuffer.DrainTo(_tradeBatch, _batchDrainSize);

                if (drained == 0)
                {
                    // Nothing to do — yield briefly to avoid spinning at 100 % CPU.
                    await Task.Yield();
                    continue;
                }

                for (var i = 0; i < drained; i++)
                {
                    ref readonly var raw = ref _tradeBatch[i];
                    var evt = ReconstituteTrade(in raw);

                    // Ensure we do not silently drop hot-path events under backpressure.
                    while (!_slowPath.TryPublish(in evt))
                    {
                        // Back off briefly to avoid busy-waiting when the slow path is saturated.
                        await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                    }
                }

                Interlocked.Add(ref _hotTradeConsumed, drained);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-path trade consumer encountered an unexpected error after consuming {Count} events", _hotTradeConsumed);
        }
    }

    private async Task ConsumeQuotesAsync(CancellationToken ct = default)
    {
        ThreadingUtilities.SetAboveNormalPriority();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var drained = _quoteBuffer.DrainTo(_quoteBatch, _batchDrainSize);

                if (drained == 0)
                {
                    await Task.Delay(0, _cts.Token).ConfigureAwait(false);
                    continue;
                }

                for (var i = 0; i < drained; i++)
                {
                    ref readonly var raw = ref _quoteBatch[i];
                    var evt = ReconstituteQuote(in raw);

                    await _slowPath.PublishAsync(evt).ConfigureAwait(false);
                }
                Interlocked.Add(ref _hotQuoteConsumed, drained);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-path quote consumer encountered an unexpected error after consuming {Count} events", _hotQuoteConsumed);
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

    // Drains remaining items from the trade ring buffer into the slow path
    // during disposal (after consumers have stopped).
    private async Task DrainRemainingTradesAsync(CancellationToken ct = default)
    {
        try
        {
            while (_tradeBuffer.TryRead(out var raw))
            {
                var evt = ReconstituteTrade(in raw);
                await _slowPath.PublishAsync(evt).ConfigureAwait(false);
                Interlocked.Increment(ref _hotTradeConsumed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error draining remaining trade ring buffer events during disposal");
        }
    }

    private async Task DrainRemainingQuotesAsync(CancellationToken ct = default)
    {
        try
        {
            while (_quoteBuffer.TryRead(out var raw))
            {
                var evt = ReconstituteQuote(in raw);
                await _slowPath.PublishAsync(evt).ConfigureAwait(false);
                Interlocked.Increment(ref _hotQuoteConsumed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error draining remaining quote ring buffer events during disposal");
        }
    }
}
