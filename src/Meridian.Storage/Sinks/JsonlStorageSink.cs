using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Core.Monitoring;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Configuration options for batched JSONL storage.
/// </summary>
public sealed class JsonlBatchOptions
{
    /// <summary>
    /// Number of events to buffer before writing to disk.
    /// Default is 1000 events.
    /// </summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Maximum time between flushes.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether batching is enabled. When disabled, writes occur immediately per event.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How appends reach the day file. <see cref="JsonlWriteMode.AppendStream"/> (default)
    /// keeps a persistent append-only handle per uncompressed file and fsyncs on the sink's flush barrier;
    /// <see cref="JsonlWriteMode.CopyOnWrite"/> preserves the previous whole-file
    /// copy-per-batch behaviour as a rollback path. Compressed files always use copy-on-write
    /// because a torn gzip member cannot be safely repaired in place.
    /// </summary>
    public JsonlWriteMode WriteMode { get; init; } = JsonlWriteMode.AppendStream;

    /// <summary>
    /// Default options with batching enabled.
    /// </summary>
    public static JsonlBatchOptions Default => new();

    /// <summary>
    /// Optimized for high throughput with larger batches.
    /// </summary>
    public static JsonlBatchOptions HighThroughput => new()
    {
        BatchSize = 5000,
        FlushInterval = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Optimized for low latency with smaller batches.
    /// </summary>
    public static JsonlBatchOptions LowLatency => new()
    {
        BatchSize = 100,
        FlushInterval = TimeSpan.FromSeconds(1)
    };

    /// <summary>
    /// Disable batching - write each event immediately.
    /// </summary>
    public static JsonlBatchOptions NoBatching => new()
    {
        Enabled = false
    };
}

/// <summary>
/// Append mechanism for JSONL day files.
/// </summary>
public enum JsonlWriteMode : byte
{
    /// <summary>Persistent append-only file handle; new bytes only, fsync on the flush barrier.</summary>
    AppendStream,

    /// <summary>Legacy whole-file copy through <c>AtomicFileWriter.AppendAsync</c> per batch.</summary>
    CopyOnWrite
}

/// <summary>
/// Buffered JSONL writer with per-path writers and configurable batch writes.
/// Supports both immediate and batched write modes for optimal performance.
/// </summary>
[StorageSink("jsonl", "JSONL Storage",
    Description = "Writes market events to newline-delimited JSON files (.jsonl / .jsonl.gz).",
    EnabledByDefault = true)]
public sealed class JsonlStorageSink : IStorageSink
{
    private readonly StorageOptions _options;
    private readonly IStoragePolicy _policy;
    private readonly JsonlBatchOptions _batchOptions;
    private readonly ILogger<JsonlStorageSink> _logger;
    private readonly RetentionManager? _retention;
    private readonly Timer? _flushTimer;
    private readonly Timer? _retentionTimer;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private int _disposed;

    // Using Lazy<WriterState> as the value type prevents a race in ConcurrentDictionary.GetOrAdd
    // where the factory can be called multiple times concurrently for the same key. Without Lazy,
    // the "losing" WriterState would open a FileStream that is never disposed (resource leak).
    // With Lazy (ExecutionAndPublication mode), only one WriterState is ever created per path.
    private readonly ConcurrentDictionary<string, Lazy<WriterState>> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MarketEventBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);

    // Cached factory delegate — the Lazy<> wrapper ensures WriterState.Create is called at most once
    // per unique path even under concurrent access, while the cached delegate avoids closure allocation.
    private readonly Func<string, Lazy<WriterState>> _writerFactory;
    private readonly Func<string, MarketEventBuffer> _bufferFactory;

    // Test seam mirroring ParquetStorageSink's injectable atomic-write delegate: when set,
    // batched flushes route through this instead of the WriterState so tests can simulate
    // write failures without filesystem tricks.
    private readonly Func<string, IReadOnlyList<MarketEvent>, CancellationToken, Task>? _writeBatchOverride;

    // Metrics
    private long _eventsBuffered;
    private long _eventsWritten;
    private long _batchesWritten;

    /// <summary>
    /// Total events currently buffered across all paths.
    /// </summary>
    public long EventsBuffered => Interlocked.Read(ref _eventsBuffered);

    /// <summary>
    /// Total events written to disk.
    /// </summary>
    public long EventsWritten => Interlocked.Read(ref _eventsWritten);

    /// <summary>
    /// Total batches written to disk.
    /// </summary>
    public long BatchesWritten => Interlocked.Read(ref _batchesWritten);

    /// <summary>
    /// Gets whether sink-side batching is enabled.
    /// </summary>
    public bool IsBatchingEnabled => _batchOptions.Enabled;

    /// <summary>
    /// Gets the configured batch size threshold.
    /// </summary>
    public int BatchSize => _batchOptions.BatchSize;

    /// <summary>
    /// Gets the configured periodic flush interval for buffered batches.
    /// </summary>
    public TimeSpan FlushInterval => _batchOptions.FlushInterval;

    /// <summary>
    /// Creates a JsonlStorageSink with default options (no batching for backward compatibility).
    /// </summary>
    public JsonlStorageSink(StorageOptions options, IStoragePolicy policy, ILogger<JsonlStorageSink>? logger = null)
        : this(options, policy, JsonlBatchOptions.NoBatching, logger)
    {
    }

    internal JsonlStorageSink(
        StorageOptions options,
        IStoragePolicy policy,
        JsonlBatchOptions batchOptions,
        Func<string, IReadOnlyList<MarketEvent>, CancellationToken, Task> writeBatchAsync,
        ILogger<JsonlStorageSink>? logger = null)
        : this(options, policy, batchOptions, logger)
    {
        _writeBatchOverride = writeBatchAsync ?? throw new ArgumentNullException(nameof(writeBatchAsync));
    }

    /// <summary>
    /// Creates a JsonlStorageSink with configurable batch options.
    /// </summary>
    public JsonlStorageSink(StorageOptions options, IStoragePolicy policy, JsonlBatchOptions batchOptions, ILogger<JsonlStorageSink>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _batchOptions = batchOptions ?? throw new ArgumentNullException(nameof(batchOptions));
        _logger = logger ?? NullLogger<JsonlStorageSink>.Instance;
        _retention = options.RetentionDays is null && options.MaxTotalBytes is null
            ? null
            : new RetentionManager(options.RootPath, options.RetentionDays, options.MaxTotalBytes, _logger);

        // Cache factory delegates once to avoid closure allocation on every GetOrAdd call.
        // The Lazy wrapper ensures WriterState.Create (which opens a FileStream) is called
        // at most once per unique path, preventing file handle leaks under concurrent access.
        var compress = _options.Compress;
        var batchSize = _batchOptions.BatchSize;
        // A gzip member has no cheap, reliable in-place recovery point after a torn write.
        // Keep compressed day files behind the atomic replacement boundary so a crash cannot
        // strand later WAL replay members behind an unreadable member.
        var copyOnWrite = _batchOptions.WriteMode == JsonlWriteMode.CopyOnWrite || compress;
        _writerFactory = p => new Lazy<WriterState>(() => WriterState.Create(p, compress, copyOnWrite), LazyThreadSafetyMode.ExecutionAndPublication);
        _bufferFactory = _ => new MarketEventBuffer(batchSize);

        if (_batchOptions.Enabled)
        {
            // Offset the initial delay by the flush interval plus 2 seconds so this timer does
            // not fire simultaneously with EventPipeline's PeriodicFlushAsync (which also
            // defaults to 5 s). Concurrent flushes on the same _flushGate semaphore
            // cause periodic latency spikes; staggering the first fire eliminates this.
            var initialDelay = _batchOptions.FlushInterval + TimeSpan.FromSeconds(2);
            _flushTimer = new Timer(
                _ => _ = FlushAllBuffersSafelyAsync(),
                null,
                initialDelay,
                _batchOptions.FlushInterval);
        }

        if (_retention != null)
        {
            _retentionTimer = new Timer(
                _ => RunRetentionCleanup(),
                null,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(15));
        }
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(JsonlStorageSink));

        EventSchemaValidator.Validate(evt);
        var path = _policy.GetPath(evt);

        if (!_batchOptions.Enabled)
        {
            // Immediate write mode (legacy behavior)
            await WriteEventImmediateAsync(path, evt, ct).ConfigureAwait(false);
            return;
        }

        // Batched write mode
        var buffer = _buffers.GetOrAdd(path, _bufferFactory);
        buffer.Add(evt);
        Interlocked.Increment(ref _eventsBuffered);

        // Flush if buffer is full
        if (buffer.ShouldFlush(_batchOptions.BatchSize))
        {
            await FlushBufferUnderGateAsync(path, buffer, ct).ConfigureAwait(false);
        }
    }

    // Size-triggered flushes must hold the same _flushGate as the periodic and disposal
    // flush paths. EventBuffer.DrainAll hands back its internal swap-buffer, which the next
    // DrainAll clears and reuses; without the gate a concurrent periodic/size flush could
    // clear the very list an in-flight WriteBatchAsync is still reading.
    private async Task FlushBufferUnderGateAsync(string path, MarketEventBuffer buffer, CancellationToken ct)
    {
        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FlushBufferAsync(path, buffer, ct).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async ValueTask WriteEventImmediateAsync(string path, MarketEvent evt, CancellationToken ct)
    {
        var writer = _writers.GetOrAdd(path, _writerFactory).Value;
        await writer.WriteEventAsync(evt, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _eventsWritten);
    }

    private async Task FlushBufferAsync(string path, MarketEventBuffer buffer, CancellationToken ct)
    {
        var events = buffer.DrainAll();
        if (events.Count == 0)
            return;

        try
        {
            if (_writeBatchOverride is not null)
            {
                await _writeBatchOverride(path, events, ct).ConfigureAwait(false);
            }
            else
            {
                var writer = _writers.GetOrAdd(path, _writerFactory).Value;
                await writer.WriteBatchAsync(events, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // DrainAll hands back the buffer's internal swap list; restore before the next
            // drain (serialised by _flushGate) so a failed write never discards the batch.
            buffer.RestoreToFront(events);
            _logger.LogError(ex, "Failed to flush {Count} buffered events to {Path}; events were restored for retry", events.Count, path);
            throw;
        }

        Interlocked.Add(ref _eventsWritten, events.Count);
        Interlocked.Add(ref _eventsBuffered, -events.Count);
        Interlocked.Increment(ref _batchesWritten);
    }

    private async Task FlushAllBuffersAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0 || _disposalCts.IsCancellationRequested)
            return;

        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FlushBufferedBatchesUnderGateAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    // Caller must hold _flushGate before invoking this helper.
    private async Task FlushBufferedBatchesUnderGateAsync(CancellationToken ct)
    {
        var tasks = new List<Task>();
        foreach (var kvp in _buffers)
        {
            if (kvp.Value.Count > 0)
            {
                tasks.Add(FlushBufferAsync(kvp.Key, kvp.Value, ct));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task FlushAllBuffersSafelyAsync(CancellationToken ct = default)
    {
        try
        {
            await FlushAllBuffersAsync(_disposalCts.Token).ConfigureAwait(false);
            await CloseIdleWritersAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
        {
            // Disposal in progress, stop flushing
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Periodic flush failed");
        }
    }

    // Rolled-over day files would otherwise hold their append handles forever, blocking
    // retention deletion on Windows. Handles idle for over two flush intervals are closed
    // (fsync-then-close); the WriterState transparently reopens on a late write.
    private async Task CloseIdleWritersAsync()
    {
        var idleThreshold = _batchOptions.FlushInterval * 2;
        foreach (var kv in _writers)
        {
            if (kv.Value.IsValueCreated)
                await kv.Value.Value.CloseIfIdleAsync(idleThreshold).ConfigureAwait(false);
        }
    }

    private void RunRetentionCleanup()
    {
        try
        {
            _retention?.MaybeCleanup();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Periodic retention cleanup failed");
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // First flush all buffers (if batching enabled)
        if (_batchOptions.Enabled)
        {
            await FlushAllBuffersAsync(ct).ConfigureAwait(false);
        }

        // Then flush all writers to disk (only those that have been realized)
        foreach (var kv in _writers)
        {
            if (kv.Value.IsValueCreated)
                await kv.Value.Value.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // 1. Signal disposal to timer callbacks
        _disposalCts.Cancel();

        // 2. Dispose timers first — waits for any in-flight callback to complete
        if (_flushTimer != null)
        {
            await _flushTimer.DisposeAsync().ConfigureAwait(false);
        }

        if (_retentionTimer != null)
        {
            await _retentionTimer.DisposeAsync().ConfigureAwait(false);
        }

        // 3. Final flush — guaranteed no concurrent timer flushes after timer disposal
        if (_batchOptions.Enabled)
        {
            try
            {
                // Wait for the gate without a timeout: once _disposed is set and the timers are
                // disposed, AppendAsync throws so no new flush can start — the gate is only ever
                // held by an in-flight flush that will complete. Waiting unbounded (as
                // WriterState.DisposeAsync already does) guarantees the final flush runs instead of
                // timing out and then clearing _buffers with still-unwritten events. Release lives
                // in the inner finally so a semaphore that was never acquired is never released.
                await _flushGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await FlushBufferedBatchesUnderGateAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _flushGate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Final buffer flush during disposal failed");
            }
        }

        // 4. Dispose all writers (only those that have been realized)
        foreach (var kv in _writers)
        {
            if (kv.Value.IsValueCreated)
                await kv.Value.Value.DisposeAsync().ConfigureAwait(false);
        }

        _writers.Clear();
        _buffers.Clear();

        // 5. Dispose remaining resources
        _retention?.Dispose();
        _flushGate.Dispose();
        _disposalCts.Dispose();
    }

    /// <summary>
    /// Returns a diagnostics snapshot for buffered JSONL persistence.
    /// </summary>
    public JsonlStorageSinkStatistics GetStatistics()
    {
        long fsyncCount = 0;
        var openHandles = 0;
        foreach (var kv in _writers)
        {
            if (!kv.Value.IsValueCreated)
                continue;
            fsyncCount += kv.Value.Value.FsyncCount;
            if (kv.Value.Value.HasOpenHandle)
                openHandles++;
        }

        return new JsonlStorageSinkStatistics(
            IsBatchingEnabled: IsBatchingEnabled,
            BatchSize: BatchSize,
            FlushInterval: FlushInterval,
            EventsBuffered: EventsBuffered,
            EventsWritten: EventsWritten,
            BatchesWritten: BatchesWritten,
            WriterCount: _writers.Count,
            BufferCount: _buffers.Count,
            Timestamp: DateTimeOffset.UtcNow,
            FsyncCount: fsyncCount,
            OpenWriterHandles: openHandles);
    }

    private sealed class WriterState : IAsyncDisposable
    {
        private static readonly JsonWriterOptions JsonWriterOptions = new()
        {
            SkipValidation = true
        };
        private static readonly ReadOnlyMemory<byte> NewlineBytes = "\n"u8.ToArray();

        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly bool _compressed;
        private readonly bool _copyOnWrite;
        private FileStream? _stream;
        private bool _dirtySinceFsync;
        private bool _compressedFileValidated;
        private long _fsyncCount;
        private DateTimeOffset _lastWriteUtc;

        private WriterState(string path, bool compressed, bool copyOnWrite)
        {
            _path = path;
            _compressed = compressed;
            _copyOnWrite = copyOnWrite;
        }

        public static WriterState Create(string path, bool compress, bool copyOnWrite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new WriterState(path, compress, copyOnWrite);
        }

        public long FsyncCount => Interlocked.Read(ref _fsyncCount);

        public bool HasOpenHandle => _stream is not null;

        // Persistent append stream (audit finding P10): the previous implementation routed
        // every flush through AtomicFileWriter.AppendAsync, which copies the ENTIRE existing
        // day file into a temp file per batch — O(day-file) I/O thousands of times per day.
        // Appending to a long-lived FileStream writes only the new bytes. Durability contract:
        // batches reach the OS on every write (FlushAsync); physical fsync happens in
        // FlushToDiskAsync, which the sink invokes from IStorageSink.FlushAsync — the barrier
        // EventPipeline awaits before committing the WAL. A crash between batch write and
        // sink flush can tear the file tail; those events are uncommitted in the WAL and
        // replay on startup. Plain JSONL tails are repaired before reopening; compressed
        // files use copy-on-write so an incomplete gzip member is never published.
        private FileStream EnsureStream()
        {
            if (_stream is not null)
                return _stream;

            var stream = new FileStream(
                _path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous);

            try
            {
                RepairTornJsonlTail(stream);
                stream.Position = stream.Length;
                _stream = stream;
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public async ValueTask WriteEventAsync(MarketEvent evt, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_copyOnWrite)
                {
                    EnsureCompressedFileIsReadable();
                    await AtomicFileWriter.AppendAsync(
                        _path,
                        stream => WriteSingleEventAsync(stream, evt, ct),
                        ct).ConfigureAwait(false);
                    return;
                }

                await WriteAppendStreamAsync(
                    stream => WriteSingleEventAsync(stream, evt, ct),
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Writes multiple events in a single batch operation without materializing
        /// intermediate JSON strings.
        /// </summary>
        public async ValueTask WriteBatchAsync(IReadOnlyList<MarketEvent> events, CancellationToken ct)
        {
            if (events.Count == 0)
                return;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_copyOnWrite)
                {
                    EnsureCompressedFileIsReadable();
                    await AtomicFileWriter.AppendAsync(
                        _path,
                        stream => WriteEventsAsync(stream, events, ct),
                        ct).ConfigureAwait(false);
                    return;
                }

                await WriteAppendStreamAsync(
                    stream => WriteEventsAsync(stream, events, ct),
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Closes the file handle when the path has been idle longer than the threshold, so
        /// rolled-over day files release their handles (retention deletion on Windows) without
        /// losing append capability — the next write transparently reopens in append mode.
        /// </summary>
        public async ValueTask CloseIfIdleAsync(TimeSpan idleThreshold)
        {
            if (_stream is null)
                return;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stream is null || DateTimeOffset.UtcNow - _lastWriteUtc < idleThreshold)
                    return;

                await CloseStreamUnderGateAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        // Caller must hold _gate.
        private async ValueTask CloseStreamUnderGateAsync()
        {
            if (_stream is null)
                return;

            if (_dirtySinceFsync)
            {
                _stream.Flush(flushToDisk: true);
                _dirtySinceFsync = false;
                Interlocked.Increment(ref _fsyncCount);
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        // Caller holds _gate. If a cancellable write fails after reaching the OS, restore the
        // file to its last complete JSONL boundary. The buffer/WAL retry can then append the
        // original events without joining valid JSON to a torn prefix.
        private async Task WriteAppendStreamAsync(Func<FileStream, Task> write, CancellationToken ct)
        {
            var stream = EnsureStream();
            var startPosition = stream.Position;
            try
            {
                await write(stream).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                _dirtySinceFsync = true;
                _lastWriteUtc = DateTimeOffset.UtcNow;
            }
            catch
            {
                stream.SetLength(startPosition);
                stream.Position = startPosition;
                throw;
            }
        }

        private static void RepairTornJsonlTail(FileStream stream)
        {
            if (stream.Length == 0)
                return;

            stream.Position = stream.Length - 1;
            if (stream.ReadByte() == '\n')
                return;

            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];
            var position = stream.Length;
            while (position > 0)
            {
                var readLength = (int)Math.Min(bufferSize, position);
                position -= readLength;
                stream.Position = position;
                stream.ReadExactly(buffer, 0, readLength);

                for (var index = readLength - 1; index >= 0; index--)
                {
                    if (buffer[index] != '\n')
                        continue;

                    stream.SetLength(position + index + 1);
                    return;
                }
            }

            stream.SetLength(0);
        }

        // Compressed files are always written copy-on-write. Refuse to append to a legacy torn
        // gzip file rather than publishing replayed members after the unreadable member and
        // allowing the WAL to commit data that readers cannot reach.
        private void EnsureCompressedFileIsReadable()
        {
            if (!_compressed || _compressedFileValidated || !File.Exists(_path))
                return;

            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var buffer = new byte[64 * 1024];
            while (gzip.Read(buffer, 0, buffer.Length) > 0)
            {
            }

            _compressedFileValidated = true;
        }

        private async Task WriteSingleEventAsync(Stream stream, MarketEvent evt, CancellationToken ct)
        {
            if (_compressed)
            {
                using var gzipStream = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
                await WriteEventToTargetStreamAsync(gzipStream, evt, ct).ConfigureAwait(false);
                await gzipStream.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            await WriteEventToTargetStreamAsync(stream, evt, ct).ConfigureAwait(false);
        }

        private async Task WriteEventsAsync(Stream stream, IReadOnlyList<MarketEvent> events, CancellationToken ct)
        {
            if (_compressed)
            {
                using var gzipStream = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
                await WriteEventsToTargetStreamAsync(gzipStream, events, ct).ConfigureAwait(false);
                await gzipStream.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            await WriteEventsToTargetStreamAsync(stream, events, ct).ConfigureAwait(false);
        }

        private static async Task WriteEventToTargetStreamAsync(Stream stream, MarketEvent evt, CancellationToken ct)
        {
            using var writer = new Utf8JsonWriter(stream, JsonWriterOptions);
            HighPerformanceJson.WriteTo(writer, evt);
            await writer.FlushAsync(ct).ConfigureAwait(false);
            await stream.WriteAsync(NewlineBytes, ct).ConfigureAwait(false);
        }

        private static async Task WriteEventsToTargetStreamAsync(Stream stream, IReadOnlyList<MarketEvent> events, CancellationToken ct)
        {
            using var writer = new Utf8JsonWriter(stream, JsonWriterOptions);

            for (var i = 0; i < events.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                HighPerformanceJson.WriteTo(writer, events[i]);
                await writer.FlushAsync(ct).ConfigureAwait(false);
                await stream.WriteAsync(NewlineBytes, ct).ConfigureAwait(false);
                writer.Reset();
            }
        }

        /// <summary>
        /// Durability barrier: physically syncs any bytes written since the last fsync. The
        /// sink calls this from IStorageSink.FlushAsync, which EventPipeline awaits before
        /// committing the WAL — so committed events are always on physical disk, at one fsync
        /// per pipeline flush instead of one whole-file copy per batch.
        /// </summary>
        public async Task FlushAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_stream is not null && _dirtySinceFsync)
                {
                    _stream.Flush(flushToDisk: true);
                    _dirtySinceFsync = false;
                    Interlocked.Increment(ref _fsyncCount);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await CloseStreamUnderGateAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
                _gate.Dispose();
            }
        }
    }

    private sealed class RetentionManager : IDisposable
    {
        private readonly string _root;
        private readonly int? _retentionDays;
        private readonly long? _maxBytes;
        private readonly ILogger _logger;
        // Using ReaderWriterLockSlim for better concurrency - reads (timestamp checks) are more frequent than writes
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
        private DateTime _lastSweep = DateTime.MinValue;
        private bool _disposed;
        private static readonly string[] _extensions = new[] { ".jsonl", ".jsonl.gz", ".jsonl.gzip" };

        public RetentionManager(string root, int? retentionDays, long? maxBytes, ILogger logger)
        {
            _root = root;
            _retentionDays = retentionDays;
            _maxBytes = maxBytes;
            _logger = logger;
        }

        public void MaybeCleanup()
        {
            if (_disposed || (_retentionDays is null && _maxBytes is null))
                return;

            // Fast path: check if cleanup is needed using read lock (allows concurrent reads)
            _lock.EnterReadLock();
            try
            {
                if ((DateTime.UtcNow - _lastSweep) < TimeSpan.FromSeconds(15))
                    return;
            }
            finally
            {
                _lock.ExitReadLock();
            }

            // Slow path: need to update timestamp, acquire write lock
            _lock.EnterWriteLock();
            try
            {
                // Double-check after acquiring write lock (another thread may have updated)
                if ((DateTime.UtcNow - _lastSweep) < TimeSpan.FromSeconds(15))
                    return;

                _lastSweep = DateTime.UtcNow;
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            try
            {
                var files = Directory.Exists(_root)
                    ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                        .Where(f => _extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        .Select(path => new FileInfo(path))
                        .ToList()
                    : new List<FileInfo>();

                if (_retentionDays is not null)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-_retentionDays.Value);
                    foreach (var f in files.Where(f => f.LastWriteTimeUtc < cutoff))
                    {
                        TryDelete(f);
                    }
                }

                if (_maxBytes is not null)
                {
                    var ordered = files
                        .OrderBy(f => f.LastWriteTimeUtc)
                        .ToList();
                    long total = ordered.Sum(f => f.Exists ? f.Length : 0);

                    var idx = 0;
                    while (total > _maxBytes && idx < ordered.Count)
                    {
                        var target = ordered[idx++];
                        total -= target.Length;
                        TryDelete(target);
                    }
                }
            }
            catch (Exception ex)
            {
                // Soft-fail; retention is best-effort and should not block writes.
                _logger.LogWarning(
                    ex,
                    "Retention cleanup failed for storage root {RootPath}. RetentionDays={RetentionDays}, MaxBytes={MaxBytes}",
                    _root,
                    _retentionDays,
                    _maxBytes);
            }
        }

        private void TryDelete(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to delete file during retention cleanup: {FilePath}, Size={FileSize} bytes",
                    file.FullName,
                    file.Exists ? file.Length : 0);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lock.Dispose();
        }
    }
}

public sealed record JsonlStorageSinkStatistics(
    bool IsBatchingEnabled,
    int BatchSize,
    TimeSpan FlushInterval,
    long EventsBuffered,
    long EventsWritten,
    long BatchesWritten,
    int WriterCount,
    int BufferCount,
    DateTimeOffset Timestamp,
    long FsyncCount = 0,
    int OpenWriterHandles = 0);
