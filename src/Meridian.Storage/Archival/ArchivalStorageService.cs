using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Services;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Storage.Archival;

/// <summary>
/// Archival-first storage service with Write-Ahead Logging for crash-safe persistence.
/// Implements the storage pipeline pattern: WAL -> Buffer -> Primary Storage -> Archive.
/// </summary>
[StorageSink("archival", "Archival storage with WAL durability",
    EnabledByDefault = false,
    Description = "WAL-backed archival sink; buffer events in memory and flush to a primary IStorageSink with crash-safe Write-Ahead Logging.")]
public sealed class ArchivalStorageService : IStorageSink, IFlushable
{
    private readonly ILogger _log = LoggingSetup.ForContext<ArchivalStorageService>();
    private readonly WriteAheadLog _wal;
    private readonly IStorageSink _primaryStorage;
    private readonly ArchivalStorageOptions _options;

    private readonly ConcurrentQueue<PendingEvent> _buffer = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private long _lastCommittedSequence;
    private long _pendingEvents;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private Task? _backgroundFlushTask;

    public ArchivalStorageService(
        string dataRoot,
        IStorageSink primaryStorage,
        ArchivalStorageOptions? options = null)
    {
        _primaryStorage = primaryStorage;
        _options = options ?? new ArchivalStorageOptions();

        var walDir = Path.Combine(dataRoot, "_wal");
        _wal = new WriteAheadLog(walDir, _options.WalOptions);
    }

    /// <summary>
    /// Initialize the service and recover any uncommitted data.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _log.Information("Initializing archival storage service");

        // Initialize WAL
        await _wal.InitializeAsync(ct);

        // Recover uncommitted records
        await RecoverUncommittedRecordsAsync(ct);

        // Start background flush task
        _backgroundFlushTask = BackgroundFlushLoopAsync(_cts.Token);

        _log.Information("Archival storage service initialized");
    }

    /// <inheritdoc/>
    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        // Write to WAL first
        var walRecord = await _wal.AppendAsync(evt, evt.Type.ToString(), ct);

        // Buffer for batch processing
        _buffer.Enqueue(new PendingEvent
        {
            WalSequence = walRecord.Sequence,
            Event = evt,
            ReceivedAt = DateTime.UtcNow
        });

        Interlocked.Increment(ref _pendingEvents);

        // Check if we should flush immediately
        if (_pendingEvents >= _options.FlushThreshold ||
            (DateTime.UtcNow - _lastFlushTime) >= _options.MaxFlushDelay)
        {
            await FlushAsync(ct);
        }
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pendingEvents == 0)
            return;

        await _flushLock.WaitAsync(ct);
        try
        {
            var eventsToFlush = new List<PendingEvent>();
            long maxSequence = _lastCommittedSequence;

            // Drain the buffer
            while (_buffer.TryDequeue(out var pending))
            {
                eventsToFlush.Add(pending);
                maxSequence = Math.Max(maxSequence, pending.WalSequence);
            }

            if (eventsToFlush.Count == 0)
                return;

            _log.Debug("Flushing {Count} events to primary storage", eventsToFlush.Count);

            // Write to primary storage
            foreach (var pending in eventsToFlush.OrderBy(e => e.WalSequence))
            {
                await _primaryStorage.AppendAsync(pending.Event, ct);
            }

            // Flush primary storage
            await _primaryStorage.FlushAsync(ct);

            // Commit the WAL
            await _wal.CommitAsync(maxSequence, ct);

            _lastCommittedSequence = maxSequence;
            Interlocked.Add(ref _pendingEvents, -eventsToFlush.Count);
            _lastFlushTime = DateTime.UtcNow;

            _log.Debug("Flushed {Count} events, committed through sequence {Sequence}",
                eventsToFlush.Count, maxSequence);

            // Optionally truncate old WAL files
            if (_options.AutoTruncateWal)
            {
                await _wal.TruncateAsync(_lastCommittedSequence, ct);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task RecoverUncommittedRecordsAsync(CancellationToken ct)
    {
        _log.Information("Recovering uncommitted records from WAL");

        var recoveredCount = 0;
        await foreach (var walRecord in _wal.GetUncommittedRecordsAsync(ct))
        {
            if (walRecord.RecordType == "COMMIT")
                continue;

            try
            {
                var evt = walRecord.DeserializePayload<MarketEvent>();
                if (evt != null)
                {
                    _buffer.Enqueue(new PendingEvent
                    {
                        WalSequence = walRecord.Sequence,
                        Event = evt,
                        ReceivedAt = walRecord.Timestamp
                    });
                    Interlocked.Increment(ref _pendingEvents);
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to deserialize WAL record {Sequence}", walRecord.Sequence);
            }
        }

        if (recoveredCount > 0)
        {
            _log.Information("Recovered {Count} uncommitted events from WAL", recoveredCount);
            await FlushAsync(ct);
        }
    }

    private async Task BackgroundFlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.BackgroundFlushInterval, ct);

                if (_pendingEvents > 0 &&
                    (DateTime.UtcNow - _lastFlushTime) >= _options.MaxFlushDelay)
                {
                    await FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in background flush loop");
            }
        }
    }

    /// <summary>
    /// Get storage statistics.
    /// </summary>
    public ArchivalStorageStats GetStats() => new()
    {
        PendingEvents = _pendingEvents,
        LastCommittedSequence = _lastCommittedSequence,
        LastFlushTime = _lastFlushTime,
        BufferDepth = _buffer.Count
    };

    public async ValueTask DisposeAsync()
    {
        _log.Information("Disposing archival storage service");

        _cts.Cancel();

        if (_backgroundFlushTask != null)
        {
            try
            { await _backgroundFlushTask; }
            catch (OperationCanceledException) { /* Expected on cancellation */ }
        }

        // Final flush
        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during final flush");
        }

        await _wal.DisposeAsync();
        await _primaryStorage.DisposeAsync();

        _flushLock.Dispose();
        _cts.Dispose();

        _log.Information("Archival storage service disposed");
    }

    private class PendingEvent
    {
        public long WalSequence { get; set; }
        public MarketEvent Event { get; set; } = null!;
        public DateTime ReceivedAt { get; set; }
    }
}

/// <summary>
/// Options for the archival storage service.
/// </summary>
public sealed class ArchivalStorageOptions
{
    /// <summary>
    /// WAL configuration.
    /// </summary>
    public WalOptions WalOptions { get; set; } = new();

    /// <summary>
    /// Number of events to buffer before flushing.
    /// </summary>
    public int FlushThreshold { get; set; } = 1000;

    /// <summary>
    /// Maximum time to hold events before flushing.
    /// </summary>
    public TimeSpan MaxFlushDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Background flush check interval.
    /// </summary>
    public TimeSpan BackgroundFlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to automatically truncate committed WAL segments.
    /// </summary>
    public bool AutoTruncateWal { get; set; } = true;
}

/// <summary>
/// Statistics for the archival storage service.
/// </summary>
public sealed class ArchivalStorageStats
{
    public long PendingEvents { get; set; }
    public long LastCommittedSequence { get; set; }
    public DateTime LastFlushTime { get; set; }
    public int BufferDepth { get; set; }
}
