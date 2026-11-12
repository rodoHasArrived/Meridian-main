using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

internal static class EntityLifecycleStoreRules
{
    internal static async Task ValidateSuccessorsAsync(EntityLifecycleRevisionDto revision,
        Func<Guid, Task<EntityLifecycleRevisionDto?>> resolve)
    {
        foreach (var target in revision.Timeline.Where(state => state.SuccessorEntityId.HasValue).Select(state => state.SuccessorEntityId!.Value).Distinct())
        {
            var visited = new HashSet<Guid> { revision.EntityId };
            var next = target;
            while (true)
            {
                if (!visited.Add(next)) throw new InvalidOperationException("Lifecycle successors cannot form a cycle.");
                var history = await resolve(next).ConfigureAwait(false);
                if (history is not null && history.TenantId != revision.TenantId)
                    throw new InvalidOperationException("Lifecycle successors must share the entity tenant.");
                var successor = history?.Timeline.LastOrDefault()?.SuccessorEntityId;
                if (!successor.HasValue) break;
                next = successor.Value;
            }
        }
    }
}
