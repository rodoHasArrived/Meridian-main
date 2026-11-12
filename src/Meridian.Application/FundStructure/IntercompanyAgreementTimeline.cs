using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed record IntercompanyAgreementTimelineResult(
    IntercompanyAgreementRevisionDto? Revision, IntercompanyAgreementStatusDto Status, DateOnly? TerminationCutoff);

/// <summary>Effective chronology takes precedence over approval order; approved history is never rewritten.</summary>
public static class IntercompanyAgreementTimeline
{
    public static IntercompanyAgreementTimelineResult Resolve(
        IEnumerable<IntercompanyAgreementRevisionDto> revisions, DateOnly asOf, DateTimeOffset? knownAtUtc = null)
    {
        var known = revisions.Where(r => !knownAtUtc.HasValue || r.ApprovedAtUtc <= knownAtUtc.Value).ToArray();
        var terms = known.Where(r => r.Action != IntercompanyAgreementActionDto.Terminate).ToArray();
        if (terms.Length == 0)
            return new(null, IntercompanyAgreementStatusDto.Unapproved, null);
        var cutoff = known.Where(r => r.Action == IntercompanyAgreementActionDto.Terminate)
            .Select(r => r.Terms.TerminationEffectiveDate).Where(d => d.HasValue).Min();
        var selected = terms.Where(r => r.Terms.EffectiveFrom <= asOf)
            .OrderByDescending(r => r.Terms.EffectiveFrom).ThenByDescending(r => r.AgreementVersion).FirstOrDefault();
        var status = cutoff.HasValue && asOf >= cutoff.Value ? IntercompanyAgreementStatusDto.Terminated
            : selected is null ? IntercompanyAgreementStatusDto.Scheduled
            : selected.Terms.EffectiveTo is { } end && asOf >= end ? IntercompanyAgreementStatusDto.Expired
            : IntercompanyAgreementStatusDto.Active;
        return new(selected, status, cutoff);
    }

    public static int? SemanticBase(IReadOnlyList<IntercompanyAgreementRevisionDto> revisions, IntercompanyAgreementDraftDto draft)
    {
        if (draft.Action == IntercompanyAgreementActionDto.Activate) return null;
        var date = draft.Action == IntercompanyAgreementActionDto.Terminate
            ? draft.TerminationEffectiveDate ?? throw new IntercompanyAgreementValidationException("Termination date is required.")
            : draft.EffectiveFrom;
        var initial = revisions.FirstOrDefault(r => r.Action == IntercompanyAgreementActionDto.Activate)
            ?? throw new IntercompanyAgreementValidationException("Agreement is not activated.");
        if (date < initial.Terms.EffectiveFrom)
            throw new IntercompanyAgreementValidationException("A change cannot precede initial commencement.");
        if (draft.Action == IntercompanyAgreementActionDto.Terminate && date > initial.Terms.EffectiveFrom)
            date = date.AddDays(-1);
        return Resolve(revisions, date).Revision?.AgreementVersion ?? initial.AgreementVersion;
    }
}
