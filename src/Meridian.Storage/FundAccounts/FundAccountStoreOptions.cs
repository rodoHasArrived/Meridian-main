namespace Meridian.Storage.FundAccounts;

public sealed class FundAccountStoreOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "fund_accounts";
}
