using Meridian.Contracts.FundStructure;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static async Task<IResult?> RequireOrganizationGraphResponseAccessAsync(
        OrganizationStructureGraphDto graph, HttpContext context)
    {
        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            return null;
        }

        // NodeId/NodeKind are presentation filters. The service intentionally retains the typed
        // summary collections, so authorizing only graph.Nodes would disclose the wider payload.
        // Refuse insufficient authority without changing the shared domain projection contract.
        var scopes = new HashSet<(AccessScopeKindDto Kind, Guid Id)>();
        void Add(AccessScopeKindDto kind, Guid? id)
        {
            if (id.HasValue)
            {
                scopes.Add((kind, id.Value));
            }
        }
        void AddMany(AccessScopeKindDto kind, IEnumerable<Guid> ids)
        {
            foreach (var id in ids)
            {
                Add(kind, id);
            }
        }

        // Parent and child ids are scope disclosures too, including references to summaries that
        // an effective-date or business filter removed. They need the same independent grants.
        foreach (var organization in graph.Organizations)
        {
            Add(AccessScopeKindDto.Organization, organization.OrganizationId);
            AddMany(AccessScopeKindDto.Business, organization.BusinessIds);
        }
        foreach (var business in graph.Businesses)
        {
            Add(AccessScopeKindDto.Business, business.BusinessId);
            Add(AccessScopeKindDto.Organization, business.OrganizationId);
            AddMany(AccessScopeKindDto.Client, business.ClientIds);
            AddMany(AccessScopeKindDto.Fund, business.FundIds);
            AddMany(AccessScopeKindDto.InvestmentPortfolio, business.InvestmentPortfolioIds);
        }
        foreach (var client in graph.Clients)
        {
            Add(AccessScopeKindDto.Client, client.ClientId);
            Add(AccessScopeKindDto.Business, client.BusinessId);
            AddMany(AccessScopeKindDto.InvestmentPortfolio, client.InvestmentPortfolioIds);
        }
        foreach (var fund in graph.Funds)
        {
            Add(AccessScopeKindDto.Fund, fund.FundId);
            Add(AccessScopeKindDto.Business, fund.BusinessId);
            AddMany(AccessScopeKindDto.Sleeve, fund.SleeveIds);
            AddMany(AccessScopeKindDto.Vehicle, fund.VehicleIds);
            AddMany(AccessScopeKindDto.LegalEntity, fund.EntityIds);
            AddMany(AccessScopeKindDto.InvestmentPortfolio, fund.InvestmentPortfolioIds);
            AddMany(AccessScopeKindDto.Account, fund.AccountIds);
        }
        foreach (var sleeve in graph.Sleeves)
        {
            Add(AccessScopeKindDto.Sleeve, sleeve.SleeveId);
            Add(AccessScopeKindDto.Fund, sleeve.FundId);
            AddMany(AccessScopeKindDto.InvestmentPortfolio, sleeve.InvestmentPortfolioIds);
            AddMany(AccessScopeKindDto.Account, sleeve.AccountIds);
        }
        foreach (var vehicle in graph.Vehicles)
        {
            Add(AccessScopeKindDto.Vehicle, vehicle.VehicleId);
            Add(AccessScopeKindDto.Fund, vehicle.FundId);
            Add(AccessScopeKindDto.LegalEntity, vehicle.LegalEntityId);
            AddMany(AccessScopeKindDto.InvestmentPortfolio, vehicle.InvestmentPortfolioIds);
            AddMany(AccessScopeKindDto.Account, vehicle.AccountIds);
        }
        foreach (var entity in graph.Entities)
        {
            Add(AccessScopeKindDto.LegalEntity, entity.EntityId);
        }
        foreach (var portfolio in graph.InvestmentPortfolios)
        {
            Add(AccessScopeKindDto.InvestmentPortfolio, portfolio.InvestmentPortfolioId);
            Add(AccessScopeKindDto.Business, portfolio.BusinessId);
            Add(AccessScopeKindDto.Client, portfolio.ClientId);
            Add(AccessScopeKindDto.Fund, portfolio.FundId);
            Add(AccessScopeKindDto.Sleeve, portfolio.SleeveId);
            Add(AccessScopeKindDto.Vehicle, portfolio.VehicleId);
            Add(AccessScopeKindDto.LegalEntity, portfolio.EntityId);
            AddMany(AccessScopeKindDto.Account, portfolio.AccountIds);
        }
        foreach (var account in graph.Accounts)
        {
            Add(AccessScopeKindDto.Account, account.AccountId);
            Add(AccessScopeKindDto.LegalEntity, account.EntityId);
            Add(AccessScopeKindDto.Fund, account.FundId);
            Add(AccessScopeKindDto.Sleeve, account.SleeveId);
            Add(AccessScopeKindDto.Vehicle, account.VehicleId);
            // PortfolioId can be an external brokerage reference, even when GUID-shaped. Its
            // text is account metadata; structural portfolios are authorized through their typed
            // summaries/references above. Strategy/run and evidence references likewise remain
            // attributes governed by their owning node, not invented FundStructure scopes.
        }
        foreach (var node in graph.Nodes)
        {
            var mapped = MapFundStructureNodeScope(node.Kind, node.NodeId);
            if (!mapped.Id.HasValue)
            {
                return EndpointHelpers.Forbidden();
            }
            Add(mapped.Kind, mapped.Id);
        }

        var knownIds = scopes.Select(static scope => scope.Id).ToHashSet();
        if (graph.OwnershipLinks.Any(link => !knownIds.Contains(link.ParentNodeId) || !knownIds.Contains(link.ChildNodeId))
            || graph.Assignments.Any(assignment => !knownIds.Contains(assignment.NodeId)))
        {
            return EndpointHelpers.Forbidden();
        }

        return await HasFundStructureScopesAccessAsync(scopes, context).ConfigureAwait(false)
            ? null
            : EndpointHelpers.Forbidden();
    }

    private static async Task<bool> HasFundStructureScopesAccessAsync(
        IEnumerable<(AccessScopeKindDto Kind, Guid Id)> scopes, HttpContext context)
    {
        // Batch by the bounded set of node kinds; never issue an authorization/lineage read per
        // account or entity. Missing batch decisions fail closed just like explicit denials.
        foreach (var group in scopes.GroupBy(static scope => scope.Kind))
        {
            var requested = group.Select(static scope => scope.Id).ToHashSet();
            var allowed = await EndpointAuthorization.AuthorizeScopedManyAsync(
                context, UserPermission.ManageFundStructure, group.Key, requested,
                context.RequestAborted).ConfigureAwait(false);
            if (!requested.IsSubsetOf(allowed))
            {
                return false;
            }
        }
        return true;
    }
}
