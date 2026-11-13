using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Immutable tenant-scoped entity status history and authority leases.</summary>
public interface IEntityLifecycleStore
{
    Task<EntityLifecycleRevisionDto?> GetLatestAsync(string tenantId, Guid entityId, CancellationToken ct = default);
    Task<EntityLifecycleRevisionDto?> GetRevisionAsync(string tenantId, Guid entityId, long revision, CancellationToken ct = default);
    Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(string tenantId, Guid entityId, CancellationToken ct = default);
    Task<EntityLifecycleRevisionDto> AppendAsync(EntityLifecycleRevisionDto revision, long expectedRevision, CancellationToken ct = default);
    /// <summary>Retain the lease until the accounting transaction commits or rolls back.</summary>
    Task<IAsyncDisposable> AcquireReadLeaseAsync(string tenantId, IReadOnlyList<Guid> entityIds, CancellationToken ct = default);
}
