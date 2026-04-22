using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.FundStructure;
using Meridian.Contracts.EnvironmentDesign;
using Meridian.Contracts.FundStructure;

namespace Meridian.Application.EnvironmentDesign;

/// <summary>
/// Local-first environment designer service with draft, validation, publish, and rollback support.
/// This admin-oriented service is intentionally optimized for deterministic correctness rather than hot-path throughput.
/// </summary>
public sealed partial class EnvironmentDesignerService :
    IEnvironmentDesignService,
    IEnvironmentValidationService,
    IEnvironmentPublishService,
    IEnvironmentRuntimeProjectionService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly HashSet<string> AllowedWorkspaceIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "research",
        "trading",
        "data-operations",
        "governance"
    };

    private static readonly HashSet<string> AllowedLandingPageTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ResearchShell",
        "TradingShell",
        "DataOperationsShell",
        "GovernanceShell",
        "EnvironmentDesigner"
    };

    private static readonly IReadOnlyDictionary<EnvironmentNodeKind, EnvironmentNodeKind[]> AllowedParentKinds =
        new Dictionary<EnvironmentNodeKind, EnvironmentNodeKind[]>
        {
            [EnvironmentNodeKind.Organization] = Array.Empty<EnvironmentNodeKind>(),
            [EnvironmentNodeKind.Business] = [EnvironmentNodeKind.Organization],
            [EnvironmentNodeKind.Client] = [EnvironmentNodeKind.Business],
            [EnvironmentNodeKind.Fund] = [EnvironmentNodeKind.Business],
            [EnvironmentNodeKind.Sleeve] = [EnvironmentNodeKind.Fund],
            [EnvironmentNodeKind.Vehicle] = [EnvironmentNodeKind.Fund],
            [EnvironmentNodeKind.InvestmentPortfolio] =
            [
                EnvironmentNodeKind.Business,
                EnvironmentNodeKind.Client,
                EnvironmentNodeKind.Fund,
                EnvironmentNodeKind.Sleeve,
                EnvironmentNodeKind.Vehicle,
                EnvironmentNodeKind.Entity,
                EnvironmentNodeKind.LedgerGroup
            ],
            [EnvironmentNodeKind.Entity] =
            [
                EnvironmentNodeKind.Organization,
                EnvironmentNodeKind.Business,
                EnvironmentNodeKind.Client,
                EnvironmentNodeKind.Fund,
                EnvironmentNodeKind.Vehicle,
                EnvironmentNodeKind.InvestmentPortfolio
            ],
            [EnvironmentNodeKind.Account] =
            [
                EnvironmentNodeKind.Business,
                EnvironmentNodeKind.Client,
                EnvironmentNodeKind.Fund,
                EnvironmentNodeKind.Sleeve,
                EnvironmentNodeKind.Vehicle,
                EnvironmentNodeKind.Entity,
                EnvironmentNodeKind.InvestmentPortfolio,
                EnvironmentNodeKind.LedgerGroup
            ],
            [EnvironmentNodeKind.LedgerGroup] =
            [
                EnvironmentNodeKind.Organization,
                EnvironmentNodeKind.Business,
                EnvironmentNodeKind.Client,
                EnvironmentNodeKind.Fund,
                EnvironmentNodeKind.Sleeve,
                EnvironmentNodeKind.Vehicle
            ]
        };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly List<EnvironmentDraftDto> _drafts = [];
    private readonly List<PublishedEnvironmentVersionDto> _versions = [];
    private readonly Dictionary<Guid, Guid> _currentVersionByOrganizationId = [];
    private readonly string? _persistencePath;

    public EnvironmentDesignerService(string? persistencePath)
    {
        _persistencePath = string.IsNullOrWhiteSpace(persistencePath) ? null : persistencePath;
        LoadState();
    }

    public Task<IReadOnlyList<EnvironmentDraftDto>> ListDraftsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<EnvironmentDraftDto>>(
                _drafts
                    .OrderByDescending(static draft => draft.UpdatedAt)
                    .ToArray());
        }
    }

    public Task<EnvironmentDraftDto?> GetDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_drafts.FirstOrDefault(draft => draft.DraftId == draftId));
        }
    }

    public async Task<EnvironmentDraftDto> CreateDraftAsync(CreateEnvironmentDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var draft = new EnvironmentDraftDto(
            request.DraftId == Guid.Empty ? Guid.NewGuid() : request.DraftId,
            string.IsNullOrWhiteSpace(request.Name) ? "Untitled Environment Draft" : request.Name.Trim(),
            NormalizeDefinition(request.Definition),
            now,
            now,
            request.CreatedBy,
            request.CreatedBy,
            Notes: request.Notes);

        lock (_gate)
        {
            if (_drafts.Any(existing => existing.DraftId == draft.DraftId))
            {
                throw new InvalidOperationException($"Environment draft '{draft.DraftId}' already exists.");
            }

            _drafts.Add(draft);
        }

        await PersistAsync(ct).ConfigureAwait(false);
        return draft;
    }

    public async Task<EnvironmentDraftDto> SaveDraftAsync(EnvironmentDraftDto draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ct.ThrowIfCancellationRequested();

        EnvironmentDraftDto normalized;
        lock (_gate)
        {
            var existingIndex = _drafts.FindIndex(item => item.DraftId == draft.DraftId);
            var now = DateTimeOffset.UtcNow;
            normalized = draft with
            {
                Name = string.IsNullOrWhiteSpace(draft.Name) ? "Untitled Environment Draft" : draft.Name.Trim(),
                Definition = NormalizeDefinition(draft.Definition),
                CreatedAt = existingIndex >= 0 ? _drafts[existingIndex].CreatedAt : draft.CreatedAt == default ? now : draft.CreatedAt,
                UpdatedAt = now,
                UpdatedBy = string.IsNullOrWhiteSpace(draft.UpdatedBy) ? draft.CreatedBy : draft.UpdatedBy
            };

            if (existingIndex >= 0)
            {
                _drafts[existingIndex] = normalized;
            }
            else
            {
                _drafts.Add(normalized);
            }
        }

        await PersistAsync(ct).ConfigureAwait(false);
        return normalized;
    }

    public async Task DeleteDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _drafts.RemoveAll(draft => draft.DraftId == draftId);
        }

        await PersistAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PublishedEnvironmentVersionDto>> ListPublishedVersionsAsync(
        Guid? organizationId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var versions = _versions
                .Where(version => organizationId is null || version.OrganizationId == organizationId.Value)
                .OrderByDescending(static version => version.PublishedAt)
                .Select(ApplyCurrentFlagLocked)
                .ToArray();

            return Task.FromResult<IReadOnlyList<PublishedEnvironmentVersionDto>>(versions);
        }
    }

    public Task<PublishedEnvironmentVersionDto?> GetPublishedVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var version = _versions.FirstOrDefault(candidate => candidate.VersionId == versionId);
            return Task.FromResult(version is null ? null : ApplyCurrentFlagLocked(version));
        }
    }

    public Task<PublishedEnvironmentVersionDto?> GetCurrentPublishedVersionAsync(Guid? organizationId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var version = GetCurrentPublishedVersionLocked(organizationId);
            return Task.FromResult(version is null ? null : ApplyCurrentFlagLocked(version));
        }
    }

    public async Task<EnvironmentValidationResultDto> ValidateAsync(
        EnvironmentDraftDto draft,
        EnvironmentPublishPlanDto? publishPlan = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ct.ThrowIfCancellationRequested();

        PublishedEnvironmentVersionDto? currentVersion;
        lock (_gate)
        {
            currentVersion = GetCurrentPublishedVersionLocked(draft.Definition.OrganizationId);
        }

        var issues = ValidateDefinition(draft.Definition, publishPlan, currentVersion);
        return await Task.FromResult(new EnvironmentValidationResultDto(
            !issues.Any(issue => issue.Severity == EnvironmentValidationSeverity.Error),
            issues)).ConfigureAwait(false);
    }

    public async Task<EnvironmentPublishPreviewDto> PreviewPublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        var draft = await RequireDraftAsync(plan.DraftId, ct).ConfigureAwait(false);

        PublishedEnvironmentVersionDto? currentVersion;
        lock (_gate)
        {
            currentVersion = GetCurrentPublishedVersionLocked(draft.Definition.OrganizationId);
        }

        var validation = await ValidateAsync(draft, plan, ct).ConfigureAwait(false);
        var changes = BuildPublishPreviewChanges(draft.Definition, currentVersion);
        var hasDestructive = changes.Any(static change => change.IsBreaking);
        return new EnvironmentPublishPreviewDto(plan.DraftId, validation, changes, hasDestructive);
    }

    public async Task<PublishedEnvironmentVersionDto> PublishAsync(EnvironmentPublishPlanDto plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        var draft = await RequireDraftAsync(plan.DraftId, ct).ConfigureAwait(false);

        PublishedEnvironmentVersionDto? currentVersion;
        lock (_gate)
        {
            currentVersion = GetCurrentPublishedVersionLocked(draft.Definition.OrganizationId);
        }

        var validation = await ValidateAsync(draft, plan, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errorSummary = string.Join(
                Environment.NewLine,
                validation.Issues
                    .Where(static issue => issue.Severity == EnvironmentValidationSeverity.Error)
                    .Select(static issue => $"{issue.Code}: {issue.Message}"));

            throw new InvalidOperationException($"Environment draft '{draft.Name}' failed validation.{Environment.NewLine}{errorSummary}");
        }

        var publishedAt = DateTimeOffset.UtcNow;
        var runtime = CompileRuntime(draft.Definition, currentVersion, publishedAt);
        var versionLabel = string.IsNullOrWhiteSpace(plan.VersionLabel)
            ? BuildNextVersionLabel(draft.Definition.OrganizationId)
            : plan.VersionLabel.Trim();

        var publishedVersion = new PublishedEnvironmentVersionDto(
            VersionId: Guid.NewGuid(),
            DraftId: draft.DraftId,
            OrganizationId: draft.Definition.OrganizationId,
            OrganizationName: draft.Definition.OrganizationName,
            VersionLabel: versionLabel,
            PublishedAt: publishedAt,
            PublishedBy: plan.PublishedBy,
            Validation: validation,
            Runtime: runtime,
            RuntimeNodeMappings: runtime.Nodes.ToDictionary(static node => node.NodeDefinitionId, static node => node.RuntimeNodeId, StringComparer.OrdinalIgnoreCase),
            Notes: plan.Notes,
            IsCurrent: true);

        lock (_gate)
        {
            ReplaceCurrentVersionLocked(draft.Definition.OrganizationId, publishedVersion);
        }

        await PersistAsync(ct).ConfigureAwait(false);
        return ApplyCurrentFlagLocked(publishedVersion);
    }

    public async Task<PublishedEnvironmentVersionDto> RollbackAsync(RollbackEnvironmentVersionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        PublishedEnvironmentVersionDto version;
        lock (_gate)
        {
            version = _versions.FirstOrDefault(candidate => candidate.VersionId == request.VersionId)
                ?? throw new InvalidOperationException($"Published environment version '{request.VersionId}' was not found.");

            _currentVersionByOrganizationId[version.OrganizationId] = version.VersionId;
        }

        await PersistAsync(ct).ConfigureAwait(false);
        return ApplyCurrentFlagLocked(version);
    }

    public Task<PublishedEnvironmentRuntimeDto?> GetCurrentRuntimeAsync(Guid? organizationId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var version = GetCurrentPublishedVersionLocked(organizationId);
            return Task.FromResult(version?.Runtime);
        }
    }

    public Task<PublishedEnvironmentRuntimeDto?> GetRuntimeForVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_versions.FirstOrDefault(version => version.VersionId == versionId)?.Runtime);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static OrganizationEnvironmentDefinitionDto NormalizeDefinition(OrganizationEnvironmentDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition with
        {
            OrganizationNodeDefinitionId = string.IsNullOrWhiteSpace(definition.OrganizationNodeDefinitionId)
                ? "org-root"
                : definition.OrganizationNodeDefinitionId.Trim(),
            OrganizationCode = string.IsNullOrWhiteSpace(definition.OrganizationCode)
                ? "ORG"
                : definition.OrganizationCode.Trim(),
            OrganizationName = string.IsNullOrWhiteSpace(definition.OrganizationName)
                ? "Untitled Organization"
                : definition.OrganizationName.Trim(),
            BaseCurrency = NormalizeCurrency(definition.BaseCurrency),
            Lanes = definition.Lanes ?? [],
            Nodes = definition.Nodes ?? [],
            Relationships = definition.Relationships ?? []
        };
    }

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

    private async Task<EnvironmentDraftDto> RequireDraftAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await GetDraftAsync(draftId, ct).ConfigureAwait(false);
        return draft ?? throw new InvalidOperationException($"Environment draft '{draftId}' was not found.");
    }

    private List<EnvironmentValidationIssueDto> ValidateDefinition(
        OrganizationEnvironmentDefinitionDto definition,
        EnvironmentPublishPlanDto? publishPlan,
        PublishedEnvironmentVersionDto? currentVersion)
    {
        var issues = new List<EnvironmentValidationIssueDto>();
        var nodes = definition.Nodes ?? [];
        var lanes = definition.Lanes ?? [];
        var relationships = definition.Relationships ?? [];

        var nodeMap = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeDefinitionId))
            .ToDictionary(node => node.NodeDefinitionId, StringComparer.OrdinalIgnoreCase);

        var duplicateNodeIds = nodes
            .GroupBy(node => node.NodeDefinitionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var duplicateNodeId in duplicateNodeIds)
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "duplicate-node-id",
                $"Node definition '{duplicateNodeId}' appears more than once.",
                NodeDefinitionId: duplicateNodeId));
        }

        foreach (var duplicateLaneId in lanes
            .GroupBy(lane => lane.LaneId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "duplicate-lane-id",
                $"Lane '{duplicateLaneId}' appears more than once.",
                LaneId: duplicateLaneId));
        }

        foreach (var duplicateRelationshipId in relationships
            .GroupBy(relationship => relationship.RelationshipDefinitionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "duplicate-relationship-id",
                $"Relationship '{duplicateRelationshipId}' appears more than once.",
                RelationshipDefinitionId: duplicateRelationshipId));
        }

        if (!nodeMap.TryGetValue(definition.OrganizationNodeDefinitionId, out var organizationNode))
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "missing-organization-root",
                "The environment definition must reference an existing organization root node.",
                NodeDefinitionId: definition.OrganizationNodeDefinitionId));
        }
        else if (organizationNode.NodeKind != EnvironmentNodeKind.Organization)
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "invalid-organization-root",
                $"Node '{definition.OrganizationNodeDefinitionId}' must be an organization node.",
                NodeDefinitionId: definition.OrganizationNodeDefinitionId));
        }

        var organizationNodeCount = nodes.Count(node => node.NodeKind == EnvironmentNodeKind.Organization);
        if (organizationNodeCount != 1)
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "organization-count",
                "The environment definition must contain exactly one organization node.",
                NodeDefinitionId: definition.OrganizationNodeDefinitionId));
        }

        var childLookup = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId))
            .GroupBy(node => node.ParentNodeDefinitionId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(node => node.NodeDefinitionId).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.NodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "blank-node-id",
                    $"Node '{node.Name}' is missing a definition identifier."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Code))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "blank-code",
                    $"Node '{node.NodeDefinitionId}' is missing a code.",
                    NodeDefinitionId: node.NodeDefinitionId));
            }

            if (string.IsNullOrWhiteSpace(node.Name))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "blank-name",
                    $"Node '{node.NodeDefinitionId}' is missing a display name.",
                    NodeDefinitionId: node.NodeDefinitionId));
            }

            if (!AllowedParentKinds.TryGetValue(node.NodeKind, out var allowedParents))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "unsupported-node-kind",
                    $"Node '{node.NodeDefinitionId}' uses an unsupported node kind '{node.NodeKind}'.",
                    NodeDefinitionId: node.NodeDefinitionId));
                continue;
            }

            if (node.NodeKind == EnvironmentNodeKind.Organization)
            {
                if (!string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId))
                {
                    issues.Add(new EnvironmentValidationIssueDto(
                        EnvironmentValidationSeverity.Error,
                        "organization-has-parent",
                        $"Organization node '{node.NodeDefinitionId}' cannot have a parent.",
                        NodeDefinitionId: node.NodeDefinitionId));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "missing-parent",
                    $"Node '{node.NodeDefinitionId}' must have a parent node.",
                    NodeDefinitionId: node.NodeDefinitionId));
                continue;
            }

            if (!nodeMap.TryGetValue(node.ParentNodeDefinitionId, out var parentNode))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "parent-not-found",
                    $"Node '{node.NodeDefinitionId}' references missing parent '{node.ParentNodeDefinitionId}'.",
                    NodeDefinitionId: node.NodeDefinitionId));
                continue;
            }

            if (!allowedParents.Contains(parentNode.NodeKind))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "invalid-parent-kind",
                    $"Node '{node.NodeDefinitionId}' of type '{node.NodeKind}' cannot be parented by '{parentNode.NodeKind}'.",
                    NodeDefinitionId: node.NodeDefinitionId));
            }

            if (CreatesParentCycle(node.NodeDefinitionId, nodeMap))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "parent-cycle",
                    $"Node '{node.NodeDefinitionId}' participates in a parent cycle.",
                    NodeDefinitionId: node.NodeDefinitionId));
            }
        }

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (nodeMap.ContainsKey(definition.OrganizationNodeDefinitionId))
        {
            Traverse(definition.OrganizationNodeDefinitionId, childLookup, reachable);
        }

        foreach (var node in nodes.Where(node => !reachable.Contains(node.NodeDefinitionId)))
        {
            issues.Add(new EnvironmentValidationIssueDto(
                EnvironmentValidationSeverity.Error,
                "orphaned-node",
                $"Node '{node.NodeDefinitionId}' is orphaned from the organization root.",
                NodeDefinitionId: node.NodeDefinitionId));
        }

        foreach (var relationship in relationships)
        {
            if (!nodeMap.ContainsKey(relationship.ParentNodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "relationship-parent-missing",
                    $"Relationship '{relationship.RelationshipDefinitionId}' references missing parent '{relationship.ParentNodeDefinitionId}'.",
                    RelationshipDefinitionId: relationship.RelationshipDefinitionId));
            }

            if (!nodeMap.ContainsKey(relationship.ChildNodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "relationship-child-missing",
                    $"Relationship '{relationship.RelationshipDefinitionId}' references missing child '{relationship.ChildNodeDefinitionId}'.",
                    RelationshipDefinitionId: relationship.RelationshipDefinitionId));
            }

            if (string.Equals(relationship.ParentNodeDefinitionId, relationship.ChildNodeDefinitionId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "self-relationship",
                    $"Relationship '{relationship.RelationshipDefinitionId}' cannot point a node at itself.",
                    RelationshipDefinitionId: relationship.RelationshipDefinitionId));
            }
        }

        ValidateDuplicateCodes(nodes, issues);

        var laneNodeOwnership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lane in lanes)
        {
            if (!nodeMap.ContainsKey(lane.RootNodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "lane-root-missing",
                    $"Lane '{lane.LaneId}' references missing root node '{lane.RootNodeDefinitionId}'.",
                    LaneId: lane.LaneId,
                    NodeDefinitionId: lane.RootNodeDefinitionId));
            }

            if (!nodeMap.ContainsKey(lane.DefaultContextNodeDefinitionId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "lane-default-context-missing",
                    $"Lane '{lane.LaneId}' references missing default context node '{lane.DefaultContextNodeDefinitionId}'.",
                    LaneId: lane.LaneId,
                    NodeDefinitionId: lane.DefaultContextNodeDefinitionId));
            }

            if (!AllowedWorkspaceIds.Contains(lane.DefaultWorkspaceId))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "invalid-default-workspace",
                    $"Lane '{lane.LaneId}' uses unsupported default workspace '{lane.DefaultWorkspaceId}'.",
                    LaneId: lane.LaneId));
            }

            if (!AllowedLandingPageTags.Contains(lane.DefaultLandingPageTag))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "invalid-default-landing",
                    $"Lane '{lane.LaneId}' uses unsupported default landing page '{lane.DefaultLandingPageTag}'.",
                    LaneId: lane.LaneId));
            }

            if (nodeMap.ContainsKey(lane.RootNodeDefinitionId) &&
                nodeMap.ContainsKey(lane.DefaultContextNodeDefinitionId) &&
                !IsWithinSubtree(lane.RootNodeDefinitionId, lane.DefaultContextNodeDefinitionId, nodeMap))
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "lane-default-context-outside-root",
                    $"Lane '{lane.LaneId}' must choose a default context within its root subtree.",
                    LaneId: lane.LaneId,
                    NodeDefinitionId: lane.DefaultContextNodeDefinitionId));
            }

            if (nodeMap.ContainsKey(lane.RootNodeDefinitionId))
            {
                var subtree = GetSubtreeNodeIds(lane.RootNodeDefinitionId, childLookup);
                foreach (var nodeId in subtree)
                {
                    if (laneNodeOwnership.TryGetValue(nodeId, out var owningLane) &&
                        !string.Equals(owningLane, lane.LaneId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(nodeId, definition.OrganizationNodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new EnvironmentValidationIssueDto(
                            EnvironmentValidationSeverity.Error,
                            "lane-overlap",
                            $"Node '{nodeId}' is claimed by both lane '{owningLane}' and lane '{lane.LaneId}'.",
                            LaneId: lane.LaneId,
                            NodeDefinitionId: nodeId));
                    }
                    else
                    {
                        laneNodeOwnership[nodeId] = lane.LaneId;
                    }
                }
            }
        }

        foreach (var vehicle in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Vehicle))
        {
            var hasEntity = nodes.Any(node =>
                node.NodeKind == EnvironmentNodeKind.Entity &&
                string.Equals(node.ParentNodeDefinitionId, vehicle.NodeDefinitionId, StringComparison.OrdinalIgnoreCase));
            if (!hasEntity)
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "vehicle-missing-entity",
                    $"Vehicle '{vehicle.NodeDefinitionId}' must have a directly attached entity node.",
                    NodeDefinitionId: vehicle.NodeDefinitionId));
            }
        }

        if (publishPlan is not null && currentVersion is not null)
        {
            var nextNodeIds = nodes.Select(static node => node.NodeDefinitionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remappedNodeIds = (publishPlan.NodeRemaps ?? [])
                .Select(static remap => remap.RemovedNodeDefinitionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var removedNode in currentVersion.Runtime.Nodes.Where(node => !nextNodeIds.Contains(node.NodeDefinitionId)))
            {
                if (removedNode.NodeKind is EnvironmentNodeKind.Account or EnvironmentNodeKind.InvestmentPortfolio or EnvironmentNodeKind.LedgerGroup &&
                    !publishPlan.AllowDestructiveDeletes &&
                    !remappedNodeIds.Contains(removedNode.NodeDefinitionId))
                {
                    issues.Add(new EnvironmentValidationIssueDto(
                        EnvironmentValidationSeverity.Error,
                        "destructive-delete-blocked",
                        $"Removing published {removedNode.NodeKind} '{removedNode.Name}' requires either an explicit remap or destructive delete approval.",
                        NodeDefinitionId: removedNode.NodeDefinitionId));
                }
            }
        }

        return issues
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ValidateDuplicateCodes(
        IReadOnlyCollection<EnvironmentNodeDefinitionDto> nodes,
        ICollection<EnvironmentValidationIssueDto> issues)
    {
        foreach (var duplicate in nodes
            .Where(node => node.NodeKind is EnvironmentNodeKind.Business or EnvironmentNodeKind.Client or EnvironmentNodeKind.Fund)
            .GroupBy(node => $"{node.NodeKind}:{node.Code}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var node in duplicate)
            {
                issues.Add(new EnvironmentValidationIssueDto(
                    EnvironmentValidationSeverity.Error,
                    "duplicate-organization-code",
                    $"Code '{node.Code}' is duplicated for node kind '{node.NodeKind}'.",
                    NodeDefinitionId: node.NodeDefinitionId));
            }
        }
    }

    private static bool CreatesParentCycle(
        string startNodeId,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = startNodeId;

        while (nodeMap.TryGetValue(currentId, out var currentNode) &&
               !string.IsNullOrWhiteSpace(currentNode.ParentNodeDefinitionId))
        {
            if (!seen.Add(currentId))
            {
                return true;
            }

            currentId = currentNode.ParentNodeDefinitionId!;
        }

        return false;
    }

    private static void Traverse(
        string rootNodeId,
        IReadOnlyDictionary<string, string[]> childLookup,
        ISet<string> visited)
    {
        if (!visited.Add(rootNodeId))
        {
            return;
        }

        if (!childLookup.TryGetValue(rootNodeId, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            Traverse(child, childLookup, visited);
        }
    }

    private static HashSet<string> GetSubtreeNodeIds(
        string rootNodeId,
        IReadOnlyDictionary<string, string[]> childLookup)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Traverse(rootNodeId, childLookup, visited);
        return visited;
    }

    private static bool IsWithinSubtree(
        string rootNodeId,
        string nodeId,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap)
    {
        var currentId = nodeId;
        while (nodeMap.TryGetValue(currentId, out var currentNode))
        {
            if (string.Equals(currentId, rootNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(currentNode.ParentNodeDefinitionId))
            {
                return false;
            }

            currentId = currentNode.ParentNodeDefinitionId!;
        }

        return false;
    }

    private List<EnvironmentPublishChangeDto> BuildPublishPreviewChanges(
        OrganizationEnvironmentDefinitionDto definition,
        PublishedEnvironmentVersionDto? currentVersion)
    {
        var changes = new List<EnvironmentPublishChangeDto>();
        var nextNodeIds = definition.Nodes.Select(static node => node.NodeDefinitionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentNodes = currentVersion?.Runtime.Nodes ?? [];

        foreach (var node in definition.Nodes)
        {
            if (currentNodes.Any(existing => string.Equals(existing.NodeDefinitionId, node.NodeDefinitionId, StringComparison.OrdinalIgnoreCase)))
            {
                changes.Add(new EnvironmentPublishChangeDto(
                    "Update",
                    node.NodeKind,
                    node.NodeDefinitionId,
                    $"Retain and refresh {node.NodeKind} '{node.Name}'."));
            }
            else
            {
                changes.Add(new EnvironmentPublishChangeDto(
                    "Add",
                    node.NodeKind,
                    node.NodeDefinitionId,
                    $"Add {node.NodeKind} '{node.Name}'."));
            }
        }

        foreach (var removedNode in currentNodes.Where(node => !nextNodeIds.Contains(node.NodeDefinitionId)))
        {
            changes.Add(new EnvironmentPublishChangeDto(
                "Remove",
                removedNode.NodeKind,
                removedNode.NodeDefinitionId,
                $"Remove published {removedNode.NodeKind} '{removedNode.Name}'.",
                IsBreaking: removedNode.NodeKind is EnvironmentNodeKind.Account or EnvironmentNodeKind.InvestmentPortfolio or EnvironmentNodeKind.LedgerGroup));
        }

        return changes
            .OrderBy(static change => change.ChangeType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static change => change.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private PublishedEnvironmentRuntimeDto CompileRuntime(
        OrganizationEnvironmentDefinitionDto definition,
        PublishedEnvironmentVersionDto? currentVersion,
        DateTimeOffset publishedAt)
    {
        var normalizedDefinition = NormalizeDefinition(definition);
        var nodes = normalizedDefinition.Nodes
            .OrderBy(static node => node.NodeKind)
            .ThenBy(static node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static node => node.NodeDefinitionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nodeMap = nodes.ToDictionary(node => node.NodeDefinitionId, StringComparer.OrdinalIgnoreCase);
        var childLookup = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId))
            .GroupBy(node => node.ParentNodeDefinitionId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(static child => child.NodeDefinitionId).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var currentMappings = currentVersion?.RuntimeNodeMappings
            ?? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var runtimeNodeIds = nodes.ToDictionary(
            node => node.NodeDefinitionId,
            node => currentMappings.TryGetValue(node.NodeDefinitionId, out var existingId) ? existingId : Guid.NewGuid(),
            StringComparer.OrdinalIgnoreCase);

        var laneRuntimeById = normalizedDefinition.Lanes
            .OrderBy(static lane => lane.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static lane => lane.LaneId, StringComparer.OrdinalIgnoreCase)
            .Select(static lane => new EnvironmentLaneRuntimeDto(
                lane.LaneId,
                lane.Name,
                lane.Archetype,
                lane.RootNodeDefinitionId,
                lane.DefaultWorkspaceId,
                lane.DefaultLandingPageTag,
                lane.DefaultContextNodeDefinitionId,
                lane.AllowedManagedScopeKinds ?? [],
                lane.LabelOverrides,
                lane.ShowInNavigation,
                lane.UseForEmptyStates))
            .ToArray();
        var primaryLaneByNodeId = BuildPrimaryLaneByNodeId(normalizedDefinition, childLookup);

        var organizations = new List<OrganizationSummaryDto>();
        var businesses = new List<BusinessSummaryDto>();
        var clients = new List<ClientSummaryDto>();
        var funds = new List<FundSummaryDto>();
        var sleeves = new List<SleeveSummaryDto>();
        var vehicles = new List<VehicleSummaryDto>();
        var entities = new List<LegalEntitySummaryDto>();
        var portfolios = new List<InvestmentPortfolioSummaryDto>();
        var accounts = new List<AccountSummaryDto>();

        var organizationRuntimeId = runtimeNodeIds[normalizedDefinition.OrganizationNodeDefinitionId];
        var businessIds = nodes
            .Where(static node => node.NodeKind == EnvironmentNodeKind.Business)
            .Select(node => runtimeNodeIds[node.NodeDefinitionId])
            .ToArray();
        organizations.Add(new OrganizationSummaryDto(
            normalizedDefinition.OrganizationId == Guid.Empty ? organizationRuntimeId : normalizedDefinition.OrganizationId,
            normalizedDefinition.OrganizationCode,
            normalizedDefinition.OrganizationName,
            normalizedDefinition.BaseCurrency,
            IsActive: true,
            EffectiveFrom: publishedAt,
            EffectiveTo: null,
            BusinessIds: businessIds));

        foreach (var businessNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Business))
        {
            var businessId = runtimeNodeIds[businessNode.NodeDefinitionId];
            var subtree = GetSubtreeNodeIds(businessNode.NodeDefinitionId, childLookup);
            businesses.Add(new BusinessSummaryDto(
                businessId,
                normalizedDefinition.OrganizationId == Guid.Empty ? organizationRuntimeId : normalizedDefinition.OrganizationId,
                businessNode.BusinessKind ?? BusinessKindDto.Hybrid,
                businessNode.Code,
                businessNode.Name,
                NormalizeCurrency(string.IsNullOrWhiteSpace(businessNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : businessNode.BaseCurrency),
                IsActive: businessNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                ClientIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Client &&
                                   string.Equals(node.ParentNodeDefinitionId, businessNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                FundIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Fund &&
                                   string.Equals(node.ParentNodeDefinitionId, businessNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                InvestmentPortfolioIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                Description: businessNode.Description));
        }

        foreach (var clientNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Client))
        {
            var clientId = runtimeNodeIds[clientNode.NodeDefinitionId];
            var businessId = FindNearestAncestorRuntimeId(clientNode.NodeDefinitionId, EnvironmentNodeKind.Business, nodeMap, runtimeNodeIds)
                ?? organizationRuntimeId;
            var subtree = GetSubtreeNodeIds(clientNode.NodeDefinitionId, childLookup);
            clients.Add(new ClientSummaryDto(
                clientId,
                businessId,
                clientNode.Code,
                clientNode.Name,
                NormalizeCurrency(string.IsNullOrWhiteSpace(clientNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : clientNode.BaseCurrency),
                IsActive: clientNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                InvestmentPortfolioIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                Description: clientNode.Description,
                ClientSegmentKind: clientNode.ClientSegmentKind));
        }

        foreach (var fundNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Fund))
        {
            var fundId = runtimeNodeIds[fundNode.NodeDefinitionId];
            var subtree = GetSubtreeNodeIds(fundNode.NodeDefinitionId, childLookup);
            funds.Add(new FundSummaryDto(
                fundId,
                FindNearestAncestorRuntimeId(fundNode.NodeDefinitionId, EnvironmentNodeKind.Business, nodeMap, runtimeNodeIds),
                fundNode.Code,
                fundNode.Name,
                NormalizeCurrency(string.IsNullOrWhiteSpace(fundNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : fundNode.BaseCurrency),
                IsActive: fundNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                SleeveIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Sleeve &&
                                   string.Equals(node.ParentNodeDefinitionId, fundNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                VehicleIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Vehicle &&
                                   string.Equals(node.ParentNodeDefinitionId, fundNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                EntityIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Entity && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                InvestmentPortfolioIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                AccountIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Account && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                Description: fundNode.Description));
        }

        foreach (var sleeveNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Sleeve))
        {
            var subtree = GetSubtreeNodeIds(sleeveNode.NodeDefinitionId, childLookup);
            sleeves.Add(new SleeveSummaryDto(
                runtimeNodeIds[sleeveNode.NodeDefinitionId],
                FindNearestAncestorRuntimeId(sleeveNode.NodeDefinitionId, EnvironmentNodeKind.Fund, nodeMap, runtimeNodeIds) ?? Guid.Empty,
                sleeveNode.Code,
                sleeveNode.Name,
                sleeveNode.Description,
                IsActive: sleeveNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                StrategyIds: [],
                InvestmentPortfolioIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                AccountIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Account && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray()));
        }

        foreach (var entityNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Entity))
        {
            entities.Add(new LegalEntitySummaryDto(
                runtimeNodeIds[entityNode.NodeDefinitionId],
                entityNode.LegalEntityType ?? LegalEntityTypeDto.Other,
                entityNode.Code,
                entityNode.Name,
                entityNode.Jurisdiction ?? "US",
                NormalizeCurrency(entityNode.BaseCurrency ?? normalizedDefinition.BaseCurrency),
                IsActive: entityNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                Description: entityNode.Description));
        }

        foreach (var vehicleNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Vehicle))
        {
            var subtree = GetSubtreeNodeIds(vehicleNode.NodeDefinitionId, childLookup);
            var legalEntityId = nodes
                .Where(node => node.NodeKind == EnvironmentNodeKind.Entity &&
                               string.Equals(node.ParentNodeDefinitionId, vehicleNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                .FirstOrDefault();
            vehicles.Add(new VehicleSummaryDto(
                runtimeNodeIds[vehicleNode.NodeDefinitionId],
                FindNearestAncestorRuntimeId(vehicleNode.NodeDefinitionId, EnvironmentNodeKind.Fund, nodeMap, runtimeNodeIds) ?? Guid.Empty,
                legalEntityId,
                vehicleNode.Code,
                vehicleNode.Name,
                NormalizeCurrency(string.IsNullOrWhiteSpace(vehicleNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : vehicleNode.BaseCurrency),
                IsActive: vehicleNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                InvestmentPortfolioIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                AccountIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Account && subtree.Contains(node.NodeDefinitionId))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                Description: vehicleNode.Description));
        }

        foreach (var portfolioNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.InvestmentPortfolio))
        {
            portfolios.Add(new InvestmentPortfolioSummaryDto(
                runtimeNodeIds[portfolioNode.NodeDefinitionId],
                FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Business, nodeMap, runtimeNodeIds)
                    ?? organizationRuntimeId,
                portfolioNode.Code,
                portfolioNode.Name,
                NormalizeCurrency(string.IsNullOrWhiteSpace(portfolioNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : portfolioNode.BaseCurrency),
                IsActive: portfolioNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                ClientId: FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Client, nodeMap, runtimeNodeIds),
                FundId: FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Fund, nodeMap, runtimeNodeIds),
                SleeveId: FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Sleeve, nodeMap, runtimeNodeIds),
                VehicleId: FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Vehicle, nodeMap, runtimeNodeIds),
                EntityId: FindNearestAncestorRuntimeId(portfolioNode.NodeDefinitionId, EnvironmentNodeKind.Entity, nodeMap, runtimeNodeIds),
                AccountIds: nodes
                    .Where(node => node.NodeKind == EnvironmentNodeKind.Account &&
                                   string.Equals(node.ParentNodeDefinitionId, portfolioNode.NodeDefinitionId, StringComparison.OrdinalIgnoreCase))
                    .Select(node => runtimeNodeIds[node.NodeDefinitionId])
                    .ToArray(),
                Description: portfolioNode.Description,
                SharedDataAccess: null));
        }

        foreach (var accountNode in nodes.Where(static node => node.NodeKind == EnvironmentNodeKind.Account))
        {
            var portfolioRuntimeId = FindNearestAncestorRuntimeId(accountNode.NodeDefinitionId, EnvironmentNodeKind.InvestmentPortfolio, nodeMap, runtimeNodeIds);
            accounts.Add(new AccountSummaryDto(
                runtimeNodeIds[accountNode.NodeDefinitionId],
                accountNode.AccountType ?? AccountTypeDto.Other,
                EntityId: FindNearestAncestorRuntimeId(accountNode.NodeDefinitionId, EnvironmentNodeKind.Entity, nodeMap, runtimeNodeIds),
                FundId: FindNearestAncestorRuntimeId(accountNode.NodeDefinitionId, EnvironmentNodeKind.Fund, nodeMap, runtimeNodeIds),
                SleeveId: FindNearestAncestorRuntimeId(accountNode.NodeDefinitionId, EnvironmentNodeKind.Sleeve, nodeMap, runtimeNodeIds),
                VehicleId: FindNearestAncestorRuntimeId(accountNode.NodeDefinitionId, EnvironmentNodeKind.Vehicle, nodeMap, runtimeNodeIds),
                AccountCode: accountNode.Code,
                DisplayName: accountNode.Name,
                BaseCurrency: NormalizeCurrency(string.IsNullOrWhiteSpace(accountNode.BaseCurrency) ? normalizedDefinition.BaseCurrency : accountNode.BaseCurrency),
                Institution: accountNode.Institution,
                IsActive: accountNode.IsActive,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                PortfolioId: portfolioRuntimeId?.ToString("D"),
                LedgerReference: accountNode.LedgerReference,
                StrategyId: null,
                RunId: null,
                CustodianDetails: null,
                BankDetails: null,
                SharedDataAccess: null));
        }

        var graphNodes = nodes
            .Where(node => TryMapToFundStructureNodeKind(node.NodeKind, out _))
            .Select(node =>
            {
                TryMapToFundStructureNodeKind(node.NodeKind, out var graphKind);
                return new FundStructureNodeDto(
                    runtimeNodeIds[node.NodeDefinitionId],
                    graphKind,
                    node.Code,
                    node.Name,
                    node.Description,
                    node.IsActive,
                    publishedAt,
                    null);
            })
            .ToArray();

        var ownershipLinks = BuildOwnershipLinks(normalizedDefinition, nodeMap, runtimeNodeIds, publishedAt);
        var assignments = BuildAssignments(nodes, nodeMap, runtimeNodeIds, publishedAt);
        var runtimeNodes = nodes
            .Select(node => new PublishedEnvironmentNodeRuntimeDto(
                node.NodeDefinitionId,
                runtimeNodeIds[node.NodeDefinitionId],
                node.NodeKind,
                node.Name,
                CreateContextKey(node.NodeKind, runtimeNodeIds[node.NodeDefinitionId]),
                primaryLaneByNodeId.TryGetValue(node.NodeDefinitionId, out var laneId) ? laneId : node.LaneId))
            .ToArray();
        var contextMappings = BuildContextMappings(normalizedDefinition, childLookup, runtimeNodeIds, nodeMap);
        var ledgerGroups = BuildLedgerGroups(nodes, childLookup, nodeMap, runtimeNodeIds, normalizedDefinition.OrganizationId);

        return new PublishedEnvironmentRuntimeDto(
            normalizedDefinition.OrganizationId == Guid.Empty ? organizationRuntimeId : normalizedDefinition.OrganizationId,
            normalizedDefinition.OrganizationName,
            normalizedDefinition.BaseCurrency,
            new OrganizationStructureGraphDto(
                organizations,
                businesses,
                clients,
                funds,
                sleeves,
                vehicles,
                entities,
                portfolios,
                accounts,
                graphNodes,
                ownershipLinks,
                assignments,
                SharedDataAccess: null),
            laneRuntimeById,
            runtimeNodes,
            contextMappings,
            ledgerGroups);
    }

    private string BuildNextVersionLabel(Guid organizationId)
    {
        lock (_gate)
        {
            var count = _versions.Count(version => version.OrganizationId == organizationId) + 1;
            return $"v{count:000}";
        }
    }

    private PublishedEnvironmentVersionDto? GetCurrentPublishedVersionLocked(Guid? organizationId)
    {
        if (organizationId.HasValue)
        {
            if (_currentVersionByOrganizationId.TryGetValue(organizationId.Value, out var currentVersionId))
            {
                return _versions.FirstOrDefault(version => version.VersionId == currentVersionId);
            }

            return _versions
                .Where(version => version.OrganizationId == organizationId.Value)
                .OrderByDescending(static version => version.PublishedAt)
                .FirstOrDefault();
        }

        if (_currentVersionByOrganizationId.Count > 0)
        {
            var currentIds = _currentVersionByOrganizationId.Values.ToHashSet();
            return _versions
                .Where(version => currentIds.Contains(version.VersionId))
                .OrderByDescending(static version => version.PublishedAt)
                .FirstOrDefault();
        }

        return _versions
            .OrderByDescending(static version => version.PublishedAt)
            .FirstOrDefault();
    }

    private PublishedEnvironmentVersionDto ApplyCurrentFlagLocked(PublishedEnvironmentVersionDto version)
    {
        var isCurrent = _currentVersionByOrganizationId.TryGetValue(version.OrganizationId, out var currentVersionId) &&
                        currentVersionId == version.VersionId;
        return version with { IsCurrent = isCurrent };
    }

    private void ReplaceCurrentVersionLocked(Guid organizationId, PublishedEnvironmentVersionDto publishedVersion)
    {
        _versions.RemoveAll(version => version.VersionId == publishedVersion.VersionId);
        _versions.Add(publishedVersion with { IsCurrent = false });
        _currentVersionByOrganizationId[organizationId] = publishedVersion.VersionId;
    }

    private void LoadState()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var state = JsonSerializer.Deserialize<PersistedEnvironmentState>(json, JsonOptions);
            if (state is null)
            {
                return;
            }

            lock (_gate)
            {
                _drafts.Clear();
                _drafts.AddRange((state.Drafts ?? [])
                    .Select(draft => draft with
                    {
                        Definition = NormalizeDefinition(draft.Definition)
                    }));

                _versions.Clear();
                _versions.AddRange((state.Versions ?? [])
                    .Select(version => version with
                    {
                        IsCurrent = false
                    }));

                _currentVersionByOrganizationId.Clear();
                if (state.CurrentVersionByOrganizationId is not null)
                {
                    foreach (var pair in state.CurrentVersionByOrganizationId)
                    {
                        _currentVersionByOrganizationId[pair.Key] = pair.Value;
                    }
                }

                if (_currentVersionByOrganizationId.Count == 0)
                {
                    foreach (var currentVersion in _versions.Where(static version => version.IsCurrent))
                    {
                        _currentVersionByOrganizationId[currentVersion.OrganizationId] = currentVersion.VersionId;
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        if (_persistencePath is null)
        {
            return;
        }

        PersistedEnvironmentState snapshot;
        lock (_gate)
        {
            snapshot = new PersistedEnvironmentState(
                Version: 1,
                Drafts: _drafts.ToList(),
                Versions: _versions.Select(ApplyCurrentFlagLocked).ToList(),
                CurrentVersionByOrganizationId: new Dictionary<Guid, Guid>(_currentVersionByOrganizationId));
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var directory = Path.GetDirectoryName(_persistencePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_persistencePath}.tmp";
        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            File.Move(tempPath, _persistencePath, overwrite: true);
        }
        finally
        {
            _persistGate.Release();
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> BuildPrimaryLaneByNodeId(
        OrganizationEnvironmentDefinitionDto definition,
        IReadOnlyDictionary<string, string[]> childLookup)
    {
        var laneByNodeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lane in definition.Lanes)
        {
            var subtree = GetSubtreeNodeIds(lane.RootNodeDefinitionId, childLookup);
            foreach (var nodeId in subtree)
            {
                if (!laneByNodeId.ContainsKey(nodeId))
                {
                    laneByNodeId[nodeId] = lane.LaneId;
                }
            }
        }

        return laneByNodeId;
    }

    private static IReadOnlyList<OwnershipLinkDto> BuildOwnershipLinks(
        OrganizationEnvironmentDefinitionDto definition,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap,
        IReadOnlyDictionary<string, Guid> runtimeNodeIds,
        DateTimeOffset publishedAt)
    {
        var links = new List<OwnershipLinkDto>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in definition.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId) ||
                !nodeMap.ContainsKey(node.ParentNodeDefinitionId) ||
                node.NodeKind == EnvironmentNodeKind.Organization ||
                node.NodeKind == EnvironmentNodeKind.LedgerGroup ||
                nodeMap[node.ParentNodeDefinitionId].NodeKind == EnvironmentNodeKind.LedgerGroup)
            {
                continue;
            }

            var relationshipType = node.ParentRelationshipType ?? GetDefaultRelationshipType(node.NodeKind);
            var dedupeKey = $"{node.ParentNodeDefinitionId}|{node.NodeDefinitionId}|{relationshipType}";
            if (!seenKeys.Add(dedupeKey))
            {
                continue;
            }

            links.Add(new OwnershipLinkDto(
                CreateStableGuid($"parent-link:{dedupeKey}"),
                runtimeNodeIds[node.ParentNodeDefinitionId],
                runtimeNodeIds[node.NodeDefinitionId],
                relationshipType,
                OwnershipPercent: null,
                IsPrimary: true,
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                Notes: "Environment designer parent relationship"));
        }

        foreach (var relationship in definition.Relationships)
        {
            if (!nodeMap.ContainsKey(relationship.ParentNodeDefinitionId) ||
                !nodeMap.ContainsKey(relationship.ChildNodeDefinitionId) ||
                nodeMap[relationship.ParentNodeDefinitionId].NodeKind == EnvironmentNodeKind.LedgerGroup ||
                nodeMap[relationship.ChildNodeDefinitionId].NodeKind == EnvironmentNodeKind.LedgerGroup)
            {
                continue;
            }

            var dedupeKey = $"{relationship.ParentNodeDefinitionId}|{relationship.ChildNodeDefinitionId}|{relationship.RelationshipType}";
            if (!seenKeys.Add(dedupeKey))
            {
                continue;
            }

            links.Add(new OwnershipLinkDto(
                CreateStableGuid($"explicit-link:{relationship.RelationshipDefinitionId}"),
                runtimeNodeIds[relationship.ParentNodeDefinitionId],
                runtimeNodeIds[relationship.ChildNodeDefinitionId],
                relationship.RelationshipType,
                relationship.OwnershipPercent,
                relationship.IsPrimary,
                publishedAt,
                EffectiveTo: null,
                relationship.Notes));
        }

        return links
            .OrderBy(static link => link.ParentNodeId)
            .ThenBy(static link => link.ChildNodeId)
            .ToArray();
    }

    private static IReadOnlyList<FundStructureAssignmentDto> BuildAssignments(
        IReadOnlyList<EnvironmentNodeDefinitionDto> nodes,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap,
        IReadOnlyDictionary<string, Guid> runtimeNodeIds,
        DateTimeOffset publishedAt)
    {
        return nodes
            .Where(node => node.NodeKind is EnvironmentNodeKind.Account or EnvironmentNodeKind.InvestmentPortfolio)
            .Where(node => !string.IsNullOrWhiteSpace(node.ParentNodeDefinitionId) &&
                           nodeMap.TryGetValue(node.ParentNodeDefinitionId, out var parentNode) &&
                           parentNode.NodeKind == EnvironmentNodeKind.LedgerGroup)
            .Select(node => new FundStructureAssignmentDto(
                CreateStableGuid($"ledger-assignment:{node.ParentNodeDefinitionId}:{node.NodeDefinitionId}"),
                runtimeNodeIds[node.NodeDefinitionId],
                AssignmentType: LedgerGroupingRules.LedgerGroupAssignmentType,
                AssignmentReference: LedgerGroupingRules.NormalizeAssignmentReference(
                    LedgerGroupingRules.LedgerGroupAssignmentType,
                    node.ParentNodeDefinitionId!),
                EffectiveFrom: publishedAt,
                EffectiveTo: null,
                IsPrimary: true))
            .ToArray();
    }

    private static IReadOnlyList<EnvironmentContextMappingDto> BuildContextMappings(
        OrganizationEnvironmentDefinitionDto definition,
        IReadOnlyDictionary<string, string[]> childLookup,
        IReadOnlyDictionary<string, Guid> runtimeNodeIds,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap)
    {
        var mappings = new List<EnvironmentContextMappingDto>();
        foreach (var lane in definition.Lanes)
        {
            var subtree = GetSubtreeNodeIds(lane.RootNodeDefinitionId, childLookup);
            foreach (var nodeId in subtree.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (!nodeMap.TryGetValue(nodeId, out var node))
                {
                    continue;
                }

                mappings.Add(new EnvironmentContextMappingDto(
                    nodeId,
                    runtimeNodeIds[nodeId],
                    CreateContextKey(node.NodeKind, runtimeNodeIds[nodeId]),
                    lane.LaneId,
                    lane.Name,
                    lane.Archetype));
            }
        }

        return mappings;
    }

    private static IReadOnlyList<EnvironmentLedgerGroupRuntimeDto> BuildLedgerGroups(
        IReadOnlyList<EnvironmentNodeDefinitionDto> nodes,
        IReadOnlyDictionary<string, string[]> childLookup,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap,
        IReadOnlyDictionary<string, Guid> runtimeNodeIds,
        Guid organizationId)
    {
        return nodes
            .Where(static node => node.NodeKind == EnvironmentNodeKind.LedgerGroup)
            .Select(node =>
            {
                var subtree = GetSubtreeNodeIds(node.NodeDefinitionId, childLookup);
                return new EnvironmentLedgerGroupRuntimeDto(
                    runtimeNodeIds[node.NodeDefinitionId],
                    node.NodeDefinitionId,
                    node.Name,
                    NormalizeCurrency(node.BaseCurrency),
                    OrganizationId: organizationId == Guid.Empty
                        ? FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Organization, nodeMap, runtimeNodeIds)
                        : organizationId,
                    BusinessId: FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Business, nodeMap, runtimeNodeIds),
                    ClientId: FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Client, nodeMap, runtimeNodeIds),
                    FundId: FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Fund, nodeMap, runtimeNodeIds),
                    SleeveId: FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Sleeve, nodeMap, runtimeNodeIds),
                    VehicleId: FindNearestAncestorRuntimeId(node.NodeDefinitionId, EnvironmentNodeKind.Vehicle, nodeMap, runtimeNodeIds),
                    AccountIds: nodes
                        .Where(candidate => candidate.NodeKind == EnvironmentNodeKind.Account && subtree.Contains(candidate.NodeDefinitionId))
                        .Select(candidate => runtimeNodeIds[candidate.NodeDefinitionId])
                        .ToArray(),
                    InvestmentPortfolioIds: nodes
                        .Where(candidate => candidate.NodeKind == EnvironmentNodeKind.InvestmentPortfolio && subtree.Contains(candidate.NodeDefinitionId))
                        .Select(candidate => runtimeNodeIds[candidate.NodeDefinitionId])
                        .ToArray());
            })
            .OrderBy(static ledger => ledger.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Guid? FindNearestAncestorRuntimeId(
        string nodeDefinitionId,
        EnvironmentNodeKind targetKind,
        IReadOnlyDictionary<string, EnvironmentNodeDefinitionDto> nodeMap,
        IReadOnlyDictionary<string, Guid> runtimeNodeIds)
    {
        var currentId = nodeDefinitionId;
        while (nodeMap.TryGetValue(currentId, out var currentNode))
        {
            if (currentNode.NodeKind == targetKind)
            {
                return runtimeNodeIds[currentId];
            }

            if (string.IsNullOrWhiteSpace(currentNode.ParentNodeDefinitionId))
            {
                return null;
            }

            currentId = currentNode.ParentNodeDefinitionId!;
        }

        return null;
    }

    private static OwnershipRelationshipTypeDto GetDefaultRelationshipType(EnvironmentNodeKind nodeKind)
        => nodeKind switch
        {
            EnvironmentNodeKind.Client => OwnershipRelationshipTypeDto.Advises,
            EnvironmentNodeKind.Fund => OwnershipRelationshipTypeDto.Operates,
            EnvironmentNodeKind.Account => OwnershipRelationshipTypeDto.CustodiesFor,
            EnvironmentNodeKind.Sleeve => OwnershipRelationshipTypeDto.AllocatesTo,
            _ => OwnershipRelationshipTypeDto.Owns
        };

    private static bool TryMapToFundStructureNodeKind(EnvironmentNodeKind nodeKind, out FundStructureNodeKindDto graphKind)
    {
        graphKind = nodeKind switch
        {
            EnvironmentNodeKind.Organization => FundStructureNodeKindDto.Organization,
            EnvironmentNodeKind.Business => FundStructureNodeKindDto.Business,
            EnvironmentNodeKind.Client => FundStructureNodeKindDto.Client,
            EnvironmentNodeKind.Fund => FundStructureNodeKindDto.Fund,
            EnvironmentNodeKind.Sleeve => FundStructureNodeKindDto.Sleeve,
            EnvironmentNodeKind.Vehicle => FundStructureNodeKindDto.Vehicle,
            EnvironmentNodeKind.InvestmentPortfolio => FundStructureNodeKindDto.InvestmentPortfolio,
            EnvironmentNodeKind.Entity => FundStructureNodeKindDto.Entity,
            EnvironmentNodeKind.Account => FundStructureNodeKindDto.Account,
            _ => default
        };

        return nodeKind != EnvironmentNodeKind.LedgerGroup;
    }

    private static string CreateContextKey(EnvironmentNodeKind nodeKind, Guid runtimeNodeId)
        => $"{nodeKind}:{runtimeNodeId:D}";

    private static Guid CreateStableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes[..16].CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private sealed record PersistedEnvironmentState(
        int Version,
        List<EnvironmentDraftDto> Drafts,
        List<PublishedEnvironmentVersionDto> Versions,
        Dictionary<Guid, Guid> CurrentVersionByOrganizationId);
}
