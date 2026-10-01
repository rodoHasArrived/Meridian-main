using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;

namespace Meridian.Application.Tenancy;

/// <summary>Guards every local fund operation before reading or mutating retained snapshots.</summary>
public sealed class TenantGuardedLocalFundStructureService(IFundStructureService inner, LocalTenantMigrationGate gate)
    : INonProductionOnlyService, IFundStructureService
{
    public bool RefusesUnattributedAccess => gate.EnforcesStrictTenancy;

    public Task<OrganizationSummaryDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateOrganizationAsync(request, ct));

    public Task<BusinessSummaryDto> CreateBusinessAsync(CreateBusinessRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateBusinessAsync(request, ct));

    public Task<ClientSummaryDto> CreateClientAsync(CreateClientRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateClientAsync(request, ct));

    public Task<FundSummaryDto> CreateFundAsync(CreateFundRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateFundAsync(request, ct));

    public Task<SleeveSummaryDto> CreateSleeveAsync(CreateSleeveRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateSleeveAsync(request, ct));

    public Task<VehicleSummaryDto> CreateVehicleAsync(CreateVehicleRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateVehicleAsync(request, ct));

    public Task<LegalEntitySummaryDto> CreateLegalEntityAsync(CreateLegalEntityRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateLegalEntityAsync(request, ct));

    public Task<LegalEntitySummaryDto> UpdateLegalEntityProfileAsync(UpdateLegalEntityProfileRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.UpdateLegalEntityProfileAsync(request, ct));

    public Task<InvestmentPortfolioSummaryDto> CreateInvestmentPortfolioAsync(CreateInvestmentPortfolioRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.CreateInvestmentPortfolioAsync(request, ct));

    public Task<OwnershipLinkDto> LinkNodesAsync(LinkFundStructureNodesRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.LinkNodesAsync(request, ct));

    public Task<OwnershipLinkDto> UpdateOwnershipLinkAsync(UpdateOwnershipLinkRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.UpdateOwnershipLinkAsync(request, ct));

    public Task<OwnershipLinkDto> ExpireOwnershipLinkAsync(ExpireOwnershipLinkRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ExpireOwnershipLinkAsync(request, ct));

    public Task<OwnershipLinkDto> ReplaceOwnershipLinkAsync(ReplaceOwnershipLinkRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ReplaceOwnershipLinkAsync(request, ct));

    public Task<OwnershipGraphValidationResultDto> ValidateOwnershipGraphAsync(ValidateOwnershipGraphRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.ValidateOwnershipGraphAsync(request, ct));

    public Task<FundStructureAssignmentDto> AssignNodeAsync(AssignFundStructureNodeRequest request, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.AssignNodeAsync(request, ct));

    public Task<OrganizationStructureGraphDto> GetOrganizationStructureAsync(OrganizationStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetOrganizationStructureAsync(query, ct));

    public Task<FundStructureGraphDto> GetFundStructureGraphAsync(FundStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetFundStructureGraphAsync(query, ct));

    public Task<AdvisoryStructureViewDto?> GetAdvisoryViewAsync(AdvisoryStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetAdvisoryViewAsync(query, ct));

    public Task<FundOperatingViewDto?> GetFundOperatingViewAsync(FundOperatingStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetFundOperatingViewAsync(query, ct));

    public Task<AccountingStructureViewDto> GetAccountingViewAsync(AccountingStructureQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetAccountingViewAsync(query, ct));

    public Task<GovernanceCashFlowViewDto?> GetCashFlowViewAsync(GovernanceCashFlowQuery query, CancellationToken ct = default)
        => gate.ExecuteAsync(() => inner.GetCashFlowViewAsync(query, ct));

}
