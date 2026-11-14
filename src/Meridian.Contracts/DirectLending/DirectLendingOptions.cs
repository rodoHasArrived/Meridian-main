namespace Meridian.Contracts.DirectLending;

/// <summary>
/// Configures direct lending persistence, projection versioning, replay, and outbox processing.
/// </summary>
public sealed class DirectLendingOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = "security_master";

    public int SnapshotIntervalVersions { get; set; } = 50;

    public int CurrentEventSchemaVersion { get; set; } = 1;

    public string ProjectionEngineVersion { get; set; } = "dl-engine-v1";

    public int OutboxBatchSize { get; set; } = 50;

    public int OutboxPollIntervalSeconds { get; set; } = 5;

    public int ReplayBatchSize { get; set; } = 250;

    public bool RequireSecurityMasterReferenceForDurableWrites { get; set; }

    /// <summary>Explicit book for configured single-currency accounting; null retains legacy period selection.</summary>
    public Guid? LedgerBookId { get; set; }

    /// <summary>Pinned chart workspace version required by configured loan posting.</summary>
    public string? LedgerChartMappingRevision { get; set; }

    /// <summary>Maps economic account roles (for example LoanPrincipal) to configured chart paths.</summary>
    public Dictionary<string, string> LedgerAccountPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
