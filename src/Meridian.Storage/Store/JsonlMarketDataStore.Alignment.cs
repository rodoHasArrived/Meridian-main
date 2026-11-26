using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Integrity;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Policies;
using Meridian.Storage.Replay;

namespace Meridian.Storage.Store;

/// <summary>A market event with the logical archive path, line and line digest encoded in its ID.</summary>
public sealed record RetainedMarketEvent(MarketEvent Event, string SourceId);

public sealed partial class JsonlMarketDataStore
{
    private const int MaxAlignmentArchiveFiles = 512;
    private const int MaxAlignmentEntriesScanned = 100_000;
    private const int MaxAlignmentArchiveChars = 24 * 1024 * 1024;
    private const int MaxAlignmentLineChars = 64 * 1024;
    private const int MaxAlignmentCandidateEvents = 30_000;

    /// <summary>
    /// Strict, bounded archive read for deterministic transforms. An incomplete read fails
    /// the transformation rather than substituting gaps for unreadable records.
    /// </summary>
    public async IAsyncEnumerable<RetainedMarketEvent> ReadAlignmentEventsAsync(
        IReadOnlySet<string> symbols,
        IReadOnlySet<MarketEventType> eventTypes,
        StorageOptions storageOptions,
        DateTimeOffset from,
        DateTimeOffset toExclusive,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            yield break;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var readToken = timeout.Token;
        var files = FindAlignmentArchives(symbols, storageOptions, from, toExclusive, readToken);
        var budget = new AlignmentReadBudget();
        var totalEvents = 0;
        foreach (var file in files)
        {
            readToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(_root, JsonlArchiveFiles.LogicalPath(file)).Replace('\\', '/');
            if (relativePath.Length > 512)
                throw new InvalidDataException("A retained archive path exceeds the alignment limit.");
            // A closed capture lease keeps a changing file or torn tail out of a published result.
            var logicalPath = JsonlArchiveFiles.LogicalPath(file);
            RefuseLinkedAlignmentArchive(logicalPath);
            if (logicalPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                RefuseLinkedAlignmentArchive(logicalPath + ".gz");
            await using var source = await JsonlArchiveFiles.OpenReadAsync(file, readToken, FileShare.None).ConfigureAwait(false);
            using var stream = CompressedJsonlStream.Decompress(source, source.Name);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, throwOnInvalidBytes: true));
            var lineNumber = 0;
            await foreach (var line in ReadAlignmentLinesAsync(reader, budget, readToken).ConfigureAwait(false))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                MarketEvent? evt;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("symbol", out var symbolElement) ||
                        symbolElement.ValueKind != JsonValueKind.String)
                        throw new JsonException("Stored event is missing its symbol.");
                    var symbol = root.TryGetProperty("canonicalSymbol", out var canonical) &&
                                 canonical.ValueKind == JsonValueKind.String
                        ? canonical.GetString() : symbolElement.GetString();
                    if (symbol is null || !symbols.Contains(symbol))
                        continue;
                    if (!root.TryGetProperty("type", out var typeElement) ||
                        !TryReadAlignmentType(typeElement, out var type))
                        throw new JsonException("Stored event has an invalid event type.");
                    if (!eventTypes.Contains(type))
                        continue;
                    evt = root.Deserialize<MarketEvent>(MarketDataJsonContext.HighPerformanceOptions);
                    if (evt is null || evt.SchemaVersion != 1 || evt.Timestamp == default ||
                        evt.Payload is null || evt.Type != type)
                        throw new JsonException("Stored event has an invalid schema, timestamp or payload.");
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
                {
                    throw new InvalidDataException($"A selected archive record at line {lineNumber} is unreadable.", ex);
                }

                // Include event timestamps near the edges for local dates and daily session labels.
                if (evt.Timestamp < from || evt.Timestamp >= toExclusive)
                    continue;
                if (++totalEvents > MaxAlignmentCandidateEvents)
                    throw new InvalidDataException("The alignment input record limit was exceeded. Shorten the date range.");

                var digest = Sha256Digest.ComputeUtf8(line);
                var id = $"jsonl:{Uri.EscapeDataString(relativePath)}:{lineNumber.ToString(CultureInfo.InvariantCulture)}:{digest}";
                yield return new(evt, id);
            }
        }
    }

    private string[] FindAlignmentArchives(
        IReadOnlySet<string> symbols, StorageOptions storageOptions, DateTimeOffset from,
        DateTimeOffset toExclusive, CancellationToken ct)
    {
        var pathIdentities = symbols.SelectMany(symbol => new[]
        {
            StoragePathSegmentCodec.EncodeSymbol(symbol),
            StoragePathSegmentCodec.EncodeLegacyForLookup(symbol)
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new Dictionary<string, string>(comparer);
        var partitionPolicy = new JsonlStoragePolicy(storageOptions);
        var scanned = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = 32,
            IgnoreInaccessible = false
        };
        foreach (var file in Directory.EnumerateFiles(_root, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (++scanned > MaxAlignmentEntriesScanned)
                throw new InvalidDataException("The archive has too many entries for a bounded alignment scan.");
            if (!JsonlArchiveFiles.IsCanonicalArchivePath(file))
                continue;

            var relativePath = Path.GetRelativePath(_root, file);
            if (!pathIdentities.Any(identity => ContainsPathIdentity(Path.DirectorySeparatorChar + relativePath, identity))
                || !MayCoverAlignmentRange(file, symbols, partitionPolicy, storageOptions, from, toExclusive))
                continue;

            var logicalPath = JsonlArchiveFiles.LogicalPath(file);
            if (!files.TryGetValue(logicalPath, out var existing))
                files.Add(logicalPath, file);
            else if (!comparer.Equals(file, existing) && file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                if (existing.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A logical archive has ambiguous gzip representations.");
                files[logicalPath] = file;
            }

            if (files.Count > MaxAlignmentArchiveFiles)
                throw new InvalidDataException("The alignment archive file limit was exceeded. Shorten the date range.");
        }

        return files.Values.Order(StringComparer.Ordinal).ToArray();
    }

    private static void RefuseLinkedAlignmentArchive(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Alignment archives cannot be opened through links.");
        }
        catch (FileNotFoundException)
        {
            // One publication representation may be absent.
        }
    }

    private static bool MayCoverAlignmentRange(string file, IReadOnlySet<string> symbols,
        JsonlStoragePolicy policy, StorageOptions options, DateTimeOffset from, DateTimeOffset toExclusive)
    {
        var partition = policy.TryParsePath(Path.GetRelativePath(options.RootPath, file));
        // Keep archived layouts that cannot be identified under the current configuration.
        if (partition?.Date is not { } start || !symbols.Contains(partition.Symbol)
            || !Enum.TryParse<MarketEventType>(partition.EventType, ignoreCase: true, out _))
            return true;
        if (start.Year == 9999)
            return start < toExclusive;

        var end = options.NamingConvention == FileNamingConvention.Canonical
            ? start.AddDays(1)
            : options.DatePartition switch
            {
                // Old monthly partitions can remain after a switch to daily/hourly.
                // A month end is a conservative upper bound for any recognized date.
                DatePartition.None => toExclusive,
                _ => new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)
            };
        return start < toExclusive && end > from;
    }

    private static bool TryReadAlignmentType(JsonElement element, out MarketEventType type)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetByte(out var number))
        {
            type = (MarketEventType)number;
            return Enum.IsDefined(type);
        }

        type = default;
        return element.ValueKind == JsonValueKind.String &&
               Enum.TryParse(element.GetString(), ignoreCase: true, out type) && Enum.IsDefined(type);
    }

    private sealed class AlignmentReadBudget
    {
        public int DecodedChars { get; set; }
    }

    private static async IAsyncEnumerable<string> ReadAlignmentLinesAsync(
        StreamReader reader, AlignmentReadBudget budget, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new char[16 * 1024];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) != 0)
        {
            budget.DecodedChars += read;
            if (budget.DecodedChars > MaxAlignmentArchiveChars)
                throw new InvalidDataException("The alignment archive read limit was exceeded. Shorten the date range.");

            var start = 0;
            while (start < read)
            {
                var end = Array.IndexOf(buffer, '\n', start, read - start);
                var length = (end < 0 ? read : end) - start;
                if (line.Length + length > MaxAlignmentLineChars)
                    throw new InvalidDataException("A retained record exceeds the alignment line limit.");
                line.Append(buffer, start, length);
                if (end < 0)
                    break;

                yield return line.ToString().TrimEnd('\r');
                line.Clear();
                start = end + 1;
            }
        }

        if (line.Length != 0)
            yield return line.ToString().TrimEnd('\r');
    }
}
