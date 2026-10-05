using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using System.Text.Json;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;

namespace Meridian.Storage.Replay;

/// <summary>
/// Reads previously captured JSONL events (optionally compressed) from a file or directory and
/// replays them as <see cref="MarketEvent"/> objects in deterministic timestamp order.
/// </summary>
public sealed class JsonlReplayer
{
    internal const int SortRunRecordLimit = 4096;
    internal const int MaxMergeReaders = 16;
    private const int MaxReplayIoHandles = 128;
    private const int MaxOpenHandlesPerSort = MaxMergeReaders + 1;
    internal const int MaxConcurrentReplaySorts = MaxReplayIoHandles / MaxOpenHandlesPerSort;
    internal const int ReplayPageRecordLimit = 128;

    // Admission is taken atomically for each complete external-sort preparation. Seven admitted
    // sorts can open at most 7 * (16 input readers + 1 output writer) = 119 handles, below the
    // global budget. Page loads use the same gate and release it before any event is yielded.
    private static readonly SemaphoreSlim ReplayIoAdmissions = new(
        MaxConcurrentReplaySorts,
        MaxConcurrentReplaySorts);

    // Same-process replay readers serialize before taking an exclusive source lease. Bounded
    // stripes avoid retaining a semaphore for each historical path.
    private static readonly SemaphoreSlim[] SourceReadGates = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly string _path;
    private readonly int _sortRunRecordLimit;
    private readonly int _maxMergeReaders;
    private readonly IReadOnlyDictionary<string, int>? _symbolOrder;
    private readonly string _spoolRoot;

    public JsonlReplayer(string path)
        : this(path, SortRunRecordLimit, MaxMergeReaders)
    {
    }

    /// <summary>Replays only these effective symbols, using their ranks for equal timestamps.</summary>
    public JsonlReplayer(string path, IReadOnlyDictionary<string, int> symbolOrder)
        : this(path)
    {
        ArgumentNullException.ThrowIfNull(symbolOrder);
        _symbolOrder = new Dictionary<string, int>(symbolOrder, StringComparer.OrdinalIgnoreCase);
    }

    internal JsonlReplayer(string path, int sortRunRecordLimit, int maxMergeReaders, string? spoolRoot = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (sortRunRecordLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(sortRunRecordLimit));
        if (maxMergeReaders is < 2 or > MaxMergeReaders)
            throw new ArgumentOutOfRangeException(nameof(maxMergeReaders));

        _sortRunRecordLimit = sortRunRecordLimit;
        _maxMergeReaders = maxMergeReaders;
        _spoolRoot = spoolRoot ?? Path.GetTempPath();
    }

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!File.Exists(_path) && !Directory.Exists(_path))
            yield break;

        IReadOnlyList<string> files = File.Exists(_path)
            ? [_path]
            : Directory.EnumerateFiles(_path, "*.jsonl*", SearchOption.AllDirectories)
                .OrderBy(static file => file, StringComparer.Ordinal)
                .ToArray();

        if (files.Count == 0)
            yield break;

        var spoolDirectory = Path.Combine(_spoolRoot, $"meridian-replay-sort-{Guid.NewGuid():N}");
        var runs = new List<string>();
        var replayCompleted = false;

        await ReplayIoAdmissions.WaitAsync(ct).ConfigureAwait(false);
        var admissionHeld = true;
        try
        {
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(spoolDirectory);
            else
                Directory.CreateDirectory(spoolDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            // Storage sinks retain arrival order, which can include late provider events. Produce
            // fixed-size sorted runs while opening only one physical source partition at a time.
            var chunk = new List<ReplayRecord>(_sortRunRecordLimit);
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                ct.ThrowIfCancellationRequested();
                await foreach (var record in ReadFileAsync(files[fileIndex], ct).ConfigureAwait(false))
                {
                    // Validate every source record before filtering by the requested symbols.
                    if (_symbolOrder is not null && !_symbolOrder.ContainsKey(record.Event.EffectiveSymbol))
                        continue;
                    chunk.Add(record with { FileIndex = fileIndex });
                    if (chunk.Count == _sortRunRecordLimit)
                        await FlushRunAsync(chunk, runs, spoolDirectory, ct).ConfigureAwait(false);
                }
            }

            if (chunk.Count > 0)
                await FlushRunAsync(chunk, runs, spoolDirectory, ct).ConfigureAwait(false);

            if (runs.Count == 0)
                yield break;

            // Collapse to one run before yielding. No input handle survives a merge pass, and every
            // pass has a bounded fan-in reserved by the replay-level admission above.
            while (runs.Count > 1)
            {
                var nextRuns = new List<string>();
                for (var offset = 0; offset < runs.Count; offset += _maxMergeReaders)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = runs.Skip(offset).Take(_maxMergeReaders).ToArray();
                    var output = Path.Combine(spoolDirectory, $"merge-{Guid.NewGuid():N}.jsonl");
                    await WriteMergedRunAsync(batch, output, ct).ConfigureAwait(false);
                    nextRuns.Add(output);
                    foreach (var consumedRun in batch)
                        File.Delete(consumedRun);
                }

                runs = nextRuns;
            }

            // Retain one final run, load a bounded page by byte offset, close its handle and
            // release admission before yielding. Do not duplicate the run into page files.
            ReplayIoAdmissions.Release();
            admissionHeld = false;

            long pageOffset = 0;
            while (true)
            {
                var page = await ReadSpoolPageAsync(runs[0], pageOffset, ct).ConfigureAwait(false);
                if (page.Records.Count == 0)
                    break;
                pageOffset = page.NextOffset;
                foreach (var record in page.Records)
                {
                    ct.ThrowIfCancellationRequested();
                    yield return record.Event;
                }
            }

            replayCompleted = true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(spoolDirectory))
                    Directory.Delete(spoolDirectory, recursive: true);
            }
            catch (IOException) when (!replayCompleted)
            {
                // Preserve the cancellation, parse failure, or early-disposal result. The unique
                // temp directory is best-effort cleanup on an abnormal path.
            }
            catch (UnauthorizedAccessException) when (!replayCompleted)
            {
                // Preserve the primary abnormal result rather than masking it with cleanup noise.
            }
            finally
            {
                if (admissionHeld)
                    ReplayIoAdmissions.Release();
            }
        }
    }

    private static async Task<(IReadOnlyList<ReplayRecord> Records, long NextOffset)> ReadSpoolPageAsync(
        string path, long offset, CancellationToken ct)
    {
        await ReplayIoAdmissions.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 4096, useAsync: true);
            stream.Position = offset;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            var records = new List<ReplayRecord>(ReplayPageRecordLimit);
            while (records.Count < ReplayPageRecordLimit &&
                   await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                records.Add(ParseSpoolRecord(line, path));
                // Writers use BOM-free UTF-8 and LF. StreamReader.Position includes read-ahead.
                offset += Encoding.UTF8.GetByteCount(line) + 1L;
            }
            return (records, offset);
        }
        finally
        {
            ReplayIoAdmissions.Release();
        }
    }

    private static StreamWriter CreateSpoolWriter(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 4096,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new StreamWriter(new FileStream(path, options), new UTF8Encoding(false)) { NewLine = "\n" };
    }

    private async Task FlushRunAsync(
        List<ReplayRecord> chunk,
        List<string> runs,
        string spoolDirectory,
        CancellationToken ct)
    {
        chunk.Sort(CompareRecords);
        var path = Path.Combine(spoolDirectory, $"run-{runs.Count:D8}-{Guid.NewGuid():N}.jsonl");
        await WriteRunAsync(chunk, path, ct).ConfigureAwait(false);
        runs.Add(path);
        chunk.Clear();
    }

    private async Task WriteMergedRunAsync(
        IReadOnlyList<string> inputRuns,
        string output,
        CancellationToken ct)
    {
        await using var writer = CreateSpoolWriter(output);
        await foreach (var record in MergeRunsAsync(inputRuns, ct).ConfigureAwait(false))
            await WriteSpoolRecordAsync(writer, record, ct).ConfigureAwait(false);
    }

    private static async Task WriteRunAsync(
        IReadOnlyList<ReplayRecord> records,
        string output,
        CancellationToken ct)
    {
        await using var writer = CreateSpoolWriter(output);
        foreach (var record in records)
            await WriteSpoolRecordAsync(writer, record, ct).ConfigureAwait(false);
    }

    private static async ValueTask WriteSpoolRecordAsync(
        StreamWriter writer,
        ReplayRecord record,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(record.Event, MarketDataJsonContext.HighPerformanceOptions);
        var line = string.Concat(
            record.FileIndex.ToString(CultureInfo.InvariantCulture),
            "\t",
            record.LineNumber.ToString(CultureInfo.InvariantCulture),
            "\t",
            json);
        await writer.WriteLineAsync(line.AsMemory(), ct)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<ReplayRecord> MergeRunsAsync(
        IReadOnlyList<string> runs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (runs.Count == 0)
            yield break;

        var enumerators = new IAsyncEnumerator<ReplayRecord>?[runs.Count];
        var heap = new PriorityQueue<int, ReplayRecord>(runs.Count, new ReplayRecordComparer(this));
        try
        {
            for (var index = 0; index < runs.Count; index++)
            {
                var enumerator = ReadSpoolRunAsync(runs[index], ct).GetAsyncEnumerator(ct);
                enumerators[index] = enumerator;
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    heap.Enqueue(index, enumerator.Current);
            }

            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var index = heap.Dequeue();
                var enumerator = enumerators[index]!;
                yield return enumerator.Current;
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    heap.Enqueue(index, enumerator.Current);
            }
        }
        finally
        {
            foreach (var enumerator in enumerators)
            {
                if (enumerator is not null)
                    await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async IAsyncEnumerable<ReplayRecord> ReadSpoolRunAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            yield return ParseSpoolRecord(line, path);
    }

    private static ReplayRecord ParseSpoolRecord(string line, string path)
    {
        var firstTab = line.IndexOf('\t');
        var secondTab = firstTab < 0 ? -1 : line.IndexOf('\t', firstTab + 1);
        if (firstTab <= 0 || secondTab <= firstTab + 1)
            throw new InvalidDataException($"Malformed replay spool record in '{path}'.");

        if (!int.TryParse(
                line.AsSpan(0, firstTab),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var fileIndex) ||
            !long.TryParse(
                line.AsSpan(firstTab + 1, secondTab - firstTab - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var lineNumber) ||
            fileIndex < 0 ||
            lineNumber <= 0)
        {
            throw new InvalidDataException($"Malformed replay spool position in '{path}'.");
        }

        MarketEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<MarketEvent>(
                line.AsSpan(secondTab + 1),
                MarketDataJsonContext.HighPerformanceOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed replay spool event in '{path}'.", ex);
        }

        if (evt is null)
            throw new InvalidDataException($"Null replay spool event in '{path}'.");

        return new ReplayRecord(evt, lineNumber, fileIndex);
    }

    private static bool HasMatchingPayload(MarketEvent evt) => evt.Type switch
    {
        MarketEventType.Trade => evt.Payload is Trade,
        MarketEventType.L2Snapshot or MarketEventType.Depth => evt.Payload is LOBSnapshot or L2SnapshotPayload,
        MarketEventType.BboQuote or MarketEventType.Quote => evt.Payload is BboQuotePayload,
        MarketEventType.OrderFlow => evt.Payload is OrderFlowStatistics,
        MarketEventType.Integrity => evt.Payload is IntegrityEvent or DepthIntegrityEvent,
        MarketEventType.Heartbeat => evt.Payload is Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload,
        MarketEventType.HistoricalBar => evt.Payload is HistoricalBar,
        MarketEventType.HistoricalQuote => evt.Payload is HistoricalQuote,
        MarketEventType.HistoricalTrade => evt.Payload is HistoricalTrade,
        MarketEventType.HistoricalAuction => evt.Payload is HistoricalAuction,
        MarketEventType.AggregateBar => evt.Payload is AggregateBarPayload,
        MarketEventType.OptionQuote => evt.Payload is OptionQuote,
        MarketEventType.OptionTrade => evt.Payload is OptionTrade,
        MarketEventType.OptionGreeks => evt.Payload is GreeksSnapshot,
        MarketEventType.OptionChain => evt.Payload is OptionChainSnapshot,
        MarketEventType.OpenInterest => evt.Payload is OpenInterestUpdate,
        MarketEventType.OrderAdd => evt.Payload is OrderAdd,
        MarketEventType.OrderModify => evt.Payload is OrderModify,
        MarketEventType.OrderCancel => evt.Payload is OrderCancel,
        MarketEventType.OrderExecute => evt.Payload is OrderExecute,
        MarketEventType.OrderReplace => evt.Payload is OrderReplace,
        _ => false
    };

    private static async IAsyncEnumerable<ReplayRecord> ReadFileAsync(
        string file,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var gate = SourceReadGates[(uint)comparer.GetHashCode(Path.GetFullPath(file)) % (uint)SourceReadGates.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fs = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            // Closed-capture lease: an active sink returns IOException instead of exposing a partial
            // tail to the parser. No source lease survives sort preparation or a public replay yield.
            var stream = CompressedJsonlStream.Decompress(fs, file);
            using var reader = new StreamReader(stream);
            long lineNumber = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    yield break;

                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                MarketEvent? evt;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var fields = document.RootElement.EnumerateObject()
                            .Select(static property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!fields.IsSupersetOf(["timestamp", "symbol", "type", "schemaVersion", "payload"]))
                            throw new JsonException("Missing required persisted event fields.");
                    }
                    evt = document.RootElement.Deserialize<MarketEvent>(MarketDataJsonContext.HighPerformanceOptions);
                    if (evt is not null && (evt.Timestamp == default || string.IsNullOrWhiteSpace(evt.Symbol) ||
                        evt.CanonicalSymbol is not null && string.IsNullOrWhiteSpace(evt.CanonicalSymbol) ||
                        evt.Type == MarketEventType.Unknown || !Enum.IsDefined(evt.Type) ||
                        evt.SchemaVersion != 1 || !HasMatchingPayload(evt)))
                        throw new JsonException("Invalid persisted event timestamp, symbol, type, schema version, or payload.");
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
                {
                    throw new InvalidDataException(
                        $"Malformed JSONL record in replay file '{file}' at line {lineNumber}.",
                        ex);
                }

                if (evt is null)
                {
                    throw new InvalidDataException(
                        $"Null JSONL record in replay file '{file}' at line {lineNumber}.");
                }

                yield return new ReplayRecord(evt, lineNumber, FileIndex: 0);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private int CompareRecords(ReplayRecord left, ReplayRecord right)
    {
        var timestampComparison = left.Event.Timestamp.UtcTicks.CompareTo(right.Event.Timestamp.UtcTicks);
        if (timestampComparison != 0)
            return timestampComparison;

        if (_symbolOrder is not null)
        {
            var symbolComparison = _symbolOrder[left.Event.EffectiveSymbol]
                .CompareTo(_symbolOrder[right.Event.EffectiveSymbol]);
            if (symbolComparison != 0)
                return symbolComparison;
        }
        var fileComparison = left.FileIndex.CompareTo(right.FileIndex);
        return fileComparison != 0 ? fileComparison : left.LineNumber.CompareTo(right.LineNumber);
    }

    private readonly record struct ReplayRecord(MarketEvent Event, long LineNumber, int FileIndex);

    private sealed class ReplayRecordComparer(JsonlReplayer owner) : IComparer<ReplayRecord>
    {
        public int Compare(ReplayRecord x, ReplayRecord y) => owner.CompareRecords(x, y);
    }
}
