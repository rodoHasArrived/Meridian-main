using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Meridian.Storage.Sinks;

/// <summary>
/// Hourly part files and their compaction into the one-file-per-day Parquet file.
/// </summary>
/// <remarks>
/// <para>
/// Flushes append row groups to a part file named
/// <c>{day file}.h{HH}.{instance}-{seq}.parquet-part</c>, so each copy-on-write append copies at
/// most an hour of one symbol's rows instead of the whole day file. The extension is deliberately
/// not <c>.parquet</c>: query, export, retention, quota and tier services match <c>*.parquet</c>
/// and must never see a part that is still being written.
/// </para>
/// <para>
/// Once a part's hour closes (or on disposal) its rows are merged into the day file as one row
/// group, in the same atomic copy-plus-rename as every other write, and the part is deleted. The
/// day file's footer records each merged part name under <see cref="CompactedPartsMetadataKey"/>,
/// so a crash between the merge and the part delete never merges the same rows twice: the next
/// pass sees the name and only deletes the part. Part names are never reused, because each new
/// part takes a fresh per-instance sequence number.
/// </para>
/// <para>
/// Parts left by a process that stopped before compacting are adopted once they are older than
/// <see cref="OrphanPartMinimumAge"/>; the scan runs when the hour changes. A live writer rolls
/// to a new part every hour, so an older part from another instance is no longer being written.
/// </para>
/// </remarks>
public sealed partial class ParquetStorageSink
{
    /// <summary>Extension of an hourly part file; not matched by <c>*.parquet</c> scanners.</summary>
    internal const string PartFileExtension = ".parquet-part";

    /// <summary>Day-file footer key listing the part files already merged into it.</summary>
    internal const string CompactedPartsMetadataKey = "meridian.compacted-parts";

    private const string DayFileExtension = ".parquet";
    private const string PartHourMarker = ".parquet.h";
    private static readonly TimeSpan OrphanPartMinimumAge = TimeSpan.FromHours(2);

    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];
    private long _partSequence;
    private long _lastOrphanScanHour = -1;

    // Guarded by _flushGate: every caller (flushes, compaction, disposal) holds it.
    private readonly Dictionary<string, OpenPart> _openParts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _closedParts = new(StringComparer.OrdinalIgnoreCase);

    private sealed record OpenPart(long HourKey, string PartPath);

    /// <summary>
    /// Returns the part file the current hour's rows for <paramref name="dayPath"/> go to, closing
    /// the previous hour's part (queuing it for compaction) when the hour has changed.
    /// </summary>
    private string GetPartPath(string dayPath)
    {
        var hourKey = CurrentHourKey();
        if (_openParts.TryGetValue(dayPath, out var open))
        {
            if (open.HourKey == hourKey)
                return open.PartPath;

            _closedParts[open.PartPath] = dayPath;
        }

        var sequence = Interlocked.Increment(ref _partSequence);
        var partPath = $"{dayPath}.h{hourKey % 100:D2}.{_instanceId}-{sequence}{PartFileExtension}";
        _openParts[dayPath] = new OpenPart(hourKey, partPath);
        return partPath;
    }

    /// <summary>
    /// Merges closed parts (and, when <paramref name="includeOpenParts"/> is set, the current
    /// hour's parts) into their day files. A part that fails to merge stays queued and is retried
    /// on the next pass; its rows are already durable in the part file.
    /// </summary>
    private async Task CompactPartsAsync(bool includeOpenParts, CancellationToken ct)
    {
        var hourKey = CurrentHourKey();
        foreach (var (dayPath, open) in _openParts.ToArray())
        {
            if (includeOpenParts || open.HourKey != hourKey)
            {
                _closedParts[open.PartPath] = dayPath;
                _openParts.Remove(dayPath);
            }
        }

        if (hourKey != _lastOrphanScanHour)
        {
            _lastOrphanScanHour = hourKey;
            AdoptOrphanParts();
        }

        foreach (var group in _closedParts.ToArray().GroupBy(static kv => kv.Value, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var partPaths = group.Select(static kv => kv.Key).OrderBy(static p => p, StringComparer.Ordinal).ToArray();
            try
            {
                await CompactIntoDayFileAsync(group.Key, partPaths, ct).ConfigureAwait(false);
                foreach (var partPath in partPaths)
                    _closedParts.Remove(partPath);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to merge {PartCount} Parquet part file(s) into {DayPath}; they stay queued for the next flush", partPaths.Length, group.Key);
            }
        }
    }

    private async Task CompactIntoDayFileAsync(string dayPath, IReadOnlyList<string> partPaths, CancellationToken ct)
    {
        var parts = new List<(string Path, string Name, ParquetSchema Schema, DataColumn[] Columns)>();
        foreach (var partPath in partPaths)
        {
            if (!File.Exists(partPath))
                continue;

            var part = await TryReadPartAsync(partPath, ct).ConfigureAwait(false);
            if (part is null)
                continue;

            if (part.Value.Columns.Length == 0)
            {
                File.Delete(partPath);
                continue;
            }

            if (parts.Count > 0 && !parts[0].Schema.Equals(part.Value.Schema))
            {
                _log.Warning("Parquet part file {PartPath} does not match the schema of the other parts for {DayPath}; quarantining it", partPath, dayPath);
                QuarantineExistingFile(partPath);
                continue;
            }

            parts.Add((partPath, Path.GetFileName(partPath), part.Value.Schema, part.Value.Columns));
        }

        if (parts.Count == 0)
            return;

        var schema = parts[0].Schema;
        var appendToExisting = File.Exists(dayPath) && await CanAppendToExistingFileAsync(dayPath, schema, ct).ConfigureAwait(false);
        var metadata = appendToExisting
            ? await ReadCustomMetadataAsync(dayPath, ct).ConfigureAwait(false)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var merged = metadata.TryGetValue(CompactedPartsMetadataKey, out var list) && !string.IsNullOrEmpty(list)
            ? new HashSet<string>(list.Split('|'), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // A part already listed was merged before a crash stopped its delete: do not merge it twice.
        var pending = parts.Where(p => !merged.Contains(p.Name)).ToList();
        if (pending.Count > 0)
        {
            foreach (var part in pending)
                merged.Add(part.Name);
            metadata[CompactedPartsMetadataKey] = string.Join('|', merged.OrderBy(static n => n, StringComparer.Ordinal));

            await _writeAtomicallyAsync(dayPath, async tempStream =>
            {
                if (appendToExisting)
                {
                    await using var existing = new FileStream(
                        dayPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 65536,
                        FileOptions.Asynchronous);
                    await existing.CopyToAsync(tempStream, 65536, ct).ConfigureAwait(false);
                }

                using var writer = await ParquetWriter.CreateAsync(schema, tempStream, append: appendToExisting, cancellationToken: ct).ConfigureAwait(false);
                writer.CompressionMethod = _parquetOptions.CompressionMethod;
                writer.CustomMetadata = metadata;
                foreach (var part in pending)
                {
                    // One row group per merged hour, instead of one per flush.
                    using var rowGroupWriter = writer.CreateRowGroup();
                    foreach (var column in part.Columns)
                        await rowGroupWriter.WriteColumnAsync(column, ct).ConfigureAwait(false);
                }
            }, ct).ConfigureAwait(false);

            _log.Debug("Merged {PartCount} Parquet part file(s) into {DayPath}", pending.Count, dayPath);
        }

        foreach (var part in parts)
            File.Delete(part.Path);
    }

    /// <summary>
    /// Reads every row group of a part into one column per field. An unreadable part is
    /// quarantined (never deleted) and <see langword="null"/> is returned.
    /// </summary>
    private async Task<(ParquetSchema Schema, DataColumn[] Columns)?> TryReadPartAsync(string partPath, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                partPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                FileOptions.Asynchronous);
            using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var fields = reader.Schema.GetDataFields();
            if (reader.RowGroupCount == 0)
                return (reader.Schema, Array.Empty<DataColumn>());

            var perField = fields.Select(static _ => new List<DataColumn>()).ToArray();
            for (var group = 0; group < reader.RowGroupCount; group++)
            {
                using var rowGroup = reader.OpenRowGroupReader(group);
                for (var i = 0; i < fields.Length; i++)
                    perField[i].Add(await rowGroup.ReadColumnAsync(fields[i], ct).ConfigureAwait(false));
            }

            var columns = perField.Select(static columns => columns.Count == 1 ? columns[0] : DataColumn.Concat(columns)).ToArray();
            return (reader.Schema, columns);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Parquet part file {PartPath} is unreadable; quarantining it", partPath);
            QuarantineExistingFile(partPath);
            return null;
        }
    }

    private static async Task<Dictionary<string, string>> ReadCustomMetadataAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.Asynchronous);
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return new Dictionary<string, string>(reader.CustomMetadata, StringComparer.Ordinal);
    }

    /// <summary>
    /// Queues part files left by another (stopped) sink instance. Only parts older than
    /// <see cref="OrphanPartMinimumAge"/> are adopted, so a live writer's current part is never taken.
    /// </summary>
    private void AdoptOrphanParts()
    {
        if (!Directory.Exists(_options.RootPath))
            return;

        // File times are wall-clock, so the age check uses the wall clock, not the injected
        // clock that decides hour boundaries.
        var cutoff = DateTime.UtcNow - OrphanPartMinimumAge;
        var ownMarker = $".{_instanceId}-";
        try
        {
            foreach (var partPath in Directory.EnumerateFiles(_options.RootPath, "*" + PartFileExtension, SearchOption.AllDirectories))
            {
                if (Path.GetFileName(partPath).Contains(ownMarker, StringComparison.Ordinal)
                    || _closedParts.ContainsKey(partPath)
                    || File.GetLastWriteTimeUtc(partPath) > cutoff)
                {
                    continue;
                }

                var dayPath = TryGetDayPath(partPath);
                if (dayPath is not null)
                    _closedParts[partPath] = dayPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Scanning {RootPath} for Parquet part files left by a stopped instance failed; retrying next hour", _options.RootPath);
        }
    }

    /// <summary>Recovers the day file a part belongs to from its name.</summary>
    internal static string? TryGetDayPath(string partPath)
    {
        var marker = partPath.LastIndexOf(PartHourMarker, StringComparison.Ordinal);
        return marker < 0 || !partPath.EndsWith(PartFileExtension, StringComparison.Ordinal)
            ? null
            : partPath[..marker] + DayFileExtension;
    }

    private long CurrentHourKey()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return (now.Year * 1_000_000L) + (now.Month * 10_000L) + (now.Day * 100L) + now.Hour;
    }
}
