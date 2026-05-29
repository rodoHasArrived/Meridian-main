using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>Store-level batch used when a setup workflow must persist all fund-structure rows in one transaction.</summary>
public sealed record FundStructureSetupBatch(
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<BusinessSummaryDto> Businesses,
    IReadOnlyList<ClientSummaryDto> Clients,
    IReadOnlyList<FundSummaryDto> Funds,
    IReadOnlyList<VehicleSummaryDto> Vehicles,
    IReadOnlyList<LegalEntitySummaryDto> LegalEntities,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    IReadOnlyList<FundStructureAssignmentDto> Assignments);
