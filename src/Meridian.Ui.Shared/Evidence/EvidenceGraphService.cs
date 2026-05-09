using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Evidence;

public sealed class EvidenceGraphService
{
    private readonly EvidenceSubjectResolver _subjectResolver;
    private readonly EvidenceTemplateRegistry _templateRegistry;
    private readonly IEnumerable<IEvidenceContributor> _contributors;
    private readonly IWorkflowActionCatalog? _actionCatalog;
    private readonly ILogger<EvidenceGraphService> _logger;

    public EvidenceGraphService(
        EvidenceSubjectResolver subjectResolver,
        EvidenceTemplateRegistry templateRegistry,
        IEnumerable<IEvidenceContributor> contributors,
        ILogger<EvidenceGraphService> logger,
        IWorkflowActionCatalog? actionCatalog = null)
    {
        _subjectResolver = subjectResolver ?? throw new ArgumentNullException(nameof(subjectResolver));
        _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
        _contributors = contributors ?? throw new ArgumentNullException(nameof(contributors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _actionCatalog = actionCatalog;
    }

    public Task<IReadOnlyList<EvidenceSubjectDto>> ListSubjectsAsync(CancellationToken ct = default)
        => _subjectResolver.ListAsync(ct);

    public bool IsSupportedSubjectKind(string? subjectKind) => _subjectResolver.IsSupportedKind(subjectKind);

    public async Task<EvidencePacketDto?> GetPacketAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
    {
        var subject = await _subjectResolver.ResolveAsync(subjectKind, subjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var nodes = new Dictionary<string, EvidenceNodeDto>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<EvidenceEdgeDto>();
        var requiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actions = new Dictionary<string, WorkflowActionDto>(StringComparer.OrdinalIgnoreCase);
        var context = new EvidenceContributionContext(subject, ct);
        var matchedContributors = 0;

        foreach (var contributor in _contributors)
        {
            ct.ThrowIfCancellationRequested();
            if (!contributor.Supports(subject))
            {
                continue;
            }

            matchedContributors++;
            try
            {
                var contribution = await contributor.ContributeAsync(context).ConfigureAwait(false);
                foreach (var warning in contribution.Warnings)
                {
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        warnings.Add(warning);
                    }
                }

                foreach (var node in contribution.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.EvidenceId))
                    {
                        warnings.Add($"{contributor.ContributorId} returned an evidence node without an id.");
                        continue;
                    }

                    nodes.TryAdd(node.EvidenceId, node);
                }

                foreach (var edge in contribution.Edges)
                {
                    edges.Add(edge);
                }

                foreach (var requiredId in contribution.RequiredEvidenceIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
                {
                    requiredIds.Add(requiredId);
                }

                foreach (var action in contribution.Actions)
                {
                    if (!string.IsNullOrWhiteSpace(action.ActionId))
                    {
                        actions[action.ActionId] = action;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Evidence contributor {ContributorId} failed for {SubjectKind}/{SubjectId}.",
                    contributor.ContributorId,
                    subject.SubjectKind,
                    subject.SubjectId);
                warnings.Add($"{contributor.ContributorId} could not load optional evidence: {ex.Message}");
            }
        }

        if (matchedContributors == 0)
        {
            warnings.Add($"No evidence contributors support subject kind '{subject.SubjectKind}'.");
        }

        var validatedEdges = ValidateEdges(edges, nodes, warnings);
        var nodeList = nodes.Values
            .OrderBy(static node => node.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var workflowId = ResolveWorkflowId(subject.SubjectKind);
        var template = _templateRegistry.GetTemplate(workflowId);
        if (template?.NoOrphanRule == true && nodeList.Length > 1)
        {
            ApplyNoOrphanWarnings(nodeList, validatedEdges, warnings);
        }

        foreach (var action in ResolveEvidenceActions())
        {
            actions[action.ActionId] = action;
        }

        var completeness = BuildCompleteness(nodeList, requiredIds);
        return new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: generatedAt,
            Nodes: nodeList,
            Edges: validatedEdges,
            Completeness: completeness,
            Actions: actions.Values.OrderBy(static action => action.ActionId, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<EvidenceGraphDto?> GetGraphAsync(
        string subjectKind,
        string subjectId,
        CancellationToken ct = default)
    {
        var packet = await GetPacketAsync(subjectKind, subjectId, ct).ConfigureAwait(false);
        return packet is null
            ? null
            : new EvidenceGraphDto(packet.Subject, packet.GeneratedAt, packet.Nodes, packet.Edges, packet.Warnings);
    }

    private IReadOnlyList<WorkflowActionDto> ResolveEvidenceActions()
    {
        var ids = new[]
        {
            WorkflowActionIds.EvidenceOpenPacket,
            WorkflowActionIds.EvidenceValidate,
            WorkflowActionIds.EvidenceExportManifest
        };

        return ids
            .Select(id => _actionCatalog?.ResolveAction(id))
            .Where(static action => action is not null)
            .Select(static action => action!)
            .ToArray();
    }

    private static string ResolveWorkflowId(string subjectKind)
        => subjectKind.ToLowerInvariant() switch
        {
            EvidenceSubjectResolver.StrategyRunKind => "strategy-to-paper-review",
            EvidenceSubjectResolver.PaperReadinessKind => "paper-trading-readiness",
            EvidenceSubjectResolver.ReconciliationReviewKind => "accounting-reconciliation-review",
            EvidenceSubjectResolver.ReportPackKind => "portfolio-reporting-output",
            EvidenceSubjectResolver.AnalysisExportKind => "portfolio-reporting-output",
            EvidenceSubjectResolver.ProviderTrustKind => "paper-trading-readiness",
            _ => string.Empty
        };

    private static IReadOnlyList<EvidenceEdgeDto> ValidateEdges(
        IEnumerable<EvidenceEdgeDto> candidateEdges,
        IReadOnlyDictionary<string, EvidenceNodeDto> nodes,
        List<string> warnings)
    {
        var validated = new List<EvidenceEdgeDto>();
        foreach (var edge in candidateEdges)
        {
            if (!nodes.ContainsKey(edge.FromId) || !nodes.ContainsKey(edge.ToId))
            {
                warnings.Add($"Evidence edge '{edge.FromId}' -> '{edge.ToId}' references a missing node.");
                continue;
            }

            validated.Add(edge);
        }

        return validated
            .GroupBy(static edge => $"{edge.FromId}\n{edge.ToId}\n{edge.Relationship}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static void ApplyNoOrphanWarnings(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlyList<EvidenceEdgeDto> edges,
        List<string> warnings)
    {
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            linked.Add(edge.FromId);
            linked.Add(edge.ToId);
        }

        foreach (var orphan in nodes.Where(node => !linked.Contains(node.EvidenceId)).Skip(1))
        {
            warnings.Add($"Evidence node '{orphan.EvidenceId}' is not linked into the packet graph.");
        }
    }

    private static EvidenceCompletenessDto BuildCompleteness(
        IReadOnlyList<EvidenceNodeDto> nodes,
        IReadOnlySet<string> requiredIds)
    {
        var nodeLookup = nodes.ToDictionary(static node => node.EvidenceId, StringComparer.OrdinalIgnoreCase);
        var required = requiredIds.Count > 0
            ? requiredIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray()
            : nodes.Select(static node => node.EvidenceId).ToArray();
        var ready = new List<string>();
        var missing = new List<string>();
        var stale = new List<string>();
        var blockers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredId in required)
        {
            if (!nodeLookup.TryGetValue(requiredId, out var node))
            {
                missing.Add(requiredId);
                continue;
            }

            if (node.Status == EvidenceStatusDto.Ready)
            {
                ready.Add(requiredId);
            }

            if (node.Status is EvidenceStatusDto.Missing)
            {
                missing.Add(requiredId);
            }

            if (node.Status is EvidenceStatusDto.Stale || node.Freshness.IsStale)
            {
                stale.Add(requiredId);
            }

            if (node.Status is EvidenceStatusDto.Blocked or EvidenceStatusDto.ReviewRequired)
            {
                foreach (var workItemId in node.RelatedWorkItemIds)
                {
                    blockers.Add(workItemId);
                }
            }
        }

        var score = required.Length == 0
            ? 100
            : (int)Math.Round(ready.Count * 100d / required.Length, MidpointRounding.AwayFromZero);
        var status =
            missing.Count > 0 || blockers.Count > 0 ? EvidenceStatusDto.Blocked :
            stale.Count > 0 ? EvidenceStatusDto.Stale :
            ready.Count == required.Length ? EvidenceStatusDto.Ready :
            EvidenceStatusDto.ReviewRequired;

        return new EvidenceCompletenessDto(
            Score: score,
            Status: status,
            RequiredIds: required,
            ReadyIds: ready.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingIds: missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            StaleIds: stale.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            BlockingWorkItemIds: blockers.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
