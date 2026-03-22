using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Application.Tracing;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Shared;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

namespace Meridian.Application.Pipeline;

/// <summary>
/// High-throughput, backpressured pipeline that decouples producers from storage sinks.
/// Includes periodic flushing, capacity monitoring, performance metrics, and optional
/// Write-Ahead Log (WAL) integration for crash-safe durability.
/// </summary>
/// <remarks>
/// When a <see cref="WriteAheadLog"/> is provided, the pipeline ensures events are
/// persisted to the WAL before being written to the primary storage sink. On startup,
/// <see cref="RecoverAsync"/> replays any uncommitted WAL records to the sink, preventing
/// data loss from crashes. The consumer writes each event to the WAL, then to the sink,
/// and commits the WAL after each batch is flushed. Both <see cref="TryPublish"/> and
/// <see cref="PublishAsync"/> defer WAL writes to the consumer to ensure each event is
/// recorded exactly once, preventing duplicate records during recovery.
/// </remarks>
public sealed class EventPipeline : IMarketEventPublisher, IBackpressureSignal, IAsyncDisposable, IFlushable
{
    private readonly Channel<TracedMarketEvent> _channel;
    private readonly IStorageSink _sink;
    private readonly WriteAheadLog? _wal;
    private readonly ILogger<EventPipeline> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _consumers;
    private readonly Task? _flusher;
    private readonly int _capacity;
    private readonly BoundedChannelFullMode _fullMode;
    private readonly bool _metricsEnabled;
    private readonly DroppedEventAuditTrail? _auditTrail;
    private readonly IEventMetrics _metrics;
    private readonly IEventValidator? _validator;
    private readonly DeadLetterSink? _deadLetterSink;
    private readonly PersistentDedupLedger? _dedupLedger;
    private readonly int _consumerCount;
    private int _disposed;
    private int _activeConsumers;
    private int _finalFlushStarted;

    // Performance metrics
    private long _publishedCount;
    private long _droppedCount;
    private long _consumedCount;
    private long _recoveredCount;
    private long _rejectedCount;
    private long _deduplicatedCount;
    private long _peakQueueSize;
    private long _totalProcessingTimeNs;
    private long _lastFlushTimestamp;
    private bool _highWaterMarkWarned;

    // WAL tracking: last sequence committed to primary storage
    private long _lastCommittedWalSequence;

    // Configuration
    private readonly TimeSpan _flushInterval;
    private readonly int _batchSize;
    private readonly int _maxAdaptiveBatchSize;
    private readonly bool _enablePeriodicFlush;
    private readonly TimeSpan _sinkFlushTimeout;

    // Pre-computed integer thresholds to avoid floating-point division on every TryPublish
    private readonly int _highWaterMark80;
    private readonly int _highWaterMark50;

    // Reader.Count sampling: check queue size every 64 events to reduce per-publish overhead.
    // Uses a bitmask (& 63) for a branch-friendly, division-free sampling check.
    private const int ReaderCountSampleMask = 63; // sample every 64th event

    /// <summary>
    /// Default maximum time to wait for the final flush during shutdown before giving up.
    /// Prevents the consumer task from hanging indefinitely if the sink is unresponsive.
    /// </summary>
    private static readonly TimeSpan DefaultFinalFlushTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default per-call sink flush timeout for periodic flushes.
    /// Prevents a hung sink from stalling the pipeline indefinitely.
    /// </summary>
    private static readonly TimeSpan DefaultSinkFlushTimeout = TimeSpan.FromSeconds(60);

    private readonly TimeSpan _finalFlushTimeout;
    private readonly TimeSpan _disposeTaskTimeout;

    /// <summary>
    /// Creates a new EventPipeline with configurable capacity and flush behavior.
    /// </summary>
    /// <param name="sink">The storage sink for persisting events.</param>
    /// <param name="capacity">Maximum number of events the queue can hold. Default is 100,000.</param>
    /// <param name="fullMode">Behavior when the queue is full. Default is DropOldest.</param>
    /// <param name="flushInterval">Interval between periodic flushes. Default is 5 seconds.</param>
    /// <param name="batchSize">Number of events to batch before writing. Default is 100.</param>
    /// <param name="enablePeriodicFlush">Whether to enable periodic flushing. Default is true.</param>
    /// <param name="logger">Optional logger for error reporting. When provided, enables logging for flush failures and disposal errors.</param>
    /// <param name="auditTrail">Optional audit trail for tracking dropped events.</param>
    /// <param name="wal">Optional Write-Ahead Log for crash-safe durability. When provided, events
    /// are written to the WAL before the primary sink. Call <see cref="RecoverAsync"/> on startup
    /// to replay any uncommitted records from a prior crash.</param>
    /// <param name="metrics">Optional event metrics for tracking pipeline throughput.</param>
    /// <param name="finalFlushTimeout">Optional timeout for the final flush during shutdown. Defaults to 30 seconds.</param>
    /// <param name="sinkFlushTimeout">Optional per-call timeout for periodic sink flushes. Prevents a hung sink from stalling the pipeline indefinitely. Defaults to 60 seconds.</param>
    /// <param name="validator">Optional event validator for pre-persistence validation.</param>
    /// <param name="deadLetterSink">Optional dead-letter sink for rejected events.</param>
    /// <param name="dedupLedger">Optional persistent deduplication ledger for suppressing duplicate events.</param>
    /// <param name="consumerCount">Requested number of consumer tasks for the slow path. Values greater than 1 are honored only when WAL, validation, dead-letter routing, and deduplication are disabled.</param>
    public EventPipeline(
        IStorageSink sink,
        int capacity = 100_000,
        BoundedChannelFullMode fullMode = BoundedChannelFullMode.DropOldest,
        TimeSpan? flushInterval = null,
        int batchSize = 100,
        bool enablePeriodicFlush = true,
        ILogger<EventPipeline>? logger = null,
        DroppedEventAuditTrail? auditTrail = null,
        WriteAheadLog? wal = null,
        IEventMetrics? metrics = null,
        TimeSpan? finalFlushTimeout = null,
        TimeSpan? sinkFlushTimeout = null,
        IEventValidator? validator = null,
        DeadLetterSink? deadLetterSink = null,
        PersistentDedupLedger? dedupLedger = null,
        int consumerCount = 1)
        : this(
            sink,
            new EventPipelinePolicy(capacity, fullMode),
            flushInterval,
            batchSize,
            enablePeriodicFlush,
            logger,
            auditTrail,
            wal,
            metrics,
            finalFlushTimeout,
            sinkFlushTimeout,
            validator,
            deadLetterSink,
            dedupLedger,
            consumerCount)
    {
    }

    /// <summary>
    /// Creates a new EventPipeline with a shared policy for capacity and backpressure.
    /// </summary>
    /// <param name="sink">The storage sink for persisting events.</param>
    /// <param name="policy">The pipeline policy controlling channel capacity and backpressure.</param>
    /// <param name="flushInterval">Interval between periodic flushes. Default is 5 seconds.</param>
    /// <param name="batchSize">Number of events to batch before writing. Default is 100.</param>
    /// <param name="enablePeriodicFlush">Whether to enable periodic flushing. Default is true.</param>
    /// <param name="logger">Optional logger for error reporting.</param>
    /// <param name="auditTrail">Optional audit trail for tracking dropped events.</param>
    /// <param name="wal">Optional Write-Ahead Log for crash-safe durability.</param>
    /// <param name="metrics">Optional event metrics for tracking pipeline throughput.</param>
    /// <param name="finalFlushTimeout">Optional timeout for the final flush during shutdown. Defaults to 30 seconds.</param>
    /// <param name="sinkFlushTimeout">Optional per-call timeout for periodic sink flushes. Defaults to 60 seconds.</param>
    /// <param name="validator">Optional event validator. When provided, events that fail validation
    /// are routed to the <paramref name="deadLetterSink"/> and excluded from primary storage.</param>
    /// <param name="deadLetterSink">Optional dead-letter sink for events rejected by the validator.</param>
    /// <param name="dedupLedger">Optional persistent deduplication ledger for suppressing duplicate events.</param>
    /// <param name="consumerCount">Requested number of consumer tasks for the slow path. Values greater than 1 are honored only when WAL, validation, dead-letter routing, and deduplication are disabled.</param>
    public EventPipeline(
        IStorageSink sink,
        EventPipelinePolicy policy,
        TimeSpan? flushInterval = null,
        int batchSize = 100,
        bool enablePeriodicFlush = true,
        ILogger<EventPipeline>? logger = null,
        DroppedEventAuditTrail? auditTrail = null,
        WriteAheadLog? wal = null,
        IEventMetrics? metrics = null,
        TimeSpan? finalFlushTimeout = null,
        TimeSpan? sinkFlushTimeout = null,
        IEventValidator? validator = null,
        DeadLetterSink? deadLetterSink = null,
        PersistentDedupLedger? dedupLedger = null,
        int consumerCount = 1)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? NullLogger<EventPipeline>.Instance;
        _auditTrail = auditTrail;
        _wal = wal;
        _metrics = metrics ?? new DefaultEventMetrics();
        _validator = validator;
        _deadLetterSink = deadLetterSink;
        _dedupLedger = dedupLedger;
        _finalFlushTimeout = finalFlushTimeout ?? DefaultFinalFlushTimeout;
        _sinkFlushTimeout = sinkFlushTimeout ?? DefaultSinkFlushTimeout;
        _disposeTaskTimeout = _finalFlushTimeout + TimeSpan.FromSeconds(5);
        if (policy is null)
            throw new ArgumentNullException(nameof(policy));
        _capacity = policy.Capacity;
        _fullMode = policy.FullMode;
        _metricsEnabled = policy.EnableMetrics;
        _highWaterMark80 = (int)(policy.Capacity * 0.8);
        _highWaterMark50 = policy.Capacity / 2;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _batchSize = Math.Max(1, batchSize);
        _maxAdaptiveBatchSize = Math.Max(_batchSize, _batchSize * 4);
        _enablePeriodicFlush = enablePeriodicFlush;
        _consumerCount = DetermineConsumerCount(consumerCount, _wal, _validator, _deadLetterSink, _dedupLedger, _logger);

        _channel = policy.CreateChannel<TracedMarketEvent>(singleReader: _consumerCount == 1, singleWriter: false);

        // Start one or more long-running consumers. Multi-consumer mode is enabled only
        // when WAL / dedup / validation side effects are disabled so ordering-sensitive
        // persistence remains single-threaded by default.
        _consumers = Enumerable.Range(0, _consumerCount)
            .Select(_ => Task.Factory.StartNew(
                () => ConsumeAsync(),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        // Start periodic flusher if enabled
        if (_enablePeriodicFlush)
        {
            _flusher = PeriodicFlushAsync();
        }

        Interlocked.Exchange(ref _lastFlushTimestamp, Stopwatch.GetTimestamp());
    }

    private static int DetermineConsumerCount(
        int requestedConsumerCount,
        WriteAheadLog? wal,
        IEventValidator? validator,
        DeadLetterSink? deadLetterSink,
        PersistentDedupLedger? dedupLedger,
        ILogger<EventPipeline> logger)
    {
        if (requestedConsumerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(requestedConsumerCount), "Consumer count must be at least 1.");

        if (requestedConsumerCount == 1)
            return 1;

        if (wal is not null || validator is not null || deadLetterSink is not null || dedupLedger is not null)
        {
            logger.LogInformation(
                "EventPipeline requested {RequestedConsumers} consumers, but advanced persistence features require single-consumer mode. Falling back to 1 consumer.",
                requestedConsumerCount);
            return 1;
        }

        return requestedConsumerCount;
    }

    #region Public Properties - Pipeline Statistics

    /// <summary>Gets the total number of events successfully published to the pipeline.</summary>
    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    /// <summary>Gets the total number of events dropped due to backpressure.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>Gets the total number of events consumed and written to storage.</summary>
    public long ConsumedCount => Interlocked.Read(ref _consumedCount);

    /// <summary>Gets the total number of events recovered from WAL on startup.</summary>
    public long RecoveredCount => Interlocked.Read(ref _recoveredCount);

    /// <summary>Gets the total number of events rejected by the validator and sent to the dead-letter sink.</summary>
    public long RejectedCount => Interlocked.Read(ref _rejectedCount);

    /// <summary>Gets the total number of duplicate events filtered by the dedup ledger.</summary>
    public long DeduplicatedCount => Interlocked.Read(ref _deduplicatedCount);

    /// <summary>Gets whether deduplication is enabled for this pipeline.</summary>
    public bool IsDeduplicationEnabled => _dedupLedger != null;

    /// <summary>Gets the peak queue size observed during operation.</summary>
    public long PeakQueueSize => Interlocked.Read(ref _peakQueueSize);

    /// <summary>Gets the current number of events in the queue.</summary>
    public int CurrentQueueSize => _channel.Reader.Count;

    /// <summary>Gets the queue capacity utilization as a percentage (0-100).</summary>
    public double QueueUtilization => (double)CurrentQueueSize / _capacity * 100;

    /// <summary>Gets the average processing time per event in microseconds.</summary>
    public double AverageProcessingTimeUs
    {
        get
        {
            var consumed = Interlocked.Read(ref _consumedCount);
            if (consumed == 0)
                return 0;
            var totalNs = Interlocked.Read(ref _totalProcessingTimeNs);
            return totalNs / 1000.0 / consumed;
        }
    }

    /// <summary>Gets the time since the last flush operation.</summary>
    public TimeSpan TimeSinceLastFlush
    {
        get
        {
            var lastTs = Interlocked.Read(ref _lastFlushTimestamp);
            return TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - lastTs) *
                (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
        }
    }

    /// <summary>Gets whether a WAL is configured for this pipeline.</summary>
    public bool IsWalEnabled => _wal != null;

    /// <summary>Gets whether event validation is enabled for this pipeline.</summary>
    public bool IsValidationEnabled => _validator != null;

    /// <summary>
    /// Returns <see langword="true"/> when the queue utilization has reached or exceeded 80 %.
    /// Upstream producers should observe this signal and slow down publishing to avoid data loss.
    /// </summary>
    public bool IsUnderPressure => _highWaterMarkWarned;

    // IBackpressureSignal: return a 0–1 fraction while the public property keeps 0–100 for
    // backwards compatibility with callers that already use it for display purposes.
    double IBackpressureSignal.QueueUtilization => QueueUtilization / 100.0;

    #endregion

    /// <summary>
    /// Recovers uncommitted events from the WAL and replays them to the storage sink.
    /// Call this method once on startup, before publishing new events, to ensure
    /// data from a prior crash is not lost.
    /// </summary>
    /// <remarks>
    /// This method initializes the WAL and reads any records that were written
    /// but not committed (i.e., not yet confirmed persisted to the primary sink).
    /// Each recovered event is written to the sink and then the WAL is committed.
    /// If no WAL is configured, this method is a no-op.
    /// </remarks>
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        if (_wal == null)
            return;

        // [3.1] E2E trace propagation: wrap recovery in a dedicated activity so WAL replay
        // appears as a structured span in distributed traces and can be correlated to the
        // startup sequence.
        using var recoveryActivity = MarketDataTracing.StartWalRecoveryActivity();

        _logger.LogInformation("Initializing WAL for pipeline recovery");
        await _wal.InitializeAsync(ct).ConfigureAwait(false);

        var recovered = 0;
        var skipped = 0;
        long maxRecoveredSequence = 0;

        await foreach (var walRecord in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
        {
            if (walRecord.RecordType == "COMMIT")
                continue;

            try
            {
                var evt = walRecord.DeserializePayload<MarketEvent>();
                if (evt != null)
                {
                    // [3.4] Idempotent writes: skip events already persisted to the sink.
                    // This guards against duplicates when the sink was partially flushed
                    // before the crash and the dedup ledger survived on disk.
                    if (_dedupLedger != null && await _dedupLedger.IsDuplicateAsync(evt, ct).ConfigureAwait(false))
                    {
                        skipped++;
                        maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                        continue;
                    }

                    await _sink.AppendAsync(evt, ct).ConfigureAwait(false);
                    maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                    recovered++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize WAL record {Sequence} during recovery", walRecord.Sequence);
            }
        }

        recoveryActivity?.SetTag("pipeline.recovered_count", recovered);
        recoveryActivity?.SetTag("pipeline.skipped_dedup_count", skipped);

        if (recovered > 0 || skipped > 0)
        {
            if (recovered > 0)
                await _sink.FlushAsync(ct).ConfigureAwait(false);

            // [1.2] WAL-sink transaction: update local sequence tracking BEFORE committing the
            // WAL.  If CommitAsync fails (e.g. transient disk error), _lastCommittedWalSequence
            // still reflects the successfully flushed extent so the next startup does not
            // re-replay already-persisted events.  The commit itself is best-effort: a failure
            // here is non-fatal because sink data is already durable.
            _lastCommittedWalSequence = maxRecoveredSequence;

            try
            {
                await _wal.CommitAsync(maxRecoveredSequence, ct).ConfigureAwait(false);
                await _wal.TruncateAsync(maxRecoveredSequence, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "WAL commit/truncate failed after successful sink flush during recovery (sequence {Seq}). " +
                    "Sink data is safe; WAL records may be replayed again on the next startup but will be " +
                    "deduplicated if a dedup ledger is configured",
                    maxRecoveredSequence);
            }

            Interlocked.Add(ref _recoveredCount, recovered);

            _logger.LogInformation(
                "Recovered {RecoveredCount} uncommitted events from WAL through sequence {MaxSequence} ({SkippedCount} skipped as duplicates)",
                recovered, maxRecoveredSequence, skipped);
        }
        else
        {
            _logger.LogInformation("WAL recovery complete, no uncommitted events found");
        }

        // Emit WAL recovery metrics to Prometheus
        PrometheusMetrics.RecordWalRecovery(
            _wal.LastRecoveryEventCount,
            _wal.LastRecoveryDurationMs / 1000.0);
    }

    /// <summary>
    /// Attempts to publish an event to the pipeline without blocking.
    /// Returns false if the queue is full (event will be dropped based on FullMode).
    /// </summary>
    /// <remarks>
    /// When WAL is enabled, the WAL write occurs in the consumer task (not at publish time)
    /// to preserve the non-blocking contract of this method. Events are WAL-protected
    /// once they reach the consumer. For full publish-time WAL protection, use
    /// <see cref="PublishAsync"/> instead.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublish(in MarketEvent evt)
    {
        var tracedEvent = CaptureTraceContext(evt);

        // For DropWrite mode, TryWrite returns true even when the new item is
        // silently discarded. Pre-check capacity to detect these silent drops.
        // (DropOldest/DropNewest evict old items, so the new item IS accepted.)
        if (_fullMode == BoundedChannelFullMode.DropWrite && _channel.Reader.Count >= _capacity)
        {
            // Channel is at capacity — the item will be silently discarded by the
            // bounded channel. Still call TryWrite so the channel can apply its
            // policy, but track the event as dropped.
            _channel.Writer.TryWrite(tracedEvent);
            RecordDrop(in evt);
            return false;
        }

        var written = _channel.Writer.TryWrite(tracedEvent);

        if (written)
        {
            var count = Interlocked.Increment(ref _publishedCount);
            if (_metricsEnabled)
            {
                _metrics.IncPublished();
            }

            // Sample Reader.Count every 64 events — Reader.Count acquires an internal lock
            // on BoundedChannel, so reading it on every publish is expensive at high throughput.
            // Peak tracking and high-water mark warnings tolerate a small sampling delay.
            if ((count & ReaderCountSampleMask) == 0)
            {
                // Read queue size once — avoid calling it multiple times per sample.
                var currentSize = _channel.Reader.Count;

                // Update peak using a lock-free compare-and-swap loop.
                var peak = Volatile.Read(ref _peakQueueSize);
                if (currentSize > peak)
                {
                    Interlocked.CompareExchange(ref _peakQueueSize, currentSize, peak);
                }

                // Use integer comparison instead of floating-point division.
                // _highWaterMark80 = (int)(capacity * 0.8), _highWaterMark50 = capacity / 2
                if (currentSize >= _highWaterMark80 && !_highWaterMarkWarned)
                {
                    _highWaterMarkWarned = true;
                    var utilization = (double)currentSize / _capacity;
                    _logger.LogWarning(
                        "Pipeline queue utilization at {Utilization:P0} ({CurrentSize}/{Capacity}). Events may be dropped if queue fills. Consider increasing capacity or reducing event rate",
                        utilization, currentSize, _capacity);
                }
                else if (_highWaterMarkWarned && currentSize < _highWaterMark50)
                {
                    _highWaterMarkWarned = false;
                    var utilization = (double)currentSize / _capacity;
                    _logger.LogInformation("Pipeline queue utilization recovered to {Utilization:P0}", utilization);
                }
            }
        }
        else
        {
            RecordDrop(in evt);
        }

        return written;
    }

    /// <summary>
    /// Attempts to publish an event and returns a <see cref="PublishResult"/> that describes
    /// the outcome in detail — accepted, accepted under pressure, or dropped.
    /// </summary>
    /// <remarks>
    /// Use this method when the caller needs to react to backpressure (e.g. pause a subscription,
    /// log a drop, or adjust polling rate). For fire-and-forget callers the original
    /// <see cref="TryPublish"/> remains the recommended hot-path method.
    /// </remarks>
    public PublishResult TryPublishWithResult(in MarketEvent evt)
    {
        var accepted = TryPublish(in evt);
        if (!accepted)
            return PublishResult.Dropped;

        return _highWaterMarkWarned ? PublishResult.AcceptedUnderPressure : PublishResult.Accepted;
    }

    /// <summary>Records a dropped event — shared by DropWrite pre-check and Wait-mode TryWrite failure.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)] // keep off the hot inlined path
    private void RecordDrop(in MarketEvent evt)
    {
        Interlocked.Increment(ref _droppedCount);
        if (_metricsEnabled)
        {
            _metrics.IncDropped();
        }

        if (_auditTrail != null)
        {
            _auditTrail.RecordDroppedEventAsync(evt, "backpressure_queue_full")
                .ObserveException(operation: "audit trail recording dropped event");
        }
    }

    /// <summary>Cached enum name lookup — avoids Enum.ToString() allocation per event.</summary>
    private static readonly string[] EventTypeNames = Enum.GetValues<MarketEventType>()
        .Select(e => e.ToString())
        .ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetEventTypeName(MarketEventType type)
    {
        var index = (int)type;
        return (uint)index < (uint)EventTypeNames.Length ? EventTypeNames[index] : type.ToString();
    }

    /// <summary>
    /// Publishes an event to the pipeline, waiting if necessary.
    /// When WAL is enabled, the WAL write is performed by the consumer task
    /// to avoid duplicate records during recovery.
    /// </summary>
    public async ValueTask PublishAsync(MarketEvent evt, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(CaptureTraceContext(evt), ct).ConfigureAwait(false);
        Interlocked.Increment(ref _publishedCount);
        if (_metricsEnabled)
        {
            _metrics.IncPublished();
        }
    }

    /// <summary>
    /// Signals that no more events will be published.
    /// </summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// Waits for the consumer to process all currently-queued events, then forces
    /// an immediate flush of buffered data to storage.
    /// </summary>
    /// <remarks>
    /// If events were dropped due to backpressure during the flush window, a warning
    /// is logged. The flush still writes all events that <em>were</em> consumed to
    /// storage — it does not suppress the flush because of drops — but callers should
    /// treat the warning as an indication that the result set is incomplete.
    /// </remarks>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        // Capture the drop baseline so we can report new drops that occurred
        // during this flush window (indicates data loss the caller may not expect).
        var droppedAtStart = Interlocked.Read(ref _droppedCount);

        // Wait for the consumer to process all currently-queued events.
        // IMPORTANT: We only wait for _consumed_ events to reach the target,
        // NOT consumed + dropped. Dropped events were never consumed and are
        // NOT in storage, so counting them as "accounted for" would let
        // FlushAsync return success while data is silently missing.
        // The secondary check (channel empty + consumer idle) handles the
        // DropOldest case where published events are silently discarded by
        // the channel — those events will never be consumed, but we should
        // wait until the channel is drained and the consumer is quiescent.
        var targetPublished = Interlocked.Read(ref _publishedCount);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var consumed = Interlocked.Read(ref _consumedCount);

            // All published events have been consumed (accounting for rejected events
            // which were read from the channel but not persisted to the primary sink)
            if (consumed + Interlocked.Read(ref _rejectedCount) >= targetPublished)
                break;

            // Channel is empty — check if the consumer has finished its batch.
            // This handles the DropOldest case where events were silently discarded
            // by the channel before reaching the consumer.
            if (_channel.Reader.Count == 0 && Volatile.Read(ref _activeConsumers) == 0)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                var newConsumed = Interlocked.Read(ref _consumedCount);
                if (_channel.Reader.Count == 0 && Volatile.Read(ref _activeConsumers) == 0 && newConsumed == consumed)
                    break; // Consumer is idle, nothing left to process
            }
            else
            {
                await Task.Delay(1, ct).ConfigureAwait(false);
            }
        }

        await _sink.FlushAsync(ct).ConfigureAwait(false);
        Interlocked.Exchange(ref _lastFlushTimestamp, Stopwatch.GetTimestamp());

        // Warn callers if events were dropped during this flush window so they
        // understand that the returned flush is not a full-fidelity confirmation.
        var newDrops = Interlocked.Read(ref _droppedCount) - droppedAtStart;
        if (newDrops > 0)
        {
            _logger.LogWarning(
                "FlushAsync completed but {DroppedCount} event(s) were dropped due to backpressure during this flush window and are NOT in storage. " +
                "Consider increasing pipeline capacity or reducing event rate.",
                newDrops);
        }
    }

    /// <summary>
    /// Gets a snapshot of current pipeline statistics.
    /// </summary>
    public PipelineStatistics GetStatistics()
    {
        return new PipelineStatistics(
            PublishedCount: PublishedCount,
            DroppedCount: DroppedCount,
            ConsumedCount: ConsumedCount,
            CurrentQueueSize: CurrentQueueSize,
            PeakQueueSize: PeakQueueSize,
            QueueCapacity: _capacity,
            QueueUtilization: QueueUtilization,
            AverageProcessingTimeUs: AverageProcessingTimeUs,
            TimeSinceLastFlush: TimeSinceLastFlush,
            Timestamp: DateTimeOffset.UtcNow,
            HighWaterMarkWarned: _highWaterMarkWarned
        );
    }

    private async Task ConsumeAsync(CancellationToken ct = default)
    {
        // Set thread priority for consistent throughput
        ThreadingUtilities.SetAboveNormalPriority();

        try
        {
            var batchBuffer = new List<TracedMarketEvent>(_maxAdaptiveBatchSize);

            while (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _activeConsumers);
                var startTs = Stopwatch.GetTimestamp();

                try
                {
                    var targetBatchSize = GetAdaptiveBatchSize();

                    // Drain up to _batchSize events from the channel
                    batchBuffer.Clear();
                    while (batchBuffer.Count < targetBatchSize && _channel.Reader.TryRead(out var evt))
                    {
                        batchBuffer.Add(evt);
                    }

                    // [3.1] E2E trace propagation: start a per-batch activity so each consume
                    // cycle appears as a structured span in distributed traces.
                    using var batchActivity = MarketDataTracing.StartBatchConsumeActivity(batchBuffer.Count);

                    long maxWalSequence = _lastCommittedWalSequence;

                    // Write each event: dedup → validate → WAL (if enabled) → sink
                    for (var i = 0; i < batchBuffer.Count; i++)
                    {
                        var tracedEvent = batchBuffer[i];
                        var evt = tracedEvent.Event;
                        using var processActivity = MarketDataTracing.StartProcessActivity(
                            evt.Type.ToString(),
                            evt.EffectiveSymbol,
                            tracedEvent.TraceContext.ParentContext);
                        processActivity?.SetTag("event.source", evt.Source);
                        processActivity?.SetTag("event.sequence", evt.Sequence);

                        using var logScope = _logger.BeginScope(CreateLogScope(evt, tracedEvent.TraceContext, processActivity));

                        // Deduplication check (when a dedup ledger is configured)
                        if (_dedupLedger != null)
                        {
                            if (await _dedupLedger.IsDuplicateAsync(evt, _cts.Token).ConfigureAwait(false))
                            {
                                Interlocked.Increment(ref _deduplicatedCount);
                                continue; // Skip duplicate events
                            }
                        }

                        // Validate event before persistence (when a validator is configured)
                        if (_validator != null)
                        {
                            var validationResult = _validator.Validate(in evt);
                            if (!validationResult.IsValid)
                            {
                                Interlocked.Increment(ref _rejectedCount);
                                if (_deadLetterSink != null)
                                {
                                    await _deadLetterSink.RecordAsync(evt, validationResult.Errors, _cts.Token).ConfigureAwait(false);
                                }
                                continue; // Skip persisting invalid events
                            }
                        }

                        if (_wal != null)
                        {
                            var walRecord = await _wal.AppendAsync(evt, GetEventTypeName(evt.Type), _cts.Token).ConfigureAwait(false);
                            maxWalSequence = Math.Max(maxWalSequence, walRecord.Sequence);
                        }

                        using var storageActivity = MarketDataTracing.StartStorageActivity(
                            _sink.GetType().Name,
                            evt.EffectiveSymbol,
                            processActivity?.Context ?? tracedEvent.TraceContext.ParentContext);
                        storageActivity?.SetTag("event.type", evt.Type.ToString());
                        storageActivity?.SetTag("event.source", evt.Source);

                        await _sink.AppendAsync(evt, _cts.Token).ConfigureAwait(false);
                    }

                    // [1.2] WAL-sink transaction: flush the sink first, then update local sequence
                    // tracking BEFORE committing the WAL.  Ordering matters:
                    //   1. Sink.FlushAsync  – makes events durable in the sink.
                    //   2. _lastCommittedWalSequence update – prevents re-flushing the same batch
                    //      on the next iteration if CommitAsync fails.
                    //   3. Wal.CommitAsync  – best-effort cleanup; failure here is non-fatal
                    //      because the sink already has the data and the dedup ledger will filter
                    //      any re-played records on a future startup.
                    if (_wal != null && maxWalSequence > _lastCommittedWalSequence)
                    {
                        await _sink.FlushAsync(_cts.Token).ConfigureAwait(false);
                        _lastCommittedWalSequence = maxWalSequence;
                        try
                        {
                            await _wal.CommitAsync(maxWalSequence, _cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "WAL commit failed after successful sink flush for sequence {Seq}. " +
                                "Events are safe in the sink; WAL records may be replayed on next startup " +
                                "but will be deduplicated if a dedup ledger is configured",
                                maxWalSequence);
                        }
                    }

                    Interlocked.Add(ref _consumedCount, batchBuffer.Count);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeConsumers);
                }

                // Track processing time amortized across the batch
                var elapsedNs = (long)(HighResolutionTimestamp.GetElapsedNanoseconds(startTs));
                Interlocked.Add(ref _totalProcessingTimeNs, elapsedNs);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (Interlocked.Exchange(ref _finalFlushStarted, 1) == 0)
            {
                // Final flush on shutdown with timeout to prevent indefinite hang
                try
                {
                    using var flushTimeoutCts = new CancellationTokenSource(_finalFlushTimeout);
                    await _sink.FlushAsync(flushTimeoutCts.Token).ConfigureAwait(false);

                    // Final WAL commit for any remaining uncommitted records
                    if (_wal != null && _lastCommittedWalSequence > 0)
                    {
                        await _wal.CommitAsync(_lastCommittedWalSequence, flushTimeoutCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "Final flush timed out after {TimeoutSeconds}s during pipeline shutdown. Consumed {ConsumedCount} events before timeout - some buffered data may be lost",
                        _finalFlushTimeout.TotalSeconds, _consumedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Final flush failed during pipeline shutdown. Consumed {ConsumedCount} events before failure - potential data loss", _consumedCount);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetAdaptiveBatchSize()
    {
        if (_consumerCount > 1)
            return _maxAdaptiveBatchSize;

        var queueSize = _channel.Reader.Count;
        if (queueSize >= _highWaterMark80)
            return _maxAdaptiveBatchSize;

        if (queueSize >= _highWaterMark50)
            return Math.Min(_maxAdaptiveBatchSize, _batchSize * 2);

        return _batchSize;
    }

    private async Task PeriodicFlushAsync(CancellationToken ct = default)
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(_flushInterval, _cts.Token).ConfigureAwait(false);

                try
                {
                    // Use a per-flush timeout combined with the pipeline cancellation token
                    // to prevent a hung sink from stalling the pipeline indefinitely.
                    using var flushTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    flushTimeoutCts.CancelAfter(_sinkFlushTimeout);

                    await _sink.FlushAsync(flushTimeoutCts.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _lastFlushTimestamp, Stopwatch.GetTimestamp());

                    // Periodically truncate committed WAL files to reclaim disk space
                    if (_wal != null && _lastCommittedWalSequence > 0)
                    {
                        await _wal.TruncateAsync(_lastCommittedWalSequence, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Sink flush timed out — log and continue so the pipeline stays alive.
                    _logger.LogWarning(
                        "Periodic flush timed out after {TimeoutSeconds}s. " +
                        "Sink may be slow or unresponsive. Queue size: {QueueSize}, consumed: {ConsumedCount}. " +
                        "Check storage health.",
                        _sinkFlushTimeout.TotalSeconds, CurrentQueueSize, _consumedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Periodic flush failed. Queue size: {QueueSize}, consumed: {ConsumedCount}. May indicate storage issues", CurrentQueueSize, _consumedCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return; // Already disposed

        // Signal no more events will be published so the consumer can drain
        // remaining items from the channel and exit naturally.
        _channel.Writer.TryComplete();

        // Wait for consumer to drain the channel. Only force-cancel as a
        // timeout fallback to prevent indefinite hang.
        try
        {
            var allConsumers = Task.WhenAll(_consumers);
            var completed = await Task.WhenAny(
                allConsumers,
                Task.Delay(_disposeTaskTimeout)).ConfigureAwait(false);

            if (completed != allConsumers)
            {
                _logger.LogWarning(
                    "{ConsumerCount} consumer task(s) did not complete within {TimeoutSeconds}s during disposal. " +
                    "Published: {PublishedCount}, consumed: {ConsumedCount}. Force-cancelling",
                    _consumerCount, _disposeTaskTimeout.TotalSeconds, _publishedCount, _consumedCount);

                await _cts.CancelAsync().ConfigureAwait(false);

                // Give a short grace period after force-cancel
                await Task.WhenAny(allConsumers, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            }
            else
            {
                await allConsumers.ConfigureAwait(false); // Observe any exception
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Consumer task failed during disposal. Published: {PublishedCount}, consumed: {ConsumedCount}", _publishedCount, _consumedCount);
        }

        // Cancel the CTS to stop the periodic flusher
        if (!_cts.IsCancellationRequested)
            await _cts.CancelAsync().ConfigureAwait(false);

        if (_flusher is not null)
        {
            try
            {
                var completed = await Task.WhenAny(
                    _flusher,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

                if (completed != _flusher)
                {
                    _logger.LogWarning("Flusher task did not complete within 5s during disposal. Proceeding with disposal");
                }
                else
                {
                    await _flusher.ConfigureAwait(false); // Observe any exception
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Flusher task failed during disposal. Last flush was {TimeSinceLastFlush} ago", TimeSinceLastFlush);
            }
        }

        _cts.Dispose();
        await _sink.DisposeAsync().ConfigureAwait(false);

        if (_wal != null)
        {
            await _wal.DisposeAsync().ConfigureAwait(false);
        }

        if (_auditTrail != null)
        {
            await _auditTrail.DisposeAsync().ConfigureAwait(false);
        }

        if (_deadLetterSink != null)
        {
            await _deadLetterSink.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the dropped event audit trail, if configured.
    /// </summary>
    public DroppedEventAuditTrail? AuditTrail => _auditTrail;

    /// <summary>
    /// Gets the queue capacity.
    /// </summary>
    public int QueueCapacity => _capacity;

    /// <summary>
    /// Gets the injected event metrics instance.
    /// </summary>
    public IEventMetrics EventMetrics => _metrics;

    private static TracedMarketEvent CaptureTraceContext(in MarketEvent evt)
        => new(evt, EventTraceContext.CaptureCurrent());

    private static Dictionary<string, object?> CreateLogScope(
        MarketEvent evt,
        EventTraceContext traceContext,
        Activity? activity)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = traceContext.CorrelationId ?? activity?.TraceId.ToString(),
            ["TraceId"] = activity?.TraceId.ToString() ?? (traceContext.HasParent ? traceContext.ParentContext.TraceId.ToString() : null),
            ["SpanId"] = activity?.SpanId.ToString(),
            ["EventType"] = evt.Type.ToString(),
            ["EventSource"] = evt.Source,
            ["Symbol"] = evt.EffectiveSymbol,
            ["Sequence"] = evt.Sequence
        };
    }

    private readonly record struct TracedMarketEvent(
        MarketEvent Event,
        EventTraceContext TraceContext);
}

/// <summary>
/// Snapshot of pipeline performance statistics.
/// </summary>
public readonly record struct PipelineStatistics(
    long PublishedCount,
    long DroppedCount,
    long ConsumedCount,
    int CurrentQueueSize,
    long PeakQueueSize,
    int QueueCapacity,
    double QueueUtilization,
    double AverageProcessingTimeUs,
    TimeSpan TimeSinceLastFlush,
    DateTimeOffset Timestamp,
    bool HighWaterMarkWarned = false
);
