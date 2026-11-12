namespace Meridian.Contracts.FundStructure;

/// <summary>One interpretation of approved, terminated, novated and superseded term windows.</summary>
public static class IntercompanyAgreementRevisionResolver
{
    public static IntercompanyAgreementDto ResolveApprovedRevision(IntercompanyAgreementHistoryDto history,
        Guid revisionId, DateTimeOffset economicDate)
    {
        ArgumentNullException.ThrowIfNull(history);
        var revisions = history.Events.Where(x => x.AffectedRevision.RevisionId == revisionId).ToArray();
        var approved = revisions.FirstOrDefault(x => x.Action == IntercompanyAgreementActionDto.Approve)?.AffectedRevision
            ?? throw new InvalidOperationException("The exact agreement revision has not been approved.");
        var terminal = revisions.LastOrDefault(x => x.Action is IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate)
            ?.AffectedRevision.TerminatedAt;
        var supersededAt = history.Events.Where(x => x.Action == IntercompanyAgreementActionDto.Approve &&
                x.AffectedRevision.SupersedesRevisionId == revisionId)
            .Select(x => (DateTimeOffset?)x.AffectedRevision.EffectiveFrom).Min();
        if (economicDate < approved.EffectiveFrom || approved.EffectiveTo <= economicDate || terminal <= economicDate || supersededAt <= economicDate)
            throw new InvalidOperationException("The economic date is outside the exact approved agreement revision window.");
        return approved;
    }
}
