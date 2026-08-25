using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;

namespace Meridian.Storage.Replay;

/// <summary>
/// Reads previously captured JSONL events (optionally gzip compressed) from a file or directory
/// and replays them as <see cref="MarketEvent"/> objects.
/// </summary>
public sealed class JsonlReplayer
{
    internal const int SortRunRecordLimit = 4096;
    internal const int MaxMergeReaders = 16;
    private const int MaxConcurrentMerges = 4;
    private const int MaxConcurrentPartitionReaders = 128;
    private static readonly SemaphoreSlim MergeSlots = new(MaxConcurrentMerges, MaxConcurrentMerges);
    private static readonly SemaphoreSlim PartitionReaderSlots = new(
        MaxConcurrentPartitionReaders,
        MaxConcurrentPartitionReaders);
    private readonly string _path;

    public JsonlReplayer(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
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
        Directory.CreateDirectory(spoolDirectory);
        var runs = new List<string>();

        try
        {
            // Storage sinks persist arrival order, including late provider events. Convert the
            // source into bounded sorted runs while opening one physical partition at a time.
            var chunk = new List<ReplayRecord>(SortRunRecordLimit);
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                ct.ThrowIfCancellationRequested();
                await foreach (var record in ReadFileAsync(files[fileIndex], ct).ConfigureAwait(false))
                {
                    chunk.Add(record with { FileIndex = fileIndex });
                    if (chunk.Count == SortRunRecordLimit)
                        await FlushRunAsync(chunk, runs, spoolDirectory, ct).ConfigureAwait(false);
                }
            }

            if (chunk.Count > 0)
                await FlushRunAsync(chunk, runs, spoolDirectory, ct).ConfigureAwait(false);

            // Collapse excess runs in bounded fan-in passes. Four merges may proceed concurrently,
            // and each holds at most sixteen reader slots, leaving capacity for source readers.
            while (runs.Count > MaxMergeReaders)
            {
                var nextRuns = new List<string>();
                for (var offset = 0; offset < runs.Count; offset += MaxMergeReaders)
                {
                    var batch = runs.Skip(offset).Take(MaxMergeReaders).ToArray();
                    var output = Path.Combine(spoolDirectory, $"merge-{Guid.NewGuid():N}.jsonl");
                    await WriteMergedRunAsync(batch, output, ct).ConfigureAwait(false);
                    nextRuns.Add(output);
                    foreach (var consumedRun in batch)
                        File.Delete(consumedRun);
                }
                runs = nextRuns;
            }

            await foreach (var record in MergeRunsAsync(runs, ct).ConfigureAwait(false))
                yield return record.Event;
        }
        finally
        {
            if (Directory.Exists(spoolDirectory))
                Directory.Delete(spoolDirectory, recursive: true);
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
            output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true));
        await foreach (var record in MergeRunsAsync(inputRuns, ct).ConfigureAwait(false))
            await WriteSpoolRecordAsync(writer, record, ct).ConfigureAwait(false);
    }

    private static async Task WriteRunAsync(
        IReadOnlyList<ReplayRecord> records,
        string output,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(new FileStream(
            output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true));
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
        await writer.WriteLineAsync($"{record.FileIndex}\t{record.LineNumber}\t{json}".AsMemory(), ct)
            .ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<ReplayRecord> MergeRunsAsync(
        IReadOnlyList<string> runs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (runs.Count == 0)
            yield break;

        await MergeSlots.WaitAsync(ct).ConfigureAwait(false);
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
            MergeSlots.Release();
        }
    }

    private static async IAsyncEnumerable<ReplayRecord> ReadSpoolRunAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await PartitionReaderSlots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                var firstTab = line.IndexOf('\t');
                var secondTab = firstTab < 0 ? -1 : line.IndexOf('\t', firstTab + 1);
                if (firstTab <= 0 || secondTab <= firstTab + 1)
                    throw new InvalidDataException($"Malformed replay spool record in '{path}'.");

                var fileIndex = int.Parse(line.AsSpan(0, firstTab), provider: null);
                var lineNumber = long.Parse(line.AsSpan(firstTab + 1, secondTab - firstTab - 1), provider: null);
                var evt = JsonSerializer.Deserialize<MarketEvent>(
                    line.AsSpan(secondTab + 1),
                    MarketDataJsonContext.HighPerformanceOptions)
                    ?? throw new InvalidDataException($"Null replay spool record in '{path}'.");
                yield return new ReplayRecord(evt, lineNumber, fileIndex);
            }
        }
        finally
        {
            PartitionReaderSlots.Release();
        }
    }

    private static async IAsyncEnumerable<ReplayRecord> ReadFileAsync(
        string file,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await PartitionReaderSlots.WaitAsync(ct).ConfigureAwait(false);
        try
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
                        $"Malformed JSONL record in replay file '{file}' at line {lineNumber}.", ex);
                }

                if (evt is null)
                    throw new InvalidDataException(
                        $"Null JSONL record in replay file '{file}' at line {lineNumber}.");

                yield return new ReplayRecord(evt, lineNumber, FileIndex: 0);
            }
        }
        finally
        {
            PartitionReaderSlots.Release();
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
