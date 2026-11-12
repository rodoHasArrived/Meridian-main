using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Application.FundStructure;

public static class IntercompanyAgreementRules
{
    public static IntercompanyAgreementDraftDto Normalize(IntercompanyAgreementDraftDto draft) => draft with
    {
        Code = draft.Code?.Trim() ?? string.Empty,
        Name = draft.Name?.Trim() ?? string.Empty,
        Currency = draft.Currency?.Trim().ToUpperInvariant() ?? string.Empty,
        PartyOneRole = draft.PartyOneRole?.Trim() ?? string.Empty,
        PartyTwoRole = draft.PartyTwoRole?.Trim() ?? string.Empty,
        Purpose = NormalizeOptional(draft.Purpose),
        Terms = draft.Terms is null ? null : draft.Terms with
        {
            RateDescription = NormalizeOptional(draft.Terms.RateDescription),
            ScheduleDescription = NormalizeOptional(draft.Terms.ScheduleDescription),
            AllocationDescription = NormalizeOptional(draft.Terms.AllocationDescription),
            Notes = NormalizeOptional(draft.Terms.Notes)
        },
        Evidence = draft.Evidence?.Select(e => e is null ? throw new IntercompanyAgreementValidationException("Evidence reference is required.")
            : e with { Reference = e.Reference?.Trim() ?? string.Empty }).ToArray() ?? []
    };

    public static void ValidateShape(IntercompanyAgreementDraftDto draft)
    {
        if (draft.PartyOneId == Guid.Empty || draft.PartyTwoId == Guid.Empty || draft.PartyOneId == draft.PartyTwoId)
            Fail("Two different legal entities are required.");
        if (!Enum.IsDefined(draft.Action) || !Enum.IsDefined(draft.AgreementType))
            Fail("Agreement type or action is invalid.");
        if (draft.Terms?.SettlementDays < 0)
            Fail("Settlement days cannot be negative.");
        if (draft.Action != IntercompanyAgreementActionDto.Terminate && draft.EffectiveTo is { } end && end <= draft.EffectiveFrom)
            Fail("EffectiveTo must be later than EffectiveFrom.");
        if (draft.Action == IntercompanyAgreementActionDto.Terminate && draft.TerminationEffectiveDate is null)
            Fail("Termination date is required.");
    }

    public static void ValidateComplete(IntercompanyAgreementDraftDto draft)
    {
        ValidateShape(draft);
        if (string.IsNullOrWhiteSpace(draft.Code) || string.IsNullOrWhiteSpace(draft.Name))
            Fail("Code and name are required.");
        if (draft.Currency.Length != 3 || draft.Currency.Any(c => c is < 'A' or > 'Z'))
            Fail("A three-letter currency is required.");
        var expected = draft.AgreementType switch
        {
            IntercompanyAgreementTypeDto.Loan => ("Lender", "Borrower"),
            IntercompanyAgreementTypeDto.ManagementFee => ("Service provider", "Recipient"),
            IntercompanyAgreementTypeDto.CostShare => ("Participant", "Participant"),
            IntercompanyAgreementTypeDto.CashSweep => ("Sweep source", "Destination"),
            IntercompanyAgreementTypeDto.Guarantee => ("Guarantor", "Supported entity"),
            _ => (draft.PartyOneRole, draft.PartyTwoRole)
        };
        var rolesMatch = (RoleEquals(draft.PartyOneRole, expected.Item1) && RoleEquals(draft.PartyTwoRole, expected.Item2))
            || (RoleEquals(draft.PartyOneRole, expected.Item2) && RoleEquals(draft.PartyTwoRole, expected.Item1));
        if (string.IsNullOrWhiteSpace(draft.PartyOneRole) || string.IsNullOrWhiteSpace(draft.PartyTwoRole) || !rolesMatch)
            Fail("Party roles are incompatible with the agreement type.");
        if (draft.Action == IntercompanyAgreementActionDto.Terminate)
            return;
        var t = draft.Terms;
        if (t is null || (t.FixedAnnualRate is null && t.SettlementDays is null
            && t.RateDescription is null && t.ScheduleDescription is null && t.AllocationDescription is null && t.Notes is null))
            Fail("A terms explanation or populated terms is required.");
        if (draft.AgreementType == IntercompanyAgreementTypeDto.CostShare && string.IsNullOrWhiteSpace(t?.AllocationDescription))
            Fail("Cost sharing requires an allocation description.");
        if (draft.AgreementType == IntercompanyAgreementTypeDto.Other && string.IsNullOrWhiteSpace(draft.Purpose))
            Fail("Other agreements require a purpose description.");
    }

    public static void ValidateImmutableParties(IntercompanyAgreementDraftDto initial, IntercompanyAgreementDraftDto draft)
    {
        if (initial.PartyOneId != draft.PartyOneId || initial.PartyTwoId != draft.PartyTwoId
            || !RoleEquals(initial.PartyOneRole, draft.PartyOneRole) || !RoleEquals(initial.PartyTwoRole, draft.PartyTwoRole)
            || initial.AgreementType != draft.AgreementType || initial.Currency != draft.Currency)
            Fail("Approved parties, roles, type and currency cannot change; create a new agreement.");
    }

    public static void ValidateExistence(IntercompanyAgreementDraftDto draft, IReadOnlyList<IntercompanyAgreementRevisionDto> revisions,
        LegalEntitySummaryDto first, LegalEntitySummaryDto second)
    {
        if (draft.Action == IntercompanyAgreementActionDto.Terminate)
        {
            var cutoff = draft.TerminationEffectiveDate!.Value;
            if (revisions.Any(r => r.Action == IntercompanyAgreementActionDto.Terminate))
                Fail("Termination is final and cannot be replaced.");
            var terms = revisions.Where(r => r.Action != IntercompanyAgreementActionDto.Terminate)
                .GroupBy(r => r.Terms.EffectiveFrom).Select(g => g.MaxBy(r => r.AgreementVersion)!)
                .OrderBy(r => r.Terms.EffectiveFrom).ToArray();
            for (var i = 0; i < terms.Length; i++)
            {
                var start = terms[i].Terms.EffectiveFrom;
                if (start >= cutoff)
                    continue;
                var end = terms[i].Terms.EffectiveTo;
                if (!end.HasValue || cutoff < end)
                    end = cutoff;
                if (i + 1 < terms.Length && terms[i + 1].Terms.EffectiveFrom < end)
                    end = terms[i + 1].Terms.EffectiveFrom;
                Contains(first, start, end);
                Contains(second, start, end);
            }
            return;
        }
        var termination = revisions.Where(r => r.Action == IntercompanyAgreementActionDto.Terminate)
            .Select(r => r.Terms.TerminationEffectiveDate).Where(d => d.HasValue).Min();
        if (termination.HasValue && draft.EffectiveFrom >= termination.Value)
            Fail("Amendment cannot reactivate a terminated agreement.");
        var effectiveEnd = draft.EffectiveTo;
        if (termination.HasValue && (!effectiveEnd.HasValue || termination < effectiveEnd))
            effectiveEnd = termination;
        Contains(first, draft.EffectiveFrom, effectiveEnd);
        Contains(second, draft.EffectiveFrom, effectiveEnd);
    }

    private static void Contains(LegalEntitySummaryDto entity, DateOnly from, DateOnly? to)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        DateTimeOffset? end = to.HasValue ? new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
        if (start < entity.EffectiveFrom || (entity.EffectiveTo.HasValue && (!end.HasValue || end > entity.EffectiveTo)))
            Fail("Agreement dates exceed a party's retained existence interval.");
    }

    public static string ReviewedHash(IntercompanyAgreementReviewPayloadDto payload) => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(payload, FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto));
    public static string CommandHash(IntercompanyAgreementCommandDto command) => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(command, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto));
    public static bool SameEvidence(IReadOnlyList<IntercompanyAgreementEvidenceDto> first, IReadOnlyList<IntercompanyAgreementEvidenceDto> second) =>
        first.Count == second.Count && first.OrderBy(e => e.Reference, StringComparer.Ordinal).SequenceEqual(second.OrderBy(e => e.Reference, StringComparer.Ordinal));
    public static bool SameActor(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static bool RoleEquals(string a, string b) => string.Equals(a.Replace(" ", string.Empty), b.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
    private static void Fail(string message) => throw new IntercompanyAgreementValidationException(message);
}
