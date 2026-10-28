namespace Meridian.Storage.FundStructure;

/// <summary>
/// Loads and saves the serialized fund structure state.
/// </summary>
public interface IFundStructureStateStore
{
    string? Load();
    Task SaveAsync(string json, CancellationToken ct);
}
