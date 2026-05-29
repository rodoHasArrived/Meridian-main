using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Shared.Services;

public sealed class FundStructureOwnershipReviewService
{
    private static readonly string[] EmptyMessages =
    [
        "No ownership links exist yet. Add ownership, advisory, operating, custody, clearing, or allocation links before completing setup.",
        "Operators can still create draft entities, but setup is not operationally complete until at least one ownership relationship is reviewed."
    ];

    private readonly IFundStructureService? _fundStructureService;

    public FundStructureOwnershipReviewService()
    {
    }

    public FundStructureOwnershipReviewService(IFundStructureService fundStructureService)
    {
        _fundStructureService = fundStructureService ?? throw new ArgumentNullException(nameof(fundStructureService));
    }

    public async Task<OwnershipReviewDto> GetReviewAsync(OwnershipReviewQueryDto query, CancellationToken ct = default)
    {
        if (_fundStructureService is null)
        {
            throw new InvalidOperationException("Fund-structure service is required to load ownership review data.");
        }

        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var graph = await _fundStructureService.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(ActiveOnly: query.ActiveOnly, AsOf: asOf),
            ct).ConfigureAwait(false);

        return Project(graph, new OwnershipReviewQueryDto(query.ActiveOnly, asOf));
    }

    public OwnershipReviewDto Project(OrganizationStructureGraphDto graph, OwnershipReviewQueryDto? query = null)
        => Project(new FundStructureGraphDto(graph.Nodes, graph.OwnershipLinks, graph.Assignments), query);

    public OwnershipReviewDto Project(FundStructureGraphDto graph, OwnershipReviewQueryDto? query = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var asOf = query?.AsOf ?? DateTimeOffset.UtcNow;
        var activeOnly = query?.ActiveOnly ?? false;
        var nodes = graph.Nodes.ToDictionary(static node => node.NodeId);
        var links = graph.OwnershipLinks
            .Where(link => !activeOnly || IsActive(link, asOf))
            .OrderBy(static link => link.ParentNodeId)
            .ThenBy(static link => link.ChildNodeId)
            .ThenBy(static link => link.RelationshipType)
            .ThenBy(static link => link.OwnershipLinkId)
            .Select(link => ProjectLink(link, nodes, asOf))
            .ToArray();

        var activeCount = links.Count(static link => link.EffectiveWindow.IsActiveAsOf);
        var invalidCount = links.Count(static link => link.ValidationState is OwnershipReviewValidationStateDto.Warning or OwnershipReviewValidationStateDto.Blocking);
        var blockingCount = links.Count(static link => link.ValidationState == OwnershipReviewValidationStateDto.Blocking);
        var primaryCount = links.Count(static link => link.IsPrimary);
        var primaryPercentTotal = links
            .Where(static link => link.IsPrimary && link.EffectiveWindow.IsActiveAsOf && link.Percent.HasValue)
            .Select(static link => link.Percent!.Value)
            .DefaultIfEmpty()
            .Sum();

        var summary = new OwnershipReviewSummaryDto(
            links.Length,
            activeCount,
            invalidCount,
            blockingCount,
            primaryCount,
            primaryCount == 0 ? null : primaryPercentTotal,
            asOf,
            BuildStatusLabel(links.Length, invalidCount, blockingCount));

        return new OwnershipReviewDto(
            asOf,
            links,
            summary,
            links.Length == 0 ? EmptyMessages : Array.Empty<string>());
    }

    private static OwnershipReviewLinkDto ProjectLink(OwnershipLinkDto link, IReadOnlyDictionary<Guid, FundStructureNodeDto> nodes, DateTimeOffset asOf)
    {
        nodes.TryGetValue(link.ParentNodeId, out var parent);
        nodes.TryGetValue(link.ChildNodeId, out var child);
        var isActive = IsActive(link, asOf);
        var messages = new List<string>();
        var actions = new List<string>();

        if (parent is null)
        {
            messages.Add("Parent node is missing from the graph snapshot.");
            actions.Add("Refresh /api/fund-structure/graph or recreate the link with a valid parent node.");
        }

        if (child is null)
        {
            messages.Add("Child node is missing from the graph snapshot.");
            actions.Add("Refresh /api/fund-structure/graph or recreate the link with a valid child node.");
        }

        if (!isActive)
        {
            messages.Add(link.EffectiveFrom > asOf
                ? "Ownership link is not effective yet for the selected as-of date."
                : "Ownership link is expired for the selected as-of date.");
            actions.Add("Change the as-of date, expire the stale link, or replace it with an active relationship.");
        }

        if (link.OwnershipPercent is < 0 or > 100)
        {
            messages.Add("Ownership percent must be between 0 and 100.");
            actions.Add("Edit the link and enter a percent from 0 to 100.");
        }

        if ((link.RelationshipType is OwnershipRelationshipTypeDto.Owns or OwnershipRelationshipTypeDto.AllocatesTo) && link.OwnershipPercent is null)
        {
            messages.Add("Economic ownership or allocation links should carry an explicit percent before setup completion.");
            actions.Add("Edit the link and add the ownership percent, or replace it with a non-economic relationship type.");
        }

        var validationState = ResolveState(messages.Count, isActive, parent is null || child is null);
        var parentLabel = Label(parent, link.ParentNodeId);
        var childLabel = Label(child, link.ChildNodeId);
        var relationshipLabel = RelationshipLabel(link.RelationshipType);

        return new OwnershipReviewLinkDto(
            link.OwnershipLinkId,
            link.ParentNodeId,
            link.ChildNodeId,
            $"{parentLabel} {relationshipLabel} {childLabel}",
            parentLabel,
            childLabel,
            link.RelationshipType,
            relationshipLabel,
            link.OwnershipPercent,
            link.OwnershipPercent.HasValue ? $"{link.OwnershipPercent:0.####}%" : "Not specified",
            link.IsPrimary,
            new OwnershipReviewEffectiveWindowDto(link.EffectiveFrom, link.EffectiveTo, isActive, WindowLabel(link, asOf, isActive)),
            validationState,
            messages,
            actions.Distinct(StringComparer.Ordinal).ToArray(),
            link.Notes);
    }

    private static bool IsActive(OwnershipLinkDto link, DateTimeOffset asOf)
        => link.EffectiveFrom <= asOf && (link.EffectiveTo is null || link.EffectiveTo > asOf);

    private static OwnershipReviewValidationStateDto ResolveState(int messageCount, bool isActive, bool missingNode)
    {
        if (missingNode)
        {
            return OwnershipReviewValidationStateDto.Blocking;
        }

        if (!isActive)
        {
            return OwnershipReviewValidationStateDto.Inactive;
        }

        return messageCount == 0 ? OwnershipReviewValidationStateDto.Valid : OwnershipReviewValidationStateDto.Warning;
    }

    private static string Label(FundStructureNodeDto? node, Guid fallbackId)
        => node is null ? $"Missing node {fallbackId:N}" : $"{node.Kind} {node.Code} — {node.Name}";

    private static string RelationshipLabel(OwnershipRelationshipTypeDto relationshipType)
        => relationshipType switch
        {
            OwnershipRelationshipTypeDto.Owns => "owns",
            OwnershipRelationshipTypeDto.Advises => "advises",
            OwnershipRelationshipTypeDto.Operates => "operates",
            OwnershipRelationshipTypeDto.ClearsFor => "clears for",
            OwnershipRelationshipTypeDto.CustodiesFor => "custodies for",
            OwnershipRelationshipTypeDto.AllocatesTo => "allocates to",
            _ => relationshipType.ToString()
        };

    private static string WindowLabel(OwnershipLinkDto link, DateTimeOffset asOf, bool isActive)
    {
        var to = link.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open-ended";
        var state = isActive ? "active" : link.EffectiveFrom > asOf ? "future" : "expired";
        return $"{link.EffectiveFrom:yyyy-MM-dd} → {to} ({state})";
    }

    private static string BuildStatusLabel(int total, int invalid, int blocking)
    {
        if (total == 0)
        {
            return "No ownership links to review";
        }

        if (blocking > 0)
        {
            return $"{blocking} blocking ownership issue(s)";
        }

        if (invalid > 0)
        {
            return $"{invalid} ownership warning(s)";
        }

        return "Ownership review ready";
    }
}
