using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class FundStructurePolicyServiceTests
{
    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenParentAndChildAreSameNode()
    {
        var service = new FundStructurePolicyService();
        var nodeId = Guid.NewGuid();
        var link = CreateLink(nodeId, nodeId, OwnershipRelationshipTypeDto.Owns);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            link,
            FundStructureNodeKindDto.Organization,
            FundStructureNodeKindDto.Organization,
            [],
            new Dictionary<Guid, FundStructureNodeKindDto> { [nodeId] = FundStructureNodeKindDto.Organization }));
    }

    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenActiveLinksWouldCreateCycle()
    {
        var service = new FundStructurePolicyService();
        var organizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var existing = CreateLink(
            businessId,
            organizationId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero));
        var candidate = CreateLink(
            organizationId,
            businessId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 01, 02, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            candidate,
            FundStructureNodeKindDto.Organization,
            FundStructureNodeKindDto.Business,
            [existing],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [organizationId] = FundStructureNodeKindDto.Organization,
                [businessId] = FundStructureNodeKindDto.Business
            }));
    }

    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenOverlappingFutureLinkWouldCreateCycle()
    {
        var service = new FundStructurePolicyService();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var candidate = CreateLink(
            parentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            effectiveTo: new DateTimeOffset(2026, 03, 01, 0, 0, 0, TimeSpan.Zero));
        var futureReverseLink = CreateLink(
            childId,
            parentId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 02, 01, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            candidate,
            FundStructureNodeKindDto.Fund,
            FundStructureNodeKindDto.Entity,
            [futureReverseLink],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [parentId] = FundStructureNodeKindDto.Fund,
                [childId] = FundStructureNodeKindDto.Entity
            }));
    }

    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenRelationshipTypeDoesNotMatchNodeKinds()
    {
        var service = new FundStructurePolicyService();
        var organizationId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var link = CreateLink(organizationId, fundId, OwnershipRelationshipTypeDto.CustodiesFor);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            link,
            FundStructureNodeKindDto.Organization,
            FundStructureNodeKindDto.Fund,
            [],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [organizationId] = FundStructureNodeKindDto.Organization,
                [fundId] = FundStructureNodeKindDto.Fund
            }));
    }

    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenPrimaryLinkOverlapsForSameChildAndRelationship()
    {
        var service = new FundStructurePolicyService();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var existing = CreateLink(organizationId, businessId, OwnershipRelationshipTypeDto.Owns, isPrimary: true);
        var candidate = CreateLink(otherOrganizationId, businessId, OwnershipRelationshipTypeDto.Owns, isPrimary: true);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            candidate,
            FundStructureNodeKindDto.Organization,
            FundStructureNodeKindDto.Business,
            [existing],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [organizationId] = FundStructureNodeKindDto.Organization,
                [otherOrganizationId] = FundStructureNodeKindDto.Organization,
                [businessId] = FundStructureNodeKindDto.Business
            }));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void ValidateOwnershipLink_ThrowsWhenOwnershipPercentIsOutOfRange(double percent)
    {
        var service = new FundStructurePolicyService();
        var fundId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var link = CreateLink(fundId, entityId, OwnershipRelationshipTypeDto.Owns, ownershipPercent: (decimal)percent);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            link,
            FundStructureNodeKindDto.Fund,
            FundStructureNodeKindDto.Entity,
            [],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [fundId] = FundStructureNodeKindDto.Fund,
                [entityId] = FundStructureNodeKindDto.Entity
            }));
    }

    [Fact]
    public void ValidateOwnershipLink_ThrowsWhenActiveSiblingOwnershipPercentagesExceedOneHundred()
    {
        var service = new FundStructurePolicyService();
        var fundId = Guid.NewGuid();
        var firstEntityId = Guid.NewGuid();
        var secondEntityId = Guid.NewGuid();
        var existing = CreateLink(fundId, firstEntityId, OwnershipRelationshipTypeDto.Owns, ownershipPercent: 60m);
        var candidate = CreateLink(fundId, secondEntityId, OwnershipRelationshipTypeDto.Owns, ownershipPercent: 50m);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipLink(
            candidate,
            FundStructureNodeKindDto.Fund,
            FundStructureNodeKindDto.Entity,
            [existing],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [fundId] = FundStructureNodeKindDto.Fund,
                [firstEntityId] = FundStructureNodeKindDto.Entity,
                [secondEntityId] = FundStructureNodeKindDto.Entity
            }));
    }

    [Fact]
    public void ValidateOwnershipWindow_ThrowsWhenEffectiveToPrecedesEffectiveFrom()
    {
        var service = new FundStructurePolicyService();
        var effectiveFrom = new DateTimeOffset(2026, 01, 02, 0, 0, 0, TimeSpan.Zero);
        var effectiveTo = effectiveFrom.AddDays(-1);

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipWindow(effectiveFrom, effectiveTo));
    }

    [Fact]
    public void ValidateOwnershipReplacement_ThrowsWhenReplacementOverlapsExpiredWindow()
    {
        var service = new FundStructurePolicyService();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var existing = CreateLink(
            parentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            effectiveTo: new DateTimeOffset(2026, 02, 01, 0, 0, 0, TimeSpan.Zero));
        var replacement = CreateLink(
            parentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 01, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipReplacement(existing, replacement));
    }

    [Fact]
    public void ValidateOwnershipUpdate_ThrowsWhenAmendedPrimaryWindowOverlapsExistingPrimaryLink()
    {
        var service = new FundStructurePolicyService();
        var firstParentId = Guid.NewGuid();
        var secondParentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var existing = CreateLink(
            firstParentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            isPrimary: false,
            effectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero));
        var competingPrimary = CreateLink(
            secondParentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            isPrimary: true,
            effectiveFrom: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero));
        var updated = existing with { IsPrimary = true };

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipUpdate(
            existing,
            updated,
            FundStructureNodeKindDto.Fund,
            FundStructureNodeKindDto.Entity,
            [existing, competingPrimary],
            new Dictionary<Guid, FundStructureNodeKindDto>
            {
                [firstParentId] = FundStructureNodeKindDto.Fund,
                [secondParentId] = FundStructureNodeKindDto.Fund,
                [childId] = FundStructureNodeKindDto.Entity
            }));
    }

    [Fact]
    public void ValidateOwnershipExpiration_ThrowsWhenExpirationPrecedesEffectiveFrom()
    {
        var service = new FundStructurePolicyService();
        var link = CreateLink(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OwnershipRelationshipTypeDto.Owns,
            effectiveFrom: new DateTimeOffset(2026, 02, 01, 0, 0, 0, TimeSpan.Zero));

        Assert.Throws<InvalidOperationException>(() => service.ValidateOwnershipExpiration(
            link,
            new DateTimeOffset(2026, 01, 31, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenHistoricalDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(historicalDays: FundStructurePolicyService.MaxCashFlowWindowDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenForecastDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(forecastDays: FundStructurePolicyService.MaxCashFlowWindowDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    [Fact]
    public void ValidateCashFlowQuery_AllowsBoundaryValues()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(
            historicalDays: FundStructurePolicyService.MaxCashFlowWindowDays,
            forecastDays: FundStructurePolicyService.MaxCashFlowWindowDays,
            bucketDays: FundStructurePolicyService.MaxCashFlowBucketDays);

        service.ValidateCashFlowQuery(query);
    }

    [Fact]
    public void ValidateCashFlowQuery_ThrowsWhenBucketDaysExceedsMaximum()
    {
        var service = new FundStructurePolicyService();
        var query = CreateQuery(bucketDays: FundStructurePolicyService.MaxCashFlowBucketDays + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.ValidateCashFlowQuery(query));
    }

    private static GovernanceCashFlowQuery CreateQuery(
        int historicalDays = 7,
        int forecastDays = 7,
        int bucketDays = 7)
        => new(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: Guid.NewGuid(),
            HistoricalDays: historicalDays,
            ForecastDays: forecastDays,
            BucketDays: bucketDays);

    private static OwnershipLinkDto CreateLink(
        Guid parentId,
        Guid childId,
        OwnershipRelationshipTypeDto relationshipType,
        decimal? ownershipPercent = null,
        bool isPrimary = false,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null) =>
        new(
            Guid.NewGuid(),
            parentId,
            childId,
            relationshipType,
            ownershipPercent,
            isPrimary,
            effectiveFrom ?? new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            effectiveTo,
            Notes: null);
}
