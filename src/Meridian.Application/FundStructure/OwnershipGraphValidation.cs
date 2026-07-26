using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Shared ownership-graph validation used by both the in-memory and Postgres fund-structure
/// services so structural rules (self-links, dangling nodes, cycles) cannot drift between the
/// durable and non-durable lanes. Cycle detection uses an iterative depth-first search with an
/// explicit stack, so deep ownership chains cannot overflow the call stack, and only nodes and
/// links that actually participate in a cycle are reported.
/// </summary>
internal static class OwnershipGraphValidation
{
    internal static bool IsOwnershipLinkVisible(OwnershipLinkDto link, bool activeOnly, DateTimeOffset asOf) =>
        !activeOnly || (link.EffectiveFrom <= asOf && (link.EffectiveTo is null || link.EffectiveTo > asOf));

    internal static List<OwnershipGraphValidationIssueDto> Validate(
        IReadOnlyList<OwnershipLinkDto> links,
        Func<Guid, bool> nodeExists)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(nodeExists);

        var issues = new List<OwnershipGraphValidationIssueDto>();
        foreach (var link in links)
        {
            if (link.ParentNodeId == link.ChildNodeId)
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.self-link",
                    "Ownership links cannot point a node to itself.",
                    link.OwnershipLinkId,
                    link.ParentNodeId));
            }

            if (!nodeExists(link.ParentNodeId))
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.parent-not-found",
                    $"Parent node {link.ParentNodeId} was not found.",
                    link.OwnershipLinkId,
                    link.ParentNodeId));
            }

            if (!nodeExists(link.ChildNodeId))
            {
                issues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.child-not-found",
                    $"Child node {link.ChildNodeId} was not found.",
                    link.OwnershipLinkId,
                    link.ChildNodeId));
            }
        }

        var adjacency = links
            .GroupBy(static link => link.ParentNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static link => (link.ChildNodeId, link.OwnershipLinkId)).ToList());
        var visited = new HashSet<Guid>();
        var cycleIssues = new List<OwnershipGraphValidationIssueDto>();
        foreach (var nodeId in adjacency.Keys)
        {
            DetectOwnershipCyclesFrom(nodeId, adjacency, visited, cycleIssues);
        }

        // Overlapping cycles can re-report the same node or link; the DTO is a record, so value
        // equality dedups identical issues while keeping distinct cycles distinct.
        issues.AddRange(cycleIssues.Distinct());
        return issues;
    }

    /// <summary>
    /// Explores the graph from <paramref name="rootNodeId"/> and reports cycles found along the
    /// depth-first traversal. Nodes are marked <paramref name="visited"/> only after their whole
    /// subtree has been explored, so nodes that merely lead into a cycle are never themselves
    /// reported as cyclic on later traversals. A parallel edge that re-enters an already-explored
    /// cycle is not re-attributed per-link; the cycle itself is still reported at its node.
    /// </summary>
    private static void DetectOwnershipCyclesFrom(
        Guid rootNodeId,
        IReadOnlyDictionary<Guid, List<(Guid ChildNodeId, Guid OwnershipLinkId)>> adjacency,
        HashSet<Guid> visited,
        List<OwnershipGraphValidationIssueDto> issues)
    {
        if (visited.Contains(rootNodeId))
        {
            return;
        }

        var frames = new Stack<PathFrame>();
        var pathNodes = new List<Guid>();
        var pathLinks = new List<Guid>(); // pathLinks[i] connects pathNodes[i] -> pathNodes[i + 1]
        var pathIndex = new Dictionary<Guid, int>();

        frames.Push(new PathFrame(rootNodeId));
        pathIndex[rootNodeId] = 0;
        pathNodes.Add(rootNodeId);

        while (frames.Count > 0)
        {
            var frame = frames.Peek();
            if (adjacency.TryGetValue(frame.NodeId, out var children) && frame.NextChildIndex < children.Count)
            {
                var (childNodeId, ownershipLinkId) = children[frame.NextChildIndex];
                frame.NextChildIndex++;

                if (pathIndex.TryGetValue(childNodeId, out var cycleStartIndex))
                {
                    ReportCycle(childNodeId, ownershipLinkId, cycleStartIndex, pathNodes, pathLinks, issues);
                    continue;
                }

                if (visited.Contains(childNodeId))
                {
                    continue;
                }

                pathIndex[childNodeId] = pathNodes.Count;
                pathNodes.Add(childNodeId);
                pathLinks.Add(ownershipLinkId);
                frames.Push(new PathFrame(childNodeId));
                continue;
            }

            frames.Pop();
            visited.Add(frame.NodeId);
            pathIndex.Remove(frame.NodeId);
            pathNodes.RemoveAt(pathNodes.Count - 1);
            if (pathLinks.Count > 0)
            {
                pathLinks.RemoveAt(pathLinks.Count - 1);
            }
        }
    }

    private static void ReportCycle(
        Guid cycleNodeId,
        Guid closingLinkId,
        int cycleStartIndex,
        List<Guid> pathNodes,
        List<Guid> pathLinks,
        List<OwnershipGraphValidationIssueDto> issues)
    {
        issues.Add(new OwnershipGraphValidationIssueDto(
            "ownership.cycle",
            $"Ownership graph contains a cycle at node {cycleNodeId}.",
            NodeId: cycleNodeId));

        // Links on the current path from the cycle's start node to the re-entered node…
        for (var i = cycleStartIndex; i < pathLinks.Count; i++)
        {
            issues.Add(new OwnershipGraphValidationIssueDto(
                "ownership.cycle-link",
                $"Ownership link {pathLinks[i]} participates in a cycle.",
                pathLinks[i],
                pathNodes[i + 1]));
        }

        // …then the closing link back to the cycle's start node.
        issues.Add(new OwnershipGraphValidationIssueDto(
            "ownership.cycle-link",
            $"Ownership link {closingLinkId} participates in a cycle.",
            closingLinkId,
            cycleNodeId));
    }

    private sealed class PathFrame(Guid nodeId)
    {
        public Guid NodeId { get; } = nodeId;
        public int NextChildIndex { get; set; }
    }
}
