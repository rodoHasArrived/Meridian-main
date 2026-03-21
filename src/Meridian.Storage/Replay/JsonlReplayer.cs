using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Domain.Events;
using Serilog;

namespace Meridian.Storage.Replay;

/// <summary>
/// Reads previously captured JSONL events (optionally gzip compressed) and replays them as <see cref="MarketEvent"/> objects.
/// </summary>
public sealed class JsonlReplayer
{
    private static readonly ILogger Log = LoggingSetup.ForContext<JsonlReplayer>();
    private readonly string _root;

    public JsonlReplayer(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            yield break;

        var files = Directory.EnumerateFiles(_root, "*.jsonl*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            await foreach (var evt in ReadFileAsync(file, ct))
                yield return evt;
        }
    }

    private static async IAsyncEnumerable<MarketEvent> ReadFileAsync(string file, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        Stream stream = fs;
        if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase))
            stream = new GZipStream(fs, CompressionMode.Decompress);

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            MarketEvent? evt = null;
            try
            { evt = JsonSerializer.Deserialize<MarketEvent>(line, MarketDataJsonContext.HighPerformanceOptions); }
            catch (JsonException ex) { Log.Debug(ex, "Failed to parse JSONL line in {File}", file); }

            if (evt is not null)
                yield return evt;
        }
    }
}
