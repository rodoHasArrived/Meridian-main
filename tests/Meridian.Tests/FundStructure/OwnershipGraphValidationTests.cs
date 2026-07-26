using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.Tests.FundStructure;

/// <summary>
/// Regression coverage for the shared ownership-graph validator, focused on the historical
/// defect where nodes that merely led into a cycle were themselves reported as cyclic on
/// later traversals because the recursive detector never unwound its visiting set.
/// </summary>
public sealed class OwnershipGraphValidationTests
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validate_AcyclicChain_ReportsNoIssues()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(a, b), Link(b, c), Link(c, d) };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_CycleBehindApproachPath_DoesNotReportApproachNodesAsCyclic()
    {
        // X -> A -> B -> C -> B : only B and C form a cycle; X and A merely lead into it.
        var (x, a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var linkIntoCycle = Link(b, c);
        var closingLink = Link(c, b);
        var links = new[] { Link(x, a), Link(a, b), linkIntoCycle, closingLink };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        var cycleIssues = issues.Where(issue => issue.Code == "ownership.cycle").ToList();
        cycleIssues.Should().HaveCount(1, "one cycle exists regardless of which node the traversal starts from");
        new Guid?[] { b, c }.Should().Contain(
            cycleIssues[0].NodeId,
            "the cycle must be attributed to a node inside it, never to an approach node");

        var cycleLinkIds = issues
            .Where(issue => issue.Code == "ownership.cycle-link")
            .Select(issue => issue.OwnershipLinkId)
            .ToList();
        cycleLinkIds.Should().BeEquivalentTo(
            new Guid?[] { linkIntoCycle.OwnershipLinkId, closingLink.OwnershipLinkId },
            "only the links inside the cycle participate in it");

        issues.Should().NotContain(
            issue => issue.NodeId == x || issue.NodeId == a,
            "nodes that only lead into a cycle are not part of it");
    }

    [Fact]
    public void Validate_MultipleRootsIntoOneCycle_ReportsTheCycleOnce()
    {
        // Two disjoint approach chains converge on the same two-node cycle.
        var (root1, root2, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(root1, b), Link(root2, b), Link(b, c), Link(c, b) };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        issues.Where(issue => issue.Code == "ownership.cycle").Should().HaveCount(1);
        issues.Should().NotContain(issue => issue.NodeId == root1 || issue.NodeId == root2);
    }

    [Fact]
    public void Validate_TwoIndependentCycles_ReportsBoth()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(a, b), Link(b, a), Link(c, d), Link(d, c) };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        issues.Where(issue => issue.Code == "ownership.cycle").Should().HaveCount(2);
        issues.Where(issue => issue.Code == "ownership.cycle-link").Should().HaveCount(4);
    }

    [Fact]
    public void Validate_OverlappingCycles_DoesNotDuplicateSharedNodeOrLinkIssues()
    {
        // A -> B -> A and A -> B -> C -> A share node A and link A->B; both cycles are found on
        // one traversal, but the shared node and link must not be reported twice.
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(a, b), Link(b, a), Link(b, c), Link(c, a) };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        // Which of the two overlapping cycles is attributed first depends on traversal order, so
        // assert the order-independent invariants: no duplicate issues, and only real cycle links.
        issues.Where(issue => issue.Code == "ownership.cycle").Should()
            .NotBeEmpty().And.OnlyHaveUniqueItems();
        var cycleLinkIds = issues
            .Where(issue => issue.Code == "ownership.cycle-link")
            .Select(issue => issue.OwnershipLinkId)
            .ToList();
        cycleLinkIds.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
        cycleLinkIds.Should().BeSubsetOf(links.Select(link => (Guid?)link.OwnershipLinkId));
    }

    [Fact]
    public void Validate_DiamondWithoutCycle_ReportsNoCycle()
    {
        // A -> B, A -> C, B -> D, C -> D : converging paths are not a cycle.
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(a, b), Link(a, c), Link(b, d), Link(c, d) };

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_DeepChain_DoesNotOverflowTheStack()
    {
        var nodes = Enumerable.Range(0, 50_000).Select(_ => Guid.NewGuid()).ToArray();
        var links = new OwnershipLinkDto[nodes.Length - 1];
        for (var i = 0; i < links.Length; i++)
        {
            links[i] = Link(nodes[i], nodes[i + 1]);
        }

        var issues = OwnershipGraphValidation.Validate(links, _ => true);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SelfLink_ReportsSelfLinkAndCycle()
    {
        var node = Guid.NewGuid();
        var selfLink = Link(node, node);

        var issues = OwnershipGraphValidation.Validate(new[] { selfLink }, _ => true);

        issues.Should().Contain(issue => issue.Code == "ownership.self-link" && issue.OwnershipLinkId == selfLink.OwnershipLinkId);
        issues.Should().Contain(issue => issue.Code == "ownership.cycle" && issue.NodeId == node);
    }

    [Fact]
    public void Validate_MissingNodes_ReportsParentAndChildNotFound()
    {
        var (known, missing) = (Guid.NewGuid(), Guid.NewGuid());
        var links = new[] { Link(known, missing), Link(missing, known) };

        var issues = OwnershipGraphValidation.Validate(links, nodeId => nodeId == known);

        issues.Should().Contain(issue => issue.Code == "ownership.child-not-found" && issue.NodeId == missing);
        issues.Should().Contain(issue => issue.Code == "ownership.parent-not-found" && issue.NodeId == missing);
    }

    private static OwnershipLinkDto Link(Guid parentNodeId, Guid childNodeId) => new(
        Guid.NewGuid(),
        parentNodeId,
        childNodeId,
        OwnershipRelationshipTypeDto.Owns,
        OwnershipPercent: null,
        IsPrimary: false,
        EffectiveFrom,
        EffectiveTo: null,
        Notes: null);
}
