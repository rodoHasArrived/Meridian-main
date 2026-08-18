using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Logging;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Prometheus;
using Serilog;

namespace Meridian.Storage.Archival;

/// <summary>
/// Write-Ahead Log (WAL) for durable, crash-safe storage operations.
/// All market events are first written to the WAL before being committed to primary storage.
/// </summary>
public sealed class WriteAheadLog : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<WriteAheadLog>();
    private readonly string _walDirectory;
    private readonly WalOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _truncateLock = new(1, 1);

    // Prometheus counters for WAL recovery observability (2.3)
    // mdc_wal_recovery_events_total is intentionally not registered here: the replay consumer
    // owns that series (see InitializeAsync), and PrometheusMetrics registers it for export.
    private static readonly Counter WalRecoveryCorruptedTotal = Metrics.CreateCounter(
        "mdc_wal_recovery_corrupted_records_total",
        "Total number of corrupted WAL records encountered during recovery");

    private static readonly Gauge WalRecoveryDurationSeconds = Metrics.CreateGauge(
        "mdc_wal_recovery_duration_seconds",
        "Duration of the most recent WAL recovery pass in seconds");

    private FileStream? _currentWalFile;
    private StreamWriter? _currentWriter;
    private string? _currentWalPath;
    private string? _lastRecoveredWalPath;
    private long _currentSequence;
    private long _currentFileSize;
    private DateTime _currentFileCreationTime;
    private int _uncommittedRecords;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private bool _disposed;
    private CancellationTokenSource? _flushLoopCts;
    private Task? _flushLoopTask;
    private Exception? _backgroundFlushFailure;
    private long _corruptedRecordCount;
    private long _skippedRecordCount;

    // WAL file header constants
    private const string WalMagic = "MDCWAL01";
    private const int WalVersion = 1;

    private static int _checksumPathWarmed;

    public WriteAheadLog(string walDirectory, WalOptions? options = null)
    {
        _walDirectory = walDirectory;
        _options = options ?? new WalOptions();
        Directory.CreateDirectory(_walDirectory);
        WarmChecksumPath();
    }

    /// <summary>
    /// Gets the number of valid events recovered during the last initialization.
    /// </summary>
    public long LastRecoveryEventCount { get; private set; }

    /// <summary>
    /// Gets the duration of the last recovery in milliseconds.
    /// </summary>
    public double LastRecoveryDurationMs { get; private set; }

    /// <summary>
    /// Gets the total number of corrupted records encountered across all reads and recoveries.
    /// </summary>
    public long CorruptedRecordCount => Interlocked.Read(ref _corruptedRecordCount);

    /// <summary>
    /// Gets the total number of records skipped due to corruption across all reads and recoveries.
    /// </summary>
    public long SkippedRecordCount => Interlocked.Read(ref _skippedRecordCount);

    /// <summary>
    /// Gets the configured corruption response mode so replay consumers can apply the same
    /// policy to records whose checksum validates but whose payload cannot be deserialized.
    /// </summary>
    public WalCorruptionMode CorruptionMode => _options.CorruptionMode;

    /// <summary>
    /// Records a checksum-valid record whose payload a replay consumer could not deserialize,
    /// applying the same corruption counters and Alert-mode signal as record-level corruption
    /// so semantic payload failures are never dropped without a monitoring signal.
    /// Callers enforcing <see cref="WalCorruptionMode.Halt"/> should report with
    /// <paramref name="recordSkip"/> set to <see langword="false"/> before throwing, so the
    /// corruption is counted without claiming the record was skipped.
    /// </summary>
    public void ReportUnreadablePayload(bool recordSkip = true)
    {
        Interlocked.Increment(ref _corruptedRecordCount);
        if (recordSkip)
            Interlocked.Increment(ref _skippedRecordCount);

        // Keep metric-based alerting in step with the Alert-mode event: semantic payload
        // corruption counts toward the same Prometheus series as record-level corruption.
        // Monotonicity with InitializeAsync's IncTo is preserved — the instance counter
        // incremented above feeds any later IncTo, which only ever raises the metric.
        WalRecoveryCorruptedTotal.Inc();

        if (_options.CorruptionMode == WalCorruptionMode.Alert)
        {
            try
            { CorruptionDetected?.Invoke(1); }
            catch (Exception ex)
            {
                _log.Error(ex, "Exception in CorruptionDetected event handler; ignoring to continue recovery");
            }
        }
    }

    /// <summary>
    /// Raised when a corrupted WAL record is detected during recovery, provided
    /// <see cref="WalOptions.CorruptionMode"/> is <see cref="WalCorruptionMode.Alert"/>.
    /// The argument is the number of corrupted records found in the current recovery pass.
    /// Subscribe to this event to forward alerts to your monitoring infrastructure.
    /// </summary>
    public event Action<long>? CorruptionDetected;

    /// <summary>
    /// Initialize the WAL, recovering any uncommitted transactions.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _log.Information("Initializing WAL in {WalDirectory}", _walDirectory);

        var recoveryStopwatch = System.Diagnostics.Stopwatch.StartNew();
        long totalRecoveredEvents = 0;

        // Find and recover any existing WAL files
        var walFiles = Directory.GetFiles(_walDirectory, "*.wal")
            .OrderBy(f => f)
            .ToList();

        if (walFiles.Count > 0)
        {
            _log.Information("Found {Count} existing WAL files, recovering...", walFiles.Count);
            foreach (var walFile in walFiles)
            {
                totalRecoveredEvents += await RecoverWalFileAsync(walFile, ct);
            }
            // Track the last file that was open at crash time; RepairAsync must not
            // rewrite it in-place because it may still be partially open / active.
            _lastRecoveredWalPath = walFiles[walFiles.Count - 1];
        }

        recoveryStopwatch.Stop();
        LastRecoveryEventCount = totalRecoveredEvents;
        LastRecoveryDurationMs = recoveryStopwatch.Elapsed.TotalMilliseconds;

        var totalCorrupted = CorruptedRecordCount;

        // Emit Prometheus recovery metrics (2.3). The events series is deliberately NOT raised
        // here: this scan counts every checksum-valid record found, including ones whose
        // payload later proves undeserializable and is dropped as corruption. Publishing that
        // tally would claim recovery successes for records that never reached durable storage,
        // and because the series uses IncTo (monotonic max) it would also mask the smaller,
        // truthful count the replay consumer reports. The scan tally stays available through
        // LastRecoveryEventCount and the log line below; the replayer owns the series.
        WalRecoveryCorruptedTotal.IncTo(totalCorrupted);
        WalRecoveryDurationSeconds.Set(recoveryStopwatch.Elapsed.TotalSeconds);

        if (totalRecoveredEvents > 0 || totalCorrupted > 0)
        {
            _log.Information(
                "WAL recovery complete: {RecoveredCount} valid events, {CorruptedCount} corrupted records, {DurationMs:F1}ms elapsed",
                totalRecoveredEvents,
                totalCorrupted,
                LastRecoveryDurationMs);
        }

        // Get the highest sequence number
        _currentSequence = await GetLastSequenceNumberAsync(ct);

        // Start a new WAL file
        await StartNewWalFileAsync(ct);
        StartDelayedFlushLoop();

        _log.Information("WAL initialized, current sequence: {Sequence}", _currentSequence);
    }

    /// <summary>
    /// Append a record to the WAL.
    /// </summary>
    public Task<WalRecord> AppendAsync<T>(T data, string recordType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        ct.ThrowIfCancellationRequested();

        var payload = data is MarketEvent marketEvent
            ? JsonSerializer.Serialize(marketEvent, MarketDataJsonContext.Default.MarketEvent)
            : JsonSerializer.Serialize(data, MarketDataJsonContext.HighPerformanceOptions);
        return AppendSerializedPayloadAsync(payload, recordType, ct);
    }

    private async Task<WalRecord> AppendSerializedPayloadAsync(string payload, string recordType, CancellationToken ct)
    {
        ThrowIfBackgroundFlushFailed();
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Check if we need to rotate the WAL file
            if (ShouldRotate())
            {
                await RotateWalFileAsync(ct).ConfigureAwait(false);
            }

            var sequence = ++_currentSequence;
            var timestamp = DateTime.UtcNow;

            // Create record with checksum
            var record = new WalRecord
            {
                Sequence = sequence,
                Timestamp = timestamp,
                RecordType = recordType,
                Payload = payload,
                Checksum = ComputeChecksum(sequence, timestamp, recordType, payload)
            };

            // Write to WAL
            await WriteRecordAsync(record, ct).ConfigureAwait(false);

            _uncommittedRecords++;

            // Check if we should flush (use internal method since we already hold _writeLock)
            if (_options.SyncMode == WalSyncMode.EveryWrite ||
                (_options.SyncMode == WalSyncMode.BatchedSync && _uncommittedRecords >= _options.SyncBatchSize) ||
                (DateTime.UtcNow - _lastFlushTime) >= _options.MaxFlushDelay)
            {
                await FlushInternalAsync(ct).ConfigureAwait(false);
            }

            return record;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Commit a batch of records, marking them as successfully persisted.
    /// </summary>
    public async Task CommitAsync(long throughSequence, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // Write a commit marker
            var commitRecord = new WalRecord
            {
                Sequence = ++_currentSequence,
                Timestamp = DateTime.UtcNow,
                RecordType = "COMMIT",
                Payload = throughSequence.ToString(),
                Checksum = string.Empty // Computed below
            };
            commitRecord.Checksum = ComputeChecksum(
                commitRecord.Sequence,
                commitRecord.Timestamp,
                commitRecord.RecordType,
                commitRecord.Payload);

            await WriteRecordAsync(commitRecord, ct);
            await FlushInternalAsync(ct);

            _log.Debug("Committed through sequence {Sequence}", throughSequence);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Flush any buffered writes to disk.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        ThrowIfBackgroundFlushFailed();
        await _writeLock.WaitAsync(ct);
        try
        {
            await FlushInternalAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Internal flush that assumes the caller already holds <see cref="_writeLock"/>.
    /// Called from <see cref="AppendAsync{T}"/> and <see cref="CommitAsync"/> which
    /// acquire the lock before invoking flush, avoiding a deadlock on the non-reentrant
    /// <see cref="SemaphoreSlim"/>.
    /// </summary>
    private async Task FlushInternalAsync(CancellationToken ct = default)
    {
        if (_currentWriter == null || _currentWalFile == null)
            return;

        await _currentWriter.FlushAsync().ConfigureAwait(false);

        if (_options.SyncMode != WalSyncMode.NoSync)
        {
            // The underlying FileStream was opened with FileOptions.WriteThrough, which bypasses
            // the .NET managed buffer and pushes data to the OS kernel buffer on each write.
            // FlushAsync submits any remaining OS buffer data without the millisecond-level
            // blocking of a synchronous fsync(2) — sufficient for BatchedSync's balanced guarantee.
            await _currentWalFile.FlushAsync(ct).ConfigureAwait(false);

            if (_options.SyncMode == WalSyncMode.EveryWrite)
            {
                // EveryWrite is documented as the "most durable" mode, so it must force contents to
                // physical disk (fsync) rather than trusting WriteThrough + async flush. There is no
                // async flush-to-disk API, so this synchronous call is intentional: it is the cost
                // callers accept when they opt into the strongest durability guarantee.
                _currentWalFile.Flush(flushToDisk: true);
            }
        }

        _uncommittedRecords = 0;
        _lastFlushTime = DateTime.UtcNow;
    }

    private void StartDelayedFlushLoop()
    {
        if (_flushLoopTask is not null ||
            _options.SyncMode != WalSyncMode.BatchedSync ||
            _options.MaxFlushDelay <= TimeSpan.Zero)
        {
            return;
        }

        _flushLoopCts = new CancellationTokenSource();
        _flushLoopTask = RunDelayedFlushLoopAsync(_flushLoopCts.Token);
    }

    private async Task RunDelayedFlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.MaxFlushDelay);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await _writeLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_uncommittedRecords > 0 &&
                        DateTime.UtcNow - _lastFlushTime >= _options.MaxFlushDelay)
                    {
                        await FlushInternalAsync(ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _writeLock.Release();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _backgroundFlushFailure = ex;
            _log.Error(ex, "Lifecycle-owned delayed WAL flush failed");
        }
    }

    private void ThrowIfBackgroundFlushFailed()
    {
        if (_backgroundFlushFailure is not null)
        {
            throw new IOException("The delayed WAL flush loop failed; new writes are blocked.", _backgroundFlushFailure);
        }
    }

    /// <summary>
    /// Get uncommitted records for replay/recovery using streaming reads.
    /// Processes records in batches to avoid loading entire WAL into memory.
    /// </summary>
    public async IAsyncEnumerable<WalRecord> GetUncommittedRecordsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // First pass: find the last committed sequence across all WAL files
        long lastCommittedSequence = 0;

        // Ordinal, matching TruncateAsync and RecoveryEnumerationIsSequenceOrdered: a
        // culture-sensitive comparison could order segment names differently than the
        // ordering those paths prove, and enumeration order is what they reason about.
        var walFiles = Directory.GetFiles(_walDirectory, "*.wal")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // Warn if total uncommitted WAL size is large
        var totalSize = walFiles.Sum(f => new FileInfo(f).Length);
        if (totalSize > _options.UncommittedSizeWarningThreshold)
        {
            _log.Warning(
                "WAL uncommitted data size is {SizeMB:F1}MB (threshold: {ThresholdMB:F1}MB). Recovery may be slow",
                totalSize / (1024.0 * 1024.0),
                _options.UncommittedSizeWarningThreshold / (1024.0 * 1024.0));
        }

        foreach (var walFile in walFiles)
        {
            await foreach (var record in ReadWalFileAsync(walFile, ct))
            {
                if (record.RecordType == "COMMIT" && long.TryParse(record.Payload, out var seq))
                {
                    lastCommittedSequence = Math.Max(lastCommittedSequence, seq);
                }
            }
        }

        // Second pass: stream uncommitted records without loading all into memory
        const int batchSize = 10_000;
        var batch = new List<WalRecord>(batchSize);

        foreach (var walFile in walFiles)
        {
            await foreach (var record in ReadWalFileAsync(walFile, ct))
            {
                if (record.RecordType == "COMMIT")
                    continue;
                if (record.Sequence <= lastCommittedSequence)
                    continue;

                batch.Add(record);

                if (batch.Count >= batchSize)
                {
                    foreach (var r in batch)
                    {
                        yield return r;
                    }
                    batch.Clear();
                }
            }
        }

        // Yield remaining records in the last batch
        foreach (var r in batch)
        {
            yield return r;
        }
    }

    /// <summary>
    /// Truncate WAL files that have been fully committed.
    /// Eligibility normally comes from segment-name metadata alone: file names embed the
    /// monotonic sequence counter at creation, so a completed segment's records are bounded
    /// by its successor's embedded base and no record scan is needed. Segments whose names
    /// do not parse (or whose ordering cannot be trusted) fall back to a full record scan.
    /// Runs under its own lock so appends and commits are never stalled behind truncation
    /// I/O such as archive compression.
    /// </summary>
    public async Task TruncateAsync(long throughSequence, CancellationToken ct = default)
    {
        await _truncateLock.WaitAsync(ct);
        try
        {
            // List first, then snapshot the active path. The active path only ever moves to
            // newly created files, so every listed file that is not the snapshot-active one
            // is provably closed: it is either already-rotated, or the snapshot-active file
            // itself (skipped below). Snapshotting before listing would allow a rotation in
            // between to slip a still-open, near-empty segment into the listing under a
            // stale active path — and the scan fallback would see it as fully committed.
            var walFiles = Directory.GetFiles(_walDirectory, "*.wal")
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            var activeWalPath = _currentWalPath;

            var inferredBounds = TryInferSegmentUpperBounds(walFiles, activeWalPath);

            foreach (var walFile in walFiles)
            {
                if (string.Equals(walFile, activeWalPath, StringComparison.Ordinal))
                    continue;

                bool fullyCommitted;
                if (inferredBounds != null && inferredBounds.TryGetValue(walFile, out var upperBound))
                {
                    // Name-derived bound: every record in this segment has a sequence at or
                    // below the successor segment's embedded base, so committed-ness is a
                    // metadata comparison. Record scanning is not required, but deletion still
                    // requires a valid header so corrupt segments remain available for recovery.
                    fullyCommitted = upperBound <= throughSequence;
                }
                else
                {
                    // Fallback: scan the records to find the segment's max sequence.
                    long maxSequence = 0;
                    await foreach (var record in ReadWalFileAsync(walFile, ct))
                    {
                        maxSequence = Math.Max(maxSequence, record.Sequence);
                    }

                    fullyCommitted = maxSequence <= throughSequence;
                }

                // A corrupt header can make the scan fallback enumerate zero records, and segment
                // metadata cannot prove that a corrupt file is safe to remove. Never delete such
                // a file — it may still hold the only copy of unreplayed records.
                if (fullyCommitted && !await HasValidHeaderAsync(walFile, ct))
                {
                    _log.Error(
                        "Refusing to truncate WAL file {File}: header is invalid; file preserved for inspection",
                        walFile);
                    continue;
                }

                if (fullyCommitted)
                {
                    // Archive or delete the WAL file
                    if (_options.ArchiveAfterTruncate)
                    {
                        var archiveDir = Path.Combine(_walDirectory, "archive");
                        Directory.CreateDirectory(archiveDir);
                        var archivePath = Path.Combine(archiveDir, Path.GetFileName(walFile) + ".gz");

                        // Fully write, flush, and close the compressed archive before touching the
                        // original. Disposing the GZipStream flushes its trailer; fsyncing the output
                        // guarantees the bytes reach disk.
                        await using (var input = File.OpenRead(walFile))
                        await using (var output = new FileStream(
                            archivePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 64 * 1024,
                            FileOptions.WriteThrough | FileOptions.Asynchronous))
                        {
                            // leaveOpen so disposing the GZipStream (which flushes its trailer)
                            // does not close 'output' before we fsync it below.
                            await using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                            {
                                await input.CopyToAsync(gzip, ct);
                            }

                            await output.FlushAsync(ct);
                            // Force an OS-level fsync so the archive is durable on every OS/filesystem
                            // before the original WAL file is removed.
                            output.Flush(flushToDisk: true);
                        }

                        // Persist the archive rename and verify the archive actually landed before
                        // deleting the only remaining copy of the records.
                        await AtomicFileWriter.SyncDirectoryAsync(archiveDir, ct);

                        var archiveInfo = new FileInfo(archivePath);
                        if (!archiveInfo.Exists || archiveInfo.Length == 0)
                        {
                            _log.Error(
                                "Refusing to delete WAL file {File}: archive {Archive} is missing or empty",
                                walFile, archivePath);
                            continue;
                        }
                    }

                    File.Delete(walFile);
                    // post-commit (the WAL file is already gone): must not observe cancellation.
                    await AtomicFileWriter.SyncDirectoryAsync(_walDirectory, CancellationToken.None);
                    _log.Information("Truncated WAL file {File}", walFile);
                }
            }
        }
        finally
        {
            _truncateLock.Release();
        }
    }

    /// <summary>
    /// Reports whether segment names prove that <see cref="GetUncommittedRecordsAsync"/> yields
    /// records in non-decreasing sequence order.
    /// </summary>
    /// <remarks>
    /// Enumeration walks segments in ordinal name order, and names embed the monotonic sequence
    /// counter's value at creation. Every record in a segment was appended before the next
    /// segment was created, so when the embedded bases are non-decreasing in name order, no
    /// later-enumerated record can carry a lower sequence than an earlier one. A clock rollback
    /// across a rotation (or a foreign <c>*.wal</c> file) breaks that correspondence and is
    /// reported here as unordered.
    /// <para>
    /// Consumers that durably acknowledge a cumulative horizon <em>mid-enumeration</em> must
    /// check this first: committing through a high sequence while lower-sequence records are
    /// still unreplayed would let the next pass filter those records as already committed and
    /// lose them. A single commit issued after the enumeration completes is always safe.
    /// </para>
    /// </remarks>
    public bool RecoveryEnumerationIsSequenceOrdered()
    {
        var walFiles = Directory.GetFiles(_walDirectory, "*.wal")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var previousBase = -1L;
        foreach (var walFile in walFiles)
        {
            if (!TryParseSegmentBaseSequence(walFile, out var baseSequence) || baseSequence < previousBase)
                return false;

            previousBase = baseSequence;
        }

        return true;
    }

    /// <summary>
    /// Derives, from segment names alone, an upper bound on each completed segment's max
    /// record sequence: names embed the value of the monotonic sequence counter at creation
    /// ("wal_{utcstamp}_{sequence:D12}"), so every record in a segment has a sequence at or
    /// below the base embedded in the next-created segment's name.
    /// Returns null — sending every file down the record-scan fallback — unless every name
    /// parses, the bases are non-decreasing in sorted order, and the active segment sorts
    /// last (all three fail together only when foreign files or clock anomalies make name
    /// order untrustworthy as creation order).
    /// </summary>
    private static Dictionary<string, long>? TryInferSegmentUpperBounds(
        List<string> sortedWalFiles,
        string? activeWalPath)
    {
        if (sortedWalFiles.Count == 0
            || activeWalPath == null
            || !string.Equals(sortedWalFiles[^1], activeWalPath, StringComparison.Ordinal))
        {
            return null;
        }

        var baseSequences = new long[sortedWalFiles.Count];
        for (var i = 0; i < sortedWalFiles.Count; i++)
        {
            if (!TryParseSegmentBaseSequence(sortedWalFiles[i], out baseSequences[i])
                || (i > 0 && baseSequences[i] < baseSequences[i - 1]))
            {
                return null;
            }
        }

        var bounds = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < sortedWalFiles.Count - 1; i++)
        {
            bounds[sortedWalFiles[i]] = baseSequences[i + 1];
        }

        return bounds;
    }

    /// <summary>
    /// Parses the creation-time base sequence out of a segment file name of the form
    /// "wal_yyyyMMdd_HHmmss_############.wal" with an optional "_N" disambiguator.
    /// </summary>
    private static bool TryParseSegmentBaseSequence(string walFilePath, out long baseSequence)
    {
        baseSequence = 0;
        var parts = Path.GetFileNameWithoutExtension(walFilePath).Split('_');
        if (parts.Length is not (4 or 5)
            || !string.Equals(parts[0], "wal", StringComparison.Ordinal)
            || parts[3].Length != 12)
        {
            return false;
        }

        if (parts.Length == 5 && !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        return long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out baseSequence);
    }

    private async Task StartNewWalFileAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var baseName = $"wal_{now:yyyyMMdd_HHmmss}_{_currentSequence:D12}";

        // Guard against the rare case where two WAL files would share the same timestamp+sequence
        // (e.g. rapid rotation within the same second, or re-initialization immediately after
        // a session whose only valid sequence was 0).  Append a monotonically-increasing suffix
        // until a unique path is found.
        var disambiguator = 0;
        do
        {
            var fileName = disambiguator == 0
                ? $"{baseName}.wal"
                : $"{baseName}_{disambiguator}.wal";
            _currentWalPath = Path.Combine(_walDirectory, fileName);
            disambiguator++;
        }
        while (File.Exists(_currentWalPath));

        _currentWalFile = new FileStream(
            _currentWalPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough | FileOptions.Asynchronous);

        _currentWriter = new StreamWriter(_currentWalFile, Encoding.UTF8, bufferSize: 32 * 1024);

        // Write header
        await _currentWriter.WriteLineAsync($"{WalMagic}|{WalVersion}|{now:O}").ConfigureAwait(false);
        await _currentWriter.FlushAsync().ConfigureAwait(false);

        _currentFileSize = _currentWalFile.Length;
        _currentFileCreationTime = now;
        _log.Debug("Started new WAL file: {File}", _currentWalPath);
    }

    private async Task RotateWalFileAsync(CancellationToken ct)
    {
        if (_currentWriter != null)
        {
            await _currentWriter.FlushAsync().ConfigureAwait(false);
            await _currentWriter.DisposeAsync().ConfigureAwait(false);
            // _currentWriter.DisposeAsync() already closes the underlying _currentWalFile stream
            _currentWriter = null;
            _currentWalFile = null;
        }

        await StartNewWalFileAsync(ct);
    }

    private bool ShouldRotate()
    {
        return _currentFileSize >= _options.MaxWalFileSizeBytes ||
               (_options.MaxWalFileAge.HasValue &&
                _currentFileCreationTime + _options.MaxWalFileAge.Value < DateTime.UtcNow);
    }

    private async Task WriteRecordAsync(WalRecord record, CancellationToken ct)
    {
        if (_currentWriter == null)
        {
            throw new InvalidOperationException("WAL not initialized");
        }

        // Write fields directly to avoid allocating a single large interpolated string.
        // The StreamWriter buffers internally, so multiple Write calls are coalesced.
        var writer = _currentWriter;
        writer.Write(record.Sequence);
        writer.Write('|');
        writer.Write(record.Timestamp.ToString("O"));
        writer.Write('|');
        writer.Write(record.RecordType);
        writer.Write('|');
        writer.Write(record.Checksum);
        writer.Write('|');
        await writer.WriteLineAsync(record.Payload).ConfigureAwait(false);

        // Approximate size tracking — avoids expensive UTF-8 measurement on every write.
        // Payload dominates; the fixed-format prefix is typically ~80 ASCII bytes.
        _currentFileSize += 80 + Encoding.UTF8.GetByteCount(record.Payload) + Environment.NewLine.Length;
    }

    private async Task<long> RecoverWalFileAsync(string walFile, CancellationToken ct)
    {
        _log.Information("Recovering WAL file: {File}", walFile);

        // Capture corruption counter before reading so we can determine
        // how many records were corrupted in this specific file.
        var corruptedBefore = Interlocked.Read(ref _corruptedRecordCount);

        long validRecords = 0;

        await foreach (var record in ReadWalFileAsync(walFile, ct))
        {
            // ReadWalFileAsync already validates checksums and only yields valid records.
            // Corrupted records are logged and counted within ReadWalFileAsync.
            validRecords++;
        }

        var corruptedInFile = Interlocked.Read(ref _corruptedRecordCount) - corruptedBefore;

        if (corruptedInFile > 0)
        {
            _log.Warning(
                "WAL recovery found {CorruptedCount} corrupted records in {File}",
                corruptedInFile, walFile);

            // Honour the configured corruption response mode.
            switch (_options.CorruptionMode)
            {
                case WalCorruptionMode.Alert:
                    // Fire the event so monitoring infrastructure can alert operators.
                    // We continue recovery — the valid records are still usable.
                    try
                    { CorruptionDetected?.Invoke(corruptedInFile); }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Exception in CorruptionDetected event handler; ignoring to continue recovery");
                    }
                    break;

                case WalCorruptionMode.Halt:
                    throw new InvalidDataException(
                        $"WAL recovery halted: {corruptedInFile} corrupted record(s) found in '{walFile}'. " +
                        "Inspect the file and either repair it or change WalOptions.CorruptionMode to Skip to bypass.");

                case WalCorruptionMode.Skip:
                default:
                    // Legacy behaviour: log the warning above and continue silently.
                    break;
            }
        }

        _log.Information(
            "Recovered {ValidCount} valid records, {CorruptedCount} corrupted from {File}",
            validRecords, corruptedInFile, walFile);

        return validRecords;
    }

    private async IAsyncEnumerable<WalRecord> ReadWalFileAsync(
        string walFile,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = new FileStream(
            walFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        // Skip header
        var header = await reader.ReadLineAsync(ct);
        if (header == null)
        {
            // Zero-byte file from a crash immediately after creation: nothing to recover.
            _log.Warning("Empty WAL file {File}", walFile);
            yield break;
        }

        if (!header.StartsWith(WalMagic))
        {
            // A non-empty file with the wrong magic may still hold records. Header corruption
            // follows the same policy as record corruption; TruncateAsync independently refuses
            // to delete files whose header is invalid, so skipping here cannot cause deletion.
            Interlocked.Increment(ref _corruptedRecordCount);
            Interlocked.Increment(ref _skippedRecordCount);

            switch (_options.CorruptionMode)
            {
                case WalCorruptionMode.Alert:
                    _log.Error(
                        "Invalid WAL header in {File}; skipping the file. It is preserved on disk for inspection",
                        walFile);
                    try
                    { CorruptionDetected?.Invoke(1); }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Exception in CorruptionDetected event handler; ignoring to continue recovery");
                    }
                    break;

                case WalCorruptionMode.Halt:
                    throw new InvalidDataException(
                        $"Invalid WAL header in '{walFile}'; refusing to treat the file as empty. " +
                        "Inspect the file or run RepairAsync before recovery can proceed.");

                case WalCorruptionMode.Skip:
                default:
                    _log.Warning("Invalid WAL header in {File}; skipping the file", walFile);
                    break;
            }

            yield break;
        }

        while (!reader.EndOfStream)
        {
            // Cancellation must throw rather than silently end the enumeration: callers
            // treat a completed scan as a full read of the file — TruncateAsync deletes
            // files and sequence recovery picks the next sequence number based on it.
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|', 5);
            if (parts.Length < 5)
            {
                _log.Warning(
                    "Malformed WAL record skipped in {File}: expected 5 fields but found {FieldCount}",
                    walFile, parts.Length);
                Interlocked.Increment(ref _corruptedRecordCount);
                Interlocked.Increment(ref _skippedRecordCount);
                continue;
            }

            if (!long.TryParse(parts[0], out var sequence))
            {
                _log.Warning("Malformed WAL record skipped in {File}: unable to parse sequence", walFile);
                Interlocked.Increment(ref _corruptedRecordCount);
                Interlocked.Increment(ref _skippedRecordCount);
                continue;
            }

            if (!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                _log.Warning(
                    "Malformed WAL record skipped in {File}: unable to parse timestamp for sequence {Sequence}",
                    walFile, sequence);
                Interlocked.Increment(ref _corruptedRecordCount);
                Interlocked.Increment(ref _skippedRecordCount);
                continue;
            }

            var recordType = parts[2];
            var checksum = parts[3];
            var payload = parts[4];

            // Validate checksum
            var expectedChecksum = ComputeChecksum(sequence, timestamp, recordType, payload);
            if (!string.Equals(checksum, expectedChecksum, StringComparison.Ordinal))
            {
                _log.Warning(
                    "Invalid checksum for WAL record with sequence {Sequence} in {File}, skipping",
                    sequence, walFile);
                Interlocked.Increment(ref _corruptedRecordCount);
                Interlocked.Increment(ref _skippedRecordCount);
                continue;
            }

            yield return new WalRecord
            {
                Sequence = sequence,
                Timestamp = timestamp,
                RecordType = recordType,
                Checksum = checksum,
                Payload = payload
            };
        }
    }

    /// <summary>
    /// Checks whether a WAL file starts with the expected magic header.
    /// An empty (zero-record) file counts as valid: it holds nothing to lose.
    /// </summary>
    private static async Task<bool> HasValidHeaderAsync(string walFile, CancellationToken ct)
    {
        await using var stream = new FileStream(
            walFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(ct);
        return header == null || header.StartsWith(WalMagic);
    }

    private async Task<long> GetLastSequenceNumberAsync(CancellationToken ct)
    {
        long maxSequence = 0;

        var walFiles = Directory.GetFiles(_walDirectory, "*.wal");
        foreach (var walFile in walFiles)
        {
            await foreach (var record in ReadWalFileAsync(walFile, ct))
            {
                maxSequence = Math.Max(maxSequence, record.Sequence);
            }
        }

        return maxSequence;
    }

    /// <summary>
    /// Computes a SHA-256 checksum for a WAL record using incremental hashing
    /// to avoid allocating a single large concatenated string.
    /// </summary>
    private static string ComputeChecksum(long sequence, DateTime timestamp, string recordType, string payload)
    {
        Span<byte> hashBytes = stackalloc byte[32]; // SHA-256 = 32 bytes
        ComputeChecksumCore(sequence, timestamp, recordType, payload, hashBytes);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static void ComputeChecksumCore(long sequence, DateTime timestamp, string recordType, string payload, Span<byte> destination)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
            {
                throw new InvalidOperationException("Failed to format WAL sequence.");
            }

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
            {
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            }

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            if (!SHA256.TryHashData(recordBytes[..written], destination, out _))
            {
                throw new InvalidOperationException("Failed to compute WAL checksum.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Repairs all WAL files by scanning every record, validating checksums,
    /// and rewriting only valid records into new WAL files.
    /// Corrupted records are discarded and counted in the result.
    /// </summary>
    public async Task<WalRepairResult> RepairAsync(CancellationToken ct = default)
    {
        _log.Information("Starting WAL repair in {WalDirectory}", _walDirectory);

        var walFiles = Directory.GetFiles(_walDirectory, "*.wal")
            .OrderBy(f => f)
            .ToList();

        int totalRecords = 0;
        int validRecords = 0;
        int corruptedRecords = 0;
        int repairedFiles = 0;

        foreach (var walFile in walFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Skip the currently active WAL file entirely; it may still be open for writes.
            if (string.Equals(walFile, _currentWalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The file that was open at crash time is still worth scanning so callers get an
            // accurate corruption/retention report, but it is not eligible for in-place rewrite.
            var canRewriteInPlace = !string.Equals(
                walFile,
                _lastRecoveredWalPath,
                StringComparison.OrdinalIgnoreCase);

            var fileValidRecords = new List<WalRecord>();
            int fileCorruptedCount = 0;

            // Read the raw file directly to count both valid and corrupted records,
            // rather than going through ReadWalFileAsync which filters corrupted ones out.
            await using (var stream = new FileStream(
                walFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var reader = new StreamReader(stream);

                // Read and validate header
                var header = await reader.ReadLineAsync();
                if (header == null || !header.StartsWith(WalMagic))
                {
                    _log.Warning("Skipping WAL file with invalid header during repair: {File}", walFile);
                    continue;
                }

                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    totalRecords++;

                    var parts = line.Split('|', 5);
                    if (parts.Length < 5 ||
                        !long.TryParse(parts[0], out var sequence) ||
                        !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                    {
                        fileCorruptedCount++;
                        corruptedRecords++;
                        continue;
                    }

                    var recordType = parts[2];
                    var checksum = parts[3];
                    var payload = parts[4];

                    var expectedChecksum = ComputeChecksum(sequence, timestamp, recordType, payload);
                    if (!string.Equals(checksum, expectedChecksum, StringComparison.Ordinal))
                    {
                        _log.Warning(
                            "Repair: corrupted record with sequence {Sequence} in {File}",
                            sequence, walFile);
                        fileCorruptedCount++;
                        corruptedRecords++;
                        continue;
                    }

                    validRecords++;
                    fileValidRecords.Add(new WalRecord
                    {
                        Sequence = sequence,
                        Timestamp = timestamp,
                        RecordType = recordType,
                        Checksum = checksum,
                        Payload = payload
                    });
                }
            }

            // Only rewrite files that are eligible for in-place repair and actually contain corruption.
            if (fileCorruptedCount > 0 && canRewriteInPlace)
            {
                var tempPath = walFile + ".repair";

                await using (var outStream = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 64 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await using var writer = new StreamWriter(outStream, Encoding.UTF8, bufferSize: 32 * 1024);

                    // Write header
                    await writer.WriteLineAsync($"{WalMagic}|{WalVersion}|{DateTime.UtcNow:O}");

                    // Write valid records
                    foreach (var record in fileValidRecords)
                    {
                        ct.ThrowIfCancellationRequested();
                        var recordLine = $"{record.Sequence}|{record.Timestamp:O}|{record.RecordType}|{record.Checksum}|{record.Payload}";
                        await writer.WriteLineAsync(recordLine);
                    }

                    await writer.FlushAsync();

                    // Force the repaired contents to physical disk before the atomic rename, so a
                    // crash immediately after the rename cannot surface a temp file whose bytes
                    // never left the OS cache. Mirrors the truncate path's flush-to-disk.
                    await outStream.FlushAsync(ct).ConfigureAwait(false);
                    outStream.Flush(flushToDisk: true);
                }

                // Replace the original with the repaired file in a single atomic rename.
                // A prior File.Delete + File.Move sequence had a crash window between the two
                // calls in which the WAL file could be lost entirely; File.Move(overwrite: true)
                // is an atomic replace on the same volume.
                File.Move(tempPath, walFile, overwrite: true);

                // Make the rename durable so the repaired file survives a crash or power loss.
                // post-commit (the repaired file is already in place): must not observe cancellation.
                await AtomicFileWriter.SyncDirectoryAsync(
                    Path.GetDirectoryName(walFile) ?? _walDirectory, CancellationToken.None);

                repairedFiles++;

                _log.Information(
                    "Repaired WAL file {File}: kept {ValidCount} records, removed {CorruptedCount} corrupted",
                    walFile, fileValidRecords.Count, fileCorruptedCount);
            }
            else if (fileCorruptedCount > 0)
            {
                _log.Information(
                    "Scanned WAL file {File}: kept {ValidCount} records and found {CorruptedCount} corrupted, but skipped in-place rewrite because it was the last recovered file",
                    walFile, fileValidRecords.Count, fileCorruptedCount);
            }
        }

        var result = new WalRepairResult(totalRecords, validRecords, corruptedRecords, repairedFiles);

        _log.Information(
            "WAL repair complete: {TotalRecords} total, {ValidRecords} valid, {CorruptedRecords} corrupted, {RepairedFiles} files repaired",
            result.TotalRecords, result.ValidRecords, result.CorruptedRecords, result.RepairedFiles);

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // Fixed acquisition order (write, then truncate) cannot deadlock: truncation never
        // takes the write lock and writers never take the truncate lock. Holding both here
        // guarantees no in-flight truncation observes the disposed semaphores.
        if (_flushLoopCts is not null)
        {
            await _flushLoopCts.CancelAsync().ConfigureAwait(false);
        }

        if (_flushLoopTask is not null)
        {
            await _flushLoopTask.ConfigureAwait(false);
        }

        await _writeLock.WaitAsync();
        await _truncateLock.WaitAsync();
        try
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_currentWriter != null)
            {
                await FlushInternalAsync(CancellationToken.None).ConfigureAwait(false);
                if (_currentWalFile is not null && _options.SyncMode != WalSyncMode.NoSync)
                {
                    _currentWalFile.Flush(flushToDisk: true);
                }
                await _currentWriter.DisposeAsync();
                // _currentWriter.DisposeAsync() already closes the underlying _currentWalFile stream
                // so we should not attempt to flush or dispose it again
            }

            _log.Information("WAL disposed, last sequence: {Sequence}", _currentSequence);
        }
        finally
        {
            _truncateLock.Release();
            _writeLock.Release();
            _writeLock.Dispose();
            _truncateLock.Dispose();
            _flushLoopCts?.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Internal benchmark / test shim.
    // Do NOT remove without updating WalChecksumBenchmarks and
    // AllocationBudgetIntegrationTests in tests/Meridian.Tests/Performance/.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exposes the private <c>ComputeChecksum</c> method to benchmark and test
    /// assemblies so they can measure the cost of checksum computation in isolation.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static string ComputeChecksumForBenchmark(long sequence, DateTime timestamp, string recordType, string payload)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        ComputeChecksumCore(sequence, timestamp, recordType, payload, hashBytes);
        return string.Empty;
    }

    /// <summary>
    /// Pre-JITs the hot checksum path so that allocation-measurement tests
    /// (and the first production call) receive clean, zero-allocation baselines.
    /// Idempotent: only runs once per process lifetime.
    /// </summary>
    /// <remarks>
    /// The medium-payload warm-up (900 chars) specifically pre-initialises the
    /// SIMD UTF-8 encoding path that .NET 9 uses for strings longer than ~64
    /// characters; without it, the first call allocates ~120 bytes of lazy-init
    /// state that inflates allocation-budget tests.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static void WarmChecksumPath()
    {
        if (Interlocked.Exchange(ref _checksumPathWarmed, 1) != 0)
        {
            return;
        }

        Span<byte> dest = stackalloc byte[32];
        // Small payload: warms the stackalloc + SHA-256 + Utf8Formatter paths.
        ComputeChecksumCore(0, DateTime.UtcNow, "Trade", new string('x', 64), dest);
        // Medium payload: pre-initialises the SIMD UTF-8 GetByteCount/GetBytes
        // code path that triggers ~120 bytes of one-time managed allocation on
        // first use with strings longer than the scalar processing threshold.
        ComputeChecksumCore(0, DateTime.UtcNow, "L2Snapshot", new string('x', 900), dest);
    }
}

/// <summary>
/// A record in the Write-Ahead Log.
/// </summary>
public sealed class WalRecord
{
    public long Sequence { get; set; }
    public DateTime Timestamp { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;

    public T? DeserializePayload<T>()
    {
        return JsonSerializer.Deserialize<T>(Payload, MarketDataJsonContext.HighPerformanceOptions);
    }
}

/// <summary>
/// WAL configuration options.
/// </summary>
public sealed class WalOptions
{
    /// <summary>
    /// Maximum WAL file size before rotation.
    /// </summary>
    public long MaxWalFileSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// Maximum WAL file age before rotation.
    /// </summary>
    public TimeSpan? MaxWalFileAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Sync mode for durability.
    /// </summary>
    public WalSyncMode SyncMode { get; set; } = WalSyncMode.BatchedSync;

    /// <summary>
    /// Number of records to batch before syncing.
    /// </summary>
    public int SyncBatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum delay between flushes.
    /// </summary>
    public TimeSpan MaxFlushDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to archive WAL files after truncation.
    /// </summary>
    public bool ArchiveAfterTruncate { get; set; } = true;

    /// <summary>
    /// Size threshold (bytes) at which a warning is logged about uncommitted WAL data.
    /// Default is 50MB.
    /// </summary>
    public long UncommittedSizeWarningThreshold { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Controls how the WAL behaves when corrupted records are detected during recovery.
    /// Defaults to <see cref="WalCorruptionMode.Alert"/> so corruption is never silent:
    /// the durability backstop must not discard records without an operator signal.
    /// Set to <see cref="WalCorruptionMode.Halt"/> to require manual operator review, or
    /// opt into <see cref="WalCorruptionMode.Skip"/> only when silent-skip is acceptable.
    /// </summary>
    public WalCorruptionMode CorruptionMode { get; set; } = WalCorruptionMode.Alert;
}

/// <summary>
/// WAL synchronization modes.
/// </summary>
public enum WalSyncMode : byte
{
    /// <summary>
    /// No explicit sync - relies on OS buffering (fastest, least durable).
    /// </summary>
    NoSync,

    /// <summary>
    /// Sync after batches of writes (balanced).
    /// </summary>
    BatchedSync,

    /// <summary>
    /// Sync after every write (slowest, most durable).
    /// </summary>
    EveryWrite
}

/// <summary>
/// Controls how the WAL responds when corrupted records are detected during recovery.
/// </summary>
public enum WalCorruptionMode
{
    /// <summary>
    /// Silently skip corrupt records and continue recovery (legacy behaviour).
    /// </summary>
    Skip,

    /// <summary>
    /// Skip corrupt records but fire the <see cref="WriteAheadLog.CorruptionDetected"/> event
    /// so monitoring systems can alert operators. Recommended for production deployments.
    /// </summary>
    Alert,

    /// <summary>
    /// Throw an <see cref="InvalidDataException"/> when any corrupt record is found.
    /// Use this when data integrity is non-negotiable and manual operator review
    /// is required before the application can start.
    /// </summary>
    Halt
}

/// <summary>
/// Result of a WAL repair operation.
/// </summary>
public sealed record WalRepairResult(
    int TotalRecords,
    int ValidRecords,
    int CorruptedRecords,
    int RepairedFiles);
