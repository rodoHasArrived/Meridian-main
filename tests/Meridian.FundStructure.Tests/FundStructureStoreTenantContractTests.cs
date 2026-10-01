using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class FundStructureStoreTenantContractTests
{
    [Theory]
    [InlineData("tenant-alpha")]
    [InlineData("all")]
    [InlineData(" ")]
    public async Task LegacyStore_RefusesSuppliedTenantWithoutCallingUnscopedWrites(string tenant)
    {
        var legacy = new LegacyStore();
        IFundStructureStore store = legacy;

        await Assert.ThrowsAsync<NotSupportedException>(() => store.UpsertOwnershipLinkAsync(Link(), tenant, default));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.UpsertAssignmentAsync(Assignment(), tenant, default));

        Assert.Equal(0, legacy.LinkWrites);
        Assert.Equal(0, legacy.AssignmentWrites);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task LegacyStore_AllowsExplicitlyUnscopedCompatibilityWrites(string? tenant)
    {
        var legacy = new LegacyStore();
        IFundStructureStore store = legacy;
        using var cancellation = new CancellationTokenSource();

        await store.UpsertOwnershipLinkAsync(Link(), tenant, cancellation.Token);
        await store.UpsertAssignmentAsync(Assignment(), tenant, cancellation.Token);

        Assert.Equal(1, legacy.LinkWrites);
        Assert.Equal(1, legacy.AssignmentWrites);
        Assert.Equal(cancellation.Token, legacy.LinkCancellation);
        Assert.Equal(cancellation.Token, legacy.AssignmentCancellation);
    }

    private static OwnershipLinkDto Link() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        OwnershipRelationshipTypeDto.Owns, 100m, true, DateTimeOffset.UtcNow, null, null);

    private static FundStructureAssignmentDto Assignment() => new(Guid.NewGuid(), Guid.NewGuid(),
        "ledger-group", "PRIMARY", DateTimeOffset.UtcNow, null, true);

    // Intentionally implements only the original unscoped write contract. The tenant overloads
    // must refuse before reaching either method, even for an existing third-party implementation.
    private sealed class LegacyStore : IFundStructureStore
    {
        public int LinkWrites { get; private set; }
        public int AssignmentWrites { get; private set; }
        public CancellationToken LinkCancellation { get; private set; }
        public CancellationToken AssignmentCancellation { get; private set; }

        public Task UpsertOwnershipLinkAsync(OwnershipLinkDto dto, CancellationToken ct = default)
        {
            LinkWrites++;
            LinkCancellation = ct;
            return Task.CompletedTask;
        }

        public Task UpsertAssignmentAsync(FundStructureAssignmentDto dto, CancellationToken ct = default)
        {
            AssignmentWrites++;
            AssignmentCancellation = ct;
            return Task.CompletedTask;
        }

        public Task UpsertOrganizationAsync(OrganizationSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationSummaryDto?> GetOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationSummaryDto>> GetAllOrganizationsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertBusinessAsync(BusinessSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BusinessSummaryDto?> GetBusinessAsync(Guid businessId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BusinessSummaryDto>> GetAllBusinessesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertClientAsync(ClientSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ClientSummaryDto?> GetClientAsync(Guid clientId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ClientSummaryDto>> GetAllClientsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertFundAsync(FundSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FundSummaryDto?> GetFundAsync(Guid fundId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FundSummaryDto>> GetAllFundsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertSleeveAsync(SleeveSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SleeveSummaryDto?> GetSleeveAsync(Guid sleeveId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SleeveSummaryDto>> GetAllSleevesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertVehicleAsync(VehicleSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VehicleSummaryDto?> GetVehicleAsync(Guid vehicleId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<VehicleSummaryDto>> GetAllVehiclesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertLegalEntityAsync(LegalEntitySummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LegalEntitySummaryDto>> GetAllLegalEntitiesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertInvestmentPortfolioAsync(InvestmentPortfolioSummaryDto dto, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InvestmentPortfolioSummaryDto?> GetInvestmentPortfolioAsync(Guid portfolioId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InvestmentPortfolioSummaryDto>> GetAllInvestmentPortfoliosAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OwnershipLinkDto>> GetAllOwnershipLinksAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FundStructureAssignmentDto>> GetAllAssignmentsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertLinkedAccountIdAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetAllLinkedAccountIdsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsEmptyAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
