namespace Meridian.Application.FundStructure;

public interface IFundStructureStateStore
{
    string? Load();
    Task SaveAsync(string json, CancellationToken ct);
}
