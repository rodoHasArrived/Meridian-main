using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>Authorizes history against the scoped graph; storage owns immutable concurrency.</summary>
public sealed class EntityLifecycleService(
    IFundStructureService structure,
    IEntityLifecycleStore store,
    IFundScopeTenantAccessor tenantAccessor) : IEntityLifecycleService
{
    private string Tenant() => !string.IsNullOrWhiteSpace(tenantAccessor.ResolveCallerTenant())
        && !tenantAccessor.ResolveCallerTenant()!.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? tenantAccessor.ResolveCallerTenant()!.Trim().ToLowerInvariant()
        : throw new FundStructureTenantScopeException("An explicit tenant is required for entity lifecycle history.");

    private async Task<OrganizationStructureGraphDto> RequireEntityAsync(Guid entityId, CancellationToken ct)
    {
        var graph = await structure.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: false), ct).ConfigureAwait(false);
        if (!graph.Entities.Any(entity => entity.EntityId == entityId))
            throw new InvalidOperationException("The legal entity was not found in the calling tenant scope.");
        return graph;
    }

    public async Task<EntityLifecycleRevisionDto?> GetAsync(Guid entityId, long? revision = null, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await RequireEntityAsync(entityId, ct).ConfigureAwait(false);
        return revision.HasValue
            ? await store.GetRevisionAsync(tenant, entityId, revision.Value, ct).ConfigureAwait(false)
            : await store.GetLatestAsync(tenant, entityId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityLifecycleRevisionDto>> GetHistoryAsync(Guid entityId, CancellationToken ct = default)
    {
        var tenant = Tenant();
        await RequireEntityAsync(entityId, ct).ConfigureAwait(false);
        return await store.GetHistoryAsync(tenant, entityId, ct).ConfigureAwait(false);
    }

    public async Task<EntityLifecycleRevisionDto> TransitionAsync(AppendEntityLifecycleRevisionRequestDto request, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = Tenant();
        if (string.IsNullOrWhiteSpace(actor)) throw new InvalidOperationException("An authenticated lifecycle actor is required.");
        var graph = await RequireEntityAsync(request.EntityId, ct).ConfigureAwait(false);
        EntityLifecyclePolicy.ValidateTimeline(request.EntityId, request.Timeline);
        foreach (var successorId in request.Timeline.Where(state => state.SuccessorEntityId.HasValue).Select(state => state.SuccessorEntityId!.Value).Distinct())
        {
            if (!graph.Entities.Any(entity => entity.EntityId == successorId))
                throw new InvalidOperationException("A lifecycle successor must be visible in the same tenant scope.");
            var visited = new HashSet<Guid> { request.EntityId };
            var next = successorId;
            while (true)
            {
                if (!visited.Add(next)) throw new InvalidOperationException("Lifecycle successors cannot form a cycle.");
                var history = await store.GetLatestAsync(tenant, next, ct).ConfigureAwait(false);
                var successor = history?.Timeline.LastOrDefault()?.SuccessorEntityId;
                if (!successor.HasValue) break;
                next = successor.Value;
            }
        }
        var previous = await store.GetLatestAsync(tenant, request.EntityId, ct).ConfigureAwait(false);
        var revision = EntityLifecyclePolicy.Seal(new EntityLifecycleRevisionDto(tenant, request.EntityId,
            request.ExpectedRevision + 1, DateTimeOffset.UtcNow, actor.Trim(), request.Reason, request.EvidenceReference,
            previous?.ContentHash ?? string.Empty, string.Empty, request.Timeline));
        var retained = await store.AppendAsync(revision, request.ExpectedRevision, ct).ConfigureAwait(false);
        if (structure is IEntityLifecycleProjectionSink projection)
            await projection.ApplyLifecycleProjectionAsync(retained, ct).ConfigureAwait(false);
        return retained;
    }
}
