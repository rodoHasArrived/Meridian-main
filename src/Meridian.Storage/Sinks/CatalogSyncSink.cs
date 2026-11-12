using System.Collections.Concurrent;
using Meridian.Contracts.Catalog;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Decorator around any <see cref="IStorageSink"/> that automatically updates the
/// <see cref="IStorageCatalogService"/> whenever data is flushed to disk.
/// This keeps directory indexes and the global catalog in sync with live writes
/// without requiring periodic <c>RebuildCatalogAsync</c> calls.
/// </summary>
public sealed class CatalogSyncSink : IStorageSink
{
    private readonly IStorageSink _inner;
    private readonly IStorageCatalogService _catalog;
    private readonly IStoragePolicy _policy;
    private readonly string _rootPath;
    private readonly ILogger<CatalogSyncSink> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _catalogSavePending;

    /// <summary>
    /// Tracks per-file write metadata accumulated between flushes.
    /// </summary>
    private readonly ConcurrentDictionary<string, FileWriteTracker> _dirty = new(StringComparer.OrdinalIgnoreCase);

    public CatalogSyncSink(
        IStorageSink inner,
        IStorageCatalogService catalog,
        IStoragePolicy policy,
        string rootPath,
        ILogger<CatalogSyncSink>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _rootPath = Path.GetFullPath(rootPath);
        _logger = logger ?? NullLogger<CatalogSyncSink>.Instance;
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _inner.AppendAsync(evt, ct).ConfigureAwait(false);
            var path = _policy.GetPath(evt);
            _dirty.GetOrAdd(path, _ => new FileWriteTracker()).RecordEvent(evt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // Admission and sync share a gate. A flush snapshot must not include metadata for
        // an append that has not crossed the inner sink's durability barrier yet.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _inner.FlushAsync(ct).ConfigureAwait(false);
            await SyncDirtyPathsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final catalog sync during disposal failed");
        }
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private async Task SyncDirtyPathsAsync(CancellationToken ct)
    {
        if (_dirty.IsEmpty && !_catalogSavePending)
            return;

        foreach (var (path, tracker) in _dirty.ToArray())
        {
            try
            {
                var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_rootPath, path);
                var relativePath = Path.GetRelativePath(_rootPath, fullPath);

                if (!File.Exists(fullPath))
                {
                    _logger.LogDebug("Skipping catalog sync for missing file {Path}", relativePath);
                    continue;
                }

                if (!tracker.BaselineLoaded)
                {
                    // Seed from the retained catalog, including after a process restart. Keep
                    // this baseline on failure: UpdateFileEntryAsync may publish its in-memory
                    // entry before persistence fails, so adding the delta again would overcount.
                    var previous = _catalog.GetFilesForSymbol(tracker.Symbol ?? string.Empty)
                        .FirstOrDefault(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                    tracker.IncludeBaseline(previous);
                }

                var fileInfo = new FileInfo(fullPath);
                var fileName = Path.GetFileName(fullPath);
                var isCompressed = fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

                var entry = new IndexedFileEntry
                {
                    FileName = fileName,
                    RelativePath = relativePath,
                    SizeBytes = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    IsCompressed = isCompressed,
                    CompressionType = isCompressed ? "gzip" : null,
                    Format = fileName.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)
                        ? "parquet"
                        : "jsonl",
                    EventCount = tracker.EventCount,
                    FirstTimestamp = tracker.FirstTimestamp,
                    LastTimestamp = tracker.LastTimestamp,
                    FirstSequence = tracker.FirstSequence,
                    LastSequence = tracker.LastSequence,
                    Symbol = tracker.Symbol,
                    EventType = tracker.EventType,
                    Source = tracker.Source,
                    IndexedAt = DateTime.UtcNow,
                    VerificationStatus = "Live"
                };

                // Try to parse date from the path
                TryExtractDate(relativePath, entry);

                _catalogSavePending = true;
                await _catalog.UpdateFileEntryAsync(entry, ct).ConfigureAwait(false);
                _dirty.TryRemove(path, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync catalog for path {Path}", path);
                // Retain the same cumulative tracker for an idempotent retry on next flush.
            }
        }

        // Persist catalog to disk (best-effort, non-blocking for writes)
        try
        {
            await _catalog.SaveCatalogAsync(ct).ConfigureAwait(false);
            _catalogSavePending = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist catalog after sync");
        }
    }

    private static void TryExtractDate(string relativePath, IndexedFileEntry entry)
    {
        // Try to extract a date from path segments or filename
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd_HH", "yyyy-MM", "yyyyMMdd" };

        foreach (var segment in segments)
        {
            var clean = Path.GetFileNameWithoutExtension(segment);
            if (clean.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                clean = Path.GetFileNameWithoutExtension(clean);

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(clean, format, null,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    entry.Date = parsed;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Accumulates write metadata for a single file path between flushes.
    /// Thread-safe via Interlocked operations.
    /// </summary>
    private sealed class FileWriteTracker
    {
        private long _eventCount;
        public bool BaselineLoaded { get; private set; }

        public void IncludeBaseline(IndexedFileEntry? previous)
        {
            if (previous is not null)
            {
                Interlocked.Add(ref _eventCount, previous.EventCount);
                if (previous.FirstTimestamp is { } first)
                    InterlockedMin(ref _firstTimestampTicks, first.Ticks);
                if (previous.LastTimestamp is { } last)
                    InterlockedMax(ref _lastTimestampTicks, last.Ticks);
                if (previous.FirstSequence is { } firstSequence)
                    InterlockedMin(ref _firstSequence, firstSequence);
                if (previous.LastSequence is { } lastSequence)
                    InterlockedMax(ref _lastSequence, lastSequence);
            }
            BaselineLoaded = true;
        }

        private long _firstSequence = long.MaxValue;
        private long _lastSequence = long.MinValue;
        private long _firstTimestampTicks = long.MaxValue;
        private long _lastTimestampTicks = long.MinValue;

        // Set from first event; assumed uniform per file path
        public string? Symbol;
        public string? EventType;
        public string? Source;

        public long EventCount => Interlocked.Read(ref _eventCount);

        public DateTime? FirstTimestamp
        {
            get
            {
                var ticks = Interlocked.Read(ref _firstTimestampTicks);
                return ticks == long.MaxValue ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public DateTime? LastTimestamp
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastTimestampTicks);
                return ticks == long.MinValue ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public long? FirstSequence
        {
            get
            {
                var val = Interlocked.Read(ref _firstSequence);
                return val == long.MaxValue ? null : val;
            }
        }

        public long? LastSequence
        {
            get
            {
                var val = Interlocked.Read(ref _lastSequence);
                return val == long.MinValue ? null : val;
            }
        }

        public void RecordEvent(MarketEvent evt)
        {
            Interlocked.Increment(ref _eventCount);

            // Track timestamp range
            var ticks = evt.Timestamp.UtcDateTime.Ticks;
            InterlockedMin(ref _firstTimestampTicks, ticks);
            InterlockedMax(ref _lastTimestampTicks, ticks);

            // Track sequence range
            if (evt.Sequence > 0)
            {
                InterlockedMin(ref _firstSequence, evt.Sequence);
                InterlockedMax(ref _lastSequence, evt.Sequence);
            }

            // Capture metadata from first event (race is benign — all events for a path share these)
            Symbol ??= evt.EffectiveSymbol;
            EventType ??= evt.Type.ToString();
            Source ??= evt.Source;
        }

        private static void InterlockedMin(ref long location, long value)
        {
            long current;
            do
            {
                current = Interlocked.Read(ref location);
                if (value >= current)
                    return;
            } while (Interlocked.CompareExchange(ref location, value, current) != current);
        }

        private static void InterlockedMax(ref long location, long value)
        {
            long current;
            do
            {
                current = Interlocked.Read(ref location);
                if (value <= current)
                    return;
            } while (Interlocked.CompareExchange(ref location, value, current) != current);
        }
    }
}
