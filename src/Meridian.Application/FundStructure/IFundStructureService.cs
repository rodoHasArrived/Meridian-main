using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Manages the organization-rooted governance structure graph that supports
/// advisory and fund-operating views over shared portfolios, accounts, and
/// legal overlays.
/// </summary>
public interface IFundStructureService
{
    Task<OrganizationSummaryDto> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        CancellationToken ct = default);

    Task<BusinessSummaryDto> CreateBusinessAsync(
        CreateBusinessRequest request,
        CancellationToken ct = default);

    Task<ClientSummaryDto> CreateClientAsync(
        CreateClientRequest request,
        CancellationToken ct = default);

    Task<FundSummaryDto> CreateFundAsync(
        CreateFundRequest request,
        CancellationToken ct = default);

    Task<SleeveSummaryDto> CreateSleeveAsync(
        CreateSleeveRequest request,
        CancellationToken ct = default);

    Task<VehicleSummaryDto> CreateVehicleAsync(
        CreateVehicleRequest request,
        CancellationToken ct = default);

    Task<LegalEntitySummaryDto> CreateLegalEntityAsync(
        CreateLegalEntityRequest request,
        CancellationToken ct = default);

    Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(
        CreateInvestmentPortfolioRequest request,
        CancellationToken ct = default);

    Task<OwnershipLinkDto> LinkNodesAsync(
        LinkFundStructureNodesRequest request,
        CancellationToken ct = default);

    Task<FundStructureAssignmentDto> AssignNodeAsync(
        AssignFundStructureNodeRequest request,
        CancellationToken ct = default);

    Task<OrganizationStructureGraphDto> GetOrganizationStructureAsync(
        OrganizationStructureQuery query,
        CancellationToken ct = default);

    Task<FundStructureGraphDto> GetFundStructureGraphAsync(
        FundStructureQuery query,
        CancellationToken ct = default);

    Task<AdvisoryStructureViewDto?> GetAdvisoryViewAsync(
        AdvisoryStructureQuery query,
        CancellationToken ct = default);

    Task<FundOperatingViewDto?> GetFundOperatingViewAsync(
        FundOperatingStructureQuery query,
        CancellationToken ct = default);

    Task<AccountingStructureViewDto> GetAccountingViewAsync(
        AccountingStructureQuery query,
        CancellationToken ct = default);

    Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(
        GovernanceCashFlowQuery query,
        CancellationToken ct = default);
}
