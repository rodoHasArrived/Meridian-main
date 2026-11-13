namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Configures Security Master persistence, snapshots, and projection replay batches.
/// </summary>
public sealed class SecurityMasterOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "security_master";
    public int SnapshotIntervalVersions { get; set; } = 50;
    public int ProjectionReplayBatchSize { get; set; } = 500;
    public bool PreloadProjectionCache { get; set; } = true;
    public bool ResolveInactiveByDefault { get; set; } = true;

    /// <summary>
    /// Enables development-configured native single-principal cash-dividend attestation commands.
    /// This default-off host gate grants no declaration, business permission or posting authority.
    /// </summary>
    public bool EnableSinglePrincipalCashDividendAttestations { get; set; }

    /// <summary>
    /// One-time migration-062 upgrade attestation that every Security Master writer has been
    /// stopped. The runner also takes transaction-scoped write locks and refuses active writers.
    /// Not required for a fresh installation or after migration 062 is recorded.
    /// </summary>
    public bool IdentifierHistoryMigrationWritersStopped { get; set; }

    /// <summary>
    /// One-time migration-062 upgrade attestation that no pre-062 binary will write after commit.
    /// Deployment owns this assertion; the runner cannot discover binaries on other hosts.
    /// Not required for a fresh installation or after migration 062 is recorded.
    /// </summary>
    public bool IdentifierHistoryMigrationLegacyWritersRetired { get; set; }

    /// <summary>
    /// One-time migration-068 upgrade attestation that every Security Master writer running after
    /// the migration commits declares <c>termsFormat</c> on the terms it persists (ADR-024 amendment
    /// 2026-11-13): no binary built before that change will write again. Migration 068 adds a check
    /// constraint that refuses an untagged row, so such a writer would fail every create and amend.
    /// The runner also takes a transaction-scoped write lock and refuses active writers. Not
    /// required for a fresh installation or after migration 068 is recorded.
    /// </summary>
    public bool TermsFormatBackfillLegacyWritersRetired { get; set; }

    /// <summary>
    /// Interval, in minutes, at which the projection warmup service re-warms the per-process
    /// projection cache from the durable store. Zero (the default) disables periodic re-warm —
    /// single-node deployments stay coherent through per-write cache upserts, and multi-node
    /// deployments through <see cref="ProjectionCacheNotificationsEnabled"/>. A positive value adds
    /// a backstop re-warm that bounds cross-node staleness even if a change notification is lost
    /// (authoritative reads always go to the durable store regardless).
    /// </summary>
    public int ProjectionCacheRefreshMinutes { get; set; }

    /// <summary>
    /// When true (the default), every committed projection write emits a PostgreSQL
    /// <c>NOTIFY</c> on the schema's projection channel inside the writing transaction, and each
    /// node runs a listener that refreshes its per-process projection cache from the durable store
    /// when another node commits a change. Cross-node staleness is then bounded by notification
    /// delivery rather than by <see cref="ProjectionCacheRefreshMinutes"/>, which remains a
    /// backstop. Disable only for single-node deployments that want to avoid the dedicated
    /// listener connection.
    /// </summary>
    public bool ProjectionCacheNotificationsEnabled { get; set; } = true;
}
