using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Text;

namespace Meridian.Entities.FundStructure;

/// <summary>Accounting configuration rules; canonical legal approval and consolidation scope remain separate authorities.</summary>
public static class IntercompanyAccountingArrangementPolicy
{
    public static void Validate(IntercompanyAccountingArrangementDto arrangement, IntercompanyAgreementRevisionDto legal)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(legal);
        if (arrangement.ArrangementId == Guid.Empty || arrangement.OrganizationId == Guid.Empty || arrangement.RevisionId == Guid.Empty
            || arrangement.CanonicalAgreementId != legal.AgreementId || arrangement.CanonicalAgreementVersion != legal.AgreementVersion
            || !string.Equals(arrangement.CanonicalPayloadHash, legal.PayloadHash, StringComparison.Ordinal)
            || legal.Action == IntercompanyAgreementActionDto.Terminate)
            throw new ArgumentException("The accounting arrangement must pin one exact approved native legal revision.");
        RequireArrangementText(arrangement.ArrangementReference, "reference");
        if (!Enum.IsDefined(arrangement.TemplateKind) || !Enum.IsDefined(arrangement.Status))
            throw new ArgumentException("Accounting template or workflow status is invalid.");
        if (arrangement.EffectiveFrom == default || arrangement.EffectiveTo <= arrangement.EffectiveFrom)
            throw new ArgumentException("Accounting mapping validity requires a nonempty [EffectiveFrom, EffectiveTo) window.");
        var start = new DateTimeOffset(legal.Terms.EffectiveFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        DateTimeOffset? end = legal.Terms.EffectiveTo is { } until
            ? new(until.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : null;
        if (arrangement.EffectiveFrom < start || end is { } cutoff && (arrangement.EffectiveTo is null || arrangement.EffectiveTo > cutoff))
            throw new ArgumentException("Accounting mapping validity must fit the pinned legal revision's window.");
        var compatible = arrangement.TemplateKind switch
        {
            IntercompanyAccountingTemplateKindDto.Loan => legal.Terms.AgreementType == IntercompanyAgreementTypeDto.Loan,
            IntercompanyAccountingTemplateKindDto.ManagementFee => legal.Terms.AgreementType == IntercompanyAgreementTypeDto.ManagementFee,
            IntercompanyAccountingTemplateKindDto.CostRecharge or IntercompanyAccountingTemplateKindDto.ExpenseShare => legal.Terms.AgreementType == IntercompanyAgreementTypeDto.CostShare,
            IntercompanyAccountingTemplateKindDto.CashSweep => legal.Terms.AgreementType == IntercompanyAgreementTypeDto.CashSweep,
            IntercompanyAccountingTemplateKindDto.Guarantee => legal.Terms.AgreementType == IntercompanyAgreementTypeDto.Guarantee,
            IntercompanyAccountingTemplateKindDto.Manual => true,
            _ => false
        };
        if (!compatible)
            throw new ArgumentException("The accounting template is incompatible with the canonical legal agreement type.");
        if (arrangement.TemplateKind is IntercompanyAccountingTemplateKindDto.Guarantee or IntercompanyAccountingTemplateKindDto.Manual
            && arrangement.PartyMappings is { Count: 0 })
            return;
        if (arrangement.PartyMappings is null || arrangement.PartyMappings.Count != 2
            || arrangement.PartyMappings.Any(item => item is null)
            || arrangement.PartyMappings.Select(item => item.EntityId).Distinct().Count() != 2
            || arrangement.PartyMappings.Select(item => item.LedgerBookId).Distinct().Count() != 2)
            throw new ArgumentException("Accounting arrangements require one distinct book/account mapping per legal party.");
        foreach (var mapping in arrangement.PartyMappings)
        {
            if ((mapping.EntityId != legal.Terms.PartyOneId && mapping.EntityId != legal.Terms.PartyTwoId) || mapping.LedgerBookId == Guid.Empty)
                throw new ArgumentException("Accounting mappings must belong to the pinned legal parties.");
            RequireArrangementText(mapping.ReceivableAccountNodeId, "receivable node");
            RequireArrangementText(mapping.PayableAccountNodeId, "payable node");
            if (mapping.ReceivableAccountNodeId == mapping.PayableAccountNodeId)
                throw new ArgumentException("Receivable and payable require different chart nodes.");
        }
    }
    public static IntercompanyAccountingArrangementStatusDto Transition(IntercompanyAccountingArrangementStatusDto status,
        IntercompanyAccountingArrangementActionDto action) => (status, action) switch
        {
            (IntercompanyAccountingArrangementStatusDto.Draft or IntercompanyAccountingArrangementStatusDto.Rejected, IntercompanyAccountingArrangementActionDto.Submit) => IntercompanyAccountingArrangementStatusDto.Submitted,
            (IntercompanyAccountingArrangementStatusDto.Submitted, IntercompanyAccountingArrangementActionDto.Approve) => IntercompanyAccountingArrangementStatusDto.Approved,
            (IntercompanyAccountingArrangementStatusDto.Submitted, IntercompanyAccountingArrangementActionDto.Reject) => IntercompanyAccountingArrangementStatusDto.Rejected,
            (IntercompanyAccountingArrangementStatusDto.Approved, IntercompanyAccountingArrangementActionDto.Retire) => IntercompanyAccountingArrangementStatusDto.Retired,
            _ => throw new InvalidOperationException($"An accounting arrangement in {status} cannot perform {action}.")
        };
    public static string RequireArrangementText(string? value, string field) => TextPrimitives.NormalizeOptional(value)
        ?? throw new ArgumentException($"Accounting arrangement {field} is required.");
}
