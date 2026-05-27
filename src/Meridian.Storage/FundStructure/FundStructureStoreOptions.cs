namespace Meridian.Storage.FundStructure;

public sealed class FundStructureStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "fund_structure";
}
