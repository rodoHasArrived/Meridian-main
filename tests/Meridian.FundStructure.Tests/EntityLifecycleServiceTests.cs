using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class EntityLifecycleServiceTests
{
    [Fact]
    public async Task LocalService_InitializesHistoryRefusesProfileBypassAndProjectsEffectiveStatus()
    {
        var store = new InMemoryEntityLifecycleStore();
        var tenant = new TenantAccessor("tenant-a");
        var structure = new InMemoryFundStructureService(new InMemoryFundAccountService(), null, null, null, store, tenant);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        var boundary = start.AddDays(1);
        var entity = await CreateOwnedEntityAsync(structure, start);
        var service = new EntityLifecycleService(structure, store, tenant);
        var first = await service.GetAsync(entity.EntityId);
        Assert.NotNull(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => structure.UpdateLegalEntityProfileAsync(
            new(entity.EntityId, "operator", LifecycleStatus: LegalEntityLifecycleStatusDto.Dissolved)));
        var revision = await service.TransitionAsync(new(entity.EntityId, first.Revision,
            [first.Timeline[0] with { EffectiveTo = boundary },
                new(Guid.NewGuid(), boundary, null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Restructure)],
            "Close existing obligations", "retained://winding-down-authority"), "authenticated-operator");
        Assert.Equal("authenticated-operator", revision.RecordedBy);
        var current = await structure.GetOrganizationStructureAsync(new(ActiveOnly: false));
        Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, Assert.Single(current.Entities).LifecycleStatus);
        var historical = await structure.GetOrganizationStructureAsync(new(ActiveOnly: false, AsOf: start));
        Assert.Equal(LegalEntityLifecycleStatusDto.Active, Assert.Single(historical.Entities).LifecycleStatus);
        Assert.Equal(2, (await service.GetHistoryAsync(entity.EntityId)).Count);
    }

    [Fact]
    public async Task ScopedService_RejectsCrossTenantEntityAndSuccessorWithoutDisclosingHistory()
    {
        var store = new InMemoryEntityLifecycleStore();
        var tenant = new TenantAccessor("tenant-a");
        var entities = new FakeFundStructureStore(isTenantPartitioned: true);
        var structure = new PostgresFundStructureService(entities, new InMemoryFundAccountService(), new FundStructurePolicyService(),
            tenantAccessor: tenant, entityLifecycleStore: store);
        var own = await CreateOwnedEntityAsync(structure, DateTimeOffset.UtcNow.AddDays(-1));
        tenant.Value = "tenant-b";
        var foreign = await CreateOwnedEntityAsync(structure, DateTimeOffset.UtcNow.AddDays(-1));
        var service = new EntityLifecycleService(structure, store, tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(own.EntityId));
        tenant.Value = "tenant-a";
        var first = (await service.GetAsync(own.EntityId))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransitionAsync(new(own.EntityId, first.Revision,
            [first.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Merged, SuccessorEntityId = foreign.EntityId }],
            "Merger", "retained://merger-authority"), "authenticated-operator"));
        Assert.Equal(1, (await service.GetAsync(own.EntityId))!.Revision);
    }

    private static CreateLegalEntityRequest Request(Guid id, DateTimeOffset start)
        => new(id, LegalEntityTypeDto.Vehicle, "ENTITY", "Synthetic entity", "US", "USD", start, "setup-operator");

    private static async Task<LegalEntitySummaryDto> CreateOwnedEntityAsync(IFundStructureService structure, DateTimeOffset start)
    {
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        await structure.CreateOrganizationAsync(new(organizationId, "ORG", "Synthetic organization", "USD", start, "setup-operator"));
        await structure.CreateBusinessAsync(new(businessId, organizationId, BusinessKindDto.FundManager,
            "BUSINESS", "Synthetic fund manager", "USD", start, "setup-operator"));
        await structure.CreateFundAsync(new(fundId, "FUND", "Synthetic fund", "USD", start, "setup-operator", BusinessId: businessId));
        var entity = await structure.CreateLegalEntityAsync(Request(Guid.NewGuid(), start));
        await structure.LinkNodesAsync(new(Guid.NewGuid(), fundId, entity.EntityId, OwnershipRelationshipTypeDto.Owns,
            start, "setup-operator", OwnershipPercent: 100m));
        return entity;
    }

    private sealed class TenantAccessor(string value) : IFundScopeTenantAccessor
    {
        public string Value { get; set; } = value;
        public string? ResolveCallerTenant() => Value;
    }
}
