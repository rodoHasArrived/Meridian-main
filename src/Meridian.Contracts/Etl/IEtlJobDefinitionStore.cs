namespace Meridian.Contracts.Etl;

public interface IEtlJobDefinitionStore
{
    Task SaveAsync(EtlJobDefinition definition, CancellationToken ct = default);
    Task<EtlJobDefinition?> GetAsync(string jobId, CancellationToken ct = default);
    Task<IReadOnlyList<EtlJobDefinition>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string jobId, CancellationToken ct = default);
}
