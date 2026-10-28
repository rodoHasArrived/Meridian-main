namespace Meridian.Storage.FundStructure;

/// <summary>
/// Configures the PostgreSQL connection and schema for fund structure storage.
/// </summary>
public sealed class FundStructureStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "fund_structure";
}
