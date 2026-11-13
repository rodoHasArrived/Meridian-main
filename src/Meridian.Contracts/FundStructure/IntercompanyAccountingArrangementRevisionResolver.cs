namespace Meridian.Contracts.FundStructure;

/// <summary>One interpretation of approved, retired and superseded accounting mapping windows.</summary>
public static class IntercompanyAccountingArrangementRevisionResolver
{
    public static IntercompanyAccountingArrangementDto ResolveApprovedRevision(IntercompanyAccountingArrangementHistoryDto history,
        Guid revisionId, DateTimeOffset economicDate)
    {
        ArgumentNullException.ThrowIfNull(history);
        var events = history.Events.Where(item => item.AffectedRevision.RevisionId == revisionId).ToArray();
        var approved = events.FirstOrDefault(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve)?.AffectedRevision
            ?? throw new InvalidOperationException("The exact accounting arrangement revision has not been approved.");
        var retired = events.LastOrDefault(item => item.Action == IntercompanyAccountingArrangementActionDto.Retire)?.AffectedRevision.RetiredAt;
        var superseded = history.Events.Where(item => item.Action == IntercompanyAccountingArrangementActionDto.Approve
                && item.AffectedRevision.SupersedesRevisionId == revisionId)
            .Select(item => (DateTimeOffset?)item.AffectedRevision.EffectiveFrom).Min();
        if (economicDate < approved.EffectiveFrom || approved.EffectiveTo <= economicDate || retired <= economicDate || superseded <= economicDate)
            throw new InvalidOperationException("The economic date is outside the exact approved accounting arrangement window.");
        return approved;
    }
}
