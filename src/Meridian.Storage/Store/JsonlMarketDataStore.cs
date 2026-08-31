using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Core.Logging;
using Meridian.Core.Serialization;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
using Prometheus;
using Serilog;

namespace Meridian.Storage.Store;

/// <summary>
/// <see cref="IMarketDataStore"/> implementation backed by JSONL (optionally compressed) files.
/// Enumerates all <c>*.jsonl</c> files under the configured root — including any compression suffix
/// the storage policy can emit (<c>.gz</c>/<c>.gzip</c>/<c>.zst</c>/<c>.lz4</c>/<c>.br</c>) —
/// deserialises each line, and applies the <see cref="MarketDataQuery"/> predicate in-process.
/// <para>
/// <b>Concurrency posture: read-only.</b> This type never writes, appends, or deletes — it opens
/// each file with <c>FileAccess.Read</c> and shares write and delete so a concurrent writer or an
/// archival replace does not fault the read. Writes to this JSONL tree are owned by the storage
/// sink, so there is no mutation posture to declare here (#2697).
/// </para>
/// </summary>
public sealed class JsonlMarketDataStore : IMarketDataStore
{
    private static readonly ILogger Log = LoggingSetup.ForContext<JsonlMarketDataStore>();

    // Read-side corruption observability. Read-time discoveries cannot honestly become
    // MarketEvents (there is no ingress pipeline at read, and republishing stored corruption
    // as fresh events would re-stamp it with a synthetic provenance), so the smallest honest
    // mechanism is the one WriteAheadLog already established for recovery-time corruption:
    // process-wide Prometheus counters plus per-store counts surfaced as properties, with the
    // skip itself logged at Warning instead of Debug.
    private static readonly Counter StoreReadMalformedLinesTotal = Metrics.CreateCounter(
        "mdc_store_read_malformed_lines_total",
        "Total number of malformed JSONL lines skipped while reading the market data store");

    private static readonly Counter StoreReadTruncatedTailsTotal = Metrics.CreateCounter(
        "mdc_store_read_truncated_tails_total",
        "Total number of truncated compressed file tails encountered while reading the market data store");

    private readonly string _root;
    private long _malformedLinesSkipped;
    private long _truncatedTailsDetected;

    public JsonlMarketDataStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>
    /// Number of malformed JSONL lines this store instance has skipped while reading.
    /// A non-zero value means query results are incomplete relative to the bytes on disk.
    /// </summary>
    public long MalformedLinesSkipped => Interlocked.Read(ref _malformedLinesSkipped);

    /// <summary>
    /// Number of truncated compressed file tails this store instance has stopped at while
    /// reading. Each one means a file's final block was torn (e.g. by a crash mid-append) and
    /// events past the tear were unrecoverable.
    /// </summary>
    public long TruncatedTailsDetected => Interlocked.Read(ref _truncatedTailsDetected);

    /// <inheritdoc/>
    public async IAsyncEnumerable<MarketEvent> QueryAsync(
        MarketDataQuery query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            yield break;

        var files = EnumerateCandidateFiles(query);
        int yielded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var evt in ReadFileAsync(file, ct))
            {
                if (!Matches(query, evt))
                    continue;

                yield return evt;
                yielded++;

                if (query.Limit.HasValue && yielded >= query.Limit.Value)
                    yield break;
            }
        }
    }

    private IEnumerable<string> EnumerateCandidateFiles(MarketDataQuery query)
    {
        var allFiles = Directory.EnumerateFiles(_root, "*.jsonl*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        if (!query.Symbol.HasValue)
            return allFiles;

        var symbol = query.Symbol.Value.Value;
        var encodedSymbol = StoragePathSegmentCodec.EncodeSymbol(symbol);
        var legacySymbol = StoragePathSegmentCodec.EncodeLegacyForLookup(symbol);

        return allFiles.Where(file =>
            ContainsPathIdentity(file, encodedSymbol) ||
            (!string.Equals(encodedSymbol, legacySymbol, StringComparison.OrdinalIgnoreCase) &&
             ContainsPathIdentity(file, legacySymbol)));
    }

    private static bool ContainsPathIdentity(string file, string pathIdentity)
    {
        return Path.GetFileName(file).Contains(pathIdentity, StringComparison.OrdinalIgnoreCase) ||
               file.Contains(
                   Path.DirectorySeparatorChar + pathIdentity + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private async IAsyncEnumerable<MarketEvent> ReadFileAsync(
        string file,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // ReadWrite|Delete sharing: the sink holds a persistent append handle on the active
        // day file, and retention may delete files mid-enumeration; neither should fail reads.
        await using var fs = new FileStream(
            file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        // Shared codec detection (magic bytes, extension fallback) so this store decodes every
        // compression suffix the storage policy can emit (.gz/.gzip/.zst/.lz4/.br), not only .gz.
        Stream stream = CompressedJsonlStream.Decompress(fs, file);

        using var reader = new StreamReader(stream);
        var lineNumber = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await ReadLineOrEndAtTruncatedTailAsync(reader, file, ct).ConfigureAwait(false);
            if (line is null)
                break;
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            MarketEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<MarketEvent>(
                    line, MarketDataJsonContext.HighPerformanceOptions);
            }
            catch (JsonException ex)
            {
                // Read-time corruption discovery: the line is unrecoverable and query results
                // are now incomplete relative to the bytes on disk, so this is a Warning with
                // counters, never a silently swallowed Debug entry.
                Interlocked.Increment(ref _malformedLinesSkipped);
                StoreReadMalformedLinesTotal.Inc();
                Log.Warning(
                    ex,
                    "Skipping malformed JSONL line {LineNumber} in {File}; query results are incomplete for this file",
                    lineNumber,
                    file);
            }

            if (evt is not null)
                yield return evt;
        }
    }

    // A crash while the sink appends a compressed batch can leave a torn trailing gzip member;
    // the decoder throws InvalidDataException mid-read. Every complete earlier member has
    // already been yielded, so treat the torn tail as end-of-file rather than failing the query —
    // but count and log it: events past the tear existed and are unrecoverable.
    private async ValueTask<string?> ReadLineOrEndAtTruncatedTailAsync(
        StreamReader reader,
        string file,
        CancellationToken ct)
    {
        try
        {
            return await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            Interlocked.Increment(ref _truncatedTailsDetected);
            StoreReadTruncatedTailsTotal.Inc();
            Log.Warning(ex, "Truncated compressed tail in {File}; stopping at the last complete block", file);
            return null;
        }
    }

    internal static bool Matches(MarketDataQuery query, MarketEvent evt)
    {
        if (query.Symbol.HasValue &&
            !string.Equals(evt.EffectiveSymbol, query.Symbol.Value.Value, StringComparison.OrdinalIgnoreCase))
            return false;

        if (query.From.HasValue && evt.Timestamp < query.From.Value)
            return false;

        if (query.To.HasValue && evt.Timestamp >= query.To.Value)
            return false;

        if (query.EventType.HasValue && evt.Type != query.EventType.Value)
            return false;

        if (query.Source is not null &&
            !string.Equals(evt.Source, query.Source, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
