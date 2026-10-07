using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;

namespace Meridian.FinancialOperations.Consolidation;

/// <summary>
/// Resolves the initial consolidation perimeter from the tenant-authorized ownership graph.
/// Caller-supplied member lists and beneficial-owner profile text cannot establish ownership.
/// </summary>
public sealed class ConsolidationPerimeterResolver(IFundStructureService structure)
{
    private readonly IFundStructureService _structure = structure ?? throw new ArgumentNullException(nameof(structure));

    public const string FirstSliceLimitation =
        "Exactly two direct, wholly owned legal entities under one fund ownership root; one shared base currency; "
        + "no indirect ownership, partial ownership, noncontrolling interests, or currency translation. "
        + "Ownership applies from EffectiveFrom inclusive to EffectiveTo exclusive.";

    public async Task<ConsolidationPerimeter> ResolveAsync(
        Guid organizationId,
        Guid ownershipRootId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || ownershipRootId == Guid.Empty)
            throw new ArgumentException("Consolidation requires organization and ownership-root identifiers.");
        ct.ThrowIfCancellationRequested();

        // Retain all authorized organizations when checking competing owners. Filtering by the
        // requested organization first would conceal an ownership claim from another organization.
        var graph = await _structure.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(ActiveOnly: false, AsOf: asOf), ct).ConfigureAwait(false);
        var organization = RequireSingle(graph.Organizations.Where(item => item.OrganizationId == organizationId), "organization");
        var root = RequireSingle(graph.Funds.Where(item => item.FundId == ownershipRootId), "fund ownership root");
        var business = RequireSingle(graph.Businesses.Where(item => item.BusinessId == root.BusinessId), "root business");
        if (business.OrganizationId != organizationId)
            throw new InvalidOperationException("The ownership root does not belong to the requested organization.");

        RequireActive(organization.IsActive, organization.EffectiveFrom, organization.EffectiveTo, asOf, "organization");
        RequireActive(business.IsActive, business.EffectiveFrom, business.EffectiveTo, asOf, "business");
        RequireActive(root.IsActive, root.EffectiveFrom, root.EffectiveTo, asOf, "ownership root");
        RequireNode(graph, organizationId, FundStructureNodeKindDto.Organization, asOf);
        RequireNode(graph, business.BusinessId, FundStructureNodeKindDto.Business, asOf);
        RequireNode(graph, ownershipRootId, FundStructureNodeKindDto.Fund, asOf);

        // Governance mutation/replacement policy uses half-open ownership windows. The graph's
        // general projection can also return a link at its expiry, so apply that rule explicitly.
        var links = graph.OwnershipLinks.Where(link => link.EffectiveFrom <= asOf
            && (link.EffectiveTo is null || asOf < link.EffectiveTo.Value)).ToArray();
        var organizationLink = RequireSingle(links.Where(link => link.ChildNodeId == business.BusinessId
            && link.RelationshipType == OwnershipRelationshipTypeDto.Owns), "organization ownership link");
        var businessLink = RequireSingle(links.Where(link => link.ChildNodeId == ownershipRootId
            && link.RelationshipType == OwnershipRelationshipTypeDto.Operates), "fund operating link");
        if (organizationLink.ParentNodeId != organizationId || businessLink.ParentNodeId != business.BusinessId)
            throw new InvalidOperationException("The authoritative ownership path disagrees with the requested organization and root.");

        var memberLinks = links.Where(link => link.ParentNodeId == ownershipRootId
            && link.RelationshipType == OwnershipRelationshipTypeDto.Owns).ToArray();
        if (memberLinks.Length != 2 || memberLinks.Select(link => link.ChildNodeId).Distinct().Count() != 2)
            throw new InvalidOperationException("The first consolidation slice requires exactly two directly owned legal entities.");

        var entities = new List<LegalEntitySummaryDto>(2);
        foreach (var link in memberLinks)
        {
            if (link.OwnershipPercent != 100m)
                throw new InvalidOperationException("Every entity requires explicit authoritative 100% ownership; partial or unspecified ownership is unsupported.");
            if (links.Count(candidate => candidate.ChildNodeId == link.ChildNodeId
                && candidate.RelationshipType == OwnershipRelationshipTypeDto.Owns) != 1)
                throw new InvalidOperationException("Overlapping or competing ownership claims make the consolidation perimeter ambiguous.");

            var entity = RequireSingle(graph.Entities.Where(item => item.EntityId == link.ChildNodeId), "owned legal entity");
            RequireActive(entity.IsActive, entity.EffectiveFrom, entity.EffectiveTo, asOf, "owned legal entity");
            RequireNode(graph, entity.EntityId, FundStructureNodeKindDto.Entity, asOf);
            entities.Add(entity);
        }

        var currency = NormalizeCurrency(entities[0].BaseCurrency);
        if (!StringComparer.Ordinal.Equals(currency, NormalizeCurrency(entities[1].BaseCurrency)))
            throw new InvalidOperationException("Both consolidation entities must use the same base currency; currency translation is unsupported.");

        var evidence = memberLinks.Concat([organizationLink, businessLink]).OrderBy(link => link.OwnershipLinkId).ToArray();
        if (evidence.Select(link => link.OwnershipLinkId).Distinct().Count() != evidence.Length
            || evidence.Any(link => link.OwnershipLinkId == Guid.Empty))
            throw new InvalidOperationException("Ownership evidence requires unique, nonempty link identifiers.");

        return new ConsolidationPerimeter(organizationId, ownershipRootId, asOf, currency,
            entities.OrderBy(entity => entity.EntityId).ToArray(), evidence, FirstSliceLimitation);
    }

    private static T RequireSingle<T>(IEnumerable<T> items, string description)
    {
        var matches = items.Take(2).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Consolidation requires one authoritative {description}; it is missing or ambiguous.");
        return matches[0];
    }

    private static void RequireNode(OrganizationStructureGraphDto graph, Guid id, FundStructureNodeKindDto kind, DateTimeOffset asOf)
    {
        var node = RequireSingle(graph.Nodes.Where(item => item.NodeId == id), "structure node");
        if (node.Kind != kind)
            throw new InvalidOperationException("The ownership node kind disagrees with the authoritative entity summary.");
        RequireActive(node.IsActive, node.EffectiveFrom, node.EffectiveTo, asOf, "structure node");
    }

    private static void RequireActive(bool active, DateTimeOffset from, DateTimeOffset? to, DateTimeOffset asOf, string description)
    {
        if (!active || from > asOf || (to.HasValue && asOf > to.Value))
            throw new InvalidOperationException($"The {description} is not active on the consolidation date.");
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new InvalidOperationException("Consolidation requires an explicit entity base currency.");
        return currency.Trim().ToUpperInvariant();
    }
}

/// <summary>Resolved members and exact effective-dated ownership records retained with a run.</summary>
public sealed record ConsolidationPerimeter(
    Guid OrganizationId,
    Guid OwnershipRootId,
    DateTimeOffset AsOf,
    string Currency,
    IReadOnlyList<LegalEntitySummaryDto> Entities,
    IReadOnlyList<OwnershipLinkDto> OwnershipEvidence,
    string ScopeLimitation);
