using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterAggregateRebuilder
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterSnapshotStore _snapshotStore;

    /// <summary>
    /// Returns all corporate action events for a security in ascending ex-date order,
    /// folding the separate CorpActEvent stream into the aggregate view for a security.
    /// </summary>
    public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(
        Guid securityId,
        CancellationToken ct = default)
        => _eventStore.LoadCorporateActionsAsync(securityId, ct);

    public SecurityMasterAggregateRebuilder(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
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
