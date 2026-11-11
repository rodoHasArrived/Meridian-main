using System.Text.Json;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Meridian.Contracts.Backfill;

namespace Meridian.Storage.Backfill;

/// <summary>
/// Storage-owned persistence for last backfill status, per-symbol checkpoint sidecars and the
/// verified coverage sidecar that resume decisions rely on.
/// </summary>
public sealed class BackfillStatusStore
{
    private readonly string _path;
    private readonly string _symbolCheckpointsPath;
    private readonly string _symbolBarCountsPath;
    private readonly string _symbolCoveragePath;

    public BackfillStatusStore(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "data" : dataRoot;
        var statusDir = Path.Combine(root, "_status");
        _path = Path.Combine(statusDir, "backfill.json");
        _symbolCheckpointsPath = Path.Combine(statusDir, "backfill-symbol-checkpoints.json");
        _symbolBarCountsPath = Path.Combine(statusDir, "backfill-symbol-barcounts.json");
        _symbolCoveragePath = Path.Combine(statusDir, "backfill-symbol-coverage.json");
    }

    public static BackfillStatusStore FromConfig(AppConfig cfg) => new(cfg.DataRoot);

    // -----------------------------------------------------------------------
    // Aggregate result (last completed run)
    // -----------------------------------------------------------------------

    public async Task WriteAsync(BackfillResult result, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(result, BackfillStatusStoreJsonContext.Default.BackfillResult);
        await AtomicFileWriter.WriteAsync(_path, json, ct);
    }

    public BackfillResult? TryRead()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, BackfillStatusStoreJsonContext.Default.BackfillResult);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private readonly SemaphoreSlim _checkpointLock = new(1, 1);

    // -----------------------------------------------------------------------
    // Per-symbol checkpoints
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records that <paramref name="symbol"/> was successfully backfilled up to and including
    /// <paramref name="lastCompletedDate"/> with <paramref name="barsWritten"/> bars.
    /// Subsequent calls update the stored date if the new value is later than the previously
    /// recorded one; the bar count is replaced unconditionally when the date advances.
    /// Thread-safe: concurrent writes are serialized through an internal lock.
    /// </summary>
    public async Task WriteSymbolCheckpointAsync(
        string symbol,
        DateOnly lastCompletedDate,
        long barsWritten = 0,
        CancellationToken ct = default)
        => await WriteSymbolCheckpointAsync(symbol, DataGranularity.Daily, lastCompletedDate, barsWritten, ct).ConfigureAwait(false);

    /// <summary>
    /// Records that <paramref name="symbol"/> was successfully backfilled up to and including
    /// <paramref name="lastCompletedDate"/> for the specified <paramref name="granularity"/>.
    /// Granularity-scoped checkpoints prevent one backfill lane from suppressing another.
    /// </summary>
    public async Task WriteSymbolCheckpointAsync(
        string symbol,
        DataGranularity granularity,
        DateOnly lastCompletedDate,
        long barsWritten = 0,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var checkpointKey = BuildCheckpointKey(symbol, granularity);

        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // ---- date checkpoint ----
            var checkpoints = TryReadRawSymbolCheckpoints() ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            bool dateAdvanced = !TryGetCheckpointDate(checkpoints, checkpointKey, out var existing) || lastCompletedDate > existing;
            if (dateAdvanced)
                checkpoints[checkpointKey] = lastCompletedDate.ToString("yyyy-MM-dd");

            var checkpointJson = JsonSerializer.Serialize(
                checkpoints,
                BackfillStatusStoreJsonContext.Default.DictionaryStringString);
            await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, checkpointJson, ct);

            // ---- bar-count sidecar ----
            if (barsWritten > 0 && dateAdvanced)
            {
                var barCounts = TryReadRawSymbolBarCounts() ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                barCounts[checkpointKey] = barsWritten;
                var barCountJson = JsonSerializer.Serialize(
                    barCounts,
                    BackfillStatusStoreJsonContext.Default.DictionaryStringInt64);
                await AtomicFileWriter.WriteAsync(_symbolBarCountsPath, barCountJson, ct);
            }
        }
        finally
        {
            _checkpointLock.Release();
        }
    }

    /// <summary>
    /// Returns the per-symbol checkpoint map, or <c>null</c> when no checkpoints have been saved yet.
    /// Keys are symbol names (case-insensitive); values are the last successfully completed date.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly>? TryReadSymbolCheckpoints()
        => TryReadSymbolCheckpoints(DataGranularity.Daily);

    /// <summary>
    /// Returns the checkpoint map for the requested <paramref name="granularity"/>.
    /// Legacy unscoped entries are treated as daily checkpoints for backward compatibility.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly>? TryReadSymbolCheckpoints(DataGranularity granularity)
        => TryReadSymbolCheckpointsAsMutable(granularity);

    /// <summary>
    /// Returns the per-symbol bar-count map persisted alongside the checkpoints, or <c>null</c>
    /// when no bar-count data has been saved yet.
    /// Keys are symbol names (case-insensitive); values are the number of bars written in the run
    /// that last advanced the checkpoint date for that symbol.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts()
        => TryReadSymbolBarCounts(DataGranularity.Daily);

    /// <summary>
    /// Returns the per-symbol bar-count map for the requested <paramref name="granularity"/>.
    /// Legacy unscoped entries are treated as daily bar counts for backward compatibility.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts(DataGranularity granularity)
        => TryReadSymbolBarCountsAsMutable(granularity);

    private Dictionary<string, DateOnly>? TryReadSymbolCheckpointsAsMutable(DataGranularity granularity)
    {
        var raw = TryReadRawSymbolCheckpoints();
        if (raw is null)
            return null;

        var result = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            if (!TryMapCheckpointKey(key, granularity, out var symbol) ||
                !DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date))
            {
                continue;
            }

            result[symbol] = date;
        }

        return result;
    }

    private Dictionary<string, string>? TryReadRawSymbolCheckpoints()
    {
        try
        {
            if (!File.Exists(_symbolCheckpointsPath))
                return null;

            var json = File.ReadAllText(_symbolCheckpointsPath);
            return JsonSerializer.Deserialize(
                json,
                BackfillStatusStoreJsonContext.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private Dictionary<string, long>? TryReadSymbolBarCountsAsMutable(DataGranularity granularity)
    {
        var raw = TryReadRawSymbolBarCounts();
        if (raw is null)
            return null;

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            if (TryMapCheckpointKey(key, granularity, out var symbol))
                result[symbol] = value;
        }

        return result;
    }

    private Dictionary<string, long>? TryReadRawSymbolBarCounts()
    {
        try
        {
            if (!File.Exists(_symbolBarCountsPath))
                return null;

            var json = File.ReadAllText(_symbolBarCountsPath);
            return JsonSerializer.Deserialize(
                       json,
                       BackfillStatusStoreJsonContext.Default.DictionaryStringInt64)
                   ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes all per-symbol checkpoints, bar-count data and verified coverage.
    /// Call when starting a fresh (non-resume) backfill run.
    /// </summary>
    public async Task ClearSymbolCheckpointsAsync(CancellationToken ct = default)
    {
        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(_symbolCheckpointsPath))
                await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, "{}", ct);

            if (File.Exists(_symbolBarCountsPath))
                await AtomicFileWriter.WriteAsync(_symbolBarCountsPath, "{}", ct);

            if (File.Exists(_symbolCoveragePath))
                await WriteCoverageDocumentAsync(new BackfillCoverageDocument(), ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Best-effort; the caller can retry.
        }
        finally
        {
            _checkpointLock.Release();
        }
    }

    /// <summary>
    /// Removes only the checkpoints, bar-count data and verified coverage associated with the requested
    /// <paramref name="granularity"/>, preserving resume state for other backfill lanes.
    /// </summary>
    public async Task ClearSymbolCheckpointsAsync(DataGranularity granularity, CancellationToken ct = default)
    {
        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var checkpoints = TryReadRawSymbolCheckpoints();
            if (checkpoints is not null)
            {
                foreach (var key in checkpoints.Keys.Where(key => IsCheckpointKeyForGranularity(key, granularity)).ToArray())
                    checkpoints.Remove(key);

                var checkpointJson = JsonSerializer.Serialize(
                    checkpoints,
                    BackfillStatusStoreJsonContext.Default.DictionaryStringString);
                await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, checkpointJson, ct);
            }

            var barCounts = TryReadRawSymbolBarCounts();
            if (barCounts is not null)
            {
                foreach (var key in barCounts.Keys.Where(key => IsCheckpointKeyForGranularity(key, granularity)).ToArray())
                    barCounts.Remove(key);

                var barCountJson = JsonSerializer.Serialize(
                    barCounts,
                    BackfillStatusStoreJsonContext.Default.DictionaryStringInt64);
                await AtomicFileWriter.WriteAsync(_symbolBarCountsPath, barCountJson, ct);
            }

            var coverage = TryReadCoverageDocument();
            if (coverage is not null)
            {
                var lane = granularity.ToUiValue();
                coverage.Entries.RemoveAll(entry => string.Equals(entry.Granularity, lane, StringComparison.OrdinalIgnoreCase));
                await WriteCoverageDocumentAsync(coverage, ct).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Best-effort; the caller can retry.
        }
        finally
        {
            _checkpointLock.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Verified per-symbol coverage
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records, in one atomic write, the date ranges whose bars were durably written for each
    /// symbol by <paramref name="provider"/> and <paramref name="feed"/>. Ranges merge with the
    /// coverage already recorded for the same symbol, granularity, provider and feed.
    /// Call only after the bars are on disk: a resumed run skips exactly these dates.
    /// </summary>
    public async Task WriteSymbolCoverageAsync(
        DataGranularity granularity,
        string provider,
        string feed,
        IReadOnlyCollection<BackfillCoverageUpdate> updates,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(feed);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
            return;

        var lane = granularity.ToUiValue();
        var providerKey = NormalizeCoverageIdentity(provider);
        var feedKey = NormalizeCoverageIdentity(feed);

        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var document = TryReadCoverageDocument() ?? new BackfillCoverageDocument();
            foreach (var update in updates)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(update.Symbol);
                if (update.Ranges.Count == 0)
                    continue;

                var symbol = update.Symbol.Trim().ToUpperInvariant();
                var entry = document.Entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.Symbol, symbol, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Granularity, lane, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Provider, providerKey, StringComparison.Ordinal) &&
                    string.Equals(candidate.Feed, feedKey, StringComparison.Ordinal));
                if (entry is null)
                {
                    entry = new BackfillCoverageEntry { Symbol = symbol, Granularity = lane, Provider = providerKey, Feed = feedKey };
                    document.Entries.Add(entry);
                }

                entry.Ranges = BackfillCoverageMath.Merge(entry.Ranges.Concat(update.Ranges)).ToList();
                entry.Sources = entry.Sources
                    .Concat(update.Sources.Where(static source => !string.IsNullOrWhiteSpace(source)).Select(static source => source.Trim()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                entry.RecordedAt = DateTimeOffset.UtcNow;
            }

            await WriteCoverageDocumentAsync(document, ct).ConfigureAwait(false);
        }
        finally
        {
            _checkpointLock.Release();
        }
    }

    /// <summary>
    /// Returns the verified coverage recorded for <paramref name="provider"/> and
    /// <paramref name="feed"/> in one granularity lane, keyed by symbol (case-insensitive), or
    /// <c>null</c> when none is recorded. Coverage from another provider or feed, and legacy
    /// date-only checkpoints, are never returned.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BackfillCoverageRange>>? TryReadSymbolCoverage(
        DataGranularity granularity,
        string provider,
        string feed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(feed);
        var document = TryReadCoverageDocument();
        if (document is null)
            return null;

        var lane = granularity.ToUiValue();
        var providerKey = NormalizeCoverageIdentity(provider);
        var feedKey = NormalizeCoverageIdentity(feed);
        var result = new Dictionary<string, IReadOnlyList<BackfillCoverageRange>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Symbol) ||
                !string.Equals(entry.Granularity, lane, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Provider, providerKey, StringComparison.Ordinal) ||
                !string.Equals(entry.Feed, feedKey, StringComparison.Ordinal))
            {
                continue;
            }

            var ranges = BackfillCoverageMath.Merge(entry.Ranges ?? []);
            if (ranges.Count > 0)
                result[entry.Symbol] = ranges;
        }

        return result.Count == 0 ? null : result;
    }

    private BackfillCoverageDocument? TryReadCoverageDocument()
    {
        try
        {
            if (!File.Exists(_symbolCoveragePath))
                return null;

            var json = File.ReadAllText(_symbolCoveragePath);
            var document = JsonSerializer.Deserialize(json, BackfillStatusStoreJsonContext.Default.BackfillCoverageDocument);
            // An unreadable or newer-format sidecar proves nothing; the caller refetches.
            if (document is null || document.Version != BackfillCoverageDocument.CurrentVersion)
                return null;
            document.Entries ??= [];
            document.Entries.RemoveAll(static entry => entry is null);
            return document;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private Task WriteCoverageDocumentAsync(BackfillCoverageDocument document, CancellationToken ct)
        => AtomicFileWriter.WriteAsync(
            _symbolCoveragePath,
            JsonSerializer.Serialize(document, BackfillStatusStoreJsonContext.Default.BackfillCoverageDocument),
            ct);

    private static string NormalizeCoverageIdentity(string value) => value.Trim().ToLowerInvariant();

    private static bool TryGetCheckpointDate(
        IReadOnlyDictionary<string, string> checkpoints,
        string checkpointKey,
        out DateOnly date)
    {
        date = default;
        return checkpoints.TryGetValue(checkpointKey, out var value)
               && DateOnly.TryParseExact(value, "yyyy-MM-dd", out date);
    }

    private static string BuildCheckpointKey(string symbol, DataGranularity granularity)
        => $"{granularity.ToUiValue()}::{symbol.Trim().ToUpperInvariant()}";

    private static bool TryMapCheckpointKey(string rawKey, DataGranularity granularity, out string symbol)
    {
        if (rawKey.Contains("::", StringComparison.Ordinal))
        {
            var parts = rawKey.Split("::", 2, StringSplitOptions.None);
            if (parts.Length == 2 &&
                DataGranularityExtensions.TryParseValue(parts[0], out var storedGranularity) &&
                storedGranularity == granularity)
            {
                symbol = parts[1];
                return true;
            }
        }
        else if (granularity == DataGranularity.Daily)
        {
            symbol = rawKey;
            return true;
        }

        symbol = string.Empty;
        return false;
    }

    private static bool IsCheckpointKeyForGranularity(string rawKey, DataGranularity granularity)
        => TryMapCheckpointKey(rawKey, granularity, out _);
}
