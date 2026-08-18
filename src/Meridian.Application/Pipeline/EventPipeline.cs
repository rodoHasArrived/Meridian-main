using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using Meridian.Application.Monitoring;
using Meridian.Core.Performance;
using Meridian.Core.Services;
using Meridian.DataIntegration.Etl;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Shared;
using Meridian.Platform.Tracing;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Contracts.Monitoring;
using Meridian.Contracts.Pipeline;
using Meridian.Core.Pipeline;

namespace Meridian.Application.Pipeline;

/// <summary>
/// High-throughput, backpressured pipeline that decouples producers from storage sinks.
/// Includes periodic flushing, capacity monitoring, performance metrics, and optional
/// Write-Ahead Log (WAL) integration for crash-safe durability.
/// </summary>
/// <remarks>
/// <para>
/// Producer-channel acceptance is <b>admission-only</b>: a successful <see cref="TryPublish"/>
/// or <see cref="PublishAsync"/> means the event entered the in-memory queue, not that it is
/// durable. Durability is established exclusively by the consumer, which processes each batch as
/// <c>validate → reserve → WAL append → WAL flush → sink append → sink flush →
/// dedup commit/flush → WAL commit</c>. A crash between admission and the WAL flush can drop
/// queued events; a crash after the WAL flush is recovered by <see cref="RecoverAsync"/>, which
/// replays uncommitted WAL records to the sink (at-least-once: a crash after the sink flush but
/// before the dedup commit may replay a duplicate, but can never lose an event).
/// </para>
/// <para>
/// When a dedup store is configured, duplicate suppression is reservation-based: identities are
/// claimed in memory during admission and durably committed (as version-2, durability-confirmed
/// entries) only after the sink flush. During recovery only durability-confirmed entries suppress
/// replay; legacy version-1 entries are untrusted and their records are replayed, then upgraded.
/// </para>
/// </remarks>
public sealed class EventPipeline : IMarketEventPublisher, IEtlEventPipeline, IBackpressureSignal, IAsyncDisposable, IFlushable, IFlushableQueueDiagnostics
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
    private readonly IDedupStore? _dedupLedger;
    private readonly int _consumerCount;
    private readonly bool _includePerEventLogScopes;
    private int _disposed;
    private int _activeConsumers;

    // Events in a batch retained for in-place retry: drained from the channel but not yet
    // counted as consumed. FlushAsync must treat them as outstanding work — the consumer's
    // active flag alone dips to zero between retry iterations.
    private int _retainedBatchEventCount;
    private int _finalFlushStarted;
    private long _consumerIterationFailures;
    private long _lastConsumerFaultTicks;

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

    // Recovery commits its sink/dedup/WAL horizon in bounded chunks so pending dedup
    // reservations never accumulate across an arbitrarily large uncommitted backlog.
    // Internal (not const) only so recovery tests can exercise the multi-chunk path.
    internal int RecoveryCommitBatchSize = 10_000;

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
        IDedupStore? dedupLedger = null,
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
        IDedupStore? dedupLedger = null,
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
        _includePerEventLogScopes = _logger.IsEnabled(LogLevel.Debug) || _logger.IsEnabled(LogLevel.Trace);
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
        IDedupStore? dedupLedger,
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

    /// <summary>Gets the approximate number of events pending flush during shutdown diagnostics.</summary>
    public long PendingFlushItemCount => CurrentQueueSize;

    /// <summary>Gets the queue capacity utilization as a percentage (0-100).</summary>
    public double QueueUtilization => (double)CurrentQueueSize / _capacity * 100;

    /// <summary>Gets the active bounded-channel full mode for this pipeline.</summary>
    public BoundedChannelFullMode QueueFullMode => _fullMode;

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


    /// <summary>
    /// Recovers uncommitted events from the WAL and replays them to the storage sink.
    /// Call this method once on startup, before publishing new events, to ensure
    /// data from a prior crash is not lost.
    /// </summary>
    /// <remarks>
    /// This method initializes the WAL and reads any records that were written but not committed
    /// (i.e., not yet confirmed persisted to the primary sink). Replay honours the dedup trust
    /// rules: only durability-confirmed (version 2) entries suppress a record; legacy version-1
    /// identities are untrusted here, so their records are replayed to the sink and upgraded to
    /// version 2 only after the sink flush succeeds. Sink failures propagate — recovery fails
    /// closed rather than acknowledging records it could not replay, and it likewise fails
    /// closed when a record's identity is claimed by an in-flight reservation this recovery
    /// pass does not hold (recovery must complete before live ingestion starts). A replay
    /// interrupted before its durable boundary releases all pending identity claims so it can
    /// be retried. If no WAL is configured, this method is a no-op.
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
        var unrecoverable = 0;
        long maxRecoveredSequence = 0;
        var chunkAppended = 0;
        var chunkProcessed = 0;
        var heldReservations = new List<DedupReservation>();
        var chunkClaimKeys = new HashSet<string>(StringComparer.Ordinal);

        // A cumulative WAL commit acknowledges every sequence at or below its horizon, so an
        // intermediate one is only safe when enumeration cannot yield a lower sequence later.
        // Segment names prove that (see RecoveryEnumerationIsSequenceOrdered); a clock rollback
        // across a rotation breaks it, and committing mid-enumeration would then let the next
        // pass filter still-unreplayed records as committed and lose them. When unproven, the
        // sink flush and dedup commits still run per chunk — keeping memory bounded — and only
        // the WAL horizon commit waits for the final, post-enumeration call.
        var sequenceOrderedEnumeration = _wal.RecoveryEnumerationIsSequenceOrdered();
        if (!sequenceOrderedEnumeration)
        {
            _logger.LogWarning(
                "WAL segment names do not prove sequence-ordered recovery enumeration; deferring the " +
                "cumulative WAL commit until every record has been replayed");
        }

        // Drives the current chunk through its durable boundary: sink flush, then dedup commit,
        // then a best-effort cumulative WAL commit through the horizon processed so far.
        async Task CommitRecoveredChunkAsync(bool finalChunk = false)
        {
            if (chunkAppended > 0)
                await _sink.FlushAsync(ct).ConfigureAwait(false);

            // Replayed identities become durability-confirmed only after the sink flush above.
            // An unavailable dedup store fails recovery closed here; the sink data is durable,
            // so a retried recovery replays at most a duplicate, never a loss.
            if (_dedupLedger != null && heldReservations.Count > 0)
            {
                await _dedupLedger.CommitDurableAsync(heldReservations, ct).ConfigureAwait(false);
                heldReservations.Clear();
            }

            // Committed identities suppress later duplicates as version-2 entries, so the
            // per-chunk claim-key set resets with the reservations it described.
            chunkClaimKeys.Clear();

            // [1.2] WAL-sink transaction: update local sequence tracking BEFORE committing the
            // WAL.  If CommitAsync fails (e.g. transient disk error), _lastCommittedWalSequence
            // still reflects the successfully flushed extent so the next startup does not
            // re-replay already-persisted events.  The commit itself is best-effort: a failure
            // here is non-fatal because sink data is already durable.
            if ((sequenceOrderedEnumeration || finalChunk) && maxRecoveredSequence > _lastCommittedWalSequence)
            {
                _lastCommittedWalSequence = maxRecoveredSequence;
                try
                {
                    await _wal.CommitAsync(maxRecoveredSequence, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "WAL commit failed after successful sink flush during recovery (sequence {Seq}). " +
                        "Sink data is safe; WAL records may be replayed again on the next startup but are " +
                        "suppressed by their durability-confirmed dedup entries when a dedup store is configured",
                        maxRecoveredSequence);
                }
            }

            // Publish the statistic per committed chunk: these events crossed their durable
            // boundary and their WAL records will not be enumerated again, so deferring the
            // count to the end of the pass would permanently underreport them if a later
            // chunk fails.
            if (chunkAppended > 0)
            {
                Interlocked.Add(ref _recoveredCount, chunkAppended);

                // Publish the replay metric here too, for the same reason: these records are
                // durable and will not be enumerated again, so a later chunk failure must not
                // strand their telemetry — a retry recovers only what remains and could never
                // restore it. The series takes a running total because IncTo raises to a
                // maximum rather than adding.
                PrometheusMetrics.RecordWalRecovery(
                    recovered,
                    _wal.LastRecoveryDurationMs / 1000.0);
            }

            chunkAppended = 0;
            chunkProcessed = 0;
        }

        try
        {
            await foreach (var walRecord in _wal.GetUncommittedRecordsAsync(ct).ConfigureAwait(false))
            {
                if (walRecord.RecordType == "COMMIT")
                    continue;

                chunkProcessed++;

                MarketEvent? evt;
                try
                {
                    evt = walRecord.DeserializePayload<MarketEvent>();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unrecoverable++;
                    if (_wal.CorruptionMode == WalCorruptionMode.Halt)
                    {
                        // Count the corruption before failing closed — the halt exists because
                        // corruption was found, so metrics must reflect it — without claiming
                        // the record was skipped (Halt does not skip it).
                        _wal.ReportUnreadablePayload(recordSkip: false);
                        throw new InvalidDataException(
                            $"WAL recovery halted: record {walRecord.Sequence} has a checksum-valid but " +
                            "undeserializable payload. Repair or remove the record before restarting.", ex);
                    }

                    // Route the semantic payload failure through the WAL corruption policy so
                    // Alert mode raises its monitoring signal, then advance the processed
                    // horizon: like checksum corruption, a non-Halt unreadable record is
                    // dropped once (with its signal) rather than re-alerting on every startup
                    // and pinning the WAL segment forever.
                    _wal.ReportUnreadablePayload();
                    _logger.LogError(ex,
                        "WAL record {Sequence} has an undeserializable payload and cannot be replayed; " +
                        "it will be dropped once the recovery horizon is committed",
                        walRecord.Sequence);
                    maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                    continue;
                }

                if (evt == null)
                {
                    unrecoverable++;
                    if (_wal.CorruptionMode == WalCorruptionMode.Halt)
                    {
                        _wal.ReportUnreadablePayload(recordSkip: false);
                        throw new InvalidDataException(
                            $"WAL recovery halted: record {walRecord.Sequence} deserialized to a null event. " +
                            "Repair or remove the record before restarting.");
                    }

                    _wal.ReportUnreadablePayload();
                    _logger.LogError(
                        "WAL record {Sequence} deserialized to a null event and cannot be replayed",
                        walRecord.Sequence);
                    maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                    continue;
                }

                if (_dedupLedger != null)
                {
                    // Recovery-scope lookup: only durability-confirmed identities (or an earlier
                    // record in this same pass) suppress the replay. Legacy version-1 identities
                    // fall through and are replayed, then upgraded at the chunk commit.
                    var reservationResult = await _dedupLedger
                        .TryReserveAsync(evt, DedupLookupScope.WalRecovery, ct).ConfigureAwait(false);
                    if (reservationResult.IsSuppressed)
                    {
                        // A pending claim only suppresses a record when this recovery pass holds
                        // it (an earlier record in the current chunk). An external, memory-only
                        // claim proves nothing durable: its holder may abandon without persisting,
                        // and committing the horizon past this record would lose its only WAL
                        // copy — fail closed instead; recovery must run before live ingestion.
                        if (reservationResult.Status == DedupReservationStatus.PendingElsewhere &&
                            (reservationResult.Reservation.Key is null ||
                             !chunkClaimKeys.Contains(reservationResult.Reservation.Key)))
                        {
                            throw new InvalidOperationException(
                                $"WAL recovery found an in-flight dedup claim for record {walRecord.Sequence} " +
                                "that this recovery pass does not hold. Recovery must complete before live " +
                                "ingestion starts; acknowledging the record past an external memory-only " +
                                "claim could lose it.");
                        }

                        skipped++;
                        maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                        continue;
                    }

                    heldReservations.Add(reservationResult.Reservation);
                    chunkClaimKeys.Add(reservationResult.Reservation.Key);
                }

                // Sink failures propagate: recovery must fail closed instead of acknowledging
                // records it could not replay.
                await _sink.AppendAsync(evt, ct).ConfigureAwait(false);
                maxRecoveredSequence = Math.Max(maxRecoveredSequence, walRecord.Sequence);
                recovered++;
                chunkAppended++;

                // Bounded chunks keep pending reservations and the recovery horizon from
                // accumulating across an arbitrarily large backlog — exactly the startup
                // scenario recovery exists for.
                if (chunkProcessed >= RecoveryCommitBatchSize)
                {
                    await CommitRecoveredChunkAsync().ConfigureAwait(false);
                }
            }

            recoveryActivity?.SetTag("pipeline.recovered_count", recovered);
            recoveryActivity?.SetTag("pipeline.skipped_dedup_count", skipped);
            recoveryActivity?.SetTag("pipeline.unrecoverable_count", unrecoverable);

            if (recovered > 0 || skipped > 0 || unrecoverable > 0)
            {
                // Enumeration is complete, so the full horizon is durably acknowledgeable even
                // when segment names could not prove ordering.
                await CommitRecoveredChunkAsync(finalChunk: true).ConfigureAwait(false);

                try
                {
                    await _wal.TruncateAsync(_lastCommittedWalSequence, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "WAL truncate failed after recovery committed through sequence {Seq}; " +
                        "committed segments are reclaimed by a later periodic truncation",
                        _lastCommittedWalSequence);
                }

                _logger.LogInformation(
                    "Recovered {RecoveredCount} uncommitted events from WAL through sequence {MaxSequence} " +
                    "({SkippedCount} suppressed as durability-confirmed duplicates, {UnrecoverableCount} unrecoverable)",
                    recovered, maxRecoveredSequence, skipped, unrecoverable);
            }
            else
            {
                _logger.LogInformation("WAL recovery complete, no uncommitted events found");
            }
        }
        catch
        {
            // Recovery failed before the current chunk's durable boundary: release its pending
            // identity claims so a retried recovery (or live ingress) can process these events
            // again. Earlier chunks already committed and are unaffected.
            if (_dedupLedger != null)
            {
                foreach (var reservation in heldReservations)
                {
                    _dedupLedger.Release(in reservation);
                }
            }

            throw;
        }

        // Final emission, covering a pass that committed no chunk (nothing to recover) so the
        // duration gauge still reflects it. Committed chunks already published their own
        // running total above, which survives a later chunk failure. The count is the number of
        // records actually replayed to the sink, never the WAL's scan tally: that tally counts
        // records whose payload proved undeserializable and were dropped as corruption, so
        // reporting it would claim recovery successes that never reached the sink and
        // contradict both the corruption counter and RecoveredCount.
        PrometheusMetrics.RecordWalRecovery(
            recovered,
            _wal.LastRecoveryDurationMs / 1000.0);
    }

    /// <summary>
    /// Attempts to publish an event to the pipeline without blocking.
    /// Returns false if the queue is full (event will be dropped based on FullMode).
    /// </summary>
    /// <remarks>
    /// A <see langword="true"/> result is admission-only, not a durable acknowledgement: the
    /// event entered the in-memory queue and becomes durable only once the consumer takes it
    /// through the WAL-flush/sink-flush boundary.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPublish(in MarketEvent evt)
    {
        // For DropWrite mode, TryWrite returns true even when the new item is
        // silently discarded. Pre-check capacity to detect these silent drops.
        // (DropOldest/DropNewest evict old items, so the new item IS accepted.)
        if (_fullMode == BoundedChannelFullMode.DropWrite && _channel.Reader.Count >= _capacity)
        {
            // Channel is at capacity — the item will be silently discarded by the
            // bounded channel. Still call TryWrite so the channel can apply its
            // policy, but track the event as dropped.
            _channel.Writer.TryWrite(CaptureTraceContext(evt));
            RecordDrop(in evt);
            return false;
        }

        var written = _channel.Writer.TryWrite(CaptureTraceContext(evt));

        if (written)
        {
            var count = Interlocked.Increment(ref _publishedCount);
            if (_metricsEnabled)
            {
                RecordPublishedMetrics(evt.Type);
            }
            TrackQueueDepthOnPublish(count);
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
    /// </summary>
    /// <remarks>
    /// Producer-channel acceptance is admission-only, not a durable acknowledgement: the event
    /// has entered the in-memory queue and becomes durable only when the consumer takes it
    /// through the WAL-flush/sink-flush boundary. Appending to the WAL at publish time is
    /// deliberately avoided — a publish-time record could receive a lower sequence than a
    /// concurrently consumed event and then be acknowledged by that batch's cumulative WAL
    /// commit while still sitting in the queue, silently losing it on a crash.
    /// </remarks>
    public async ValueTask PublishAsync(MarketEvent evt, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(CaptureTraceContext(evt), ct).ConfigureAwait(false);
        var count = Interlocked.Increment(ref _publishedCount);
        if (_metricsEnabled)
        {
            RecordPublishedMetrics(evt.Type);
        }
        TrackQueueDepthOnPublish(count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordPublishedMetrics(MarketEventType type)
    {
        _metrics.IncPublished();

        switch (type)
        {
            case MarketEventType.Trade:
            case MarketEventType.OptionTrade:
            case MarketEventType.HistoricalTrade:
                _metrics.IncTrades();
                break;
            case MarketEventType.BboQuote:
            case MarketEventType.Quote:
            case MarketEventType.OptionQuote:
            case MarketEventType.HistoricalQuote:
                _metrics.IncQuotes();
                break;
            case MarketEventType.Depth:
            case MarketEventType.L2Snapshot:
            case MarketEventType.OrderAdd:
            case MarketEventType.OrderModify:
            case MarketEventType.OrderCancel:
            case MarketEventType.OrderExecute:
            case MarketEventType.OrderReplace:
                _metrics.IncDepthUpdates();
                break;
            case MarketEventType.HistoricalBar:
            case MarketEventType.AggregateBar:
                _metrics.IncHistoricalBars();
                break;
            case MarketEventType.Integrity:
                _metrics.IncIntegrity();
                break;
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
            // A batch retained for retry is outstanding work even though the channel is empty
            // and the consumer briefly reads as inactive between retry iterations — the flush
            // must not acknowledge events that never reached the sink.
            if (_channel.Reader.Count == 0 && Volatile.Read(ref _activeConsumers) == 0 &&
                Volatile.Read(ref _retainedBatchEventCount) == 0)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
                var newConsumed = Interlocked.Read(ref _consumedCount);
                if (_channel.Reader.Count == 0 && Volatile.Read(ref _activeConsumers) == 0 &&
                    Volatile.Read(ref _retainedBatchEventCount) == 0 && newConsumed == consumed)
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
        var lastFaultTicks = Interlocked.Read(ref _lastConsumerFaultTicks);
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
            RecoveredCount: RecoveredCount,
            RejectedCount: RejectedCount,
            DeduplicatedCount: DeduplicatedCount,
            IsWalEnabled: IsWalEnabled,
            IsValidationEnabled: IsValidationEnabled,
            IsDeduplicationEnabled: IsDeduplicationEnabled,
            QueueFullMode: _fullMode,
            HighWaterMarkWarned: _highWaterMarkWarned,
            ConsumerCount: _consumerCount,
            ActiveConsumers: Volatile.Read(ref _activeConsumers),
            FaultedConsumers: _consumers.Count(static t => t.IsFaulted),
            ConsumerIterationFailures: Interlocked.Read(ref _consumerIterationFailures),
            LastConsumerFaultAtUtc: lastFaultTicks > 0 ? new DateTimeOffset(lastFaultTicks, TimeSpan.Zero) : null
        );
    }

    private async Task ConsumeAsync(CancellationToken ct = default)
    {
        // Set thread priority for consistent throughput
        ThreadingUtilities.SetAboveNormalPriority();

        var batchBuffer = new List<TracedMarketEvent>(_maxAdaptiveBatchSize);
        var reservationScratch = new List<DedupReservation>(_maxAdaptiveBatchSize);
        var nextPendingEventIndex = 0;

        try
        {
            var retryPendingBatch = false;
            var admittedThroughIndex = 0;
            var walBatchFlushed = false;
            var sinkBatchFlushed = false;
            var dedupBatchCommitted = false;

            while (retryPendingBatch || await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _activeConsumers);
                var startTs = Stopwatch.GetTimestamp();

                try
                {
                    var targetBatchSize = GetAdaptiveBatchSize();

                    // Drain up to the target batch size from the channel. A batch that failed
                    // mid-way is retried with its phase and per-event progress retained before
                    // any later events are read, because WAL commits are cumulative through a
                    // sequence number and dedup reservations must resolve exactly once.
                    if (!retryPendingBatch)
                    {
                        batchBuffer.Clear();
                        admittedThroughIndex = 0;
                        nextPendingEventIndex = 0;
                        walBatchFlushed = false;
                        sinkBatchFlushed = false;
                        dedupBatchCommitted = false;
                        while (batchBuffer.Count < targetBatchSize && _channel.Reader.TryRead(out var evt))
                        {
                            batchBuffer.Add(evt);
                        }
                    }

                    // [3.1] E2E trace propagation: start a per-batch activity so each consume
                    // cycle appears as a structured span in distributed traces.
                    using var batchActivity = MarketDataTracing.StartBatchConsumeActivity(batchBuffer.Count);

                    // Phase 1 — admission (resumable per event): validate, reserve the dedup
                    // identity, then append to the WAL. Validation precedes the dedup claim so a
                    // rejected payload can never consume the identity of a later corrected event.
                    // Reservations are memory-only, so no identity is durably recorded before the
                    // sink flush that proves it.
                    for (var i = admittedThroughIndex; i < batchBuffer.Count; i++)
                    {
                        var tracedEvent = batchBuffer[i];

                        // Suppression decisions (validation rejects, duplicate claims) are final
                        // for the batch: when admission restarts after releasing claims to an
                        // external holder, suppressed items must not be re-processed or
                        // re-counted.
                        if (tracedEvent.Suppressed)
                        {
                            admittedThroughIndex = i + 1;
                            continue;
                        }

                        var evt = tracedEvent.Event;
                        using var processActivity = MarketDataTracing.StartProcessActivity(
                            GetEventTypeName(evt.Type),
                            evt.EffectiveSymbol,
                            tracedEvent.TraceContext.ParentContext);
                        processActivity?.SetTag("event.source", evt.Source);
                        processActivity?.SetTag("event.sequence", evt.Sequence);
                        processActivity?.SetTag("event.type", GetEventTypeName(evt.Type));

                        using var logScope = BeginEventLogScope(evt, tracedEvent.TraceContext, processActivity);

                        // Remember the admission span so the storage span in the later sink pass
                        // stays parented under it (provider → process → store).
                        if (processActivity is not null)
                        {
                            tracedEvent = tracedEvent with { ProcessContext = processActivity.Context };
                            batchBuffer[i] = tracedEvent;
                        }

                        // Validate event before persistence (when a validator is configured)
                        if (_validator != null)
                        {
                            var validationResult = _validator.Validate(in evt);
                            if (!validationResult.IsValid)
                            {
                                if (_deadLetterSink != null)
                                {
                                    await _deadLetterSink.RecordAsync(evt, validationResult.Errors, _cts.Token).ConfigureAwait(false);
                                }

                                Interlocked.Increment(ref _rejectedCount);
                                batchBuffer[i] = tracedEvent with { Suppressed = true };
                                admittedThroughIndex = i + 1;
                                continue; // Skip persisting invalid events
                            }
                        }

                        // Reserve the dedup identity (when a dedup store is configured). The
                        // claim is pending and memory-only until the sink flush confirms it.
                        if (_dedupLedger != null)
                        {
                            var reservationResult = await _dedupLedger
                                .TryReserveAsync(evt, DedupLookupScope.LiveIngress, _cts.Token).ConfigureAwait(false);
                            if (reservationResult.IsSuppressed)
                            {
                                // A pending claim justifies discarding this delivery only when
                                // this batch holds the claim itself (a duplicate within the
                                // batch). An external memory-only claim proves nothing durable —
                                // its holder may abandon and release — so the event must be
                                // retained: raise a retryable fault and let the batch wait for
                                // the claim to commit (then a durable duplicate suppresses it)
                                // or release (then this batch claims it).
                                if (reservationResult.Status == DedupReservationStatus.PendingElsewhere &&
                                    (reservationResult.Reservation.Key is null ||
                                     !IsBatchLocalClaim(batchBuffer, reservationResult.Reservation.Key)))
                                {
                                    // Never wait on an external claim while holding claims of our
                                    // own: two consumers admitting crossed identity orders would
                                    // otherwise each hold what the other waits for, deadlocked in
                                    // their retry loops. Releasing our claims (and restarting
                                    // admission on the retry) lets the external holder make
                                    // progress; nothing here has touched the sink yet.
                                    ReleaseAllReservations(batchBuffer);
                                    admittedThroughIndex = 0;
                                    throw new PendingExternalDedupClaimException(
                                        "Event identity is claimed by an in-flight reservation outside this " +
                                        "batch; batch claims were released and the delivery is retained until " +
                                        "the external claim resolves.");
                                }

                                Interlocked.Increment(ref _deduplicatedCount);
                                batchBuffer[i] = tracedEvent with { Suppressed = true };
                                admittedThroughIndex = i + 1;
                                continue; // Skip duplicate events
                            }

                            tracedEvent = tracedEvent with { Reservation = reservationResult.Reservation };
                            batchBuffer[i] = tracedEvent;
                        }

                        if (_wal != null && tracedEvent.WalSequence == 0)
                        {
                            try
                            {
                                var walRecord = await _wal.AppendAsync(evt, GetEventTypeName(evt.Type), _cts.Token).ConfigureAwait(false);
                                batchBuffer[i] = tracedEvent with { WalSequence = walRecord.Sequence };
                            }
                            catch (Exception walEx) when (walEx is not OperationCanceledException)
                            {
                                // The identity claim must not outlive a failed admission: release
                                // it so the in-process retry can claim it again. The failure is
                                // wrapped as retryable — even when no record in the batch has a
                                // sequence yet — because an unavailable WAL is an unavailable
                                // durability store: the batch must wait for it rather than be
                                // abandoned, which without a dead-letter sink would silently
                                // drop events the producer can no longer retry.
                                ReleaseReservationAt(batchBuffer, i);
                                throw new WalAdmissionException(
                                    "WAL append failed during batch admission; the batch is retained and retried.",
                                    walEx);
                            }
                            catch
                            {
                                ReleaseReservationAt(batchBuffer, i);
                                throw;
                            }
                        }

                        admittedThroughIndex = i + 1;
                    }

                    long maxWalSequence = _lastCommittedWalSequence;
                    var heldReservationCount = 0;
                    for (var i = 0; i < batchBuffer.Count; i++)
                    {
                        if (batchBuffer[i].WalSequence > maxWalSequence)
                            maxWalSequence = batchBuffer[i].WalSequence;
                        if (batchBuffer[i].Reservation.IsHeld)
                            heldReservationCount++;
                    }

                    var walAdvanced = _wal != null && maxWalSequence > _lastCommittedWalSequence;

                    // Phase 2 — WAL flush: the batch's records must be durable in the WAL before
                    // the first sink append so the WAL always remains a superset of the sink and
                    // a crash can only ever replay, never lose.
                    if (walAdvanced && !walBatchFlushed)
                    {
                        await _wal!.FlushAsync(_cts.Token).ConfigureAwait(false);
                        walBatchFlushed = true;
                    }

                    // Phase 3 — sink appends (resumable per event). A failure here retries from
                    // the same index without touching the dedup ledger: a sink failure must not
                    // cause a premature dedup mark.
                    for (var i = nextPendingEventIndex; i < batchBuffer.Count; i++)
                    {
                        var tracedEvent = batchBuffer[i];
                        if (tracedEvent.Suppressed)
                        {
                            nextPendingEventIndex = i + 1;
                            continue;
                        }

                        var evt = tracedEvent.Event;
                        using var storageActivity = MarketDataTracing.StartStorageActivity(
                            _sink.GetType().Name,
                            evt.EffectiveSymbol,
                            tracedEvent.ProcessContext != default
                                ? tracedEvent.ProcessContext
                                : tracedEvent.TraceContext.ParentContext);
                        storageActivity?.SetTag("event.type", GetEventTypeName(evt.Type));
                        storageActivity?.SetTag("event.source", evt.Source);

                        await _sink.AppendAsync(evt, _cts.Token).ConfigureAwait(false);
                        // AppendAsync returning successfully is the sink acknowledgement boundary.
                        // If a later append or the batch flush fails, retry only the suffix that has
                        // not crossed that boundary; arbitrary sinks are not necessarily idempotent.
                        nextPendingEventIndex = i + 1;
                    }

                    // Phase 4 — sink flush: the batch's events become durable in primary storage.
                    // Required before any dedup identity may be durably committed, whether or not
                    // a WAL is configured.
                    if ((walAdvanced || heldReservationCount > 0) && !sinkBatchFlushed)
                    {
                        await _sink.FlushAsync(_cts.Token).ConfigureAwait(false);
                        sinkBatchFlushed = true;
                    }

                    // Phase 5 — dedup commit/flush: identities become durability-confirmed
                    // (version 2) only now that the sink flush proved them. A failure here
                    // retries just this phase — the sink is never re-appended.
                    if (_dedupLedger != null && heldReservationCount > 0 && !dedupBatchCommitted)
                    {
                        reservationScratch.Clear();
                        for (var i = 0; i < batchBuffer.Count; i++)
                        {
                            if (batchBuffer[i].Reservation.IsHeld)
                                reservationScratch.Add(batchBuffer[i].Reservation);
                        }

                        await _dedupLedger.CommitDurableAsync(reservationScratch, _cts.Token).ConfigureAwait(false);
                        dedupBatchCommitted = true;
                        reservationScratch.Clear();

                        // The claims are resolved; clear them so an abandon path cannot try to
                        // release identities that are already durably committed.
                        for (var i = 0; i < batchBuffer.Count; i++)
                        {
                            if (batchBuffer[i].Reservation.IsHeld)
                                batchBuffer[i] = batchBuffer[i] with { Reservation = default };
                        }
                    }

                    // Phase 6 — WAL commit: best-effort cleanup. Local sequence tracking updates
                    // first so a commit failure cannot re-flush the same batch; replayed records
                    // are suppressed on the next startup by their durability-confirmed entries.
                    if (walAdvanced)
                    {
                        _lastCommittedWalSequence = maxWalSequence;
                        try
                        {
                            await _wal!.CommitAsync(maxWalSequence, _cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex,
                                "WAL commit failed after successful sink flush for sequence {Seq}. " +
                                "Events are safe in the sink; WAL records may be replayed on next startup " +
                                "but are suppressed by their durability-confirmed dedup entries when a " +
                                "dedup store is configured",
                                maxWalSequence);
                        }
                    }

                    Interlocked.Add(ref _consumedCount, batchBuffer.Count);
                    retryPendingBatch = false;
                    Volatile.Write(ref _retainedBatchEventCount, 0);
                    admittedThroughIndex = 0;
                    nextPendingEventIndex = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A persistence failure must not kill the consumer: before this catch existed,
                    // any sink/WAL/dedup exception silently faulted the consumer task (observed
                    // only at disposal) while producers kept publishing into a channel nobody
                    // drained. A WAL-backed batch that has appended records must be retried
                    // before later batches: committing a later WAL sequence would otherwise
                    // cumulatively acknowledge the failed records. Phase flags and per-event
                    // progress are retained so the retry resumes exactly where it failed.
                    Interlocked.Increment(ref _consumerIterationFailures);
                    Interlocked.Exchange(ref _lastConsumerFaultTicks, DateTimeOffset.UtcNow.UtcTicks);
                    // Retry (rather than abandon) when the batch has WAL-appended records — a
                    // cumulative later commit would acknowledge them — or when the sink flush
                    // already succeeded and only the dedup commit is outstanding: those events
                    // are durable, so the batch must retry the commit-only phase even without a
                    // WAL instead of releasing claims for identities that are already stored.
                    retryPendingBatch =
                        (_wal != null && batchBuffer.Any(static traced => traced.WalSequence > 0)) ||
                        (sinkBatchFlushed && _dedupLedger != null && !dedupBatchCommitted &&
                         batchBuffer.Any(static traced => traced.Reservation.IsHeld)) ||
                        ex is PendingExternalDedupClaimException ||
                        ex is WalAdmissionException;
                    // A retained batch is outstanding work that is no longer visible in the
                    // channel and not yet counted as consumed: publish it so FlushAsync's
                    // idle check cannot acknowledge a flush while its events are undelivered.
                    Volatile.Write(ref _retainedBatchEventCount, retryPendingBatch ? batchBuffer.Count : 0);
                    _logger.LogError(ex,
                        "Pipeline consumer iteration failed while persisting a batch of {BatchCount} events; {RecoveryAction}",
                        batchBuffer.Count,
                        retryPendingBatch
                            ? "the batch will retry from its retained phase before later batches are committed"
                            : "WAL commit withheld and consumer continuing");

                    // Without a WAL, events appended before the failure sit in the sink's
                    // buffer and can still become durable at the next periodic or final
                    // flush. Flush them now and durably commit their identity claims so an
                    // upstream re-send of the persisted prefix is suppressed rather than
                    // appended twice. If the flush itself fails nothing became durable, so the
                    // claims are released below — duplicates stay possible, loss does not.
                    var prefixCommitted = false;
                    var prefixFlushed = false;
                    if (!retryPendingBatch)
                    {
                        if (_dedupLedger != null && nextPendingEventIndex > 0 && !dedupBatchCommitted)
                        {
                            try
                            {
                                reservationScratch.Clear();
                                for (var i = 0; i < nextPendingEventIndex; i++)
                                {
                                    if (batchBuffer[i].Reservation.IsHeld)
                                        reservationScratch.Add(batchBuffer[i].Reservation);
                                }

                                if (reservationScratch.Count > 0)
                                {
                                    await _sink.FlushAsync(_cts.Token).ConfigureAwait(false);
                                    // The prefix is durable from here. Its claims must not be
                                    // released now: releasing them would leave persisted events
                                    // with no durable identity, so a re-send would append them
                                    // a second time. Tracked locally rather than through
                                    // sinkBatchFlushed, which would make a retry skip the flush
                                    // that events appended after this prefix still need.
                                    prefixFlushed = true;
                                    await _dedupLedger.CommitDurableAsync(reservationScratch, _cts.Token).ConfigureAwait(false);
                                }

                                reservationScratch.Clear();
                                prefixCommitted = true;
                            }
                            catch (Exception promoteEx)
                            {
                                reservationScratch.Clear();

                                if (prefixFlushed)
                                {
                                    // The flush succeeded and only the identity commit failed, so
                                    // the prefix is durable while its identities are not.
                                    // Releasing those claims would let a re-send append the
                                    // prefix twice, so retain the batch instead of abandoning
                                    // it: the retry resumes from the sink-acknowledgement index,
                                    // never re-appending the prefix, and commits every claim
                                    // once its events are flushed.
                                    retryPendingBatch = true;
                                    Volatile.Write(ref _retainedBatchEventCount, batchBuffer.Count);
                                    _logger.LogWarning(promoteEx,
                                        "Failed to commit identity claims for the flushed prefix of a batch; " +
                                        "the batch is retained and retried so the durable prefix keeps its claims");
                                }
                                else
                                {
                                    _logger.LogWarning(promoteEx,
                                        "Failed to flush and commit the appended prefix of an abandoned batch; " +
                                        "its identity claims will be released and a re-send may append duplicates");
                                }
                            }
                        }
                    }

                    if (!retryPendingBatch)
                    {
                        // The remaining pending identity claims must not outlive the abandoned
                        // batch, otherwise a legitimate upstream re-send of the same event would
                        // be suppressed as an in-flight duplicate forever. Committed prefix
                        // claims are already resolved; token-checked release skips them.
                        ReleaseAllReservations(batchBuffer);

                        if (_deadLetterSink != null)
                        {
                            // When the appended prefix was flushed and committed, those events
                            // are durably persisted and do not belong in the dead-letter record;
                            // if the promotion failed their durability is unknown, so record the
                            // whole batch conservatively.
                            var deadLetterFromIndex = prefixCommitted ? nextPendingEventIndex : 0;
                            for (var i = deadLetterFromIndex; i < batchBuffer.Count; i++)
                            {
                                var traced = batchBuffer[i];

                                // Suppressed events were already dead-lettered by validation or
                                // are duplicates of retained data — do not record them twice.
                                if (traced.Suppressed)
                                    continue;

                                try
                                {
                                    await _deadLetterSink.RecordAsync(
                                        traced.Event,
                                        new[] { $"pipeline-persist-failure: {ex.GetType().Name}: {ex.Message}" },
                                        CancellationToken.None).ConfigureAwait(false);
                                }
                                catch
                                {
                                    // DeadLetterSink logs its own failures; recording is best-effort.
                                }
                            }
                        }
                    }

                    try
                    {
                        // Brief backoff so a persistently failing sink cannot spin the consumer.
                        await Task.Delay(TimeSpan.FromMilliseconds(250), _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Shutdown requested; the next WaitToReadAsync observes the cancellation.
                    }
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
            // The consumer is exiting for good: nothing here is retained work any more (the
            // final flush below promotes what it can), so unblock any explicit FlushAsync.
            Volatile.Write(ref _retainedBatchEventCount, 0);

            if (Interlocked.Exchange(ref _finalFlushStarted, 1) == 0)
            {
                // Final flush on shutdown with timeout to prevent indefinite hang
                try
                {
                    using var flushTimeoutCts = new CancellationTokenSource(_finalFlushTimeout);
                    await _sink.FlushAsync(flushTimeoutCts.Token).ConfigureAwait(false);

                    // The final flush just made any partially appended batch prefix durable in
                    // the sink. Commit those events' identity claims BEFORE the release below:
                    // releasing the claim of a durably persisted event would let successor
                    // pipelines accept the same event again and WAL replay re-append it.
                    if (_dedupLedger != null && nextPendingEventIndex > 0)
                    {
                        try
                        {
                            reservationScratch.Clear();
                            var prefixBound = Math.Min(nextPendingEventIndex, batchBuffer.Count);
                            for (var i = 0; i < prefixBound; i++)
                            {
                                if (batchBuffer[i].Reservation.IsHeld)
                                    reservationScratch.Add(batchBuffer[i].Reservation);
                            }

                            if (reservationScratch.Count > 0)
                            {
                                await _dedupLedger.CommitDurableAsync(reservationScratch, flushTimeoutCts.Token).ConfigureAwait(false);
                            }

                            reservationScratch.Clear();
                        }
                        catch (Exception commitEx)
                        {
                            _logger.LogWarning(commitEx,
                                "Failed to commit identity claims for the flushed batch prefix during shutdown; " +
                                "the claims will be released and a replay or re-send may append duplicates");
                        }
                    }

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

            // Cancellation (e.g. forced disposal) can exit mid-batch without reaching the
            // in-loop cleanup, and the dedup store is an injected singleton that outlives this
            // pipeline: any claims still pending here would stay PendingElsewhere for every
            // later consumer of the same store. Runs after the final flush so claims for the
            // just-persisted prefix were committed, not discarded; token-checked release
            // skips claims the commit above already resolved.
            ReleaseAllReservations(batchBuffer);
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
    {
        var traceContext = EventTraceContext.CaptureCurrent();
        var tracedEvent = traceContext.HasParent
            ? evt.StampTraceContext(traceContext.ParentContext)
            : evt;

        return new TracedMarketEvent(tracedEvent, traceContext);
    }

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
            ["EventType"] = GetEventTypeName(evt.Type),
            ["EventSource"] = evt.Source,
            ["Symbol"] = evt.EffectiveSymbol,
            ["Sequence"] = evt.Sequence
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IDisposable? BeginEventLogScope(
        MarketEvent evt,
        EventTraceContext traceContext,
        Activity? activity)
    {
        if (!_includePerEventLogScopes)
            return null;

        return _logger.BeginScope(CreateLogScope(evt, traceContext, activity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TrackQueueDepthOnPublish(long publishedCount)
    {
        if ((publishedCount & ReaderCountSampleMask) != 0)
            return;

        var currentSize = _channel.Reader.Count;
        var peak = Volatile.Read(ref _peakQueueSize);
        if (currentSize > peak)
        {
            Interlocked.CompareExchange(ref _peakQueueSize, currentSize, peak);
        }

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

    private static bool IsBatchLocalClaim(List<TracedMarketEvent> batchBuffer, string claimKey)
    {
        for (var i = 0; i < batchBuffer.Count; i++)
        {
            var reservation = batchBuffer[i].Reservation;
            if (reservation.IsHeld && string.Equals(reservation.Key, claimKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Raised during admission when an event's identity is held by an in-flight reservation
    /// outside the current batch. Retryable by design: the batch waits (with backoff) until the
    /// external claim commits or releases, so the delivery is never silently discarded while no
    /// durable copy of it exists.
    /// </summary>
    private sealed class PendingExternalDedupClaimException : InvalidOperationException
    {
        public PendingExternalDedupClaimException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Raised when a WAL append fails during batch admission. Retryable by design, even when no
    /// record in the batch was assigned a sequence: an unavailable WAL is an unavailable
    /// durability store, so the batch waits for it instead of being abandoned into silent loss.
    /// </summary>
    private sealed class WalAdmissionException : InvalidOperationException
    {
        public WalAdmissionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private void ReleaseReservationAt(List<TracedMarketEvent> batchBuffer, int index)
    {
        if (_dedupLedger == null)
            return;

        var reservation = batchBuffer[index].Reservation;
        if (reservation.IsHeld)
        {
            _dedupLedger.Release(in reservation);
            batchBuffer[index] = batchBuffer[index] with { Reservation = default };
        }
    }

    private void ReleaseAllReservations(List<TracedMarketEvent> batchBuffer)
    {
        if (_dedupLedger == null)
            return;

        for (var i = 0; i < batchBuffer.Count; i++)
        {
            ReleaseReservationAt(batchBuffer, i);
        }
    }

    /// <summary>
    /// A channel item and its per-batch persistence progress: the WAL sequence assigned at
    /// admission, the pending dedup reservation (memory-only), whether the event was suppressed
    /// (validation-rejected or duplicate) and must skip persistence, and the admission span
    /// context that parents the storage span.
    /// </summary>
    private readonly record struct TracedMarketEvent(
        MarketEvent Event,
        EventTraceContext TraceContext,
        long WalSequence = 0,
        DedupReservation Reservation = default,
        bool Suppressed = false,
        ActivityContext ProcessContext = default);
}
