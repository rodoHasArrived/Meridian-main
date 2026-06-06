using System.Text.Json;

namespace Meridian.Workflow.Runbooks;

public interface IRunbookStore
{
    Task<IReadOnlyList<RunbookDefinition>> ListAsync(CancellationToken ct = default);
    Task<RunbookDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(RunbookDefinition definition, CancellationToken ct = default);
}

public sealed class JsonRunbookStore : IRunbookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath;

    public JsonRunbookStore(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "artifacts" : dataRoot;
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "runbooks.json");
    }

    public async Task<IReadOnlyList<RunbookDefinition>> ListAsync(CancellationToken ct = default)
    {
        var map = await ReadMapAsync(ct).ConfigureAwait(false);
        return map.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<RunbookDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        var map = await ReadMapAsync(ct).ConfigureAwait(false);
        return map.TryGetValue(id, out var value) ? value : null;
    }

    public async Task SaveAsync(RunbookDefinition definition, CancellationToken ct = default)
    {
        var map = await ReadMapAsync(ct).ConfigureAwait(false);
        map[definition.Id] = definition;
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, map, JsonOptions, ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, RunbookDefinition>> ReadMapAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(_filePath);
        var map = await JsonSerializer.DeserializeAsync<Dictionary<string, RunbookDefinition>>(stream, JsonOptions, ct).ConfigureAwait(false);
        return map ?? new(StringComparer.OrdinalIgnoreCase);
    }
}
