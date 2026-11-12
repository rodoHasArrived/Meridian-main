using Meridian.Contracts.FundStructure;

namespace Meridian.Entities.FundStructure;

/// <summary>Pure agreement rules; agreement eligibility is independent of a consolidation perimeter.</summary>
public static class IntercompanyAgreementPolicy
{
    public static void ValidateTerms(IntercompanyAgreementDto agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        if (agreement.AgreementId == Guid.Empty || agreement.OrganizationId == Guid.Empty ||
            agreement.PartyOneEntityId == Guid.Empty || agreement.PartyTwoEntityId == Guid.Empty ||
            agreement.PartyOneEntityId == agreement.PartyTwoEntityId)
            throw new ArgumentException("An agreement requires an organization and two distinct legal entities.");
        if (!Enum.IsDefined(agreement.Kind) || !Enum.IsDefined(agreement.Status))
            throw new ArgumentException("Agreement kind or workflow status is invalid.");
        RequireText(agreement.FacilityReference, "facility reference");
        RequireText(agreement.PartyOneRole, "first party role");
        RequireText(agreement.PartyTwoRole, "second party role");
        RequireText(agreement.SettlementCadence, "settlement cadence");
        if (agreement.SettlementDueDays is < 0 or > 3650)
            throw new ArgumentException("Settlement due days must be between 0 and 3650 calendar days.");
        if (agreement.Currency.Length != 3 || !agreement.Currency.All(char.IsAsciiLetter) || agreement.Currency != agreement.Currency.ToUpperInvariant())
            throw new ArgumentException("An agreement currency must be an uppercase three-letter ISO code.");
        if (agreement.EffectiveFrom == default || agreement.EffectiveTo <= agreement.EffectiveFrom)
            throw new ArgumentException("An agreement requires a nonempty [EffectiveFrom, EffectiveTo) window.");
        if (agreement.SignedDocument is { } document)
        {
            RequireText(document.VaultId, "signed document vault");
            RequireText(document.DocumentId, "signed document identity");
            RequireText(document.ArtifactId, "signed document artifact");
            RequireText(document.Scope, "signed document scope");
            if (document.ContentHashSha256 is null || document.ContentHashSha256.Length != 64 || !document.ContentHashSha256.All(char.IsAsciiHexDigit))
                throw new ArgumentException("The signed document must pin a SHA-256 content hash.");
        }
        if (agreement.Kind == IntercompanyAgreementKindDto.Guarantee && agreement.PartyMappings is { Count: 0 }) return;
        if (agreement.PartyMappings is null || agreement.PartyMappings.Count != 2 ||
            agreement.PartyMappings.Any(x => x is null) ||
            agreement.PartyMappings.Select(x => x.EntityId).Distinct().Count() != 2)
            throw new ArgumentException("An agreement requires exactly one book/account mapping for each party.");
        foreach (var mapping in agreement.PartyMappings)
        {
            if (mapping.EntityId != agreement.PartyOneEntityId && mapping.EntityId != agreement.PartyTwoEntityId || mapping.LedgerBookId == Guid.Empty)
                throw new ArgumentException("Each mapping must identify an agreement party and a ledger book.");
            RequireText(mapping.ReceivableAccountNodeId, "receivable account node");
            RequireText(mapping.PayableAccountNodeId, "payable account node");
            if (mapping.ReceivableAccountNodeId == mapping.PayableAccountNodeId)
                throw new ArgumentException("Receivable and payable must use different chart nodes.");
        }
        if (agreement.PartyMappings.Select(x => x.LedgerBookId).Distinct().Count() != 2)
            throw new ArgumentException("The agreement parties must use distinct ledger books.");
    }

    public static IntercompanyAgreementStatusDto Transition(IntercompanyAgreementStatusDto status, IntercompanyAgreementActionDto action)
        => (status, action) switch
        {
            (IntercompanyAgreementStatusDto.Draft or IntercompanyAgreementStatusDto.Rejected, IntercompanyAgreementActionDto.Submit) => IntercompanyAgreementStatusDto.Submitted,
            (IntercompanyAgreementStatusDto.Submitted, IntercompanyAgreementActionDto.Approve) => IntercompanyAgreementStatusDto.Approved,
            (IntercompanyAgreementStatusDto.Submitted, IntercompanyAgreementActionDto.Reject) => IntercompanyAgreementStatusDto.Rejected,
            (IntercompanyAgreementStatusDto.Approved, IntercompanyAgreementActionDto.Terminate) => IntercompanyAgreementStatusDto.Terminated,
            (IntercompanyAgreementStatusDto.Approved, IntercompanyAgreementActionDto.Novate) => IntercompanyAgreementStatusDto.Novated,
            _ => throw new InvalidOperationException($"An agreement in {status} cannot perform {action}.")
        };

    public static void ValidateEntity(IntercompanyAgreementDto agreement, LegalEntitySummaryDto entity)
    {
        if (entity.EntityId != agreement.PartyOneEntityId && entity.EntityId != agreement.PartyTwoEntityId)
            throw new ArgumentException("Entity is not an agreement party.");
        if (!entity.IsActive || entity.LifecycleStatus != LegalEntityLifecycleStatusDto.Active)
            throw new InvalidOperationException("New agreement terms require active legal entities.");
        if (entity.EffectiveFrom > agreement.EffectiveFrom ||
            entity.EffectiveTo is { } end && (agreement.EffectiveTo is null || agreement.EffectiveTo > end))
            throw new InvalidOperationException("The agreement window must fit each party's legal existence window.");
    }

    public static string RequireText(string? value, string field) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim() : throw new ArgumentException($"Agreement {field} is required.");
}
