using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

public interface IFundStructurePolicyService
{
    void EnsureSingleOperatingParent(CreateInvestmentPortfolioRequest request);
    void ValidateOwnershipLink(
        OwnershipLinkDto candidate,
        FundStructureNodeKindDto parentKind,
        FundStructureNodeKindDto childKind,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds);

    void ValidateOwnershipUpdate(
        OwnershipLinkDto existingLink,
        OwnershipLinkDto updatedLink,
        FundStructureNodeKindDto parentKind,
        FundStructureNodeKindDto childKind,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds);

    void ValidateOwnershipExpiration(OwnershipLinkDto existingLink, DateTimeOffset effectiveTo);
    void ValidateOwnershipWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo);
    void ValidateOwnershipReplacement(OwnershipLinkDto existingLink, OwnershipLinkDto replacementLink);
    void ValidateOwnershipReplacement(
        OwnershipLinkDto existingLink,
        OwnershipLinkDto expiredExistingLink,
        OwnershipLinkDto replacementLink,
        FundStructureNodeKindDto parentKind,
        FundStructureNodeKindDto childKind,
        IReadOnlyCollection<OwnershipLinkDto> existingLinks,
        IReadOnlyDictionary<Guid, FundStructureNodeKindDto> nodeKinds);

    void ValidateCashFlowQuery(GovernanceCashFlowQuery query);
}
