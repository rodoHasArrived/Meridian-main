using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;

namespace Meridian.Application.Backfill;

/// <summary>
/// Persists and reads last backfill status so both the collector and UI can surface progress.
/// Also tracks per-symbol checkpoints so interrupted jobs can be resumed without re-fetching
/// bars that were already written successfully.
/// </summary>
public sealed class BackfillStatusStore
{
    private readonly string _path;
    private readonly string _symbolCheckpointsPath;
    private readonly string _symbolBarCountsPath;

    public BackfillStatusStore(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "data" : dataRoot;
        var statusDir = Path.Combine(root, "_status");
        _path = Path.Combine(statusDir, "backfill.json");
        _symbolCheckpointsPath = Path.Combine(statusDir, "backfill-symbol-checkpoints.json");
        _symbolBarCountsPath = Path.Combine(statusDir, "backfill-symbol-barcounts.json");
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
    /// Removes all per-symbol checkpoints and bar-count data.
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
    /// Removes only the checkpoints and bar-count data associated with the requested
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
