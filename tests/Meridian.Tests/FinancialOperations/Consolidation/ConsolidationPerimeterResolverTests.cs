using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Consolidation;
using Moq;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class ConsolidationPerimeterResolverTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RootId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FirstId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task ResolveAsync_DerivesTwoMembersAndCurrencyFromAuthoritativeGraph()
    {
        var graph = Graph();
        var service = Service(graph, out var authority);

        var result = await service.ResolveAsync(OrganizationId, RootId, AsOf);

        Assert.Equal(new[] { FirstId, SecondId }, result.Entities.Select(entity => entity.EntityId));
        Assert.Equal("USD", result.Currency);
        Assert.Equal(AsOf, result.AsOf);
        Assert.Equal(4, result.OwnershipEvidence.Count);
        Assert.All(graph.OwnershipLinks, link => Assert.Contains(link, result.OwnershipEvidence));
        Assert.Contains("no indirect ownership", result.ScopeLimitation, StringComparison.Ordinal);
        authority.Verify(item => item.GetOrganizationStructureAsync(
            It.Is<OrganizationStructureQuery>(query => query.OrganizationId == null
                && query.NodeId == null && !query.ActiveOnly && query.AsOf == AsOf), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(99)]
    public async Task ResolveAsync_RejectsUnspecifiedOrPartialOwnership(int? percent)
    {
        var graph = Graph();
        graph = graph with
        {
            OwnershipLinks = graph.OwnershipLinks.Select(link => link.ChildNodeId == FirstId
            ? link with { OwnershipPercent = percent } : link).ToArray()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Fact]
    public async Task ResolveAsync_RejectsAdditionalPartialMemberInsteadOfTruncatingIt()
    {
        var graph = Graph();
        graph = graph with { OwnershipLinks = [.. graph.OwnershipLinks, Link(RootId, Guid.NewGuid(), percent: 25m)] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Fact]
    public async Task ResolveAsync_RejectsConflictingOwnerOutsideRequestedOrganization()
    {
        var graph = Graph();
        graph = graph with { OwnershipLinks = [.. graph.OwnershipLinks, Link(Guid.NewGuid(), FirstId, percent: 20m)] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Fact]
    public async Task ResolveAsync_RejectsOverlappingDuplicateOwnership()
    {
        var graph = Graph();
        graph = graph with { OwnershipLinks = [.. graph.OwnershipLinks, Link(RootId, FirstId)] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Fact]
    public async Task ResolveAsync_UsesReplacementAtExactOwnershipBoundary()
    {
        var graph = Graph();
        var oldLink = graph.OwnershipLinks.Single(link => link.ChildNodeId == FirstId) with { EffectiveTo = AsOf };
        var replacement = oldLink with { OwnershipLinkId = Guid.NewGuid(), EffectiveFrom = AsOf, EffectiveTo = null };
        graph = graph with { OwnershipLinks = [.. graph.OwnershipLinks.Where(link => link.ChildNodeId != FirstId), oldLink, replacement] };

        var result = await Service(graph).ResolveAsync(OrganizationId, RootId, AsOf);

        Assert.Contains(replacement, result.OwnershipEvidence);
        Assert.DoesNotContain(oldLink, result.OwnershipEvidence);
        var previous = await Service(graph).ResolveAsync(OrganizationId, RootId, AsOf.AddTicks(-1));
        Assert.Contains(oldLink, previous.OwnershipEvidence);
        Assert.DoesNotContain(replacement, previous.OwnershipEvidence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveAsync_RejectsExpiredOrFutureMembership(bool expired)
    {
        var graph = Graph();
        graph = graph with
        {
            OwnershipLinks = graph.OwnershipLinks.Select(link => link.ChildNodeId == FirstId
            ? expired ? link with { EffectiveTo = AsOf } : link with { EffectiveFrom = AsOf.AddTicks(1) }
            : link).ToArray()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("")]
    public async Task ResolveAsync_RejectsMixedOrMissingEntityCurrency(string currency)
    {
        var graph = Graph();
        graph = graph with
        {
            Entities = graph.Entities.Select(entity => entity.EntityId == FirstId
            ? entity with { BaseCurrency = currency } : entity).ToArray()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Theory]
    [InlineData("entity-inactive")]
    [InlineData("entity-future")]
    [InlineData("entity-expired")]
    [InlineData("entity-missing")]
    [InlineData("wrong-node-kind")]
    [InlineData("root-inactive")]
    [InlineData("organization-inactive")]
    [InlineData("wrong-organization")]
    [InlineData("missing-operating-path")]
    [InlineData("profile-only-membership")]
    public async Task ResolveAsync_RejectsUnsupportedOrInconsistentAuthority(string invalid)
    {
        var graph = Graph();
        graph = invalid switch
        {
            "entity-inactive" => graph with { Entities = [graph.Entities[0] with { IsActive = false }, graph.Entities[1]] },
            "entity-future" => graph with { Entities = [graph.Entities[0] with { EffectiveFrom = AsOf.AddDays(1) }, graph.Entities[1]] },
            "entity-expired" => graph with { Entities = [graph.Entities[0] with { EffectiveTo = AsOf.AddTicks(-1) }, graph.Entities[1]] },
            "entity-missing" => graph with { Entities = [graph.Entities[1]] },
            "wrong-node-kind" => graph with { Nodes = graph.Nodes.Select(node => node.NodeId == FirstId ? node with { Kind = FundStructureNodeKindDto.Vehicle } : node).ToArray() },
            "root-inactive" => graph with { Funds = [graph.Funds[0] with { IsActive = false }] },
            "organization-inactive" => graph with { Organizations = [graph.Organizations[0] with { IsActive = false }] },
            "wrong-organization" => graph with { Businesses = [graph.Businesses[0] with { OrganizationId = Guid.NewGuid() }] },
            "missing-operating-path" => graph with { OwnershipLinks = graph.OwnershipLinks.Where(link => link.ChildNodeId != RootId).ToArray() },
            "profile-only-membership" => graph with { OwnershipLinks = graph.OwnershipLinks.Where(link => link.ParentNodeId != RootId).ToArray() },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(graph).ResolveAsync(OrganizationId, RootId, AsOf));
    }

    [Fact]
    public async Task ResolveAsync_RereadsAuthoritativeOwnershipOnEveryRun()
    {
        var graph = Graph();
        var authority = new Mock<IFundStructureService>(MockBehavior.Strict);
        authority.SetupSequence(item => item.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph)
            .ReturnsAsync(graph with
            {
                OwnershipLinks = graph.OwnershipLinks.Select(link => link.ChildNodeId == FirstId
                ? link with { OwnershipPercent = 75m } : link).ToArray()
            });
        var service = new ConsolidationPerimeterResolver(authority.Object);

        await service.ResolveAsync(OrganizationId, RootId, AsOf);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync(OrganizationId, RootId, AsOf));
    }

    private static ConsolidationPerimeterResolver Service(OrganizationStructureGraphDto graph)
        => Service(graph, out _);

    private static ConsolidationPerimeterResolver Service(OrganizationStructureGraphDto graph, out Mock<IFundStructureService> authority)
    {
        authority = new Mock<IFundStructureService>(MockBehavior.Strict);
        authority.Setup(item => item.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        return new ConsolidationPerimeterResolver(authority.Object);
    }

    private static OrganizationStructureGraphDto Graph()
    {
        var since = AsOf.AddYears(-1);
        return new OrganizationStructureGraphDto(
            Organizations: [new(OrganizationId, "ORG", "Group", "USD", true, since, null, [BusinessId])],
            Businesses: [new(BusinessId, OrganizationId, BusinessKindDto.FundManager, "BUS", "Business", "USD", true, since, null, [], [RootId], [])],
            Clients: [],
            Funds: [new(RootId, BusinessId, "FUND", "Ownership root", "USD", true, since, null, [], [], [FirstId, SecondId], [], [])],
            Sleeves: [], Vehicles: [],
            Entities: [Entity(FirstId), Entity(SecondId)],
            InvestmentPortfolios: [], Accounts: [],
            Nodes:
            [
                Node(OrganizationId, FundStructureNodeKindDto.Organization),
                Node(BusinessId, FundStructureNodeKindDto.Business),
                Node(RootId, FundStructureNodeKindDto.Fund),
                Node(FirstId, FundStructureNodeKindDto.Entity),
                Node(SecondId, FundStructureNodeKindDto.Entity)
            ],
            OwnershipLinks:
            [
                Link(OrganizationId, BusinessId),
                Link(BusinessId, RootId) with { RelationshipType = OwnershipRelationshipTypeDto.Operates, OwnershipPercent = null },
                Link(RootId, FirstId), Link(RootId, SecondId)
            ],
            Assignments: []);
    }

    private static LegalEntitySummaryDto Entity(Guid id)
        => new(id, LegalEntityTypeDto.Vehicle, id.ToString(), "Entity", "US", "USD", true, AsOf.AddYears(-1), null);

    private static FundStructureNodeDto Node(Guid id, FundStructureNodeKindDto kind)
        => new(id, kind, id.ToString(), "Node", null, true, AsOf.AddYears(-1), null);

    private static OwnershipLinkDto Link(Guid parent, Guid child, decimal percent = 100m)
        => new(Guid.NewGuid(), parent, child, OwnershipRelationshipTypeDto.Owns, percent, true, AsOf.AddYears(-1), null, null);
}
