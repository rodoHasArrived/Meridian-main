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
        // Delegate the actual write to the inner sink
        await _inner.AppendAsync(evt, ct).ConfigureAwait(false);

        // Track metadata for this file path
        var path = _policy.GetPath(evt);
        var tracker = _dirty.GetOrAdd(path, _ => new FileWriteTracker());
        tracker.RecordEvent(evt);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // Flush the inner sink first so data is on disk
        await _inner.FlushAsync(ct).ConfigureAwait(false);

        // Now sync dirty paths to the catalog
        await SyncDirtyPathsAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        // Final sync before disposal
        try
        {
            await SyncDirtyPathsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final catalog sync during disposal failed");
        }

        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private async Task SyncDirtyPathsAsync(CancellationToken ct)
    {
        if (_dirty.IsEmpty)
            return;

        // Snapshot and clear dirty set atomically per-key
        var snapshot = new List<KeyValuePair<string, FileWriteTracker>>();
        foreach (var key in _dirty.Keys.ToArray())
        {
            if (_dirty.TryRemove(key, out var tracker))
            {
                snapshot.Add(new KeyValuePair<string, FileWriteTracker>(key, tracker));
            }
        }

        foreach (var (path, tracker) in snapshot)
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

                await _catalog.UpdateFileEntryAsync(entry, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync catalog for path {Path}", path);
                // Re-add tracker so we retry on next flush
                _dirty.TryAdd(path, tracker);
            }
        }

        // Persist catalog to disk (best-effort, non-blocking for writes)
        try
        {
            await _catalog.SaveCatalogAsync(ct).ConfigureAwait(false);
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
