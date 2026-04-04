namespace Meridian.Contracts.FundStructure;

public sealed class AccountManagementOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "fund_accounts";

    /// Maximum tolerated amount variance (absolute) before a cash check is flagged as a break.
    public decimal ReconciliationAmountTolerance { get; set; } = 0.01m;

    /// Maximum drift in minutes between the internal snapshot and the external statement date
    /// before the reconciliation run is rejected as stale.
    public int ReconciliationMaxAsOfDriftMinutes { get; set; } = 1440;
}
