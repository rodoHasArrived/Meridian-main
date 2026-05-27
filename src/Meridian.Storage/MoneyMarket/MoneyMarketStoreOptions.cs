namespace Meridian.Storage.MoneyMarket;

/// <summary>
/// Configuration for the PostgreSQL-backed money market fund store.
/// </summary>
public sealed class MoneyMarketStoreOptions
{
    /// <summary>Full Npgsql connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>PostgreSQL schema name.  Defaults to "money_market".</summary>
    public string Schema { get; set; } = "money_market";
}
