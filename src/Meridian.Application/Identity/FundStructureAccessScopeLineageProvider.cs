using Meridian.Application.FundStructure;
using Meridian.Identity.Auth;
using Meridian.Contracts.FundStructure;
using Meridian.Identity;

namespace Meridian.Application.Identity;

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

        var graph = await _fundStructureService.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(ActiveOnly: false, AsOf: DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        var nodeKinds = graph.Nodes.ToDictionary(
            static node => node.NodeId,
            static node => ToAccessScopeKind(node.Kind));
        var parentByChild = graph.OwnershipLinks
            .Where(static link => link.EffectiveTo is null || link.EffectiveTo > DateTimeOffset.UtcNow)
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

    private static AccessScopeKindDto ToAccessScopeKind(FundStructureNodeKindDto kind)
        => kind switch
        {
            FundStructureNodeKindDto.Organization => AccessScopeKindDto.Organization,
            FundStructureNodeKindDto.Business => AccessScopeKindDto.Business,
            FundStructureNodeKindDto.Client => AccessScopeKindDto.Client,
            FundStructureNodeKindDto.Fund => AccessScopeKindDto.Fund,
            FundStructureNodeKindDto.Sleeve => AccessScopeKindDto.Sleeve,
            FundStructureNodeKindDto.Vehicle => AccessScopeKindDto.Vehicle,
            FundStructureNodeKindDto.InvestmentPortfolio => AccessScopeKindDto.InvestmentPortfolio,
            FundStructureNodeKindDto.Entity => AccessScopeKindDto.LegalEntity,
            FundStructureNodeKindDto.Account => AccessScopeKindDto.Account,
            _ => AccessScopeKindDto.Global
        };
}
