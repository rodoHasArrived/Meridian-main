namespace Meridian.Application.FundStructure;

/// <summary>Why the derivation refused to attribute a node to a tenant.</summary>
public enum FundStructureTenantQuarantineReason
{
    /// <summary>
    /// The node's ownership resolves to more than one tenant — a genuinely shared ancestor.
    /// Attributing it to either claimant would hand that claimant the other's subtree.
    /// </summary>
    MixedOwnership,

    /// <summary>
    /// Nothing the registry can attribute reaches this node in either direction, so there is no
    /// evidence of ownership to derive from.
    /// </summary>
    Underivable,
}

/// <summary>A node the derivation would not guess at, and what it saw.</summary>
public sealed record FundStructureTenantQuarantineEntry(
    Guid NodeId,
    string NodeKind,
    FundStructureTenantQuarantineReason Reason,
    IReadOnlyList<string> CandidateTenantIds);

/// <summary>The ownership edges and node kinds the derivation walks.</summary>
/// <param name="NodeKinds">Every node in the graph, mapped to its kind label for quarantine records.</param>
/// <param name="Edges">Parent-to-child ownership edges. Edges naming unknown nodes are ignored.</param>
public sealed record FundStructureTenantAttributionGraph(
    IReadOnlyDictionary<Guid, string> NodeKinds,
    IReadOnlyList<FundStructureOwnershipEdge> Edges);

/// <summary>One parent-to-child ownership edge.</summary>
public readonly record struct FundStructureOwnershipEdge(Guid ParentNodeId, Guid ChildNodeId);

/// <summary>What the derivation concluded.</summary>
/// <param name="Attributions">Nodes the derivation attributed, mapped to their owning tenant.</param>
/// <param name="Quarantined">Nodes it declined to attribute, with the reason and the claimants seen.</param>
public sealed record FundStructureTenantAttributionResult(
    IReadOnlyDictionary<Guid, string> Attributions,
    IReadOnlyList<FundStructureTenantQuarantineEntry> Quarantined);

/// <summary>
/// Derives fund-structure tenant ownership from the fund profiles <c>fund_profile_tenancy</c> can
/// attribute, so the hierarchy can be stamped before any reader is tightened to fail closed.
/// </summary>
/// <remarks>
/// <para><b>Why a derivation and not a stamp.</b> The registry is fund-keyed. The hierarchy above a
/// fund — organizations, businesses, clients — and the structure below it were never attributed to
/// anyone. On a populated database all three naive options are wrong: stamping every row to the
/// upgrading caller misassigns shared ancestors, leaving them null preserves the very leak being
/// closed, and rejecting null hides the retained graph from every reader. So ownership is
/// <i>derived</i>: downward from the seeds, which is sound because a fund owns its subtree, and
/// upward only where every attributed descendant agrees. Anything else is quarantined rather than
/// guessed.</para>
///
/// <para><b>Direction matters and the two directions are not symmetric.</b> Downward inheritance is
/// an assertion about ownership: a sleeve of tenant A's fund is tenant A's. Upward inference is an
/// assertion about exclusivity, and it only holds when the ancestor has no other attributed
/// descendant — which is exactly the check that separates a single-tenant parent from a shared one.
/// A shared ancestor is not an error in the data; it is a modelling fact the derivation is not
/// entitled to resolve.</para>
///
/// <para>Pure and graph-shaped on purpose: the Postgres fund-structure suite is skipped in CI for
/// want of a database, and an attribution bug that ships uncaught either leaks a subtree or hides
/// one. Keeping the decision here lets every branch be proven without one.</para>
/// </remarks>
public static class FundStructureTenantAttribution
{
    /// <summary>
    /// Derives ownership for every node in <paramref name="graph"/> from <paramref name="seeds"/>.
    /// </summary>
    /// <param name="graph">The ownership graph. Cycles are tolerated; propagation runs to a fixpoint.</param>
    /// <param name="seeds">
    /// Nodes whose owner the registry states directly — in practice fund-structure nodes reached
    /// through <c>ledger_books (fund_profile_id, fund_structure_node_id)</c>. A seed is authoritative
    /// for its own node and is never overridden by inheritance.
    /// </param>
    public static FundStructureTenantAttributionResult Derive(
        FundStructureTenantAttributionGraph graph,
        IReadOnlyDictionary<Guid, string> seeds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(seeds);

        var nodes = graph.NodeKinds;
        var children = BuildAdjacency(graph.Edges, nodes, childrenOfParent: true);
        var parents = BuildAdjacency(graph.Edges, nodes, childrenOfParent: false);

        var normalizedSeeds = NormalizeSeeds(seeds, nodes);

        // Downward: a node inherits every tenant that owns a node above it. More than one arriving
        // means the node sits under two owners, which is a conflict, not a merge.
        var inherited = Propagate(nodes.Keys, normalizedSeeds, children);

        var attributions = new Dictionary<Guid, string>();
        var candidates = new Dictionary<Guid, SortedSet<string>>();

        foreach (var nodeId in nodes.Keys)
        {
            if (normalizedSeeds.TryGetValue(nodeId, out var seeded))
            {
                // The registry speaks directly about this node; nothing inferred outranks it.
                attributions[nodeId] = seeded;
                continue;
            }

            if (inherited.TryGetValue(nodeId, out var reaching) && reaching.Count > 0)
            {
                if (reaching.Count == 1)
                {
                    attributions[nodeId] = reaching.First();
                }
                else
                {
                    candidates[nodeId] = reaching;
                }
            }
        }

        // Upward: an ancestor takes its descendants' tenant only when they all agree. Seeded from
        // what resolved downward, then pushed child-to-parent to a fixpoint.
        var descendantOwners = Propagate(nodes.Keys, attributions, parents);

        var quarantined = new List<FundStructureTenantQuarantineEntry>();

        foreach (var nodeId in nodes.Keys)
        {
            if (attributions.ContainsKey(nodeId))
            {
                continue;
            }

            if (candidates.TryGetValue(nodeId, out var conflicting))
            {
                quarantined.Add(Quarantine(
                    nodeId, nodes, FundStructureTenantQuarantineReason.MixedOwnership, conflicting));
                continue;
            }

            var owners = descendantOwners.TryGetValue(nodeId, out var seen) ? seen : [];
            switch (owners.Count)
            {
                case 1:
                    attributions[nodeId] = owners.First();
                    break;

                case 0:
                    quarantined.Add(Quarantine(
                        nodeId, nodes, FundStructureTenantQuarantineReason.Underivable, owners));
                    break;

                default:
                    quarantined.Add(Quarantine(
                        nodeId, nodes, FundStructureTenantQuarantineReason.MixedOwnership, owners));
                    break;
            }
        }

        return new FundStructureTenantAttributionResult(
            attributions,
            [.. quarantined.OrderBy(entry => entry.NodeId)]);
    }

    /// <summary>
    /// Pushes each node's tenant set along <paramref name="successors"/> until nothing changes.
    /// </summary>
    /// <remarks>
    /// A worklist rather than a recursive walk: the ownership graph is operator-maintained, and a
    /// cycle in it must leave the derivation reporting a conflict, not overflowing the stack.
    /// </remarks>
    private static Dictionary<Guid, SortedSet<string>> Propagate(
        IEnumerable<Guid> nodeIds,
        IReadOnlyDictionary<Guid, string> origins,
        IReadOnlyDictionary<Guid, List<Guid>> successors)
    {
        var reaching = new Dictionary<Guid, SortedSet<string>>();
        var worklist = new Queue<Guid>();

        foreach (var nodeId in nodeIds)
        {
            reaching[nodeId] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var (nodeId, tenantId) in origins)
        {
            if (reaching.TryGetValue(nodeId, out var set) && set.Add(tenantId))
            {
                worklist.Enqueue(nodeId);
            }
        }

        while (worklist.Count > 0)
        {
            var nodeId = worklist.Dequeue();
            if (!successors.TryGetValue(nodeId, out var next))
            {
                continue;
            }

            var source = reaching[nodeId];
            foreach (var successorId in next)
            {
                var target = reaching[successorId];
                var changed = false;
                foreach (var tenantId in source)
                {
                    changed |= target.Add(tenantId);
                }

                if (changed)
                {
                    worklist.Enqueue(successorId);
                }
            }
        }

        return reaching;
    }

    private static Dictionary<Guid, List<Guid>> BuildAdjacency(
        IReadOnlyList<FundStructureOwnershipEdge> edges,
        IReadOnlyDictionary<Guid, string> nodes,
        bool childrenOfParent)
    {
        var adjacency = new Dictionary<Guid, List<Guid>>();
        foreach (var edge in edges)
        {
            // An edge naming a node the graph does not carry cannot inform ownership of anything the
            // derivation will stamp, so it is ignored rather than half-applied.
            if (!nodes.ContainsKey(edge.ParentNodeId) || !nodes.ContainsKey(edge.ChildNodeId))
            {
                continue;
            }

            var from = childrenOfParent ? edge.ParentNodeId : edge.ChildNodeId;
            var to = childrenOfParent ? edge.ChildNodeId : edge.ParentNodeId;

            if (!adjacency.TryGetValue(from, out var list))
            {
                list = [];
                adjacency[from] = list;
            }

            list.Add(to);
        }

        return adjacency;
    }

    private static Dictionary<Guid, string> NormalizeSeeds(
        IReadOnlyDictionary<Guid, string> seeds,
        IReadOnlyDictionary<Guid, string> nodes)
    {
        var normalized = new Dictionary<Guid, string>();
        foreach (var (nodeId, tenantId) in seeds)
        {
            // A blank tenant is not an attribution; treating it as one would stamp the graph to "".
            if (string.IsNullOrWhiteSpace(tenantId) || !nodes.ContainsKey(nodeId))
            {
                continue;
            }

            normalized[nodeId] = tenantId.Trim();
        }

        return normalized;
    }

    private static FundStructureTenantQuarantineEntry Quarantine(
        Guid nodeId,
        IReadOnlyDictionary<Guid, string> nodes,
        FundStructureTenantQuarantineReason reason,
        SortedSet<string> candidates)
        => new(
            nodeId,
            nodes.TryGetValue(nodeId, out var kind) ? kind : "Unknown",
            reason,
            [.. candidates]);
}
