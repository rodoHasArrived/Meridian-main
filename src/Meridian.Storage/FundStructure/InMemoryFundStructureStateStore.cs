namespace Meridian.Storage.FundStructure;

/// <summary>
/// Retains serialized fund structure state in memory for the lifetime of the store.
/// </summary>
public sealed class InMemoryFundStructureStateStore : IFundStructureStateStore
{
    private string? _json;

    public string? Load() => _json;

    public Task SaveAsync(string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _json = json;
        return Task.CompletedTask;
    }
}
