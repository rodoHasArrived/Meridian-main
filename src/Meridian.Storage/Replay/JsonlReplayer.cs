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

        // Storage sinks accept late provider events and persist arrival order. Read one partition
        // at a time so concurrent replays never compete for a fixed pool of retained file handles,
        // then impose the canonical UTC/file/line order across the complete replay.
        var records = new List<ReplayRecord>();
        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            ct.ThrowIfCancellationRequested();
            await foreach (var record in ReadFileAsync(files[fileIndex], ct).ConfigureAwait(false))
            {
                records.Add(record with { FileIndex = fileIndex });
            }
        }

        records.Sort(static (left, right) =>
        {
            var timestampComparison = left.Event.Timestamp.UtcTicks.CompareTo(right.Event.Timestamp.UtcTicks);
            if (timestampComparison != 0)
                return timestampComparison;
            var fileComparison = left.FileIndex.CompareTo(right.FileIndex);
            return fileComparison != 0 ? fileComparison : left.LineNumber.CompareTo(right.LineNumber);
        });

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return record.Event;
        }
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

    private readonly record struct ReplayRecord(MarketEvent Event, long LineNumber, int FileIndex);
}
