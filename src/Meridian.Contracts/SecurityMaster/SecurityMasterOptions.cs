namespace Meridian.Contracts.SecurityMaster;

public sealed class SecurityMasterOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "security_master";
    public int SnapshotIntervalVersions { get; set; } = 50;
    public int ProjectionReplayBatchSize { get; set; } = 500;
    public bool PreloadProjectionCache { get; set; } = true;
    public bool ResolveInactiveByDefault { get; set; } = true;

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
