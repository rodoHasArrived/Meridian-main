using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Shared.Services;

public interface IOwnershipReviewReadService
{
    OwnershipReviewModelDto Project(FundStructureGraphDto graph, DateTimeOffset? asOf = null);
}

public sealed class OwnershipReviewReadService : IOwnershipReviewReadService
{
    private const string EmptyTitle = "Ownership setup incomplete";
    private const string EmptyDetail = "Create ownership links before completing setup so operators can see who owns, advises, operates, clears, custodies, or allocates each node.";

    public OwnershipReviewModelDto Project(FundStructureGraphDto graph, DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var effectiveAsOf = asOf ?? DateTimeOffset.UtcNow;
        var nodes = graph.Nodes.ToDictionary(node => node.NodeId);
        var links = graph.OwnershipLinks
            .OrderBy(link => ResolveNodeLabel(nodes, link.ParentNodeId))
            .ThenBy(link => ResolveNodeLabel(nodes, link.ChildNodeId))
            .ThenBy(link => link.RelationshipType.ToString(), StringComparer.Ordinal)
            .ThenBy(link => link.OwnershipLinkId)
            .Select(link => ProjectLink(link, nodes, effectiveAsOf))
            .ToArray();

        var invalidLinks = links.Where(link => link.ValidationState == OwnershipReviewValidationStateDto.Blocking).ToArray();
        var activeLinks = links.Where(link => link.EffectiveWindow.IsActiveAsOf).ToArray();
        var percentTotal = activeLinks.Sum(link => link.Percent ?? 0m);
        var blockers = invalidLinks.SelectMany(link => link.BlockingMessages).Distinct(StringComparer.Ordinal).ToArray();
        var rollupSummary = links.Length == 0
            ? "No ownership links have been configured."
            : $"{activeLinks.Length} active of {links.Length} ownership links; {invalidLinks.Length} blocking; {percentTotal:0.##}% explicit active ownership.";

        return new OwnershipReviewModelDto(
            effectiveAsOf,
            links,
            new OwnershipReviewSummaryDto(
                links.Length,
                activeLinks.Length,
                invalidLinks.Length,
                percentTotal,
                rollupSummary,
                blockers),
            EmptyTitle,
            EmptyDetail);
    }

    private static OwnershipReviewLinkDto ProjectLink(
        OwnershipLinkDto link,
        IReadOnlyDictionary<Guid, FundStructureNodeDto> nodes,
        DateTimeOffset asOf)
    {
        var parentLabel = ResolveNodeLabel(nodes, link.ParentNodeId);
        var childLabel = ResolveNodeLabel(nodes, link.ChildNodeId);
        var relationshipLabel = FormatRelationship(link.RelationshipType);
        var isActive = link.EffectiveFrom <= asOf && (link.EffectiveTo is null || link.EffectiveTo >= asOf);
        var blockers = BuildBlockingMessages(link, nodes).ToArray();
        var remediation = BuildRemediationActions(link, nodes, blockers, isActive).ToArray();
        var validation = blockers.Length > 0
            ? OwnershipReviewValidationStateDto.Blocking
            : isActive
                ? OwnershipReviewValidationStateDto.Valid
                : OwnershipReviewValidationStateDto.Warning;

        return new OwnershipReviewLinkDto(
            link.OwnershipLinkId,
            $"{parentLabel} {relationshipLabel} {childLabel}",
            parentLabel,
            childLabel,
            relationshipLabel,
            link.OwnershipPercent,
            link.IsPrimary,
            new OwnershipReviewEffectiveWindowDto(
                link.EffectiveFrom,
                link.EffectiveTo,
                isActive,
                FormatEffectiveWindow(link.EffectiveFrom, link.EffectiveTo, isActive)),
            validation,
            blockers,
            remediation,
            BuildLifecycleCommands(link.OwnershipLinkId));
    }

    private static IEnumerable<string> BuildBlockingMessages(
        OwnershipLinkDto link,
        IReadOnlyDictionary<Guid, FundStructureNodeDto> nodes)
    {
        if (!nodes.ContainsKey(link.ParentNodeId))
            yield return "Parent node is missing from the fund-structure graph.";

        if (!nodes.ContainsKey(link.ChildNodeId))
            yield return "Child node is missing from the fund-structure graph.";

        if (link.ParentNodeId == link.ChildNodeId)
            yield return "Parent and child cannot be the same node.";

        if (link.OwnershipPercent is < 0m or > 100m)
            yield return "Ownership percent must be between 0 and 100.";

        if (link.EffectiveTo is not null && link.EffectiveTo < link.EffectiveFrom)
            yield return "Effective end must be on or after the effective start.";
    }

    private static IEnumerable<string> BuildRemediationActions(
        OwnershipLinkDto link,
        IReadOnlyDictionary<Guid, FundStructureNodeDto> nodes,
        IReadOnlyCollection<string> blockers,
        bool isActive)
    {
        if (!nodes.ContainsKey(link.ParentNodeId))
            yield return "Select an existing parent node or recreate the missing parent before setup completion.";

        if (!nodes.ContainsKey(link.ChildNodeId))
            yield return "Select an existing child node or recreate the missing child before setup completion.";

        if (link.ParentNodeId == link.ChildNodeId)
            yield return "Replace this link with distinct parent and child nodes.";

        if (link.OwnershipPercent is < 0m or > 100m)
            yield return "Edit the percent to a value from 0 through 100, or leave it blank when the relationship is non-economic.";

        if (link.EffectiveTo is not null && link.EffectiveTo < link.EffectiveFrom)
            yield return "Expire the link at a date after the start date, or replace it with a corrected effective window.";

        if (blockers.Count == 0 && !isActive)
            yield return "Review whether this historical or future-dated link should be replaced by an active ownership link.";

        if (blockers.Count == 0 && isActive)
            yield return "No remediation required.";
    }

    private static IReadOnlyList<OwnershipLifecycleCommandDto> BuildLifecycleCommands(Guid ownershipLinkId)
    {
        var encodedId = Uri.EscapeDataString(ownershipLinkId.ToString());
        var disabledReason = "Backend ownership lifecycle methods are not enabled for this environment yet.";
        return
        [
            new OwnershipLifecycleCommandDto("Edit", $"/api/fund-structure/links/{encodedId}/edit", false, disabledReason),
            new OwnershipLifecycleCommandDto("Expire", $"/api/fund-structure/links/{encodedId}/expire", false, disabledReason),
            new OwnershipLifecycleCommandDto("Replace", $"/api/fund-structure/links/{encodedId}/replace", false, disabledReason)
        ];
    }

    private static string ResolveNodeLabel(IReadOnlyDictionary<Guid, FundStructureNodeDto> nodes, Guid nodeId)
        => nodes.TryGetValue(nodeId, out var node)
            ? $"{node.Name} ({node.Kind})"
            : $"Missing node {nodeId:N}";

    private static string FormatRelationship(OwnershipRelationshipTypeDto relationship)
        => relationship switch
        {
            OwnershipRelationshipTypeDto.ClearsFor => "clears for",
            OwnershipRelationshipTypeDto.CustodiesFor => "custodies for",
            OwnershipRelationshipTypeDto.AllocatesTo => "allocates to",
            _ => relationship.ToString().ToLowerInvariant()
        };

    private static string FormatEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, bool isActive)
    {
        var end = effectiveTo?.ToString("yyyy-MM-dd") ?? "open-ended";
        var status = isActive ? "active" : "inactive";
        return $"{effectiveFrom:yyyy-MM-dd} to {end} ({status})";
    }
}
