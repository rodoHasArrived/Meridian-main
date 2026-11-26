using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Services;

/// <summary>Tenant-authorized immutable entity lifecycle history.</summary>
public interface IEntityLifecycleService
{
    Task<EntityLifecycleRevisionDto?> GetAsync(Guid entityId, long? revision = null, CancellationToken ct = default);
    Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(Guid entityId, CancellationToken ct = default);
    /// <param name="actor">Actor resolved by the authenticated host, never from the submitted JSON.</param>
    Task<EntityLifecycleRevisionDto> TransitionAsync(AppendEntityLifecycleRevisionRequestDto request, string actor, CancellationToken ct = default);
}

/// <summary>Local compatibility projection; implementations must verify the retained revision.</summary>
public interface IEntityLifecycleProjectionSink
{
    Task ApplyLifecycleProjectionAsync(EntityLifecycleRevisionDto revision, CancellationToken ct = default);
}
