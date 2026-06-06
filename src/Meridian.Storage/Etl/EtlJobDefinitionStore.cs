using System.Text.Json;
using Meridian.Contracts.Etl;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Etl;

public sealed class EtlJobDefinitionStore : IEtlJobDefinitionStore
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public EtlJobDefinitionStore(string dataRoot)
    {
        _rootPath = Path.Combine(dataRoot, "_etl", "jobs");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(EtlJobDefinition definition, CancellationToken ct = default)
    {
        var path = GetPath(definition.JobId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(definition, _jsonOptions);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    public async Task<EtlJobDefinition?> GetAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetPath(jobId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EtlJobDefinition>(json, _jsonOptions);
    }

    public async Task<IReadOnlyList<EtlJobDefinition>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<EtlJobDefinition>();
        foreach (var file in Directory.EnumerateFiles(_rootPath, "etl_*.json"))
        {
            var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var item = JsonSerializer.Deserialize<EtlJobDefinition>(json, _jsonOptions);
            if (item is not null)
                list.Add(item);
        }

        return list;
    }

    public Task DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var path = GetPath(jobId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string jobId) => Path.Combine(_rootPath, $"etl_{jobId}.json");
}
