using Meridian.Contracts.FundStructure;

namespace Meridian.Application.FundStructure;

/// <summary>
/// Validates a complete fund-structure setup draft before applying missing nodes and ownership links.
/// The service does all graph reads up front so validation failures do not mutate the structure.
/// </summary>
public sealed class FundStructureSetupService : IFundStructureSetupService
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IFundStructureService _structureService;

    public FundStructureSetupService(IFundStructureService structureService)
    {
        _structureService = structureService ?? throw new ArgumentNullException(nameof(structureService));
    }

    public async Task<FundStructureSetupPreviewDto> PreviewSetupAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(request, ct).ConfigureAwait(false);
        return plan.Preview;
    }

    public async Task<FundStructureSetupCommitResultDto> CommitSetupAsync(
        FundStructureSetupDraftRequest request,
        CancellationToken ct = default)
    {
        var plan = await BuildPlanAsync(request, ct).ConfigureAwait(false);
        if (!plan.Preview.IsValid)
        {
            return new FundStructureSetupCommitResultDto(
                false,
                request.DraftId,
                [],
                plan.Preview.Nodes.Where(static node => node.ReusesExisting).Select(static node => node.NodeId).Distinct().ToList(),
                [],
                plan.Preview,
                plan.Preview.ValidationMessages);
        }

        var createdNodeIds = new List<Guid>();
        var reusedNodeIds = plan.Preview.Nodes
            .Where(static node => node.ReusesExisting)
            .Select(static node => node.NodeId)
            .Distinct()
            .ToList();
        var createdLinkIds = new List<Guid>();

        foreach (var node in plan.Nodes.Where(static node => node.Preview.WillCreate).OrderBy(static node => NodeCreateRank(node.Draft.Kind)))
        {
            ct.ThrowIfCancellationRequested();
            await CreateNodeAsync(node, plan, request, ct).ConfigureAwait(false);
            createdNodeIds.Add(node.Preview.NodeId);
        }

        foreach (var link in plan.Links.Where(static link => link.Preview.WillCreate))
        {
            ct.ThrowIfCancellationRequested();
            if (await LinkAlreadyExistsAsync(link.Preview, ct).ConfigureAwait(false))
            {
                continue;
            }

            await _structureService.LinkNodesAsync(
                    new LinkFundStructureNodesRequest(
                        link.Preview.OwnershipLinkId,
                        link.Preview.ParentNodeId,
                        link.Preview.ChildNodeId,
                        link.Preview.RelationshipType,
                        request.EffectiveFrom,
                        request.RequestedBy,
                        link.Draft.OwnershipPercent,
                        link.Draft.IsPrimary,
                        link.Draft.Notes),
                    ct)
                .ConfigureAwait(false);
            createdLinkIds.Add(link.Preview.OwnershipLinkId);
        }

        var committedPreview = plan.Preview with
        {
            Nodes = plan.Preview.Nodes
                .Select(node => createdNodeIds.Contains(node.NodeId) ? node with { WillCreate = false, Message = "Created." } : node)
                .ToList(),
            Links = plan.Preview.Links
                .Select(link => createdLinkIds.Contains(link.OwnershipLinkId) ? link with { WillCreate = false, Message = "Created." } : link)
                .ToList()
        };

        return new FundStructureSetupCommitResultDto(
            true,
            request.DraftId,
            createdNodeIds,
            reusedNodeIds,
            createdLinkIds,
            committedPreview,
            committedPreview.ValidationMessages);
    }

    private async Task<SetupPlan> BuildPlanAsync(FundStructureSetupDraftRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var messages = new List<FundStructureSetupValidationMessageDto>();
        if (string.IsNullOrWhiteSpace(request.RequestedBy))
            messages.Add(Error("requestedBy.required", "requestedBy is required."));
        if (request.Nodes.Count == 0)
            messages.Add(Error("nodes.required", "At least one setup node is required."));

        var graph = await _structureService.GetOrganizationStructureAsync(
                new OrganizationStructureQuery(ActiveOnly: false),
                ct)
            .ConfigureAwait(false);
        var existingNodes = graph.Nodes.ToList();
        var existingLinks = graph.Links.ToList();
        var draftByKey = new Dictionary<string, FundStructureSetupNodeDraftDto>(KeyComparer);
        var resolvedNodeIds = new Dictionary<string, Guid>(KeyComparer);
        var plannedNodes = new List<PlannedNode>();

        foreach (var node in request.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ClientKey))
            {
                messages.Add(Error("node.clientKey.required", "Every node requires a clientKey."));
                continue;
            }

            var key = node.ClientKey.Trim();
            if (!draftByKey.TryAdd(key, node))
            {
                messages.Add(Error("node.clientKey.duplicate", $"Node clientKey '{key}' is duplicated.", key));
                continue;
            }

            ValidateNode(node, messages);
            var existing = ResolveExistingNode(node, existingNodes, request.ReuseExistingEntities);
            var nodeId = existing?.NodeId ?? node.NodeId ?? Guid.NewGuid();
            resolvedNodeIds[key] = nodeId;
            plannedNodes.Add(new PlannedNode(node, new FundStructureSetupNodePreviewDto(
                key,
                node.Kind,
                nodeId,
                node.Code.Trim(),
                node.Name.Trim(),
                existing is null,
                existing is not null,
                existing is null ? "Will create missing node." : "Will reuse existing node.")));
        }

        foreach (var group in plannedNodes.GroupBy(static item => (item.Draft.Kind, Code: Normalize(item.Draft.Code))))
        {
            if (!string.IsNullOrEmpty(group.Key.Code) && group.Count() > 1)
            {
                messages.Add(Error("node.code.duplicate", $"Draft contains duplicate {group.Key.Kind} code '{group.First().Draft.Code}'."));
            }
        }

        var plannedLinks = new List<PlannedLink>();
        foreach (var link in request.Links)
        {
            var parentKey = link.ParentClientKey?.Trim() ?? string.Empty;
            var childKey = link.ChildClientKey?.Trim() ?? string.Empty;
            if (!resolvedNodeIds.TryGetValue(parentKey, out var parentId))
            {
                messages.Add(Error("link.parent.missing", $"Link parent '{parentKey}' was not found in the setup draft.", parentKey));
                continue;
            }
            if (!resolvedNodeIds.TryGetValue(childKey, out var childId))
            {
                messages.Add(Error("link.child.missing", $"Link child '{childKey}' was not found in the setup draft.", childKey));
                continue;
            }

            var existing = existingLinks.FirstOrDefault(candidate =>
                candidate.ParentNodeId == parentId &&
                candidate.ChildNodeId == childId &&
                candidate.RelationshipType == link.RelationshipType &&
                candidate.EffectiveTo is null);
            var linkId = existing?.OwnershipLinkId ?? link.OwnershipLinkId ?? Guid.NewGuid();
            plannedLinks.Add(new PlannedLink(link, new FundStructureSetupLinkPreviewDto(
                linkId,
                parentId,
                childId,
                link.RelationshipType,
                existing is null,
                existing is not null,
                existing is null ? "Will create ownership link." : "Will reuse existing ownership link.")));
        }

        ValidateRequiredRelationships(plannedNodes, plannedLinks, messages);

        var preview = new FundStructureSetupPreviewDto(
            !messages.Any(static message => string.Equals(message.Severity, "Error", StringComparison.OrdinalIgnoreCase)),
            request.DraftId,
            plannedNodes.Select(static node => node.Preview).ToList(),
            plannedLinks.Select(static link => link.Preview).ToList(),
            messages,
            plannedNodes.Count(static node => node.Preview.WillCreate),
            plannedNodes.Count(static node => node.Preview.ReusesExisting),
            plannedLinks.Count(static link => link.Preview.WillCreate));
        return new SetupPlan(plannedNodes, plannedLinks, preview);
    }


    private async Task<bool> LinkAlreadyExistsAsync(FundStructureSetupLinkPreviewDto link, CancellationToken ct)
    {
        var graph = await _structureService.GetOrganizationStructureAsync(
                new OrganizationStructureQuery(ActiveOnly: false),
                ct)
            .ConfigureAwait(false);
        return graph.Links.Any(candidate =>
            candidate.ParentNodeId == link.ParentNodeId &&
            candidate.ChildNodeId == link.ChildNodeId &&
            candidate.RelationshipType == link.RelationshipType &&
            candidate.EffectiveTo is null);
    }

    private static void ValidateNode(FundStructureSetupNodeDraftDto node, List<FundStructureSetupValidationMessageDto> messages)
    {
        if (string.IsNullOrWhiteSpace(node.Code)) messages.Add(Error("node.code.required", "Node code is required.", node.ClientKey));
        if (string.IsNullOrWhiteSpace(node.Name)) messages.Add(Error("node.name.required", "Node name is required.", node.ClientKey));
        if (string.IsNullOrWhiteSpace(node.BaseCurrency) && node.Kind is not FundStructureNodeKindDto.Sleeve)
            messages.Add(Error("node.currency.required", "Node baseCurrency is required.", node.ClientKey));
        if (node.Kind == FundStructureNodeKindDto.Business && node.BusinessKind is null)
            messages.Add(Error("business.kind.required", "Business nodes require businessKind.", node.ClientKey));
        if (node.Kind == FundStructureNodeKindDto.Entity)
        {
            if (node.LegalEntityType is null) messages.Add(Error("entity.type.required", "Entity nodes require legalEntityType.", node.ClientKey));
            if (string.IsNullOrWhiteSpace(node.Jurisdiction)) messages.Add(Error("entity.jurisdiction.required", "Entity nodes require jurisdiction.", node.ClientKey));
        }
        if (node.Kind == FundStructureNodeKindDto.Account)
            messages.Add(Error("node.kind.unsupported", "Setup drafts cannot create account nodes; create accounts through the account workflow and link existing accounts separately.", node.ClientKey));
    }

    private static FundStructureNodeDto? ResolveExistingNode(
        FundStructureSetupNodeDraftDto node,
        IReadOnlyList<FundStructureNodeDto> existingNodes,
        bool reuseExisting)
    {
        if (!reuseExisting) return null;
        if (node.NodeId.HasValue)
        {
            var byId = existingNodes.FirstOrDefault(existing => existing.NodeId == node.NodeId.Value && existing.Kind == node.Kind);
            if (byId is not null) return byId;
        }

        var code = Normalize(node.Code);
        var name = Normalize(node.Name);
        return existingNodes.FirstOrDefault(existing =>
            existing.Kind == node.Kind &&
            (!string.IsNullOrEmpty(code) && Normalize(existing.Code) == code ||
             !string.IsNullOrEmpty(name) && Normalize(existing.Name) == name));
    }

    private static void ValidateRequiredRelationships(
        IReadOnlyList<PlannedNode> nodes,
        IReadOnlyList<PlannedLink> links,
        List<FundStructureSetupValidationMessageDto> messages)
    {
        var byId = nodes.ToDictionary(static node => node.Preview.NodeId);
        foreach (var node in nodes)
        {
            var parents = links
                .Where(link => link.Preview.ChildNodeId == node.Preview.NodeId)
                .Select(link => byId.TryGetValue(link.Preview.ParentNodeId, out var parent) ? parent.Draft.Kind : (FundStructureNodeKindDto?)null)
                .ToList();
            switch (node.Draft.Kind)
            {
                case FundStructureNodeKindDto.Business when !parents.Contains(FundStructureNodeKindDto.Organization):
                    messages.Add(Error("business.organization.required", "Business setup nodes require an Organization parent link.", node.Preview.ClientKey));
                    break;
                case FundStructureNodeKindDto.Client when !parents.Contains(FundStructureNodeKindDto.Business):
                    messages.Add(Error("client.business.required", "Client setup nodes require a Business parent link.", node.Preview.ClientKey));
                    break;
                case FundStructureNodeKindDto.Fund when !parents.Contains(FundStructureNodeKindDto.Business):
                    messages.Add(Error("fund.business.required", "Fund setup nodes require a Business parent link.", node.Preview.ClientKey));
                    break;
                case FundStructureNodeKindDto.Sleeve when !parents.Contains(FundStructureNodeKindDto.Fund):
                    messages.Add(Error("sleeve.fund.required", "Sleeve setup nodes require a Fund parent link.", node.Preview.ClientKey));
                    break;
                case FundStructureNodeKindDto.Vehicle when !(parents.Contains(FundStructureNodeKindDto.Fund) && parents.Contains(FundStructureNodeKindDto.Entity)):
                    messages.Add(Error("vehicle.parents.required", "Vehicle setup nodes require Fund and Entity parent links.", node.Preview.ClientKey));
                    break;
                case FundStructureNodeKindDto.InvestmentPortfolio when !parents.Contains(FundStructureNodeKindDto.Business):
                    messages.Add(Error("portfolio.business.required", "InvestmentPortfolio setup nodes require a Business parent link.", node.Preview.ClientKey));
                    break;
            }
        }
    }

    private async Task CreateNodeAsync(
        PlannedNode node,
        SetupPlan plan,
        FundStructureSetupDraftRequest request,
        CancellationToken ct)
    {
        Guid Parent(FundStructureNodeKindDto kind) => plan.Links
            .Where(link => link.Preview.ChildNodeId == node.Preview.NodeId)
            .Select(link => plan.Nodes.FirstOrDefault(candidate => candidate.Preview.NodeId == link.Preview.ParentNodeId))
            .First(candidate => candidate is not null && candidate.Draft.Kind == kind)!
            .Preview.NodeId;
        Guid? OptionalParent(FundStructureNodeKindDto kind) => plan.Links
            .Where(link => link.Preview.ChildNodeId == node.Preview.NodeId)
            .Select(link => plan.Nodes.FirstOrDefault(candidate => candidate.Preview.NodeId == link.Preview.ParentNodeId))
            .FirstOrDefault(candidate => candidate is not null && candidate.Draft.Kind == kind)?
            .Preview.NodeId;

        switch (node.Draft.Kind)
        {
            case FundStructureNodeKindDto.Organization:
                await _structureService.CreateOrganizationAsync(new CreateOrganizationRequest(node.Preview.NodeId, node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Business:
                await _structureService.CreateBusinessAsync(new CreateBusinessRequest(node.Preview.NodeId, Parent(FundStructureNodeKindDto.Organization), node.Draft.BusinessKind!.Value, node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Client:
                await _structureService.CreateClientAsync(new CreateClientRequest(node.Preview.NodeId, Parent(FundStructureNodeKindDto.Business), node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description, node.Draft.ClientSegmentKind ?? ClientSegmentKind.Unspecified), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Fund:
                await _structureService.CreateFundAsync(new CreateFundRequest(node.Preview.NodeId, node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description, Parent(FundStructureNodeKindDto.Business)), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Sleeve:
                await _structureService.CreateSleeveAsync(new CreateSleeveRequest(node.Preview.NodeId, Parent(FundStructureNodeKindDto.Fund), node.Draft.Code.Trim(), node.Draft.Name.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Mandate, node.Draft.StrategyIds), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Entity:
                await _structureService.CreateLegalEntityAsync(new CreateLegalEntityRequest(node.Preview.NodeId, node.Draft.LegalEntityType!.Value, node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.Jurisdiction!.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.Vehicle:
                await _structureService.CreateVehicleAsync(new CreateVehicleRequest(node.Preview.NodeId, Parent(FundStructureNodeKindDto.Fund), Parent(FundStructureNodeKindDto.Entity), node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, node.Draft.Description), ct).ConfigureAwait(false);
                break;
            case FundStructureNodeKindDto.InvestmentPortfolio:
                await _structureService.CreateInvestmentPortfolioAsync(new CreateInvestmentPortfolioRequest(node.Preview.NodeId, Parent(FundStructureNodeKindDto.Business), node.Draft.Code.Trim(), node.Draft.Name.Trim(), node.Draft.BaseCurrency.Trim(), request.EffectiveFrom, request.RequestedBy, OptionalParent(FundStructureNodeKindDto.Client), OptionalParent(FundStructureNodeKindDto.Fund), OptionalParent(FundStructureNodeKindDto.Sleeve), OptionalParent(FundStructureNodeKindDto.Vehicle), OptionalParent(FundStructureNodeKindDto.Entity), node.Draft.Description), ct).ConfigureAwait(false);
                break;
        }
    }

    private static int NodeCreateRank(FundStructureNodeKindDto kind) => kind switch
    {
        FundStructureNodeKindDto.Organization => 0,
        FundStructureNodeKindDto.Entity => 0,
        FundStructureNodeKindDto.Business => 1,
        FundStructureNodeKindDto.Client => 2,
        FundStructureNodeKindDto.Fund => 2,
        FundStructureNodeKindDto.Sleeve => 3,
        FundStructureNodeKindDto.Vehicle => 3,
        FundStructureNodeKindDto.InvestmentPortfolio => 4,
        _ => 9
    };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static FundStructureSetupValidationMessageDto Error(string code, string message, string? clientKey = null) =>
        new("Error", code, message, clientKey);

    private sealed record PlannedNode(FundStructureSetupNodeDraftDto Draft, FundStructureSetupNodePreviewDto Preview);
    private sealed record PlannedLink(FundStructureSetupLinkDraftDto Draft, FundStructureSetupLinkPreviewDto Preview);
    private sealed record SetupPlan(IReadOnlyList<PlannedNode> Nodes, IReadOnlyList<PlannedLink> Links, FundStructureSetupPreviewDto Preview);
}
