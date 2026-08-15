using System.Collections.Concurrent;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// An in-memory <see cref="IFundStructureStore"/> so <c>PostgresFundStructureService</c> can be
/// exercised without PostgreSQL.
/// </summary>
/// <remarks>
/// The scoping rules this suite is about live in the *service*, not the store:
/// <c>PostgresFundStructureStore</c> issues plain per-entity selects and does no cross-entity
/// filtering, so swapping it for a dictionary changes nothing the tests assert on. Without this the
/// Postgres half of the contract would skip wherever no database is available, which is most of the
/// time — and a contract suite that only ever runs one side of the contract proves nothing about the
/// other.
/// </remarks>
public sealed class FakeFundStructureStore : IFundStructureStore
{
    private readonly ConcurrentDictionary<Guid, OrganizationSummaryDto> _organizations = new();
    private readonly ConcurrentDictionary<Guid, BusinessSummaryDto> _businesses = new();
    private readonly ConcurrentDictionary<Guid, ClientSummaryDto> _clients = new();
    private readonly ConcurrentDictionary<Guid, FundSummaryDto> _funds = new();
    private readonly ConcurrentDictionary<Guid, SleeveSummaryDto> _sleeves = new();
    private readonly ConcurrentDictionary<Guid, VehicleSummaryDto> _vehicles = new();
    private readonly ConcurrentDictionary<Guid, LegalEntitySummaryDto> _entities = new();
    private readonly ConcurrentDictionary<Guid, InvestmentPortfolioSummaryDto> _portfolios = new();
    private readonly List<OwnershipLinkDto> _links = [];
    private readonly List<FundStructureAssignmentDto> _assignments = [];
    private readonly HashSet<Guid> _linkedAccountIds = [];

    public Task UpsertOrganizationAsync(OrganizationSummaryDto dto, CancellationToken ct = default)
    {
        _organizations[dto.OrganizationId] = dto;
        return Task.CompletedTask;
    }

    public Task<OrganizationSummaryDto?> GetOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(_organizations.TryGetValue(organizationId, out var value) ? value : null);

    public Task<IReadOnlyList<OrganizationSummaryDto>> GetAllOrganizationsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrganizationSummaryDto>>([.. _organizations.Values]);

    public Task UpsertBusinessAsync(BusinessSummaryDto dto, CancellationToken ct = default)
    {
        _businesses[dto.BusinessId] = dto;
        return Task.CompletedTask;
    }

    public Task<BusinessSummaryDto?> GetBusinessAsync(Guid businessId, CancellationToken ct = default)
        => Task.FromResult(_businesses.TryGetValue(businessId, out var value) ? value : null);

    public Task<IReadOnlyList<BusinessSummaryDto>> GetAllBusinessesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BusinessSummaryDto>>([.. _businesses.Values]);

    public Task UpsertClientAsync(ClientSummaryDto dto, CancellationToken ct = default)
    {
        _clients[dto.ClientId] = dto;
        return Task.CompletedTask;
    }

    public Task<ClientSummaryDto?> GetClientAsync(Guid clientId, CancellationToken ct = default)
        => Task.FromResult(_clients.TryGetValue(clientId, out var value) ? value : null);

    public Task<IReadOnlyList<ClientSummaryDto>> GetAllClientsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ClientSummaryDto>>([.. _clients.Values]);

    public Task UpsertFundAsync(FundSummaryDto dto, CancellationToken ct = default)
    {
        _funds[dto.FundId] = dto;
        return Task.CompletedTask;
    }

    public Task<FundSummaryDto?> GetFundAsync(Guid fundId, CancellationToken ct = default)
        => Task.FromResult(_funds.TryGetValue(fundId, out var value) ? value : null);

    public Task<IReadOnlyList<FundSummaryDto>> GetAllFundsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FundSummaryDto>>([.. _funds.Values]);

    public Task UpsertSleeveAsync(SleeveSummaryDto dto, CancellationToken ct = default)
    {
        _sleeves[dto.SleeveId] = dto;
        return Task.CompletedTask;
    }

    public Task<SleeveSummaryDto?> GetSleeveAsync(Guid sleeveId, CancellationToken ct = default)
        => Task.FromResult(_sleeves.TryGetValue(sleeveId, out var value) ? value : null);

    public Task<IReadOnlyList<SleeveSummaryDto>> GetAllSleevesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SleeveSummaryDto>>([.. _sleeves.Values]);

    public Task UpsertVehicleAsync(VehicleSummaryDto dto, CancellationToken ct = default)
    {
        _vehicles[dto.VehicleId] = dto;
        return Task.CompletedTask;
    }

    public Task<VehicleSummaryDto?> GetVehicleAsync(Guid vehicleId, CancellationToken ct = default)
        => Task.FromResult(_vehicles.TryGetValue(vehicleId, out var value) ? value : null);

    public Task<IReadOnlyList<VehicleSummaryDto>> GetAllVehiclesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VehicleSummaryDto>>([.. _vehicles.Values]);

    public Task UpsertLegalEntityAsync(LegalEntitySummaryDto dto, CancellationToken ct = default)
    {
        _entities[dto.EntityId] = dto;
        return Task.CompletedTask;
    }

    public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
        => Task.FromResult(_entities.TryGetValue(entityId, out var value) ? value : null);

    public Task<IReadOnlyList<LegalEntitySummaryDto>> GetAllLegalEntitiesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LegalEntitySummaryDto>>([.. _entities.Values]);

    public Task UpsertInvestmentPortfolioAsync(InvestmentPortfolioSummaryDto dto, CancellationToken ct = default)
    {
        _portfolios[dto.InvestmentPortfolioId] = dto;
        return Task.CompletedTask;
    }

    public Task<InvestmentPortfolioSummaryDto?> GetInvestmentPortfolioAsync(Guid portfolioId, CancellationToken ct = default)
        => Task.FromResult(_portfolios.TryGetValue(portfolioId, out var value) ? value : null);

    public Task<IReadOnlyList<InvestmentPortfolioSummaryDto>> GetAllInvestmentPortfoliosAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InvestmentPortfolioSummaryDto>>([.. _portfolios.Values]);

    public Task UpsertOwnershipLinkAsync(OwnershipLinkDto dto, CancellationToken ct = default)
    {
        lock (_links)
        {
            _links.RemoveAll(existing => existing.LinkId == dto.LinkId);
            _links.Add(dto);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OwnershipLinkDto>> GetAllOwnershipLinksAsync(CancellationToken ct = default)
    {
        lock (_links)
        {
            return Task.FromResult<IReadOnlyList<OwnershipLinkDto>>([.. _links]);
        }
    }

    public Task UpsertAssignmentAsync(FundStructureAssignmentDto dto, CancellationToken ct = default)
    {
        lock (_assignments)
        {
            _assignments.RemoveAll(existing => existing.AssignmentId == dto.AssignmentId);
            _assignments.Add(dto);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FundStructureAssignmentDto>> GetAllAssignmentsAsync(CancellationToken ct = default)
    {
        lock (_assignments)
        {
            return Task.FromResult<IReadOnlyList<FundStructureAssignmentDto>>([.. _assignments]);
        }
    }

    public Task UpsertLinkedAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        lock (_linkedAccountIds)
        {
            _linkedAccountIds.Add(accountId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetAllLinkedAccountIdsAsync(CancellationToken ct = default)
    {
        lock (_linkedAccountIds)
        {
            return Task.FromResult<IReadOnlyList<Guid>>([.. _linkedAccountIds]);
        }
    }

    public Task<bool> IsEmptyAsync(CancellationToken ct = default)
        => Task.FromResult(_organizations.IsEmpty && _businesses.IsEmpty && _clients.IsEmpty && _funds.IsEmpty);
}
