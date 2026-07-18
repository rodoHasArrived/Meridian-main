using System.Text.Json;
using Meridian.Storage.Store;

namespace Meridian.Workflow.Runbooks;

public interface IRunbookStore
{
    Task<IReadOnlyList<RunbookDefinition>> ListAsync(CancellationToken ct = default);
    Task<RunbookDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task SaveAsync(RunbookDefinition definition, CancellationToken ct = default);
}

public sealed class JsonRunbookStore : JsonFileSnapshotStore<Dictionary<string, RunbookDefinition>>, IRunbookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonRunbookStore(string dataRoot)
        : base(ResolveFilePath(dataRoot), JsonOptions)
    {
    }

    protected override Dictionary<string, RunbookDefinition> CreateEmptySnapshot()
        => new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<RunbookDefinition>> ListAsync(CancellationToken ct = default)
        => ReadSnapshotAsync<IReadOnlyList<RunbookDefinition>>(
            static map => map.Values.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            ct);

    public Task<RunbookDefinition?> GetAsync(string id, CancellationToken ct = default)
        => ReadSnapshotAsync(map => map.TryGetValue(id, out var value) ? value : null, ct);

    public Task SaveAsync(RunbookDefinition definition, CancellationToken ct = default)
        => UpdateSnapshotAsync(map =>
        {
            map[definition.Id] = definition;
            return map;
        }, ct);

    private static string ResolveFilePath(string dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? "artifacts" : dataRoot;
        Directory.CreateDirectory(root);
        return Path.Combine(root, "runbooks.json");
    }
}
