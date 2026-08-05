using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using static Meridian.Ui.Shared.Evidence.EvidenceContributionHelpers;

namespace Meridian.Ui.Shared.Evidence;

public sealed class StrategyRunEvidenceContributor : IEvidenceContributor
{
    private const string WorkstationTenantParameterKey = "workstationTenantId";
    private const string WorkstationCompanyParameterKey = "workstationCompanyId";
    private readonly IServiceProvider _services;

    public StrategyRunEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "strategy-run";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.StrategyRunKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var runService = _services.GetService<StrategyRunReadService>();
        if (runService is null)
        {
            return Empty("Strategy run read service is not registered.");
        }

        var ct = context.CancellationToken;
        var scope = ResolveStrategyRunReadScope();
        var run = scope is null
            ? await runService.GetRunDetailAsync(context.Subject.SubjectId, ct).ConfigureAwait(false)
            : await runService.GetRunDetailAsync(context.Subject.SubjectId, scope, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Empty($"Strategy run '{context.Subject.SubjectId}' was not found.");
        }

        if (!CanAccessScopedRun(run.Parameters))
        {
            // Match the unknown-run shape so generic packet and graph routes do not become
            // a cross-tenant strategy-run existence oracle.
            return Empty($"Strategy run '{context.Subject.SubjectId}' was not found.");
        }

        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string>();
        var generatedAt = DateTimeOffset.UtcNow;
        var detailId = NodeId(context.Subject, "detail");
        var ledgerId = NodeId(context.Subject, "ledger");
        nodes.Add(Node(
            context.Subject,
            detailId,
            "strategy-run-detail",
            EvidenceStatusDto.Ready,
            $"Run {run.Summary.RunId} is available with status {run.Summary.Status}.",
            "StrategyRunReadService",
            run.Summary.LastUpdatedAt,
            artifacts:
            [
                Artifact(
                    $"{detailId}:review-packet",
                    "review-packet-route",
                    route: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.Summary.RunId),
                    generatedAt: generatedAt)
            ]));
        required.Add(detailId);

        AddLinkedNode(
            nodes,
            edges,
            required,
            context.Subject,
            detailId,
            "ledger",
            "run-ledger",
            run.Ledger is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            run.Ledger is null
                ? "No ledger summary is available for this run."
                : $"Ledger evidence is available with {run.Ledger.LedgerEntryCount} entry reference(s).",
            "StrategyRunReadService",
            run.Summary.LastUpdatedAt,
            artifacts: run.Ledger is null
                ? []
                : BuildRunLedgerArtifacts(ledgerId, run.Summary.RunId, run.Ledger.AsOf));

        AddLinkedNode(
            nodes,
            edges,
            required,
            context.Subject,
            detailId,
            "portfolio",
            "run-portfolio",
            run.Portfolio is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            run.Portfolio is null
                ? "No portfolio summary is available for this run."
                : $"Portfolio evidence is available with {run.Portfolio.Positions.Count} position reference(s).",
            "StrategyRunReadService",
            run.Summary.LastUpdatedAt);

        var promotionStatus = run.Promotion?.RequiresReview == true || run.Summary.Promotion?.RequiresReview == true
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Ready;
        AddLinkedNode(
            nodes,
            edges,
            required,
            context.Subject,
            detailId,
            "promotion",
            "promotion-review",
            promotionStatus,
            run.Promotion?.Reason ?? run.Summary.Promotion?.Reason ?? "Promotion review evidence is available.",
            "StrategyRunReadService",
            run.Summary.LastUpdatedAt,
            workItemIds: promotionStatus == EvidenceStatusDto.ReviewRequired
                ? [$"promotion-review:{run.Summary.RunId}"]
                : []);

        foreach (var checklistItem in run.AcceptanceChecklist)
        {
            var nodeId = NodeId(
                context.Subject,
                $"promotion-checklist-{checklistItem.ChecklistId.ToLowerInvariant().Replace('_', '-')}");
            var status = checklistItem.Status switch
            {
                StrategyRunAcceptanceChecklistStatusDto.Ready => EvidenceStatusDto.Ready,
                StrategyRunAcceptanceChecklistStatusDto.Rejected => EvidenceStatusDto.Blocked,
                _ => EvidenceStatusDto.ReviewRequired
            };
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["checklistId"] = checklistItem.ChecklistId,
                ["decisionStatus"] = checklistItem.Status.ToString()
            };
            AddMetadata(metadata, "evidenceReference", checklistItem.EvidenceReference);
            AddMetadata(metadata, "decidedBy", checklistItem.DecidedBy);
            AddMetadata(metadata, "decidedAt", checklistItem.DecidedAt?.ToString("O", CultureInfo.InvariantCulture));
            AddMetadata(metadata, "auditReference", checklistItem.AuditReference);
            AddMetadata(metadata, "blocker", checklistItem.Blocker);

            var checklistNode = Node(
                context.Subject,
                nodeId,
                "strategy-promotion-checklist",
                status,
                checklistItem.Status == StrategyRunAcceptanceChecklistStatusDto.Ready
                    ? $"{checklistItem.Label} is backed by a durable promotion decision and keyed evidence."
                    : checklistItem.Blocker ?? $"{checklistItem.Label} requires operator review.",
                "StrategyPromotionRecord",
                checklistItem.DecidedAt,
                artifacts:
                [
                    Artifact(
                        $"{nodeId}:review-packet",
                        "strategy-run-review-packet",
                        route: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.Summary.RunId),
                        generatedAt: generatedAt)
                ],
                workItemIds: checklistItem.Status == StrategyRunAcceptanceChecklistStatusDto.Ready
                    ? []
                    : [$"promotion-checklist:{run.Summary.RunId}:{checklistItem.ChecklistId}"]);
            nodes.Add(checklistNode with { Metadata = metadata });
            edges.Add(new EvidenceEdgeDto(
                detailId,
                nodeId,
                "governed-by",
                $"Canonical promotion checklist item {checklistItem.ChecklistId} governs run {run.Summary.RunId}."));
            required.Add(nodeId);
        }

        var reviewPacketService = _services.GetService<StrategyRunReviewPacketService>();
        if (reviewPacketService is null)
        {
            return new EvidenceContribution(nodes, edges, [], required, ["Strategy run review-packet service is not registered."]);
        }

        var packet = scope is null
            ? await reviewPacketService.GetAsync(context.Subject.SubjectId, ct: ct).ConfigureAwait(false)
            : await reviewPacketService.GetAsync(context.Subject.SubjectId, scope, ct: ct).ConfigureAwait(false);
        if (packet is null)
        {
            return new EvidenceContribution(nodes, edges, [], required, [$"Review packet for run '{context.Subject.SubjectId}' was not found."]);
        }

        AddLinkedNode(
            nodes,
            edges,
            required,
            context.Subject,
            detailId,
            "continuity",
            "run-continuity",
            packet.Continuity?.ContinuityStatus.Warnings.Count > 0 ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
            packet.Continuity is null
                ? "Continuity detail is not available."
                : packet.Continuity.ContinuityStatus.Warnings.Count == 0
                    ? "Run continuity evidence has no open warnings."
                    : $"{packet.Continuity.ContinuityStatus.Warnings.Count} continuity warning(s) require review.",
            "StrategyRunContinuityService",
            packet.GeneratedAt,
            workItemIds: packet.WorkItems.Select(static item => item.WorkItemId).ToArray());

        AddLinkedNode(
            nodes,
            edges,
            null,
            context.Subject,
            detailId,
            "fills",
            "run-fills",
            packet.Fills is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            packet.Fills is null ? "Fill summary is not available." : "Fill summary evidence is available.",
            "StrategyRunReadService",
            packet.GeneratedAt);

        AddLinkedNode(
            nodes,
            edges,
            null,
            context.Subject,
            detailId,
            "attribution",
            "run-attribution",
            packet.Attribution is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            packet.Attribution is null ? "Attribution summary is not available." : "Attribution summary evidence is available.",
            "StrategyRunReadService",
            packet.GeneratedAt);

        return new EvidenceContribution(nodes, edges, [], required, packet.Warnings);
    }

    private static EvidenceContribution Empty(string warning)
        => new([], [], [], [], [warning]);

    private static void AddMetadata(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private StrategyRunReadScope? ResolveStrategyRunReadScope()
    {
        var trustedScope = ResolveTrustedWorkstationScope();
        return string.IsNullOrWhiteSpace(trustedScope.TenantId)
            || string.IsNullOrWhiteSpace(trustedScope.CompanyId)
                ? null
                : new StrategyRunReadScope(trustedScope.TenantId, trustedScope.CompanyId);
    }

    private bool CanAccessScopedRun(IReadOnlyDictionary<string, string> parameters)
    {
        var hasTenant = parameters.TryGetValue(WorkstationTenantParameterKey, out var tenantId);
        var hasCompany = parameters.TryGetValue(WorkstationCompanyParameterKey, out var companyId);
        if (!hasTenant && !hasCompany)
        {
            // Historical non-covered-call strategy runs predate workstation scope metadata.
            return true;
        }

        if (!hasTenant
            || !hasCompany
            || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(companyId))
        {
            return false;
        }

        var trustedScope = ResolveTrustedWorkstationScope();
        return !string.IsNullOrWhiteSpace(trustedScope.TenantId)
               && !string.IsNullOrWhiteSpace(trustedScope.CompanyId)
               && string.Equals(
                   trustedScope.TenantId.Trim(),
                   tenantId.Trim(),
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   trustedScope.CompanyId.Trim(),
                   companyId.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private WorkstationTenantContext ResolveTrustedWorkstationScope()
    {
        var accessor = _services.GetService<IWorkstationTenantContextAccessor>();
        if (accessor is not null && accessor.TryGetCurrent(out var trustedScope))
        {
            return trustedScope;
        }

        var httpContext = _services.GetService<IHttpContextAccessor>()?.HttpContext;
        return httpContext is null
            ? new WorkstationTenantContext(null, null, null, null, UserPermission.None)
            : HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildRunLedgerArtifacts(
        string ledgerId,
        string runId,
        DateTimeOffset generatedAt)
    {
        return
        [
            Artifact(
                $"{ledgerId}:journal",
                "ledger-journal",
                route: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerJournal, "runId", runId),
                generatedAt: generatedAt),
            Artifact(
                $"{ledgerId}:trial-balance",
                "ledger-trial-balance",
                route: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", runId),
                generatedAt: generatedAt)
        ];
    }
}
