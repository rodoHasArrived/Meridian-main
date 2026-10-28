using Meridian.Storage.Archival;

namespace Meridian.Storage.FundStructure;

/// <summary>
/// Loads fund structure state from a JSON file and saves it using atomic file replacement.
/// </summary>
public sealed class JsonFileFundStructureStateStore : IFundStructureStateStore
{
    private readonly string _path;

    public JsonFileFundStructureStateStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string? Load() => File.Exists(_path) ? File.ReadAllText(_path) : null;

    public Task SaveAsync(string json, CancellationToken ct) => AtomicFileWriter.WriteAsync(_path, json, ct);
}
