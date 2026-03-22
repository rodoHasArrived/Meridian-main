namespace Meridian.Contracts.SecurityMaster;

public sealed class SecurityMasterOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "security_master";
    public int SnapshotIntervalVersions { get; set; } = 50;
    public int ProjectionReplayBatchSize { get; set; } = 500;
    public bool PreloadProjectionCache { get; set; } = true;
    public bool ResolveInactiveByDefault { get; set; } = true;
}
