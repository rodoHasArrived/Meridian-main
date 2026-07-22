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

    /// <summary>
    /// Rejects every append that does not carry the typed accounting posting command. Supported
    /// production composition enables this; isolated compatibility tests may leave it disabled.
    /// </summary>
    public bool RequireGovernedPostingCommand { get; set; }

    /// <summary>
    /// Requires the typed posting command to carry the accounting-period version observed by the
    /// caller. The store compares it while holding the same transaction/row lock as the append.
    /// </summary>
    public bool RequireExpectedVersion { get; set; }
}
