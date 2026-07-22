using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Identity.Auth;

namespace Meridian.Identity;

public sealed class FundStructureAccessScopeLineageProvider : IAccessScopeLineageProvider
{
    private readonly IFundStructureService _fundStructureService;

    public FundStructureAccessScopeLineageProvider(IFundStructureService fundStructureService)
    {
        _fundStructureService = fundStructureService ?? throw new ArgumentNullException(nameof(fundStructureService));
    }

    public async Task<IReadOnlyList<AccessScopeRef>> ResolveLineageAsync(
        AccessScopeKindDto scopeKind,
        Guid scopeId,
        CancellationToken ct = default)
    {
        if (scopeKind == AccessScopeKindDto.Global)
        {
            return [];
        }

        var asOf = DateTimeOffset.UtcNow;
        var graph = await _fundStructureService.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(ActiveOnly: false, AsOf: asOf),
            ct).ConfigureAwait(false);
        // Only nodes whose kind maps to a known access scope contribute an authorizing scope.
        // An unmapped node kind is deliberately excluded (least privilege): it never widens
        // authority, while ancestor traversal still continues through it via the ownership links.
        var nodeKinds = new Dictionary<Guid, AccessScopeKindDto>();
        foreach (var node in graph.Nodes)
        {
            if (TryMapAccessScopeKind(node.Kind, out var mappedKind))
            {
                nodeKinds[node.NodeId] = mappedKind;
            }
        }
        var parentByChild = graph.OwnershipLinks
            .Where(link => link.EffectiveTo is null || link.EffectiveTo > asOf)
            .GroupBy(static link => link.ChildNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(link => link.ParentNodeId).ToArray());

        var scopes = new List<AccessScopeRef>();
        var visited = new HashSet<Guid> { scopeId };
        var queue = new Queue<Guid>();
        queue.Enqueue(scopeId);
        while (queue.Count > 0)
        {
            var child = queue.Dequeue();
            if (!parentByChild.TryGetValue(child, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (!visited.Add(parent))
                {
                    continue;
                }

                if (nodeKinds.TryGetValue(parent, out var parentKind))
                {
                    scopes.Add(new AccessScopeRef(parentKind, parent));
                }

                queue.Enqueue(parent);
            }
        }

        return scopes;
    }

    /// <summary>
    /// Maps a fund-structure node kind to its authorizing access scope. Returns
    /// <see langword="false"/> for any unmapped kind so callers can omit it from the scope
    /// lineage rather than defaulting to the highest-privilege <see cref="AccessScopeKindDto.Global"/>
    /// scope (which would widen authority).
    /// </summary>
    private static bool TryMapAccessScopeKind(FundStructureNodeKindDto kind, out AccessScopeKindDto scopeKind)
    {
        switch (kind)
        {
            case FundStructureNodeKindDto.Organization:
                scopeKind = AccessScopeKindDto.Organization;
                return true;
            case FundStructureNodeKindDto.Business:
                scopeKind = AccessScopeKindDto.Business;
                return true;
            case FundStructureNodeKindDto.Client:
                scopeKind = AccessScopeKindDto.Client;
                return true;
            case FundStructureNodeKindDto.Fund:
                scopeKind = AccessScopeKindDto.Fund;
                return true;
            case FundStructureNodeKindDto.Sleeve:
                scopeKind = AccessScopeKindDto.Sleeve;
                return true;
            case FundStructureNodeKindDto.Vehicle:
                scopeKind = AccessScopeKindDto.Vehicle;
                return true;
            case FundStructureNodeKindDto.InvestmentPortfolio:
                scopeKind = AccessScopeKindDto.InvestmentPortfolio;
                return true;
            case FundStructureNodeKindDto.Entity:
                scopeKind = AccessScopeKindDto.LegalEntity;
                return true;
            case FundStructureNodeKindDto.Account:
                scopeKind = AccessScopeKindDto.Account;
                return true;
            default:
                scopeKind = default;
                return false;
        }
    }
}
