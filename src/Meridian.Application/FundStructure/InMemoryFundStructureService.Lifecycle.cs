using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed partial class InMemoryFundStructureService
{
    private readonly Dictionary<Guid, long> _projectedLifecycleRevisions = new();
    public async Task ApplyLifecycleProjectionAsync(EntityLifecycleRevisionDto revision, CancellationToken ct = default)
    {
        var tenant = _lifecycleTenantAccessor?.ResolveCallerTenant();
        if (_entityLifecycleStore is null || !string.Equals(tenant?.Trim(), revision.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new FundStructureTenantScopeException("The lifecycle projection requires the retained tenant-scoped history store.");
        var retained = await _entityLifecycleStore.GetLatestAsync(revision.TenantId, revision.EntityId, ct).ConfigureAwait(false);
        if (retained?.ContentHash != revision.ContentHash)
            throw new InvalidOperationException("A lifecycle projection must match the latest retained revision.");
        var now = DateTimeOffset.UtcNow;
        var state = retained.Timeline.SingleOrDefault(item => item.EffectiveFrom <= now && (!item.EffectiveTo.HasValue || now < item.EffectiveTo.Value));
        if (state is null) return;
        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            if (_projectedLifecycleRevisions.TryGetValue(revision.EntityId, out var projected) && projected > revision.Revision)
                return;
            if (!_entities.TryGetValue(revision.EntityId, out var entity))
                throw new InvalidOperationException("The lifecycle projection entity was not found.");
            _entities[revision.EntityId] = entity with { LifecycleStatus = state.Status };
            _projectedLifecycleRevisions[revision.EntityId] = revision.Revision;
            snapshot = CaptureSnapshotLocked();
        }
        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
    }
}
