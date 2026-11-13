using Meridian.Storage.FundStructure;
using static Meridian.Application.FundStructure.FundStructureProjector;

namespace Meridian.Application.FundStructure;

internal static class EntityLifecycleSnapshotProjection
{
    internal static async Task<StructureSnapshot> ApplyAsync(StructureSnapshot snapshot, IEntityLifecycleStore? store,
        string? tenant, DateTimeOffset asOf, CancellationToken ct)
    {
        if (store is null || string.IsNullOrWhiteSpace(tenant) || tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return snapshot;
        var entities = snapshot.Entities.ToArray();
        for (var index = 0; index < entities.Length; index++)
        {
            var history = await store.GetLatestAsync(tenant, entities[index].EntityId, ct).ConfigureAwait(false);
            var state = history?.Timeline.SingleOrDefault(item => item.EffectiveFrom <= asOf
                && (!item.EffectiveTo.HasValue || asOf < item.EffectiveTo.Value));
            if (state is not null)
                entities[index] = entities[index] with { LifecycleStatus = state.Status };
        }
        return snapshot with { Entities = entities };
    }
}
