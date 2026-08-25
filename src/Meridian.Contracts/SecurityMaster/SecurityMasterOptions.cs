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
    /// single-node deployments stay coherent through per-write cache upserts. Multi-node
    /// deployments should set this to bound cross-node staleness: a publish on one node reaches
    /// another node's cache within one refresh interval (authoritative reads always go to the
    /// durable store regardless).
    /// </summary>
    public int ProjectionCacheRefreshMinutes { get; set; }
}
