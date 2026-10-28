namespace Meridian.Storage.FundAccounts;

/// <summary>
/// Configures the PostgreSQL connection and schema for fund account storage.
/// </summary>
public sealed class FundAccountStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "fund_accounts";
}
