namespace Meridian.Storage.FundStructure;

public sealed class InMemoryFundStructureStateStore : IFundStructureStateStore
{
    private string? _json;

    /// <summary>Always null: nothing is persisted, so there is nothing to quarantine.</summary>
    public string? BackingFilePath => null;

    public string? Load() => _json;

    public Task SaveAsync(string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _json = json;
        return Task.CompletedTask;
    }
}
