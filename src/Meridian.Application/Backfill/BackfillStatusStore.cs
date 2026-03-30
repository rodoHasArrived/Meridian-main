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
    /// <paramref name="lastCompletedDate"/>. Subsequent calls update the stored date if the new
    /// value is later than the previously recorded one.
    /// Thread-safe: concurrent writes are serialized through an internal lock.
    /// </summary>
    public async Task WriteSymbolCheckpointAsync(
        string symbol,
        DateOnly lastCompletedDate,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        await _checkpointLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Load mutable copy from file, or start fresh.
            var checkpoints = TryReadSymbolCheckpointsAsMutable() ?? new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

            // Keep the most recent (latest) date per symbol.
            if (!checkpoints.TryGetValue(symbol, out var existing) || lastCompletedDate > existing)
                checkpoints[symbol] = lastCompletedDate;

            // Serialize as a flat object keyed by symbol (camelCase not applicable to dynamic keys).
            var serializable = checkpoints.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToString("yyyy-MM-dd"),
                StringComparer.OrdinalIgnoreCase);

            var json = JsonSerializer.Serialize(serializable, JsonOptions);
            await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, json, ct);
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

    /// <summary>
    /// Removes all per-symbol checkpoints. Call when starting a fresh (non-resume) backfill run.
    /// </summary>
    public async Task ClearSymbolCheckpointsAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_symbolCheckpointsPath))
            {
                // Overwrite with empty object rather than deleting, to preserve directory structure.
                await AtomicFileWriter.WriteAsync(_symbolCheckpointsPath, "{}", ct);
            }
        }
        catch (IOException)
        {
            // Best-effort; the caller can retry.
        }
    }
}
