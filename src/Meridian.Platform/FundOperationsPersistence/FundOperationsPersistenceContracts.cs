namespace Meridian.Platform.FundOperationsPersistence;

public enum FundOperationsDomain
{
    FundStructure,
    FundAccounts,
    DirectLending,
    Banking,
    MoneyMarket
}

public enum DomainReadMode
{
    LegacyInMemory = 0,
    PersistedProjection = 1
}

public sealed record DomainCutoverMode(bool ShadowWritesEnabled, DomainReadMode ReadMode);

public sealed record FundOperationsPersistenceOptions
{
    public Dictionary<FundOperationsDomain, DomainCutoverMode> DomainModes { get; init; } = new();

    public DomainCutoverMode GetMode(FundOperationsDomain domain)
        => DomainModes.TryGetValue(domain, out var mode)
            ? mode
            : new DomainCutoverMode(ShadowWritesEnabled: true, ReadMode: DomainReadMode.LegacyInMemory);
}

public sealed record ProjectionDiscrepancy(
    FundOperationsDomain Domain,
    string ProjectionName,
    string EntityKey,
    string Summary,
    DateTimeOffset DetectedAt);

/// <summary>Compares a domain projection with its source and reports detected discrepancies.</summary>
public interface IDomainProjectionReconciliationJob
{
    FundOperationsDomain Domain { get; }
    Task<IReadOnlyList<ProjectionDiscrepancy>> ReconcileAsync(CancellationToken ct = default);
}

/// <summary>Writes named shadow projection payloads for entities within a fund-operations domain.</summary>
public interface IShadowProjectionWriter
{
    FundOperationsDomain Domain { get; }
    Task WriteAsync(string projectionName, string entityKey, object payload, CancellationToken ct = default);
}
