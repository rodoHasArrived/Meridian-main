using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Core.Logging;
using Meridian.Core.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Meridian.Storage.Services;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Serilog;
using Meridian.Core.Monitoring;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Apache Parquet storage sink for high-performance columnar storage.
/// Provides 10-20x better compression than JSONL and optimized for analytics.
///
/// Deliberately KEEPS copy-on-write row-group appends (unlike the JSONL sink's persistent
/// append streams): Parquet's footer-at-end format means a persistently open writer leaves an
/// unreadable, footerless file after a crash, violating the published-artifact readability
/// invariant that copy-plus-rename guarantees; part-file compaction would change the
/// one-file-per-day contract query/export paths consume. Row-group flushes are batched and
/// lower-cadence than the JSONL hot path, so the O(day-file) copy is the accepted cost.
/// Revisit only with a measured need.
///
/// Based on: https://github.com/aloneguid/parquet-dotnet (MIT)
/// Reference: docs/open-source-references.md #20
/// </summary>
[StorageSink("parquet", "Apache Parquet Storage",
    Description = "Writes market events to columnar Parquet files for high-performance analytics.")]
public sealed class ParquetStorageSink : IStorageSink
{
    private readonly ILogger _log = LoggingSetup.ForContext<ParquetStorageSink>();
    private readonly StorageOptions _options;
    private readonly ParquetStorageOptions _parquetOptions;
    private readonly IStoragePolicy _policy;
    private readonly Func<string, Func<Stream, Task>, CancellationToken, Task> _writeAtomicallyAsync;
    private readonly ConcurrentDictionary<string, MarketEventBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _flushLoopTask;
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private int _disposed;

    private static readonly IReadOnlyList<OrderBookLevel> EmptyBookLevels = Array.Empty<OrderBookLevel>();

    // Trade event schema
    private static readonly ParquetSchema TradeSchema = new(
        new DataField<DateTime>("Timestamp"),
        new DataField<string>("Symbol"),
        new DataField<decimal>("Price"),
        new DataField<long>("Size"),
        new DataField<string>("AggressorSide"),
        new DataField<long>("SequenceNumber"),
        new DataField<string>("Venue"),
        new DataField<string>("Source")
    );

    // Quote event schema
    private static readonly ParquetSchema QuoteSchema = new(
        new DataField<DateTime>("Timestamp"),
        new DataField<string>("Symbol"),
        new DataField<decimal>("BidPrice"),
        new DataField<long>("BidSize"),
        new DataField<decimal>("AskPrice"),
        new DataField<long>("AskSize"),
        new DataField<decimal>("Spread"),
        new DataField<long>("SequenceNumber"),
        new DataField<string>("Source")
    );

    // L2 Snapshot schema
    private static readonly ParquetSchema L2Schema = new(
        new DataField<DateTime>("Timestamp"),
        new DataField<string>("Symbol"),
        new DataField<int>("BidLevels"),
        new DataField<int>("AskLevels"),
        new DataField<decimal>("BestBid"),
        new DataField<decimal>("BestAsk"),
        new DataField<decimal?>("Spread"),
        new DataField<long>("SequenceNumber"),
        new DataField<string>("Source"),
        new DataField<string>("BidsJson"),
        new DataField<string>("AsksJson")
    );

    // Historical bar schema
    private static readonly ParquetSchema BarSchema = new(
        new DataField<DateTime>("Timestamp"),
        new DataField<string>("Symbol"),
        new DataField<decimal>("Open"),
        new DataField<decimal>("High"),
        new DataField<decimal>("Low"),
        new DataField<decimal>("Close"),
        new DataField<decimal>("Volume"),
        new DataField<long>("SequenceNumber"),
        new DataField<string>("Source")
    );

    public ParquetStorageSink(
        StorageOptions options,
        ParquetStorageOptions? parquetOptions = null,
        IStoragePolicy? policy = null)
        : this(options, parquetOptions, WriteAtomicallyAsync, policy)
    {
    }

    internal ParquetStorageSink(
        StorageOptions options,
        ParquetStorageOptions? parquetOptions,
        Func<string, Func<Stream, Task>, CancellationToken, Task> writeAtomicallyAsync,
        IStoragePolicy? policy = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _parquetOptions = parquetOptions ?? ParquetStorageOptions.Default;
        // Directory layout is owned by the shared storage policy so a composite JSONL + Parquet sink
        // converges on one directory tree for every naming convention. Falling back to a policy built
        // from the same options keeps existing single-argument construction (and tests) working.
        _policy = policy ?? new JsonlStoragePolicy(_options);
        _writeAtomicallyAsync = writeAtomicallyAsync ?? throw new ArgumentNullException(nameof(writeAtomicallyAsync));

        _flushLoopTask = RunPeriodicFlushLoopAsync(_disposalCts.Token);

        _log.Information("ParquetStorageSink initialized with buffer size {BufferSize}, flush interval {FlushInterval}s",
            _parquetOptions.BufferSize, _parquetOptions.FlushInterval.TotalSeconds);
    }

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ParquetStorageSink));

        EventSchemaValidator.Validate(evt);

        var bufferKey = GetBufferKey(evt);
        var buffer = _buffers.GetOrAdd(bufferKey, _ => new MarketEventBuffer(_parquetOptions.BufferSize));

        buffer.Add(evt);

        // Flush if buffer is full
        if (buffer.ShouldFlush(_parquetOptions.BufferSize))
        {
            await FlushSingleBufferAsync(bufferKey, buffer, ct).ConfigureAwait(false);
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await FlushAllBuffersAsync(ct);
    }

    private async Task RunPeriodicFlushLoopAsync(CancellationToken ct)
    {
        using var periodicTimer = new PeriodicTimer(_parquetOptions.FlushInterval);

        try
        {
            while (await periodicTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await FlushAllBuffersSafelyAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposal in progress, stop flushing
        }
    }

    private async Task FlushAllBuffersSafelyAsync(CancellationToken ct)
    {
        try
        {
            await FlushAllBuffersAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposal in progress, stop flushing
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Periodic Parquet flush failed — {BufferCount} buffers may contain unflushed data", _buffers.Count);
        }
    }

    private async Task FlushAllBuffersAsync(CancellationToken ct = default)
    {
        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var kvp in _buffers)
            {
                if (kvp.Value.Count > 0)
                {
                    await FlushBufferCoreAsync(kvp.Key, kvp.Value, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushSingleBufferAsync(string bufferKey, MarketEventBuffer buffer, CancellationToken ct)
    {
        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FlushBufferCoreAsync(bufferKey, buffer, ct).ConfigureAwait(false);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushBufferCoreAsync(string bufferKey, MarketEventBuffer buffer, CancellationToken ct)
    {
        // DrainAll() uses a swap-buffer strategy — no copy allocation.
        // Flushes are serialised by _flushGate so the returned list is not cleared
        // before this method returns.
        var events = buffer.DrainAll();
        if (events.Count == 0)
            return;

        try
        {
            var path = GetFilePath(events[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var eventType = events[0].Type;

            switch (eventType)
            {
                case MarketEventType.Trade:
                    await WriteTradesAsync(path, events, ct);
                    break;
                case MarketEventType.BboQuote:
                    await WriteQuotesAsync(path, events, ct);
                    break;
                case MarketEventType.L2Snapshot:
                    await WriteL2SnapshotsAsync(path, events, ct);
                    break;
                case MarketEventType.HistoricalBar:
                    await WriteBarsAsync(path, events, ct);
                    break;
                default:
                    // Write as generic event
                    await WriteGenericEventsAsync(path, events, ct);
                    break;
            }

            _log.Debug("Flushed {Count} events to Parquet file: {Path}", events.Count, path);
        }
        catch (Exception ex)
        {
            buffer.RestoreToFront(events);
            _log.Error(ex, "Failed to flush {Count} events to Parquet for {BufferKey}; buffered events were restored for retry", events.Count, bufferKey);
            throw;
        }
    }

    private async Task WriteTradesAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        // Count valid trades first to size arrays exactly
        var count = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is Trade)
                count++;
        }

        if (count == 0)
            return;

        // Single-pass: build all column arrays simultaneously
        var timestamps = new DateTime[count];
        var symbols = new string[count];
        var prices = new decimal[count];
        var sizes = new long[count];
        var aggressors = new string[count];
        var sequences = new long[count];
        var venues = new string[count];
        var sources = new string[count];

        var idx = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is not Trade trade)
                continue;
            var evt = events[i];
            timestamps[idx] = evt.Timestamp.UtcDateTime;
            symbols[idx] = evt.EffectiveSymbol;
            prices[idx] = trade.Price;
            sizes[idx] = trade.Size;
            aggressors[idx] = trade.Aggressor.ToString();
            sequences[idx] = trade.SequenceNumber;
            venues[idx] = trade.Venue ?? "UNKNOWN";
            sources[idx] = evt.Source;
            idx++;
        }

        await WriteRowGroupAtomicallyAsync(path, TradeSchema, new[]
        {
            new DataColumn(TradeSchema.DataFields[0], timestamps),
            new DataColumn(TradeSchema.DataFields[1], symbols),
            new DataColumn(TradeSchema.DataFields[2], prices),
            new DataColumn(TradeSchema.DataFields[3], sizes),
            new DataColumn(TradeSchema.DataFields[4], aggressors),
            new DataColumn(TradeSchema.DataFields[5], sequences),
            new DataColumn(TradeSchema.DataFields[6], venues),
            new DataColumn(TradeSchema.DataFields[7], sources),
        }, ct);
    }

    private async Task WriteQuotesAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        var count = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is BboQuotePayload)
                count++;
        }

        if (count == 0)
            return;

        var timestamps = new DateTime[count];
        var symbols = new string[count];
        var bidPrices = new decimal[count];
        var bidSizes = new long[count];
        var askPrices = new decimal[count];
        var askSizes = new long[count];
        var spreads = new decimal[count];
        var sequences = new long[count];
        var sources = new string[count];

        var idx = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is not BboQuotePayload quote)
                continue;
            var evt = events[i];
            timestamps[idx] = evt.Timestamp.UtcDateTime;
            symbols[idx] = evt.EffectiveSymbol;
            bidPrices[idx] = quote.BidPrice;
            bidSizes[idx] = quote.BidSize;
            askPrices[idx] = quote.AskPrice;
            askSizes[idx] = quote.AskSize;
            spreads[idx] = quote.Spread ?? 0m;
            sequences[idx] = quote.SequenceNumber;
            sources[idx] = evt.Source;
            idx++;
        }

        await WriteRowGroupAtomicallyAsync(path, QuoteSchema, new[]
        {
            new DataColumn(QuoteSchema.DataFields[0], timestamps),
            new DataColumn(QuoteSchema.DataFields[1], symbols),
            new DataColumn(QuoteSchema.DataFields[2], bidPrices),
            new DataColumn(QuoteSchema.DataFields[3], bidSizes),
            new DataColumn(QuoteSchema.DataFields[4], askPrices),
            new DataColumn(QuoteSchema.DataFields[5], askSizes),
            new DataColumn(QuoteSchema.DataFields[6], spreads),
            new DataColumn(QuoteSchema.DataFields[7], sequences),
            new DataColumn(QuoteSchema.DataFields[8], sources),
        }, ct);
    }

    private async Task WriteL2SnapshotsAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        var snapshots = events
            .Select(e => (Event: e, Data: ExtractL2Data(e)))
            .Where(x => x.Data.Snapshot is not null)
            .Select(x => (x.Event, Snapshot: x.Data.Snapshot!, x.Data.SequenceNumber))
            .ToList();

        if (snapshots.Count is 0)
            return;

        var count = snapshots.Count;
        var timestamps = new DateTime[count];
        var symbols = new string[count];
        var bidCounts = new int[count];
        var askCounts = new int[count];
        var bestBids = new decimal[count];
        var bestAsks = new decimal[count];
        var spreads = new decimal?[count];
        var seqNums = new long[count];
        var sources = new string[count];
        var bidsJson = new string[count];
        var asksJson = new string[count];

        for (var i = 0; i < count; i++)
        {
            var (evt, snap, seq) = snapshots[i];
            timestamps[i] = evt.Timestamp.UtcDateTime;
            symbols[i] = evt.EffectiveSymbol;
            bidCounts[i] = snap.Bids?.Count ?? 0;
            askCounts[i] = snap.Asks?.Count ?? 0;
            bestBids[i] = snap.Bids is { Count: > 0 } bids ? bids[0].Price : 0m;
            bestAsks[i] = snap.Asks is { Count: > 0 } asks ? asks[0].Price : 0m;
            spreads[i] = ComputeSpread(snap);
            seqNums[i] = seq;
            sources[i] = evt.Source;
            bidsJson[i] = JsonSerializer.Serialize(snap.Bids ?? EmptyBookLevels, MarketDataJsonContext.HighPerformanceOptions);
            asksJson[i] = JsonSerializer.Serialize(snap.Asks ?? EmptyBookLevels, MarketDataJsonContext.HighPerformanceOptions);
        }

        await WriteRowGroupAtomicallyAsync(path, L2Schema, new[]
        {
            new DataColumn(L2Schema.DataFields[0], timestamps),
            new DataColumn(L2Schema.DataFields[1], symbols),
            new DataColumn(L2Schema.DataFields[2], bidCounts),
            new DataColumn(L2Schema.DataFields[3], askCounts),
            new DataColumn(L2Schema.DataFields[4], bestBids),
            new DataColumn(L2Schema.DataFields[5], bestAsks),
            new DataColumn(L2Schema.DataFields[6], spreads),
            new DataColumn(L2Schema.DataFields[7], seqNums),
            new DataColumn(L2Schema.DataFields[8], sources),
            new DataColumn(L2Schema.DataFields[9], bidsJson),
            new DataColumn(L2Schema.DataFields[10], asksJson),
        }, ct);
    }

    private static (LOBSnapshot? Snapshot, long SequenceNumber) ExtractL2Data(MarketEvent evt) => evt.Payload switch
    {
        L2SnapshotPayload l2 => (l2.Snapshot, l2.SequenceNumber),
        LOBSnapshot lob => (lob, lob.SequenceNumber),
        _ => (null, 0)
    };

    private static decimal? ComputeSpread(LOBSnapshot snap)
    {
        var bestBid = snap.Bids?.FirstOrDefault()?.Price ?? 0;
        var bestAsk = snap.Asks?.FirstOrDefault()?.Price ?? 0;
        return bestBid > 0 && bestAsk > 0 ? bestAsk - bestBid : null;
    }

    private async Task WriteBarsAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        var count = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is HistoricalBar)
                count++;
        }

        if (count == 0)
            return;

        var timestamps = new DateTime[count];
        var symbols = new string[count];
        var opens = new decimal[count];
        var highs = new decimal[count];
        var lows = new decimal[count];
        var closes = new decimal[count];
        var volumes = new decimal[count];
        var sequences = new long[count];
        var sources = new string[count];

        var idx = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Payload is not HistoricalBar bar)
                continue;
            var evt = events[i];
            timestamps[idx] = evt.Timestamp.UtcDateTime;
            symbols[idx] = evt.EffectiveSymbol;
            opens[idx] = bar.Open;
            highs[idx] = bar.High;
            lows[idx] = bar.Low;
            closes[idx] = bar.Close;
            volumes[idx] = bar.Volume;
            sequences[idx] = bar.SequenceNumber;
            sources[idx] = evt.Source;
            idx++;
        }

        await WriteRowGroupAtomicallyAsync(path, BarSchema, new[]
        {
            new DataColumn(BarSchema.DataFields[0], timestamps),
            new DataColumn(BarSchema.DataFields[1], symbols),
            new DataColumn(BarSchema.DataFields[2], opens),
            new DataColumn(BarSchema.DataFields[3], highs),
            new DataColumn(BarSchema.DataFields[4], lows),
            new DataColumn(BarSchema.DataFields[5], closes),
            new DataColumn(BarSchema.DataFields[6], volumes),
            new DataColumn(BarSchema.DataFields[7], sequences),
            new DataColumn(BarSchema.DataFields[8], sources),
        }, ct);
    }

    // Generic (non-specialised) events are written as JSON strings in a simple schema
    private static readonly ParquetSchema GenericSchema = new(
        new DataField<DateTime>("Timestamp"),
        new DataField<string>("Symbol"),
        new DataField<string>("Type"),
        new DataField<string>("PayloadJson"),
        new DataField<long>("Sequence"),
        new DataField<string>("Source")
    );

    private async Task WriteGenericEventsAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        var count = events.Count;
        var timestamps = new DateTime[count];
        var symbols = new string[count];
        var types = new string[count];
        var payloads = new string[count];
        var sequences = new long[count];
        var sources = new string[count];

        for (var i = 0; i < count; i++)
        {
            var e = events[i];
            timestamps[i] = e.Timestamp.UtcDateTime;
            symbols[i] = e.EffectiveSymbol;
            types[i] = e.Type.ToString();
            payloads[i] = JsonSerializer.Serialize(e, MarketDataJsonContext.Default.MarketEvent);
            sequences[i] = e.Sequence;
            sources[i] = e.Source;
        }

        await WriteRowGroupAtomicallyAsync(path, GenericSchema, new[]
        {
            new DataColumn(GenericSchema.DataFields[0], timestamps),
            new DataColumn(GenericSchema.DataFields[1], symbols),
            new DataColumn(GenericSchema.DataFields[2], types),
            new DataColumn(GenericSchema.DataFields[3], payloads),
            new DataColumn(GenericSchema.DataFields[4], sequences),
            new DataColumn(GenericSchema.DataFields[5], sources),
        }, ct);
    }

    /// <summary>
    /// Writes Parquet data atomically using the shared <see cref="AtomicFileWriter"/> durable
    /// write path (temp file, fsync, rename, directory fsync). Prevents partially written or
    /// non-durable files from appearing at the destination on crash or I/O error.
    /// </summary>
    private static Task WriteAtomicallyAsync(string path, Func<Stream, Task> writeAsync, CancellationToken ct = default)
    {
        return AtomicFileWriter.WriteStreamAsync(path, writeAsync, ct);
    }

    /// <summary>
    /// Appends one row group to the deterministic per-day file. Same-day flushes share a file
    /// path, so the existing file's bytes are copied into the atomic temp file and the new row
    /// group is appended — replacing the file outright would erase every earlier flush of the
    /// day. An existing file that is unreadable or has an incompatible schema is quarantined
    /// (never overwritten) and a fresh file is written in its place.
    /// </summary>
    private async Task WriteRowGroupAtomicallyAsync(
        string path,
        ParquetSchema schema,
        IReadOnlyList<DataColumn> columns,
        CancellationToken ct)
    {
        var appendToExisting = File.Exists(path) && await CanAppendToExistingFileAsync(path, schema, ct).ConfigureAwait(false);

        await _writeAtomicallyAsync(path, async tempStream =>
        {
            if (appendToExisting)
            {
                await using (var existing = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 65536,
                    FileOptions.Asynchronous))
                {
                    await existing.CopyToAsync(tempStream, 65536, ct).ConfigureAwait(false);
                }
            }

            using var groupWriter = await ParquetWriter.CreateAsync(schema, tempStream, append: appendToExisting).ConfigureAwait(false);
            // Honour the configured compression method (previously ignored, so every file was written
            // with the Parquet default regardless of ParquetStorageOptions.CompressionMethod).
            groupWriter.CompressionMethod = _parquetOptions.CompressionMethod;
            using var rowGroupWriter = groupWriter.CreateRowGroup();

            foreach (var column in columns)
            {
                await rowGroupWriter.WriteColumnAsync(column).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates that the existing file at <paramref name="path"/> is a readable Parquet file
    /// whose schema matches the row group about to be appended. On mismatch or corruption the
    /// file is quarantined so its bytes are preserved for manual recovery.
    /// </summary>
    private async Task<bool> CanAppendToExistingFileAsync(string path, ParquetSchema schema, CancellationToken ct)
    {
        try
        {
            await using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                FileOptions.Asynchronous))
            {
                using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (reader.Schema.Equals(schema))
                    return true;
            }

            _log.Warning("Existing Parquet file {Path} has an incompatible schema; quarantining it before writing a fresh file", path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Existing Parquet file {Path} is unreadable; quarantining it before writing a fresh file", path);
        }

        QuarantineExistingFile(path);
        return false;
    }

    private void QuarantineExistingFile(string path)
    {
        var quarantinePath = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            File.Move(path, quarantinePath);
            _log.Error("Quarantined incompatible Parquet file to {QuarantinePath}; a fresh file will be written at {Path}", quarantinePath, path);
        }
        catch (Exception ex)
        {
            // If the quarantine move fails, refuse to continue — overwriting the existing
            // file would destroy data that could not be preserved. The flush restores the
            // buffered events and retries later.
            throw new IOException($"Failed to quarantine incompatible Parquet file '{path}'.", ex);
        }
    }

    private string GetBufferKey(MarketEvent evt)
    {
        // Key each buffer by its full destination path so every event in a buffer maps to exactly
        // one output file. Keying only by symbol/type/date would co-mingle events the policy routes
        // to different directories (BySource/ByAssetClass/Hierarchical/Canonical); the flush writes
        // the whole drained buffer to GetFilePath(events[0]), so those events would be misplaced
        // into the first event's directory and diverge from the JSONL layout.
        return GetFilePath(evt);
    }

    private string GetFilePath(MarketEvent evt)
    {
        var date = evt.Timestamp.Date;
        var typeName = evt.Type.ToString().ToLowerInvariant();
        var fileName = $"{evt.EffectiveSymbol}_{typeName}_{date:yyyyMMdd}.parquet";

        // Delegate the directory layout to the shared storage policy so every naming convention is
        // honored identically to the JSONL sink. Previously only BySymbol/ByDate/ByType were handled
        // and the remaining conventions (BySource, ByAssetClass, Hierarchical, Canonical) silently
        // collapsed to a flat root, diverging from a co-located JSONL sink. The Parquet-specific
        // file name and .parquet extension are retained; only the directory comes from the policy.
        var directory = Path.GetDirectoryName(_policy.GetPath(evt));
        return string.IsNullOrEmpty(directory)
            ? Path.Combine(_options.RootPath, fileName)
            : Path.Combine(directory, fileName);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // 1. Signal cancellation to stop the background flush loop
        _disposalCts.Cancel();

        // 2. Await the background loop so no fire-and-forget flush remains detached from disposal
        await _flushLoopTask.ConfigureAwait(false);

        // 3. Final flush — guaranteed no concurrent background flushes after loop completion
        try
        {
            await _flushGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var kvp in _buffers)
                {
                    if (kvp.Value.Count > 0)
                    {
                        await FlushBufferCoreAsync(kvp.Key, kvp.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _flushGate.Release();
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Final buffer flush during disposal failed");
        }

        _buffers.Clear();
        _flushGate.Dispose();
        _disposalCts.Dispose();

        _log.Information("ParquetStorageSink disposed");
    }
}

/// <summary>
/// Configuration options for Parquet storage.
/// </summary>
public sealed class ParquetStorageOptions
{
    /// <summary>
    /// Number of events to buffer before writing to disk.
    /// </summary>
    public int BufferSize { get; init; } = 10000;

    /// <summary>
    /// Maximum time between flushes.
    /// </summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Compression method for Parquet files.
    /// </summary>
    public CompressionMethod CompressionMethod { get; init; } = CompressionMethod.Snappy;

    public static ParquetStorageOptions Default => new();

    public static ParquetStorageOptions HighCompression => new()
    {
        CompressionMethod = CompressionMethod.Gzip,
        BufferSize = 50000
    };

    public static ParquetStorageOptions LowLatency => new()
    {
        BufferSize = 1000,
        FlushInterval = TimeSpan.FromSeconds(5),
        CompressionMethod = CompressionMethod.None
    };
}
