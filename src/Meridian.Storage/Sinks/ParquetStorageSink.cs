using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Serilog;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Apache Parquet storage sink for high-performance columnar storage.
/// Provides 10-20x better compression than JSONL and optimized for analytics.
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

    public ParquetStorageSink(StorageOptions options, ParquetStorageOptions? parquetOptions = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _parquetOptions = parquetOptions ?? ParquetStorageOptions.Default;

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
            await FlushBufferAsync(bufferKey, buffer, ct);
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
                    await FlushBufferAsync(kvp.Key, kvp.Value, ct);
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushBufferAsync(string bufferKey, MarketEventBuffer buffer, CancellationToken ct)
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
            _log.Error(ex, "Failed to flush {Count} events to Parquet", events.Count);
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

        await WriteAtomicallyAsync(path, async tempStream =>
        {
            using var groupWriter = await ParquetWriter.CreateAsync(TradeSchema, tempStream);
            using var rowGroupWriter = groupWriter.CreateRowGroup();

            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[0], timestamps));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[1], symbols));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[2], prices));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[3], sizes));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[4], aggressors));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[5], sequences));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[6], venues));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(TradeSchema.DataFields[7], sources));
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

        await WriteAtomicallyAsync(path, async tempStream =>
        {
            using var groupWriter = await ParquetWriter.CreateAsync(QuoteSchema, tempStream);
            using var rowGroupWriter = groupWriter.CreateRowGroup();

            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[0], timestamps));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[1], symbols));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[2], bidPrices));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[3], bidSizes));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[4], askPrices));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[5], askSizes));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[6], spreads));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[7], sequences));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(QuoteSchema.DataFields[8], sources));
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

        await WriteAtomicallyAsync(path, async tempStream =>
        {
            using var groupWriter = await ParquetWriter.CreateAsync(L2Schema, tempStream);
            using var rowGroupWriter = groupWriter.CreateRowGroup();

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

            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[0], timestamps));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[1], symbols));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[2], bidCounts));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[3], askCounts));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[4], bestBids));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[5], bestAsks));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[6], spreads));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[7], seqNums));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[8], sources));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[9], bidsJson));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(L2Schema.DataFields[10], asksJson));
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

        await WriteAtomicallyAsync(path, async tempStream =>
        {
            using var groupWriter = await ParquetWriter.CreateAsync(BarSchema, tempStream);
            using var rowGroupWriter = groupWriter.CreateRowGroup();

            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[0], timestamps));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[1], symbols));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[2], opens));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[3], highs));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[4], lows));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[5], closes));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[6], volumes));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[7], sequences));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(BarSchema.DataFields[8], sources));
        }, ct);
    }

    private async Task WriteGenericEventsAsync(string path, IReadOnlyList<MarketEvent> events, CancellationToken ct)
    {
        // For generic events, write as JSON strings in a simple schema
        var genericSchema = new ParquetSchema(
            new DataField<DateTime>("Timestamp"),
            new DataField<string>("Symbol"),
            new DataField<string>("Type"),
            new DataField<string>("PayloadJson"),
            new DataField<long>("Sequence"),
            new DataField<string>("Source")
        );

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

        await WriteAtomicallyAsync(path, async tempStream =>
        {
            using var groupWriter = await ParquetWriter.CreateAsync(genericSchema, tempStream);
            using var rowGroupWriter = groupWriter.CreateRowGroup();

            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[0], timestamps));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[1], symbols));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[2], types));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[3], payloads));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[4], sequences));
            await rowGroupWriter.WriteColumnAsync(new DataColumn(genericSchema.DataFields[5], sources));
        }, ct);
    }

    /// <summary>
    /// Writes Parquet data atomically using a temp-file-then-rename strategy.
    /// Prevents partially written files from appearing at the destination on crash or I/O error.
    /// </summary>
    private static async Task WriteAtomicallyAsync(string path, Func<Stream, Task> writeAsync, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tempPath = GetAtomicTempPath(path);
        try
        {
            {
                await using var tempStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 65536,
                    FileOptions.Asynchronous);
                await writeAsync(tempStream);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static string GetAtomicTempPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? ".";
        var fileName = Path.GetFileName(destinationPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort: do not mask the original exception
        }
    }

    private string GetBufferKey(MarketEvent evt)
    {
        return $"{evt.EffectiveSymbol}_{evt.Type}_{evt.Timestamp.Date:yyyyMMdd}";
    }

    private string GetFilePath(MarketEvent evt)
    {
        var date = evt.Timestamp.Date;
        var typeName = evt.Type.ToString().ToLowerInvariant();
        var effectiveSymbol = evt.EffectiveSymbol;
        var fileName = $"{effectiveSymbol}_{typeName}_{date:yyyyMMdd}.parquet";

        return _options.NamingConvention switch
        {
            FileNamingConvention.BySymbol => Path.Combine(_options.RootPath, effectiveSymbol, fileName),
            FileNamingConvention.ByDate => Path.Combine(_options.RootPath, $"{date:yyyy}", $"{date:MM}", $"{date:dd}", fileName),
            FileNamingConvention.ByType => Path.Combine(_options.RootPath, typeName, fileName),
            _ => Path.Combine(_options.RootPath, fileName)
        };
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
                        await FlushBufferAsync(kvp.Key, kvp.Value, CancellationToken.None);
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

    /// <summary>
    /// Row group size for Parquet files.
    /// </summary>
    public int RowGroupSize { get; init; } = 50000;

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
