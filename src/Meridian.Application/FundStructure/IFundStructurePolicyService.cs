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

    void ValidateOwnershipWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo);
    void ValidateOwnershipReplacement(OwnershipLinkDto existingLink, OwnershipLinkDto replacementLink);
    void ValidateCashFlowQuery(GovernanceCashFlowQuery query);
}
