using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Application.Config;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        var json = JsonSerializer.Serialize(result, JsonOptions);
        await AtomicFileWriter.WriteAsync(_path, json, ct);
    }

    public BackfillResult? TryRead()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<BackfillResult>(json, JsonOptions);
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // ---- date checkpoint ----
            var checkpoints = TryReadSymbolCheckpointsAsMutable() ?? new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

            bool dateAdvanced = !checkpoints.TryGetValue(symbol, out var existing) || lastCompletedDate > existing;
            if (dateAdvanced)
                checkpoints[symbol] = lastCompletedDate;

            var serializable = checkpoints.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToString("yyyy-MM-dd"),
                StringComparer.OrdinalIgnoreCase);

            var checkpointJson = JsonSerializer.Serialize(serializable, JsonOptions);
            await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, checkpointJson, ct);

            // ---- bar-count sidecar ----
            if (barsWritten > 0 && dateAdvanced)
            {
                var barCounts = TryReadSymbolBarCountsAsMutable() ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                barCounts[symbol] = barsWritten;
                var barCountJson = JsonSerializer.Serialize(barCounts, JsonOptions);
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
        => TryReadSymbolCheckpointsAsMutable();

    /// <summary>
    /// Returns the per-symbol bar-count map persisted alongside the checkpoints, or <c>null</c>
    /// when no bar-count data has been saved yet.
    /// Keys are symbol names (case-insensitive); values are the number of bars written in the run
    /// that last advanced the checkpoint date for that symbol.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts()
        => TryReadSymbolBarCountsAsMutable();

    private Dictionary<string, DateOnly>? TryReadSymbolCheckpointsAsMutable()
    {
        try
        {
            if (!File.Exists(_symbolCheckpointsPath))
                return null;

            var json = File.ReadAllText(_symbolCheckpointsPath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (raw is null)
                return null;

            var result = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in raw)
            {
                if (DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date))
                    result[key] = date;
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private Dictionary<string, long>? TryReadSymbolBarCountsAsMutable()
    {
        try
        {
            if (!File.Exists(_symbolBarCountsPath))
                return null;

            var json = File.ReadAllText(_symbolBarCountsPath);
            return JsonSerializer.Deserialize<Dictionary<string, long>>(json, JsonOptions)
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
    }
}
