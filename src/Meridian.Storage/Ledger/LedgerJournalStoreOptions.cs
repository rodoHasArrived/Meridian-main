namespace Meridian.Storage.Ledger;

/// <summary>Configuration for the Postgres-backed ledger journal store.</summary>
public sealed class LedgerJournalStoreOptions
{
    /// <summary>Postgres connection string used for journal and accounting-period persistence.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Database schema that owns the ledger tables.</summary>
    public string SchemaName { get; set; } = "ledger";

    /// <summary>
    /// Enables explicit row locking when period records are updated.
    /// Serializable transactions still protect writes; this flag controls whether the store also
    /// issues <c>FOR UPDATE</c> before applying the optimistic-version guard.
    /// </summary>
    public bool EnablePeriodLocking { get; set; } = true;
}
