namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL configuration for immutable reporting artifact storage.
/// </summary>
public sealed class ReportingArtifactStoreOptions
{
    public string ConnectionString { get; init; } = string.Empty;

    public string Schema { get; init; } = "reporting";
}
