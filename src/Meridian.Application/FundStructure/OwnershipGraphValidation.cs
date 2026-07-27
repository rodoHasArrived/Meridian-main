using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Shared ownership-graph validation used by both the in-memory and Postgres fund-structure
/// services so structural rules (self-links, dangling nodes, cycles) cannot drift between the
/// durable and non-durable lanes. Cycle detection uses iterative strongly connected component
/// discovery, so deep ownership chains cannot overflow the call stack and every link that
/// participates in any cycle is reported, including links in overlapping cycles.
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

        issues.AddRange(FindCycleIssues(links));
        return issues;
    }

    /// <summary>
    /// Finds strongly connected components with an iterative Kosaraju traversal. Every edge whose
    /// endpoints belong to the same cyclic component participates in at least one directed cycle,
    /// which makes the resulting link evidence complete even when cycles overlap.
    /// </summary>
    private static IEnumerable<OwnershipGraphValidationIssueDto> FindCycleIssues(
        IReadOnlyList<OwnershipLinkDto> links)
    {
        if (links.Count == 0)
        {
            return [];
        }

        var nodes = new HashSet<Guid>();
        var adjacency = new Dictionary<Guid, List<Guid>>();
        var reverseAdjacency = new Dictionary<Guid, List<Guid>>();
        foreach (var link in links)
        {
            nodes.Add(link.ParentNodeId);
            nodes.Add(link.ChildNodeId);
            AddNeighbor(adjacency, link.ParentNodeId, link.ChildNodeId);
            AddNeighbor(reverseAdjacency, link.ChildNodeId, link.ParentNodeId);
        }

        var finishingOrder = BuildFinishingOrder(nodes, adjacency);
        var components = BuildStronglyConnectedComponents(finishingOrder, reverseAdjacency);
        var componentByNode = new Dictionary<Guid, int>(nodes.Count);
        var cyclicComponents = new bool[components.Count];
        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            var component = components[componentIndex];
            cyclicComponents[componentIndex] = component.Count > 1;
            foreach (var nodeId in component)
            {
                componentByNode[nodeId] = componentIndex;
            }
        }

        foreach (var link in links)
        {
            if (link.ParentNodeId == link.ChildNodeId)
            {
                cyclicComponents[componentByNode[link.ParentNodeId]] = true;
            }
        }

        var cycleIssues = new List<OwnershipGraphValidationIssueDto>();
        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            if (!cyclicComponents[componentIndex])
            {
                continue;
            }

            var representativeNodeId = components[componentIndex].Min();
            cycleIssues.Add(new OwnershipGraphValidationIssueDto(
                "ownership.cycle",
                $"Ownership graph contains a cycle at node {representativeNodeId}.",
                NodeId: representativeNodeId));
        }

        foreach (var link in links)
        {
            var parentComponent = componentByNode[link.ParentNodeId];
            if (cyclicComponents[parentComponent]
                && parentComponent == componentByNode[link.ChildNodeId])
            {
                cycleIssues.Add(new OwnershipGraphValidationIssueDto(
                    "ownership.cycle-link",
                    $"Ownership link {link.OwnershipLinkId} participates in a cycle.",
                    link.OwnershipLinkId,
                    link.ChildNodeId));
            }
        }

        return cycleIssues.Distinct();
    }

    private static List<Guid> BuildFinishingOrder(
        IEnumerable<Guid> nodes,
        IReadOnlyDictionary<Guid, List<Guid>> adjacency)
    {
        var visited = new HashSet<Guid>();
        var finishingOrder = new List<Guid>();
        foreach (var rootNodeId in nodes)
        {
            if (!visited.Add(rootNodeId))
            {
                continue;
            }

            var frames = new Stack<PathFrame>();
            frames.Push(new PathFrame(rootNodeId));
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (adjacency.TryGetValue(frame.NodeId, out var children)
                    && frame.NextChildIndex < children.Count)
                {
                    var childNodeId = children[frame.NextChildIndex];
                    frame.NextChildIndex++;
                    if (visited.Add(childNodeId))
                    {
                        frames.Push(new PathFrame(childNodeId));
                    }

                    continue;
                }

                frames.Pop();
                finishingOrder.Add(frame.NodeId);
            }
        }

        return finishingOrder;
    }

    private static List<List<Guid>> BuildStronglyConnectedComponents(
        List<Guid> finishingOrder,
        IReadOnlyDictionary<Guid, List<Guid>> reverseAdjacency)
    {
        var assigned = new HashSet<Guid>();
        var components = new List<List<Guid>>();
        for (var i = finishingOrder.Count - 1; i >= 0; i--)
        {
            var rootNodeId = finishingOrder[i];
            if (!assigned.Add(rootNodeId))
            {
                continue;
            }

            var component = new List<Guid>();
            var pending = new Stack<Guid>();
            pending.Push(rootNodeId);
            while (pending.Count > 0)
            {
                var nodeId = pending.Pop();
                component.Add(nodeId);
                if (!reverseAdjacency.TryGetValue(nodeId, out var parents))
                {
                    continue;
                }

                foreach (var parentNodeId in parents)
                {
                    if (assigned.Add(parentNodeId))
                    {
                        pending.Push(parentNodeId);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static void AddNeighbor(
        Dictionary<Guid, List<Guid>> adjacency,
        Guid nodeId,
        Guid neighborNodeId)
    {
        if (!adjacency.TryGetValue(nodeId, out var neighbors))
        {
            neighbors = [];
            adjacency[nodeId] = neighbors;
        }

        neighbors.Add(neighborNodeId);
    }

    private sealed class PathFrame(Guid nodeId)
    {
        public Guid NodeId { get; } = nodeId;
        public int NextChildIndex { get; set; }
    }
}
