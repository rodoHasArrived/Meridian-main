using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterAggregateRebuilder
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterSnapshotStore _snapshotStore;

    public SecurityMasterAggregateRebuilder(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
    }

    public async Task<SecurityEconomicDefinitionRecord?> RebuildEconomicDefinitionAsync(
        Guid securityId,
        SecurityProjectionRecord? projectionWithAliases,
        CancellationToken ct = default)
    {
        var snapshot = await _snapshotStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        SecurityEconomicDefinitionRecord? rebuilt = snapshot is null
            ? null
            : SecurityMasterMapping.FromEconomicPayload(snapshot.Payload);

        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        foreach (var @event in events.Where(e => snapshot is null || e.StreamVersion > snapshot.Version))
        {
            rebuilt = SecurityMasterMapping.FromEconomicPayload(@event.Payload);
        }

        if (rebuilt is null)
        {
            return projectionWithAliases is null
                ? null
                : SecurityEconomicDefinitionAdapter.ToEconomicRecord(projectionWithAliases);
        }

        return rebuilt;
    }

    public async Task<SecurityProjectionRecord?> RebuildAsync(
        Guid securityId,
        SecurityProjectionRecord? projectionWithAliases,
        CancellationToken ct = default)
    {
        var rebuilt = await RebuildEconomicDefinitionAsync(securityId, projectionWithAliases, ct).ConfigureAwait(false);
        if (rebuilt is null)
        {
            return null;
        }

        return SecurityEconomicDefinitionAdapter.ToProjection(
            rebuilt,
            projectionWithAliases?.Aliases);
    }
}
