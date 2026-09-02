using System.Globalization;
using System.Runtime.CompilerServices;
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

    private readonly string _path;
    private readonly int _sortRunRecordLimit;
    private readonly int _maxMergeReaders;

    public JsonlReplayer(string path)
        : this(path, SortRunRecordLimit, MaxMergeReaders)
    {
    }

    internal JsonlReplayer(string path, int sortRunRecordLimit, int maxMergeReaders)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (sortRunRecordLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(sortRunRecordLimit));
        if (maxMergeReaders is < 2 or > MaxMergeReaders)
            throw new ArgumentOutOfRangeException(nameof(maxMergeReaders));

        _sortRunRecordLimit = sortRunRecordLimit;
        _maxMergeReaders = maxMergeReaders;
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

        var spoolDirectory = Path.Combine(Path.GetTempPath(), $"meridian-replay-sort-{Guid.NewGuid():N}");
        var runs = new List<string>();
        var replayCompleted = false;

        await ReplayIoAdmissions.WaitAsync(ct).ConfigureAwait(false);
        var admissionHeld = true;
        try
        {
            Directory.CreateDirectory(spoolDirectory);

            // Storage sinks retain arrival order, which can include late provider events. Produce
            // fixed-size sorted runs while opening only one physical source partition at a time.
            var chunk = new List<ReplayRecord>(_sortRunRecordLimit);
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                ct.ThrowIfCancellationRequested();
                await foreach (var record in ReadFileAsync(files[fileIndex], ct).ConfigureAwait(false))
                {
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

            // Convert the final run to small pages before releasing admission. Each page is loaded
            // and its handle closed before any event is yielded. This is essential when an outer
            // multi-symbol merge primes many replayers: no global permit or reader handle may be
            // retained across a yield while the next replayer waits to start.
            var replayPages = await WriteReplayPagesAsync(
                runs[0],
                spoolDirectory,
                ReplayPageRecordLimit,
                ct).ConfigureAwait(false);
            File.Delete(runs[0]);

            ReplayIoAdmissions.Release();
            admissionHeld = false;

            foreach (var page in replayPages)
            {
                ct.ThrowIfCancellationRequested();
                var records = await ReadSpoolPageAsync(page, ct).ConfigureAwait(false);
                File.Delete(page);
                foreach (var record in records)
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

    private static async Task<IReadOnlyList<string>> WriteReplayPagesAsync(
        string inputRun,
        string spoolDirectory,
        int pageRecordLimit,
        CancellationToken ct)
    {
        var pages = new List<string>();
        StreamWriter? writer = null;
        var recordsInPage = 0;
        try
        {
            await foreach (var record in ReadSpoolRunAsync(inputRun, ct).ConfigureAwait(false))
            {
                if (writer is null || recordsInPage == pageRecordLimit)
                {
                    if (writer is not null)
                        await writer.DisposeAsync().ConfigureAwait(false);

                    var page = Path.Combine(spoolDirectory, $"page-{pages.Count:D8}.jsonl");
                    writer = new StreamWriter(new FileStream(
                        page,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 4096,
                        useAsync: true));
                    pages.Add(page);
                    recordsInPage = 0;
                }

                await WriteSpoolRecordAsync(writer, record, ct).ConfigureAwait(false);
                recordsInPage++;
            }
        }
        finally
        {
            if (writer is not null)
                await writer.DisposeAsync().ConfigureAwait(false);
        }

        return pages;
    }

    private static async Task<IReadOnlyList<ReplayRecord>> ReadSpoolPageAsync(
        string path,
        CancellationToken ct)
    {
        await ReplayIoAdmissions.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
            var records = new ReplayRecord[lines.Length];
            for (var index = 0; index < lines.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                records[index] = ParseSpoolRecord(lines[index], path);
            }

            return records;
        }
        finally
        {
            ReplayIoAdmissions.Release();
        }
    }

    private static async Task FlushRunAsync(
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

    private static async Task WriteMergedRunAsync(
        IReadOnlyList<string> inputRuns,
        string output,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(new FileStream(
            output,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true));
        await foreach (var record in MergeRunsAsync(inputRuns, ct).ConfigureAwait(false))
            await WriteSpoolRecordAsync(writer, record, ct).ConfigureAwait(false);
    }

    private static async Task WriteRunAsync(
        IReadOnlyList<ReplayRecord> records,
        string output,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(new FileStream(
            output,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true));
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

    private static async IAsyncEnumerable<ReplayRecord> MergeRunsAsync(
        IReadOnlyList<string> runs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (runs.Count == 0)
            yield break;

        var enumerators = new IAsyncEnumerator<ReplayRecord>?[runs.Count];
        var heap = new PriorityQueue<int, ReplayRecord>(runs.Count, ReplayRecordComparer.Instance);
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

    private static async IAsyncEnumerable<ReplayRecord> ReadFileAsync(
        string file,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var fs = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
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
                evt = JsonSerializer.Deserialize<MarketEvent>(line, MarketDataJsonContext.HighPerformanceOptions);
            }
            catch (JsonException ex)
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

    private static int CompareRecords(ReplayRecord left, ReplayRecord right)
    {
        var timestampComparison = left.Event.Timestamp.UtcTicks.CompareTo(right.Event.Timestamp.UtcTicks);
        if (timestampComparison != 0)
            return timestampComparison;

        var fileComparison = left.FileIndex.CompareTo(right.FileIndex);
        return fileComparison != 0 ? fileComparison : left.LineNumber.CompareTo(right.LineNumber);
    }

    private readonly record struct ReplayRecord(MarketEvent Event, long LineNumber, int FileIndex);

    private sealed class ReplayRecordComparer : IComparer<ReplayRecord>
    {
        internal static ReplayRecordComparer Instance { get; } = new();

        public int Compare(ReplayRecord x, ReplayRecord y) => CompareRecords(x, y);
    }
}
