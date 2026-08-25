namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Pure fold over a security's append-only corporate-action events: normalizes stored
/// EventType aliases via <see cref="CorporateActionTypeDescriptorCatalog"/>, follows
/// <c>SupersedesCorpActId</c> chains to their tips, and derives the effective lifecycle
/// state at a given as-of time. Consumers that apply economic effects (price adjustment,
/// ledger posting) should act on the projected effective actions, never the raw rows, so
/// amendments collapse to their latest terms and cancellations drop out.
/// </summary>
public static class CorporateActionEffectiveStateProjector
{
    public static IReadOnlyList<CorporateActionEffectiveState> Project(
        IReadOnlyList<CorporateActionDto> actions,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(actions);
        actions = CorporateActionRevisionMetadata.FilterKnown(actions, asOf);
        if (actions.Count == 0)
        {
            return [];
        }
        var byId = new Dictionary<Guid, CorporateActionDto>(actions.Count);
        foreach (var action in actions)
        {
            // Duplicate ids should not occur in an append-only store; last one wins on read.
            byId[action.CorpActId] = action;
        }

        var supersededIds = actions
            .Where(static action => action.SupersedesCorpActId.HasValue)
            .Select(static action => action.SupersedesCorpActId!.Value)
            .ToHashSet();

        var results = new List<CorporateActionEffectiveState>(actions.Count);
        foreach (var action in actions)
        {
            if (supersededIds.Contains(action.CorpActId))
            {
                continue; // Not a chain tip; it appears in a tip's timeline instead.
            }

            var timeline = BuildTimeline(action, byId);
            var effective = WithNormalizedEventType(action);
            var lifecycleState = CorporateActionLifecycleStates.Resolve(
                effective.LifecycleState, effective.ExDate, effective.PayDate, asOf);

            results.Add(new CorporateActionEffectiveState(
                Effective: effective,
                LifecycleState: lifecycleState,
                IsCancelled: lifecycleState == CorporateActionLifecycleStates.Cancelled,
                Timeline: timeline));
        }

        return results
            .OrderBy(static state => state.Effective.ExDate)
            .ThenBy(static state => state.Effective.CorpActId)
            .ToArray();
    }

    /// <summary>Effective, non-cancelled actions only — the list economic consumers act on.</summary>
    public static IReadOnlyList<CorporateActionDto> ProjectEffectiveActions(
        IReadOnlyList<CorporateActionDto> actions,
        DateTimeOffset asOf)
        => Project(actions, asOf)
            .Where(static state => !state.IsCancelled)
            .Select(static state => state.Effective)
            .ToArray();

    private static IReadOnlyList<CorporateActionDto> BuildTimeline(
        CorporateActionDto tip,
        IReadOnlyDictionary<Guid, CorporateActionDto> byId)
    {
        var timeline = new List<CorporateActionDto> { tip };
        var visited = new HashSet<Guid> { tip.CorpActId };
        var current = tip;

        // Walk back to the original announcement; tolerate orphan references (the
        // superseded event was not loaded) and defensive-guard cycles.
        while (current.SupersedesCorpActId is { } supersededId
               && visited.Add(supersededId)
               && byId.TryGetValue(supersededId, out var superseded))
        {
            timeline.Add(superseded);
            current = superseded;
        }

        timeline.Reverse();
        return timeline;
    }

    private static CorporateActionDto WithNormalizedEventType(CorporateActionDto action)
    {
        var canonical = CorporateActionEventTypes.Normalize(action.EventType);
        return string.Equals(canonical, action.EventType, StringComparison.Ordinal)
            ? action
            : action with { EventType = canonical };
    }
}

/// <summary>
/// One effective corporate action: the chain tip with a canonical EventType, its resolved
/// lifecycle state at the projection's as-of time, and the full amendment timeline
/// (original announcement first, tip last).
/// </summary>
public sealed record CorporateActionEffectiveState(
    CorporateActionDto Effective,
    string LifecycleState,
    bool IsCancelled,
    IReadOnlyList<CorporateActionDto> Timeline);
