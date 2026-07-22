using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Rebuild;

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

    /// <summary>
    /// Rebuilds the security's state as it was recorded at <paramref name="asOfUtc"/>
    /// (transaction time). Every event payload carries the full definition, so the as-of
    /// state is the payload of the last event recorded at or before the timestamp. Returns
    /// <c>null</c> when the stream has history but none of it existed at that time;
    /// securities with no event history fall back to the supplied current projection.
    /// </summary>
    public async Task<SecurityProjectionRecord?> RebuildAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        SecurityProjectionRecord? projectionWithAliases,
        CancellationToken ct = default)
    {
        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        if (events.Count == 0)
        {
            return projectionWithAliases;
        }

        SecurityEconomicDefinitionRecord? rebuilt = null;
        foreach (var @event in events.Where(e => e.EventTimestamp <= asOfUtc))
        {
            rebuilt = SecurityMasterMapping.FromEconomicPayload(@event.Payload);
        }

        return rebuilt is null
            ? null
            : SecurityEconomicDefinitionAdapter.ToProjection(rebuilt, projectionWithAliases?.Aliases);
    }

    /// <summary>
    /// Rebuilds only from retained event history and returns <c>null</c> when no event was
    /// recorded at or before <paramref name="asOfUtc"/>. Accounting as-of workflows use this
    /// strict variant so a projection-only current definition cannot masquerade as history.
    /// Aliases are retained only when they were both recorded and effective at the cutoff;
    /// their enabled state is preserved for downstream resolution policy.
    /// </summary>
    public async Task<SecurityProjectionRecord?> RebuildRecordedAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        SecurityProjectionRecord? projectionWithAliases,
        CancellationToken ct = default)
    {
        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        SecurityEconomicDefinitionRecord? rebuilt = null;
        foreach (var @event in events.Where(e => e.EventTimestamp <= asOfUtc))
        {
            rebuilt = SecurityMasterMapping.FromEconomicPayload(@event.Payload);
        }

        var aliasesAsOf = projectionWithAliases?.Aliases
            .Where(alias => alias.CreatedAt <= asOfUtc
                            && alias.ValidFrom <= asOfUtc
                            && (!alias.ValidTo.HasValue || alias.ValidTo.Value > asOfUtc))
            .ToArray();

        return rebuilt is null
            ? null
            : SecurityEconomicDefinitionAdapter.ToProjection(rebuilt, aliasesAsOf);
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
