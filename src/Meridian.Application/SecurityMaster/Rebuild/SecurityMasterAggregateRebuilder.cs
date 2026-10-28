using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Reconstructs security economic definitions and corporate actions from retained events.
/// </summary>
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
            rebuilt = SecurityMasterMapping.FromEventEnvelope(@event);
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
    /// Rebuilds from the last retained stream version whose stored event timestamp is at or before
    /// <paramref name="asOfUtc"/>. Every payload carries the full definition. Returns <c>null</c>
    /// when the stream has no timestamp-eligible event; projection-only securities fall back to
    /// the supplied current projection. Event timestamps may precede lock waits and commit, so this
    /// selects eligible rows read now and does not reconstruct historical transaction visibility.
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
            rebuilt = SecurityMasterMapping.FromEventEnvelope(@event);
        }

        return rebuilt is null
            ? null
            : SecurityEconomicDefinitionAdapter.ToProjection(rebuilt, projectionWithAliases?.Aliases);
    }

    /// <summary>
    /// Rebuilds only from retained events with stored timestamps at or before <paramref name="asOfUtc"/>,
    /// returning <c>null</c> when no eligible event is read. Projection-only current definitions
    /// are never substituted. Aliases must satisfy their stored recording and valid-time cutoffs;
    /// their enabled state is preserved for downstream resolution policy. Callers supply the
    /// aliases as recorded at the cutoff (<see cref="ISecurityMasterStore.GetAliasesRecordedAsOfAsync"/>)
    /// so current alias rows cannot replace the retained revisions. These timestamps are not commit
    /// receipts: later commits can change repeated results for the same cutoff, and the event/alias
    /// reads do not share a single historical transaction snapshot.
    /// </summary>
    public async Task<SecurityProjectionRecord?> RebuildRecordedAsOfAsync(
        Guid securityId,
        DateTimeOffset asOfUtc,
        SecurityProjectionRecord? projectionWithAliases,
        CancellationToken ct = default,
        IReadOnlyList<SecurityAliasDto>? aliasesRecordedAtCutoff = null)
    {
        var events = await _eventStore.LoadAsync(securityId, ct).ConfigureAwait(false);
        SecurityEconomicDefinitionRecord? rebuilt = null;
        foreach (var @event in events.Where(e => e.EventTimestamp <= asOfUtc))
        {
            rebuilt = SecurityMasterMapping.FromEventEnvelope(@event);
        }

        var aliasesAsOf = (aliasesRecordedAtCutoff ?? projectionWithAliases?.Aliases)
            ?.Where(alias => alias.CreatedAt <= asOfUtc
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
