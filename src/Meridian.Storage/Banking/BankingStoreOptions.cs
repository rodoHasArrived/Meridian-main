namespace Meridian.Storage.Banking;

/// <summary>
/// Configuration for the PostgreSQL-backed banking store.
/// </summary>
public sealed class BankingStoreOptions
{
    /// <summary>Full Npgsql connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>PostgreSQL schema name.  Defaults to "banking".</summary>
    public string Schema { get; set; } = "banking";
}
