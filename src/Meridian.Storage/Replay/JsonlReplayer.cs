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

        var enumerators = new IAsyncEnumerator<ReplayRecord>?[files.Count];
        var heap = new PriorityQueue<int, (long UtcTicks, int FileIndex, long LineNumber)>(files.Count);

        try
        {
            // A directory can contain overlapping daily, tiered, or compressed partitions. Prime
            // one event from every physical source and merge those sources instead of concatenating
            // file contents, which would make replay order depend on file names.
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var enumerator = ReadFileAsync(files[fileIndex], ct).GetAsyncEnumerator(ct);
                enumerators[fileIndex] = enumerator;

                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    EnqueueCurrent(heap, enumerator.Current, fileIndex);
            }

            while (heap.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                var fileIndex = heap.Dequeue();
                var enumerator = enumerators[fileIndex]!;
                yield return enumerator.Current.Event;

                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    EnqueueCurrent(heap, enumerator.Current, fileIndex);
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

    private static void EnqueueCurrent(
        PriorityQueue<int, (long UtcTicks, int FileIndex, long LineNumber)> heap,
        ReplayRecord record,
        int fileIndex)
    {
        heap.Enqueue(
            fileIndex,
            (record.Event.Timestamp.UtcTicks, fileIndex, record.LineNumber));
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
        // Shared codec detection (magic bytes, extension fallback) so this reader honors every
        // compression suffix the storage policy can emit, not just gzip.
        var stream = CompressedJsonlStream.Decompress(fs, file);

        using var reader = new StreamReader(stream);
        long lineNumber = 0;
        long? previousUtcTicks = null;
        long previousLineNumber = 0;

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

            var utcTicks = evt.Timestamp.UtcTicks;
            if (previousUtcTicks.HasValue && utcTicks < previousUtcTicks.Value)
            {
                throw new InvalidDataException(
                    $"Replay file '{file}' is not chronological: line {lineNumber} timestamp " +
                    $"{evt.Timestamp:O} precedes line {previousLineNumber}.");
            }

            previousUtcTicks = utcTicks;
            previousLineNumber = lineNumber;
            yield return new ReplayRecord(evt, lineNumber);
        }
    }

    private readonly record struct ReplayRecord(MarketEvent Event, long LineNumber);
}
