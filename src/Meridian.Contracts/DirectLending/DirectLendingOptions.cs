namespace Meridian.Contracts.DirectLending;

public sealed class DirectLendingOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = "direct_lending";

    public int SnapshotIntervalVersions { get; set; } = 50;

    public int CurrentEventSchemaVersion { get; set; } = 1;

    public string ProjectionEngineVersion { get; set; } = "dl-engine-v1";

    public int OutboxBatchSize { get; set; } = 50;

    public int OutboxPollIntervalSeconds { get; set; } = 5;

    public int ReplayBatchSize { get; set; } = 250;
}
