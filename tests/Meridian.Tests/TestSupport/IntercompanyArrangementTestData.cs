using Meridian.Contracts.FundStructure;
using Moq;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;

namespace Meridian.Tests.TestSupport;

internal static class IntercompanyArrangementTestData
{
    public static ResolvedIntercompanyAccountingArrangementDto Create(Guid arrangementId, Guid organizationId,
        IntercompanyAccountingTemplateKindDto kind, Guid partyOne, Guid partyTwo, string currency,
        IReadOnlyList<IntercompanyPartyMappingDto> mappings, Guid revisionId, DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null, long version = 3, string reference = "reviewed-accounting-arrangement",
        Guid? canonicalId = null, int canonicalVersion = 1)
    {
        var legalId = canonicalId ?? Guid.Parse("cc111111-1111-1111-1111-111111111111");
        var legal = new IntercompanyAgreementRevisionDto(legalId, canonicalVersion, null, null,
            IntercompanyAgreementActionDto.Activate,
            new(reference, reference, partyOne, "Party one", partyTwo, "Party two", kind switch
            {
                IntercompanyAccountingTemplateKindDto.Loan or IntercompanyAccountingTemplateKindDto.LoanInterest => IntercompanyAgreementTypeDto.Loan,
                IntercompanyAccountingTemplateKindDto.ManagementFee => IntercompanyAgreementTypeDto.ManagementFee,
                IntercompanyAccountingTemplateKindDto.CashSweep => IntercompanyAgreementTypeDto.CashSweep,
                IntercompanyAccountingTemplateKindDto.Guarantee => IntercompanyAgreementTypeDto.Guarantee,
                _ => IntercompanyAgreementTypeDto.CostShare
            }, currency, DateOnly.FromDateTime(effectiveFrom.UtcDateTime),
                effectiveTo is { } end ? DateOnly.FromDateTime(end.UtcDateTime) : null,
                new(ScheduleDescription: "Monthly", SettlementDays: 30)),
            Guid.NewGuid(), new string('a', 64), ["legal-preparer"], "legal-submitter", "legal-reviewer",
            effectiveFrom.AddDays(-1), "Approved synthetic legal terms");
        var arrangement = new IntercompanyAccountingArrangementDto(arrangementId, organizationId, reference, kind,
            legal.AgreementId, legal.AgreementVersion, legal.PayloadHash, effectiveFrom, effectiveTo, revisionId, 1,
            version, IntercompanyAccountingArrangementStatusDto.Approved, mappings,
            "mapping-preparer", effectiveFrom, "mapping-reviewer", effectiveFrom,
            "mapping-reviewer", effectiveFrom);
        return new(arrangement, legal);
    }

    public static Mock<IIntercompanyAccountingArrangementLegalAuthority> LegalAuthority(ResolvedIntercompanyAccountingArrangementDto resolved)
    {
        var authority = new Mock<IIntercompanyAccountingArrangementLegalAuthority>();
        authority.Setup(x => x.GetRevisionAsync(It.IsAny<string>(), resolved.Arrangement.CanonicalReference, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved.LegalRevision);
        authority.Setup(x => x.GetEffectiveRevisionAsync(It.IsAny<string>(), resolved.Arrangement.CanonicalReference,
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolved.LegalRevision);
        return authority;
    }
    public static IntercompanyAccountingArrangementService Service(IIntercompanyAccountingArrangementStore store,
        ResolvedIntercompanyAccountingArrangementDto resolved) => new(store,
            Mock.Of<IIntercompanyAccountingArrangementScopeAuthority>(), LegalAuthority(resolved).Object);

}
