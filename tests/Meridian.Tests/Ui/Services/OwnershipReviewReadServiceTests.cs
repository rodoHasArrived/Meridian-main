using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui.Services;

/// <summary>
/// Protects the operator setup scenario where ownership state must be explicit before setup completion.
/// </summary>
public sealed class OwnershipReviewReadServiceTests
{
    [Fact]
    public void Project_StableGraph_OrdersLinksAndBuildsRollupSummary()
    {
        var service = new OwnershipReviewReadService();
        var asOf = DateTimeOffset.Parse("2026-05-29T00:00:00Z");
        var fund = Node("00000000-0000-0000-0000-000000000001", FundStructureNodeKindDto.Fund, "ALPHA", "Alpha Fund");
        var sleeve = Node("00000000-0000-0000-0000-000000000002", FundStructureNodeKindDto.Sleeve, "CORE", "Core Sleeve");
        var account = Node("00000000-0000-0000-0000-000000000003", FundStructureNodeKindDto.Account, "PB", "Prime Brokerage");
        var graph = new FundStructureGraphDto(
            [account, sleeve, fund],
            [
                Link("00000000-0000-0000-0000-000000000012", sleeve.NodeId, account.NodeId, 25m),
                Link("00000000-0000-0000-0000-000000000011", fund.NodeId, sleeve.NodeId, 75m)
            ],
            []);

        var review = service.Project(graph, asOf);

        review.Links.Select(link => link.NodeDisplayLabel).Should().Equal(
            "Alpha Fund (Fund) owns Core Sleeve (Sleeve)",
            "Core Sleeve (Sleeve) owns Prime Brokerage (Account)");
        review.Summary.ActiveLinkCount.Should().Be(2);
        review.Summary.ExplicitOwnershipPercentTotal.Should().Be(100m);
        review.Summary.RollupSummary.Should().Contain("2 active of 2 ownership links");
    }

    [Fact]
    public void Project_EmptyGraph_ReturnsOperatorEmptyState()
    {
        var service = new OwnershipReviewReadService();

        var review = service.Project(new FundStructureGraphDto([], [], []), DateTimeOffset.Parse("2026-05-29T00:00:00Z"));

        review.Links.Should().BeEmpty();
        review.EmptyStateTitle.Should().Be("Ownership setup incomplete");
        review.Summary.RollupSummary.Should().Be("No ownership links have been configured.");
    }

    [Fact]
    public void Project_InvalidOwnershipLink_ReturnsBlockingWarningsAndRemediation()
    {
        var service = new OwnershipReviewReadService();
        var fund = Node("00000000-0000-0000-0000-000000000001", FundStructureNodeKindDto.Fund, "ALPHA", "Alpha Fund");
        var missingChild = Guid.Parse("00000000-0000-0000-0000-000000000099");
        var graph = new FundStructureGraphDto(
            [fund],
            [new OwnershipLinkDto(Guid.Parse("00000000-0000-0000-0000-000000000021"), fund.NodeId, missingChild, OwnershipRelationshipTypeDto.Owns, 120m, true, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, null)],
            []);

        var review = service.Project(graph, DateTimeOffset.Parse("2026-05-29T00:00:00Z"));

        review.Summary.InvalidLinkCount.Should().Be(1);
        review.Links[0].ValidationState.Should().Be(OwnershipReviewValidationStateDto.Blocking);
        review.Links[0].BlockingMessages.Should().Contain("Child node is missing from the fund-structure graph.");
        review.Links[0].BlockingMessages.Should().Contain("Ownership percent must be between 0 and 100.");
        review.Links[0].SuggestedRemediationActions.Should().Contain(action => action.Contains("existing child node"));
    }

    private static FundStructureNodeDto Node(
        string id,
        FundStructureNodeKindDto kind,
        string code,
        string name)
        => new(Guid.Parse(id), kind, code, name, null, true, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null);

    private static OwnershipLinkDto Link(string id, Guid parentId, Guid childId, decimal percent)
        => new(
            Guid.Parse(id),
            parentId,
            childId,
            OwnershipRelationshipTypeDto.Owns,
            percent,
            true,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            null,
            null);
}
