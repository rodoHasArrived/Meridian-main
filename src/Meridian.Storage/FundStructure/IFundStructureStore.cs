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

    // Account node identities retained independently from active links/assignments
    Task UpsertLinkedAccountIdAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetAllLinkedAccountIdsAsync(CancellationToken ct = default);

    // Emptiness check
    Task<bool> IsEmptyAsync(CancellationToken ct = default);

    // Tenant partition (W9-GOV-008 criterion 2)
    //
    // Deliberately two narrow methods rather than a tenant argument on every read and a tenant
    // member on every summary DTO. The DTOs are the contract the workstation, the desktop lane and
    // the API all bind, and ownership is not a property of a node that any of them should render or
    // round-trip -- it is a property of who may see it. Keeping it out of the DTOs also means the
    // scoping cannot be defeated by a caller echoing a summary back with a different tenant on it.

    /// <summary>
    /// The tenant stamped on each node this store partitions, for the nodes that carry one.
    /// </summary>
    /// <remarks>
    /// Stores with no tenant partition return <see cref="FundStructureTenantMap.Unpartitioned"/>,
    /// which is not the same as "partitioned but nothing stamped yet": the first says ownership is
    /// not modelled here at all, so there is nothing to enforce, while the second says an
    /// attribution has not run and a fail-closed reader would rightly hide the graph.
    /// </remarks>
    Task<FundStructureTenantMap> GetNodeTenantsAsync(CancellationToken ct = default)
        => Task.FromResult(FundStructureTenantMap.Unpartitioned);

    /// <summary>Stamps <paramref name="nodeId"/> as owned by <paramref name="tenantId"/>.</summary>
    /// <remarks>A no-op in stores with no tenant partition.</remarks>
    Task StampNodeTenantAsync(Guid nodeId, string tenantId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Imports one legacy JSON snapshot as a single database transaction when the store is empty.
    /// The source hash is committed in the same transaction so startup can safely recover a process
    /// failure between database commit and source-file archival.
    /// </summary>
    Task<FundStructureLegacyImportResult> ImportLegacySnapshotIfEmptyAsync(
        FundStructureLegacyImportRequest request,
        CancellationToken ct = default)
        => Task.FromException<FundStructureLegacyImportResult>(
            new NotSupportedException("This fund-structure store does not support transactional legacy imports."));
}

/// <summary>
/// Which fund-structure nodes a store attributes to a tenant.
/// </summary>
/// <param name="IsPartitioned">
/// Whether this store models tenant ownership at all. False for the in-memory and JSON-backed
/// stores, which serve one undivided graph to every session and are marked non-production for
/// exactly that reason.
/// </param>
/// <param name="NodeTenants">
/// Node id to owning tenant, for stamped nodes only. A node absent from this map is unattributed:
/// visible to everyone under the deployment-boundary posture, hidden from scoped callers under the
/// fail-closed one.
/// </param>
public sealed record FundStructureTenantMap(
    bool IsPartitioned,
    IReadOnlyDictionary<Guid, string> NodeTenants)
{
    /// <summary>A store that does not model tenant ownership.</summary>
    public static FundStructureTenantMap Unpartitioned { get; } =
        new(IsPartitioned: false, new Dictionary<Guid, string>());
}

public sealed record FundStructureLegacyImportRequest(
    string SourceHash,
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<BusinessSummaryDto> Businesses,
    IReadOnlyList<ClientSummaryDto> Clients,
    IReadOnlyList<FundSummaryDto> Funds,
    IReadOnlyList<SleeveSummaryDto> Sleeves,
    IReadOnlyList<VehicleSummaryDto> Vehicles,
    IReadOnlyList<LegalEntitySummaryDto> Entities,
    IReadOnlyList<InvestmentPortfolioSummaryDto> InvestmentPortfolios,
    IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
    IReadOnlyList<FundStructureAssignmentDto> Assignments,
    IReadOnlyList<Guid> LinkedAccountIds);

public enum FundStructureLegacyImportResult : byte
{
    Imported = 0,
    AlreadyImported = 1,
    StoreNotEmpty = 2
}
