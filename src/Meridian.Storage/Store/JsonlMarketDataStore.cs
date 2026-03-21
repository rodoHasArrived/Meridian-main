using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Serilog;

namespace Meridian.Storage.Store;

/// <summary>
/// <see cref="IMarketDataStore"/> implementation backed by JSONL (optionally gzip-compressed) files.
/// Enumerates all <c>*.jsonl</c> and <c>*.jsonl.gz</c> files under the configured root,
/// deserialises each line, and applies the <see cref="MarketDataQuery"/> predicate in-process.
/// </summary>
public sealed class JsonlMarketDataStore : IMarketDataStore
{
    private static readonly ILogger Log = LoggingSetup.ForContext<JsonlMarketDataStore>();
    private readonly string _root;

    public JsonlMarketDataStore(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

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

        var symbolStr = query.Symbol.Value.Value;
        return allFiles.Where(f =>
            Path.GetFileName(f).Contains(symbolStr, StringComparison.OrdinalIgnoreCase) ||
            f.Contains(Path.DirectorySeparatorChar + symbolStr + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async IAsyncEnumerable<MarketEvent> ReadFileAsync(
        string file,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        Stream stream = fs;
        if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            stream = new GZipStream(fs, CompressionMode.Decompress);

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
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
                Log.Debug(ex, "Skipping malformed JSONL line in {File}", file);
            }

            if (evt is not null)
                yield return evt;
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
