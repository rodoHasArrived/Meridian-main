using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Monitoring;
using Meridian.Application.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    /// Pre-serialize events in parallel when batch size exceeds this threshold.
    /// Default is 5000 events — below this, the context-switching overhead of
    /// Parallel.For exceeds the serialization savings.
    /// </summary>
    public int ParallelSerializationThreshold { get; init; } = 5000;

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
        FlushInterval = TimeSpan.FromSeconds(10),
        ParallelSerializationThreshold = 5000
    };

    /// <summary>
    /// Optimized for low latency with smaller batches.
    /// </summary>
    public static JsonlBatchOptions LowLatency => new()
    {
        BatchSize = 100,
        FlushInterval = TimeSpan.FromSeconds(1),
        ParallelSerializationThreshold = 5000
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
    /// Creates a JsonlStorageSink with default options (no batching for backward compatibility).
    /// </summary>
    public JsonlStorageSink(StorageOptions options, IStoragePolicy policy, ILogger<JsonlStorageSink>? logger = null)
        : this(options, policy, JsonlBatchOptions.NoBatching, logger)
    {
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
        _writerFactory = p => new Lazy<WriterState>(() => WriterState.Create(p, compress), LazyThreadSafetyMode.ExecutionAndPublication);
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
            await FlushBufferAsync(path, buffer, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteEventImmediateAsync(string path, MarketEvent evt, CancellationToken ct)
    {
        var writer = _writers.GetOrAdd(path, _writerFactory).Value;
        var json = JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);
        await writer.WriteLineAsync(json, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _eventsWritten);
    }

    private async Task FlushBufferAsync(string path, MarketEventBuffer buffer, CancellationToken ct)
    {
        var events = buffer.DrainAll();
        if (events.Count == 0)
            return;

        var writer = _writers.GetOrAdd(path, _writerFactory).Value;

        // Serialize events - use parallel serialization only for very large batches
        // where the parallelism savings outweigh context-switching overhead
        var lines = new string[events.Count];
        if (events.Count >= _batchOptions.ParallelSerializationThreshold)
        {
            Parallel.For(0, events.Count, i =>
            {
                lines[i] = JsonSerializer.Serialize(events[i], MarketDataJsonContext.HighPerformanceOptions);
            });
        }
        else
        {
            for (var i = 0; i < events.Count; i++)
            {
                lines[i] = JsonSerializer.Serialize(events[i], MarketDataJsonContext.HighPerformanceOptions);
            }
        }

        // Write all lines in a single batch
        await writer.WriteBatchAsync(lines, ct).ConfigureAwait(false);

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
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushAllBuffersSafelyAsync(CancellationToken ct = default)
    {
        try
        {
            await FlushAllBuffersAsync(_disposalCts.Token).ConfigureAwait(false);
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
                await _flushGate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                try
                {
                    var tasks = new List<Task>();
                    foreach (var kvp in _buffers)
                    {
                        if (kvp.Value.Count > 0)
                        {
                            tasks.Add(FlushBufferAsync(kvp.Key, kvp.Value, CancellationToken.None));
                        }
                    }
                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
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

    private sealed class WriterState : IAsyncDisposable
    {
        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly bool _compressed;

        private WriterState(string path, bool compressed)
        {
            _path = path;
            _compressed = compressed;
        }

        public static WriterState Create(string path, bool compress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new WriterState(path, compress);
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken ct)
        {
            await WriteBatchAsync([line], ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes multiple lines in a single batch operation, minimizing lock contention.
        /// </summary>
        public async ValueTask WriteBatchAsync(string[] lines, CancellationToken ct)
        {
            if (lines.Length == 0)
                return;

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await AtomicFileWriter.AppendAsync(
                    _path,
                    async stream =>
                    {
                        Stream writeStream = stream;
                        if (_compressed)
                        {
                            writeStream = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true);
                        }

                        await using var writer = new StreamWriter(
                            writeStream,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                            1 << 16,
                            leaveOpen: true);

                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line).ConfigureAwait(false);
                        }

                        await writer.FlushAsync().ConfigureAwait(false);
                        if (_compressed && writeStream is GZipStream gzipStream)
                        {
                            await gzipStream.FlushAsync(ct).ConfigureAwait(false);
                        }
                    },
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
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
                // No persistent stream state is held between writes.
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
