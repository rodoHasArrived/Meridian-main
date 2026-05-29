using Meridian.Contracts.FundStructure;
using Meridian.Ui.Shared.Services;

namespace Meridian.FundStructure.Tests;

/// <summary>Guards the operator setup-review scenario where ownership must be explicit before setup completion.</summary>
public sealed class FundStructureOwnershipReviewServiceTests
{
    [Fact]
    public void Project_StableGraph_OrdersRowsAndBuildsRollupSummary()
    {
        var service = new FundStructureOwnershipReviewService();
        var asOf = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var graph = CreateGraph(asOf);

        var review = service.Project(graph, new OwnershipReviewQueryDto(AsOf: asOf));

        Assert.Equal(2, review.Summary.TotalLinkCount);
        Assert.Equal(2, review.Summary.ActiveLinkCount);
        Assert.Equal(1, review.Summary.PrimaryLinkCount);
        Assert.Equal(80m, review.Summary.PrimaryOwnershipPercentTotal);
        Assert.Equal("Ownership review ready", review.Summary.StatusLabel);
        Assert.Equal("Organization ORG — Organization owns Fund FUND — Flagship Fund", review.Links[0].NodeDisplayLabel);
        Assert.Equal("Fund FUND — Flagship Fund allocates to InvestmentPortfolio PORT — Core Portfolio", review.Links[1].NodeDisplayLabel);
    }

    [Fact]
    public void Project_EmptyGraph_ReturnsOperatorEmptyState()
    {
        var service = new FundStructureOwnershipReviewService();
        var asOf = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);

        var review = service.Project(new FundStructureGraphDto(Array.Empty<FundStructureNodeDto>(), Array.Empty<OwnershipLinkDto>(), Array.Empty<FundStructureAssignmentDto>()), new OwnershipReviewQueryDto(AsOf: asOf));

        Assert.Empty(review.Links);
        Assert.Equal("No ownership links to review", review.Summary.StatusLabel);
        Assert.Contains(review.EmptyStateMessages, message => message.Contains("No ownership links", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_InvalidOwnershipLink_ReturnsWarningsAndRemediation()
    {
        var service = new FundStructureOwnershipReviewService();
        var asOf = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var parent = Node(Guid.Parse("10000000-0000-0000-0000-000000000001"), FundStructureNodeKindDto.Organization, "ORG", "Organization", asOf);
        var child = Node(Guid.Parse("20000000-0000-0000-0000-000000000001"), FundStructureNodeKindDto.Fund, "FUND", "Flagship Fund", asOf);
        var link = new OwnershipLinkDto(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            parent.NodeId,
            child.NodeId,
            OwnershipRelationshipTypeDto.Owns,
            null,
            true,
            asOf,
            null,
            null);

        var review = service.Project(new FundStructureGraphDto([parent, child], [link], []), new OwnershipReviewQueryDto(AsOf: asOf));

        var row = Assert.Single(review.Links);
        Assert.Equal(OwnershipReviewValidationStateDto.Warning, row.ValidationState);
        Assert.Contains(row.BlockingMessages, message => message.Contains("explicit percent", StringComparison.Ordinal));
        Assert.Contains(row.SuggestedRemediationActions, action => action.Contains("add the ownership percent", StringComparison.Ordinal));
        Assert.Equal(1, review.Summary.InvalidLinkCount);
    }

    [Fact]
    public void Project_ActiveOnly_FiltersExpiredLinksAtAsOfDate()
    {
        var service = new FundStructureOwnershipReviewService();
        var asOf = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);
        var graph = CreateGraph(asOf) with
        {
            OwnershipLinks = [
                ..CreateGraph(asOf).OwnershipLinks,
                new OwnershipLinkDto(
                    Guid.Parse("30000000-0000-0000-0000-000000000099"),
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    OwnershipRelationshipTypeDto.Advises,
                    null,
                    false,
                    asOf.AddDays(-10),
                    asOf.AddDays(-1),
                    null)
            ]
        };

        var review = service.Project(graph, new OwnershipReviewQueryDto(ActiveOnly: true, AsOf: asOf));

        Assert.Equal(2, review.Links.Count);
        Assert.All(review.Links, link => Assert.True(link.EffectiveWindow.IsActiveAsOf));
    }

    private static FundStructureGraphDto CreateGraph(DateTimeOffset asOf)
    {
        var organization = Node(Guid.Parse("10000000-0000-0000-0000-000000000001"), FundStructureNodeKindDto.Organization, "ORG", "Organization", asOf);
        var fund = Node(Guid.Parse("20000000-0000-0000-0000-000000000001"), FundStructureNodeKindDto.Fund, "FUND", "Flagship Fund", asOf);
        var portfolio = Node(Guid.Parse("20000000-0000-0000-0000-000000000002"), FundStructureNodeKindDto.InvestmentPortfolio, "PORT", "Core Portfolio", asOf);
        var owns = new OwnershipLinkDto(Guid.Parse("30000000-0000-0000-0000-000000000001"), organization.NodeId, fund.NodeId, OwnershipRelationshipTypeDto.Owns, 80m, true, asOf.AddDays(-1), null, null);
        var allocates = new OwnershipLinkDto(Guid.Parse("30000000-0000-0000-0000-000000000002"), fund.NodeId, portfolio.NodeId, OwnershipRelationshipTypeDto.AllocatesTo, 100m, false, asOf.AddDays(-1), null, null);
        return new FundStructureGraphDto([portfolio, fund, organization], [allocates, owns], []);
    }

    private static FundStructureNodeDto Node(Guid id, FundStructureNodeKindDto kind, string code, string name, DateTimeOffset asOf)
        => new(id, kind, code, name, null, true, asOf.AddDays(-30), null);
}
