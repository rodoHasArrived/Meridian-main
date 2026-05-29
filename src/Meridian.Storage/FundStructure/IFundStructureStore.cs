using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

/// <summary>
/// Persistence interface for the Fund Structure domain.
/// All entity types are stored individually; back-reference lists (e.g. BusinessIds on Organization)
/// are maintained by the service layer and stored alongside the owning entity.
/// </summary>
public interface IFundStructureStore
{
    // Organizations
    Task UpsertOrganizationAsync(OrganizationSummaryDto dto, CancellationToken ct = default);
    Task<OrganizationSummaryDto?> GetOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationSummaryDto>> GetAllOrganizationsAsync(CancellationToken ct = default);

    // Businesses
    Task UpsertBusinessAsync(BusinessSummaryDto dto, CancellationToken ct = default);
    Task<BusinessSummaryDto?> GetBusinessAsync(Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessSummaryDto>> GetAllBusinessesAsync(CancellationToken ct = default);

    // Clients
    Task UpsertClientAsync(ClientSummaryDto dto, CancellationToken ct = default);
    Task<ClientSummaryDto?> GetClientAsync(Guid clientId, CancellationToken ct = default);
    Task<IReadOnlyList<ClientSummaryDto>> GetAllClientsAsync(CancellationToken ct = default);

    // Funds
    Task UpsertFundAsync(FundSummaryDto dto, CancellationToken ct = default);
    Task<FundSummaryDto?> GetFundAsync(Guid fundId, CancellationToken ct = default);
    Task<IReadOnlyList<FundSummaryDto>> GetAllFundsAsync(CancellationToken ct = default);

    // Sleeves
    Task UpsertSleeveAsync(SleeveSummaryDto dto, CancellationToken ct = default);
    Task<SleeveSummaryDto?> GetSleeveAsync(Guid sleeveId, CancellationToken ct = default);
    Task<IReadOnlyList<SleeveSummaryDto>> GetAllSleevesAsync(CancellationToken ct = default);

    // Vehicles
    Task UpsertVehicleAsync(VehicleSummaryDto dto, CancellationToken ct = default);
    Task<VehicleSummaryDto?> GetVehicleAsync(Guid vehicleId, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleSummaryDto>> GetAllVehiclesAsync(CancellationToken ct = default);

    // Legal Entities
    Task UpsertLegalEntityAsync(LegalEntitySummaryDto dto, CancellationToken ct = default);
    Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<LegalEntitySummaryDto>> GetAllLegalEntitiesAsync(CancellationToken ct = default);

    // Investment Portfolios
    Task UpsertInvestmentPortfolioAsync(InvestmentPortfolioSummaryDto dto, CancellationToken ct = default);
    Task<InvestmentPortfolioSummaryDto?> GetInvestmentPortfolioAsync(Guid portfolioId, CancellationToken ct = default);
    Task<IReadOnlyList<InvestmentPortfolioSummaryDto>> GetAllInvestmentPortfoliosAsync(CancellationToken ct = default);

    // Ownership Links
    Task UpsertOwnershipLinkAsync(OwnershipLinkDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<OwnershipLinkDto>> GetAllOwnershipLinksAsync(CancellationToken ct = default);

    // Assignments
    Task UpsertAssignmentAsync(FundStructureAssignmentDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<FundStructureAssignmentDto>> GetAllAssignmentsAsync(CancellationToken ct = default);

    // Atomic setup workflow persistence
    Task CommitSetupBatchAsync(FundStructureSetupBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        throw new NotSupportedException("This fund-structure store does not support atomic setup batches.");
    }

    // Emptiness check
    Task<bool> IsEmptyAsync(CancellationToken ct = default);
}
