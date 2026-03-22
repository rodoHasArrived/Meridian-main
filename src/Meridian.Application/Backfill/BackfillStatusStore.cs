using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Meridian.Application.Config;
using Meridian.Storage.Archival;

namespace Meridian.Application.Backfill;

/// <summary>
/// Persists and reads last backfill status so both the collector and UI can surface progress.
/// </summary>
public sealed class BackfillStatusStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BackfillStatusStore(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "data" : dataRoot;
        _path = Path.Combine(root, "_status", "backfill.json");
    }

    public static BackfillStatusStore FromConfig(AppConfig cfg) => new(cfg.DataRoot);

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
}
