using Meridian.Contracts.Api;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Strategies.Live.Designer;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using static Meridian.Ui.Shared.Evidence.EvidenceContributionHelpers;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>
/// Adds strategy-run provenance and supporting execution evidence to an evidence graph.
/// </summary>
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
        if (run.Summary.StrategyVersionSha256 is not null &&
            (_services.GetService<IHttpContextAccessor>()?.HttpContext is not { } request ||
             !await EvidenceEndpoints.CanViewStrategyEvidenceAccountAsync(request, run.Summary.FundAccountId, ct).ConfigureAwait(false)))
            return Empty($"Strategy run '{context.Subject.SubjectId}' was not found.");

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

        await AddDesignerEvidenceAsync(context, runService, scope, run, nodes, edges, detailId)
            .ConfigureAwait(false);

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

    private async Task AddDesignerEvidenceAsync(
        EvidenceContributionContext context, StrategyRunReadService readService, StrategyRunReadScope? scope,
        StrategyRunDetail run, List<EvidenceNodeDto> nodes, List<EvidenceEdgeDto> edges, string detailId)
    {
        if (!run.Parameters.ContainsKey(DesignerDocumentLiveSource.DesignerDocumentParameterKey))
            return;

        var subject = context.Subject;
        var version = run.Summary.StrategyVersionSha256;
        var simulated = !string.Equals(run.Summary.DataProvenanceToken, "real", StringComparison.OrdinalIgnoreCase);
        var provenance = simulated ? "Simulated" : "Verified input provenance";
        AddLinkedNode(nodes, edges, null, subject, detailId, "designer-version", "strategy-version",
            version is null || simulated ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
            version is null ? "Version unrecorded" : $"{provenance} · Strategy version {version}",
            "StrategyRunStore", run.Summary.LastUpdatedAt);
        if (version is null || scope is null || run.Summary.FundAccountId is not { } accountId)
            return;

        AddLinkedNode(nodes, edges, null, subject, detailId, "designer-inputs", "retained-replay-inputs",
            EvidenceStatusDto.Ready,
            $"{provenance} · Draft {run.Parameters.GetValueOrDefault(DesignerStrategyVersion.DraftRevisionKey)} · " +
            $"Replay {run.Parameters.GetValueOrDefault(DesignerStrategyVersion.DataSnapshotKey)}",
            "StrategyRunStore", run.Summary.StartedAt);

        var ct = context.CancellationToken;
        var targetRunId = run.Summary.Mode == StrategyRunMode.Paper
            ? run.Summary.RunId : run.Summary.Identity?.PromotionTargetRunId;
        if (string.IsNullOrWhiteSpace(targetRunId))
            return;

        var paper = targetRunId == run.Summary.RunId ? run :
            await readService.GetRunDetailAsync(targetRunId, scope, ct).ConfigureAwait(false);
        if (paper is null || paper.Summary.Mode != StrategyRunMode.Paper || paper.Summary.FundAccountId != accountId ||
            paper.Summary.StrategyVersionSha256 != version ||
            (run.Summary.Mode == StrategyRunMode.Backtest && paper.Summary.ParentRunId != run.Summary.RunId))
            return;

        var promotion = paper.Promotion ?? paper.Summary.Promotion;
        var paperId = NodeId(subject, "designer-paper");
        AddLinkedNode(nodes, edges, null, subject, detailId, "designer-paper", "paper-review",
            promotion?.ApprovedBy is null ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
            $"{provenance} · Paper run {targetRunId}" +
            (promotion?.ApprovedBy is { } reviewer ? $" · Reviewed by {reviewer} · Audit {promotion.AuditReference}" : ""),
            "StrategyRunStore", paper.Summary.LastUpdatedAt);

        if (_services.GetService<IOperationalCaseHistoryStore>() is { } history)
        {
            var orders = await new PaperOrderEvidenceReader(history).ReadForRunAsync(targetRunId, accountId, version, ct)
                .ConfigureAwait(false);
            foreach (var order in orders)
            {
                var state = order.Admission?.IntentStatus ?? OrderIntentStatus.ReconciliationRequired;
                var orderKey = $"paper-order-{order.IntentId:N}";
                AddLinkedNode(nodes, edges, null, subject, paperId, orderKey, "paper-order-intent",
                    state == OrderIntentStatus.ReconciliationRequired ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
                    $"Simulated · Intent {order.IntentId:D} · Order {order.OrderId} · {state} · {order.Admission?.OrderState?.Status}",
                    "OrderManagementSystem", order.Admission?.OrderState?.LastUpdatedAt);
                foreach (var handoff in order.Handoffs)
                {
                    var fill = handoff.Fill;
                    AddLinkedNode(nodes, edges, null, subject, NodeId(subject, orderKey), $"paper-fill-{fill.FillId:N}",
                        "simulated-accounting-handoff", EvidenceStatusDto.Ready,
                        FormattableString.Invariant($"Simulated · Fill {fill.FillId:D} · {fill.FilledQuantity} {fill.Symbol} at {fill.FillPrice} · Cost {fill.Commission} · Accounting handoff {handoff.ReceiptSha256}"),
                        "OrderManagementSystem", fill.OccurredAt);
                }
            }
        }

        if (_services.GetService<ExecutionAuditTrailService>() is { } audit)
        {
            var sessionId = PaperSessionPersistenceService.GetStrategyRunSessionId(targetRunId);
            var latest = (await audit.GetAllAsync(ct).ConfigureAwait(false))
                .LastOrDefault(entry => entry.CorrelationId == sessionId && entry.Action == "VerifyReplay");
            AddLinkedNode(nodes, edges, null, subject, paperId, "paper-reconciliation", "paper-reconciliation",
                latest?.Outcome == "Completed" ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
                latest?.Message is { } message ? $"Simulated · {message} · Audit {latest.AuditId}"
                    : "Simulated · Retained session reconciliation is pending.",
                "PaperSessionPersistenceService", latest?.OccurredAt);
        }
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
