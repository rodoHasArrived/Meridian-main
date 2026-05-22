namespace Meridian.Application.FundOperationsPersistence;

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
            : new DomainCutoverMode(ShadowWritesEnabled: true, DomainReadMode.LegacyInMemory);
}

public sealed record ProjectionDiscrepancy(
    FundOperationsDomain Domain,
    string ProjectionName,
    string EntityKey,
    string Summary,
    DateTimeOffset DetectedAt);

public interface IDomainProjectionReconciliationJob
{
    FundOperationsDomain Domain { get; }
    Task<IReadOnlyList<ProjectionDiscrepancy>> ReconcileAsync(CancellationToken ct = default);
}

public interface IShadowProjectionWriter
{
    FundOperationsDomain Domain { get; }
    Task WriteAsync(string projectionName, string entityKey, object payload, CancellationToken ct = default);
}
