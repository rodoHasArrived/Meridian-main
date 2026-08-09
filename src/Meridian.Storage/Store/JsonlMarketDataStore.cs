using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Core.Logging;
using Meridian.Core.Serialization;
using Meridian.Contracts.Store;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;
using Serilog;

namespace Meridian.Storage.Store;

/// <summary>
/// <see cref="IMarketDataStore"/> implementation backed by JSONL (optionally compressed) files.
/// Enumerates all <c>*.jsonl</c> files under the configured root — including any compression suffix
/// the storage policy can emit (<c>.gz</c>/<c>.gzip</c>/<c>.zst</c>/<c>.lz4</c>/<c>.br</c>) —
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

    private static async IAsyncEnumerable<MarketEvent> ReadFileAsync(
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
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await ReadLineOrEndAtTruncatedTailAsync(reader, file, ct).ConfigureAwait(false);
            if (line is null)
                break;
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

    // A crash while the sink appends a compressed batch can leave a torn trailing gzip member;
    // the decoder throws InvalidDataException mid-read. Every complete earlier member has
    // already been yielded, so treat the torn tail as end-of-file rather than failing the query.
    private static async ValueTask<string?> ReadLineOrEndAtTruncatedTailAsync(
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
