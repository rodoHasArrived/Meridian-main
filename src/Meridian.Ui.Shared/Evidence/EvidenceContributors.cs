using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using static Meridian.Ui.Shared.Evidence.EvidenceContributionHelpers;

namespace Meridian.Ui.Shared.Evidence;

public sealed class StrategyRunEvidenceContributor : IEvidenceContributor
{
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
        var run = await runService.GetRunDetailAsync(context.Subject.SubjectId, ct).ConfigureAwait(false);
        if (run is null)
        {
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

        var reviewPacketService = _services.GetService<StrategyRunReviewPacketService>();
        if (reviewPacketService is null)
        {
            return new EvidenceContribution(nodes, edges, [], required, ["Strategy run review-packet service is not registered."]);
        }

        var packet = await reviewPacketService.GetAsync(context.Subject.SubjectId, ct: ct).ConfigureAwait(false);
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

public sealed class TradingReadinessEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public TradingReadinessEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "trading-readiness";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.PaperReadinessKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var readinessService = _services.GetService<TradingOperatorReadinessService>();
        if (readinessService is null)
        {
            return new EvidenceContribution([], [], [], [], ["Trading readiness service is not registered."]);
        }

        var readiness = await readinessService.GetAsync(ct: context.CancellationToken).ConfigureAwait(false);
        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string>();
        var rootId = NodeId(context.Subject, "readiness-gates");
        nodes.Add(Node(
            context.Subject,
            rootId,
            "readiness-gate",
            MapStatus(readiness.OverallStatus),
            $"Trading readiness is {readiness.OverallStatus} with {readiness.WorkItems.Count} work item(s).",
            "TradingOperatorReadinessService",
            readiness.AsOf,
            workItemIds: readiness.WorkItems.Select(static item => item.WorkItemId).ToArray()));
        required.Add(rootId);

        foreach (var gate in readiness.AcceptanceGates)
        {
            var gateId = NodeId(context.Subject, $"gate-{gate.GateId}");
            nodes.Add(Node(
                context.Subject,
                gateId,
                "readiness-gate",
                MapStatus(gate.Status),
                gate.Detail,
                "TradingOperatorReadinessService",
                readiness.AsOf,
                workItemIds: readiness.WorkItems
                    .Where(item => string.Equals(item.RunId, gate.RunId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(item.AuditReference, gate.AuditReference, StringComparison.OrdinalIgnoreCase))
                    .Select(static item => item.WorkItemId)
                    .ToArray()));
            edges.Add(new EvidenceEdgeDto(rootId, gateId, "contains", gate.Label));
            required.Add(gateId);
        }

        if (readiness.ReportPack is not null)
        {
            var reportPackId = NodeId(context.Subject, "report-pack");
            nodes.Add(Node(
                context.Subject,
                reportPackId,
                "report-pack",
                MapStatus(readiness.ReportPack.Status),
                readiness.ReportPack.Detail,
                "TradingOperatorReadinessService",
                readiness.ReportPack.GeneratedAt ?? readiness.AsOf,
                artifacts: string.IsNullOrWhiteSpace(readiness.ReportPack.ManifestPath)
                    ? []
                    :
                    [
                        Artifact(
                            $"{reportPackId}:manifest",
                            "report-pack-manifest",
                            path: readiness.ReportPack.ManifestPath,
                            generatedAt: readiness.ReportPack.GeneratedAt ?? readiness.AsOf)
                    ]));
            edges.Add(new EvidenceEdgeDto(rootId, reportPackId, "requires", "Report-pack approval evidence supports paper readiness."));
        }

        return new EvidenceContribution(nodes, edges, [], required, readiness.Warnings);
    }
}

public sealed class ReconciliationEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public ReconciliationEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "reconciliation";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.StrategyRunKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.StatementRunKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReconciliationReviewKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var runId = context.Subject.SubjectId;
        if (string.Equals(context.Subject.SubjectKind, EvidenceSubjectResolver.StatementRunKind, StringComparison.OrdinalIgnoreCase))
        {
            return await ContributeStatementRunAsync(context, runId).ConfigureAwait(false);
        }

        var service = _services.GetService<IReconciliationRunService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Reconciliation run service is not registered."]);
        }

        var detail = string.Equals(context.Subject.SubjectKind, EvidenceSubjectResolver.StrategyRunKind, StringComparison.OrdinalIgnoreCase)
            ? await service.GetLatestForRunAsync(runId, context.CancellationToken).ConfigureAwait(false)
            : await service.GetByIdAsync(runId, context.CancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return new EvidenceContribution([], [], [], [], [$"No reconciliation evidence is available for '{runId}'."]);
        }

        var nodeId = NodeId(context.Subject, "reconciliation");
        var status = detail.Summary.OpenBreakCount > 0 || detail.Summary.HasTimingDrift
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Ready;
        var node = Node(
            context.Subject,
            nodeId,
            "reconciliation-run",
            status,
            $"{detail.Summary.MatchCount} match(es), {detail.Summary.OpenBreakCount} open break(s), timing drift: {detail.Summary.HasTimingDrift}.",
            "ReconciliationRunService",
            detail.Summary.CreatedAt,
            workItemIds: detail.Breaks.Select(static item => item.CheckId).ToArray());

        return new EvidenceContribution([node], [], [], [nodeId], []);
    }

    private async Task<EvidenceContribution> ContributeStatementRunAsync(EvidenceContributionContext context, string runId)
    {
        var service = _services.GetService<IReconciliationApiService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Statement reconciliation API service is not registered."]);
        }

        var detail = await service.GetStatementRunAsync(runId, context.CancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return new EvidenceContribution([], [], [], [], [$"No statement-run evidence is available for '{runId}'."]);
        }

        var nodeId = NodeId(context.Subject, "statement-run");
        var runKey = string.IsNullOrWhiteSpace(detail.RunId) ? runId : detail.RunId!;
        var matchSummary = detail.MatchSummary;
        var openExceptionCount = detail.Breaks?.Count(static item =>
            string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Status, "InReview", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var status = openExceptionCount > 0 ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready;
        var generatedAt = detail.CompletedAtUtc ?? detail.ImportedAtUtc ?? detail.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var route = $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(runKey)}";
        var sourceFileHash = string.IsNullOrWhiteSpace(detail.SourceFileHash) ? null : detail.SourceFileHash;
        var node = Node(
            context.Subject,
            nodeId,
            "statement-run",
            status,
            matchSummary is null
                ? $"{openExceptionCount} open exception(s)."
                : $"{matchSummary.MatchedItemCount}/{matchSummary.StatementItemCount} item(s) matched; {matchSummary.BreakCount} break(s); {openExceptionCount} open exception(s).",
            "ReconciliationApiService",
            generatedAt,
            artifacts: sourceFileHash is null
                ? []
                :
                [
                    Artifact(
                        $"{nodeId}:detail",
                        "statement-run-detail-route",
                        route: route,
                        generatedAt: generatedAt,
                        hash: sourceFileHash)
                ],
            workItemIds: detail.Breaks?
                .Select(static item => item.BreakId)
                .Where(static breakId => !string.IsNullOrWhiteSpace(breakId))
                .Select(static breakId => breakId!)
                .ToArray() ?? []);

        return new EvidenceContribution([node], [], [], [nodeId], []);
    }
}

public sealed class ReportPackEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public ReportPackEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "report-pack";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.StrategyRunKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReportPackKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var repository = _services.GetService<IGovernanceReportPackRepository>();
        if (repository is null)
        {
            return new EvidenceContribution([], [], [], [], ["Report-pack repository is not registered."]);
        }

        FundReportPackSnapshotDto? snapshot = null;
        if (string.Equals(context.Subject.SubjectKind, EvidenceSubjectResolver.StrategyRunKind, StringComparison.OrdinalIgnoreCase))
        {
            snapshot = await repository.FindLatestByRunIdAsync(context.Subject.SubjectId, context.CancellationToken).ConfigureAwait(false);
        }
        else if (Guid.TryParse(context.Subject.SubjectId, out var reportId))
        {
            snapshot = await repository.GetAsync(reportId, context.CancellationToken).ConfigureAwait(false);
        }

        if (snapshot is null)
        {
            return new EvidenceContribution([], [], [], [], [$"No report-pack manifest is available for '{context.Subject.SubjectId}'."]);
        }

        var nodeId = NodeId(context.Subject, "report-pack");
        var node = Node(
            context.Subject,
            nodeId,
            "report-pack",
            MapReportPackStatus(snapshot),
            $"{snapshot.DisplayName} is {FormatStatus(snapshot.Status)} with {snapshot.Artifacts.Count} artifact reference(s), {snapshot.ValidationIssues.Count} validation issue(s), and {snapshot.Warnings.Count} warning(s).",
            "report-pack-repository",
            snapshot.GeneratedAt,
            artifacts: snapshot.Artifacts.Select(artifact => Artifact(
                $"{nodeId}:{artifact.ArtifactKind}:{artifact.RelativePath}",
                artifact.ArtifactKind,
                path: artifact.RelativePath,
                generatedAt: snapshot.GeneratedAt,
                hash: artifact.ChecksumSha256)).ToArray());

        var nodes = new List<EvidenceNodeDto> { node };
        var edges = new List<EvidenceEdgeDto>();
        AddReportPackDeliverySummary(context.Subject, snapshot.ReportId, nodeId, nodes, edges);

        return new EvidenceContribution(nodes, edges, [], [nodeId], snapshot.Warnings);
    }

    private static EvidenceStatusDto MapReportPackStatus(FundReportPackSnapshotDto snapshot)
        => snapshot.Status switch
        {
            GovernanceReportPackStatusDto.Validated or
            GovernanceReportPackStatusDto.Approved or
            GovernanceReportPackStatusDto.Exported or
            GovernanceReportPackStatusDto.Retained => EvidenceStatusDto.Ready,
            GovernanceReportPackStatusDto.Superseded or
            GovernanceReportPackStatusDto.Restated => EvidenceStatusDto.Stale,
            GovernanceReportPackStatusDto.Rejected => EvidenceStatusDto.Blocked,
            GovernanceReportPackStatusDto.Generated or
            GovernanceReportPackStatusDto.ReviewRequired => EvidenceStatusDto.ReviewRequired,
            _ => snapshot.Warnings.Count > 0 ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready
        };

    private static string FormatStatus(GovernanceReportPackStatusDto status)
        => status == GovernanceReportPackStatusDto.Unknown
            ? "legacy"
            : status.ToString();

    private void AddReportPackDeliverySummary(
        EvidenceSubjectDto subject,
        Guid reportId,
        string reportPackNodeId,
        List<EvidenceNodeDto> nodes,
        List<EvidenceEdgeDto> edges)
    {
        if (!string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReportPackKind, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var attempts = ListReportPackDeliveryAttempts(_services, reportId, 50);
        if (attempts.Count == 0)
        {
            return;
        }

        var latest = attempts[0];
        var deliverySubjectId = BuildReportPackDeliverySubjectId(latest);
        var deliveryRecordId = NodeId(subject, $"delivery-record-{latest.AttemptId:N}");
        nodes.Add(Node(
            subject,
            deliveryRecordId,
            "delivery-record",
            MapDeliveryAttemptStatus(latest.State),
            $"Latest delivery attempt {latest.AttemptNumber} for {latest.Recipient} is {latest.State} through {latest.Channel}; {attempts.Count} report-pack delivery attempt(s) are retained.",
            "ReportPackDeliveryService",
            latest.AttemptedAtUtc,
            artifacts:
            [
                Artifact(
                    $"{deliveryRecordId}:packet",
                    "report-pack-delivery-packet-route",
                    route: BuildReportPackDeliveryPacketRoute(deliverySubjectId),
                    generatedAt: latest.AttemptedAtUtc,
                    retained: true) with
                {
                    CanonicalSubjectKind = EvidenceSubjectResolver.ReportPackDeliveryKind,
                    CanonicalSubjectId = deliverySubjectId
                }
            ],
            workItemIds: latest.State == ReportPackDeliveryStateDto.Delivered
                ? []
                : [$"report-pack-delivery:{latest.State.ToString().ToLowerInvariant()}:{latest.AttemptId:N}"]));
        edges.Add(new EvidenceEdgeDto(reportPackNodeId, deliveryRecordId, "delivered-by", "Report-pack output is tied to the latest retained delivery attempt."));

        var packet = latest.Package?.DeliveryEvidencePacket;
        if (latest.Package is null && packet is null)
        {
            return;
        }

        var packetId = NodeId(subject, $"delivery-evidence-packet-{latest.AttemptId:N}");
        nodes.Add(Node(
            subject,
            packetId,
            "delivery-evidence-packet",
            packet is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            packet is null
                ? $"Delivery attempt {latest.AttemptNumber} has no retained delivery evidence packet."
                : $"{packet.PacketKind} retained {packet.RecipientList.Count} recipient(s), {packet.PackageContents.Count} content item(s), and {packet.DeliveryEvidence.Count} delivery evidence link(s).",
            "ReportPackDeliveryService",
            packet?.DeliveredAtUtc ?? latest.Package?.CreatedAtUtc ?? latest.AttemptedAtUtc,
            artifacts:
            [
                Artifact(
                    $"{packetId}:packet",
                    "report-pack-delivery-packet-route",
                    route: BuildReportPackDeliveryPacketRoute(deliverySubjectId),
                    generatedAt: packet?.DeliveredAtUtc ?? latest.Package?.CreatedAtUtc ?? latest.AttemptedAtUtc,
                    retained: true) with
                {
                    CanonicalSubjectKind = EvidenceSubjectResolver.ReportPackDeliveryKind,
                    CanonicalSubjectId = deliverySubjectId
                }
            ],
            workItemIds: packet?.AuditEventReferences ?? []));
        edges.Add(new EvidenceEdgeDto(deliveryRecordId, packetId, "proves", "Delivery evidence packet provides the retained recipient, package, channel, and audit metadata."));
    }

    private static IReadOnlyList<ReportPackDeliveryAttemptDto> ListReportPackDeliveryAttempts(
        IServiceProvider services,
        Guid reportId,
        int limit)
    {
        var service = services.GetService<ReportPackDeliveryService>();
        if (service is not null)
        {
            return service.GetHistory(reportId, limit);
        }

        var store = services.GetService<IReportPackDeliveryRecordStore>();
        if (store is null)
        {
            return [];
        }

        return store.Load()
            .Where(attempt => attempt.ReportId == reportId)
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .ToArray();
    }

    private static EvidenceStatusDto MapDeliveryAttemptStatus(ReportPackDeliveryStateDto state)
        => state switch
        {
            ReportPackDeliveryStateDto.Delivered => EvidenceStatusDto.Ready,
            ReportPackDeliveryStateDto.Failed or ReportPackDeliveryStateDto.Blocked => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static string BuildReportPackDeliverySubjectId(ReportPackDeliveryAttemptDto attempt)
        => $"{attempt.ReportId:D}:{attempt.AttemptId:D}";

    private static string BuildReportPackDeliveryPacketRoute(string deliverySubjectId)
        => $"/api/workstation/evidence/subjects/{EvidenceSubjectResolver.ReportPackDeliveryKind}/{Uri.EscapeDataString(deliverySubjectId)}/packet";
}

public sealed class ReportPackDeliveryEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public ReportPackDeliveryEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "report-pack-delivery";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReportPackDeliveryKind, StringComparison.OrdinalIgnoreCase);

    public Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var (attempts, sourceAvailable) = ListDeliveryAttempts(_services, 500);
        if (!sourceAvailable)
        {
            return Task.FromResult(new EvidenceContribution([], [], [], [], ["Report-pack delivery service or record store is not registered."]));
        }

        var attempt = ResolveDeliveryAttempt(attempts, context.Subject.SubjectId);
        if (attempt is null)
        {
            return Task.FromResult(new EvidenceContribution([], [], [], [], [$"Report-pack delivery '{context.Subject.SubjectId}' was not found."]));
        }

        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string>();
        var rootId = NodeId(context.Subject, "delivery-record");
        nodes.Add(Node(
            context.Subject,
            rootId,
            "delivery-record",
            MapDeliveryStatus(attempt.State),
            $"Delivery attempt {attempt.AttemptNumber} for {attempt.Recipient} is {attempt.State} through {attempt.Channel}; reference {attempt.DeliveryReference}.",
            "ReportPackDeliveryService",
            attempt.AttemptedAtUtc,
            artifacts: BuildAttemptEvidenceArtifacts(context.Subject, attempt, rootId),
            workItemIds: BuildDeliveryWorkItems(attempt)) with
        {
            Metadata = BuildDeliveryNodeMetadata(attempt, attempt.Package)
        });
        required.Add(rootId);

        var recipientId = NodeId(context.Subject, "recipient");
        nodes.Add(Node(
            context.Subject,
            recipientId,
            "delivery-recipient",
            string.IsNullOrWhiteSpace(attempt.Recipient) || string.IsNullOrWhiteSpace(attempt.Channel)
                ? EvidenceStatusDto.ReviewRequired
                : EvidenceStatusDto.Ready,
            $"{attempt.RecipientRole} recipient {attempt.Recipient} receives this package through {attempt.Channel}.",
            "ReportPackDeliveryService",
            attempt.AttemptedAtUtc));
        edges.Add(new EvidenceEdgeDto(rootId, recipientId, "delivered-to", "Delivery record identifies the stakeholder recipient and channel."));
        required.Add(recipientId);

        var requestId = NodeId(context.Subject, "request-history");
        nodes.Add(Node(
            context.Subject,
            requestId,
            "delivery-request-history",
            EvidenceStatusDto.Ready,
            BuildRequestSummary(attempt),
            "ReportPackDeliveryService",
            attempt.AttemptedAtUtc));
        edges.Add(new EvidenceEdgeDto(rootId, requestId, "requested-by", "Delivery request history explains why this attempt was created."));
        required.Add(requestId);

        var package = attempt.Package;
        var packageId = NodeId(context.Subject, "package");
        if (package is null)
        {
            nodes.Add(Node(
                context.Subject,
                packageId,
                "delivery-package",
                EvidenceStatusDto.Missing,
                attempt.FailureReason ?? "Delivery attempt does not have a retained package.",
                "ReportPackDeliveryService",
                attempt.AttemptedAtUtc));
            edges.Add(new EvidenceEdgeDto(rootId, packageId, "requires", "Delivery evidence requires a retained package."));
            required.Add(packageId);
            return Task.FromResult(new EvidenceContribution(nodes, edges, [], required, []));
        }

        nodes.Add(Node(
            context.Subject,
            packageId,
            "delivery-package",
            EvidenceStatusDto.Ready,
            $"{package.PackageId} retains {package.Artifacts.Count} artifact(s) for {package.DeliveryMode}; {package.IntegritySummary ?? "integrity summary unavailable"}.",
            "ReportPackDeliveryService",
            package.CreatedAtUtc,
            artifacts:
            [
                DeliveryArtifact(
                    $"{packageId}:manifest",
                    "delivery-package-manifest",
                    context.Subject,
                    path: null,
                    route: package.SecureLink,
                    generatedAt: package.CreatedAtUtc,
                    hash: package.PublicationEvidenceHash,
                    retained: !string.IsNullOrWhiteSpace(package.SecureLink))
            ]) with
        {
            Metadata = BuildDeliveryNodeMetadata(attempt, package)
        });
        edges.Add(new EvidenceEdgeDto(rootId, packageId, "retains", "Delivery record retains the package manifest and secure package route."));
        required.Add(packageId);

        var artifactsId = NodeId(context.Subject, "artifacts");
        nodes.Add(Node(
            context.Subject,
            artifactsId,
            "delivery-artifact",
            package.Artifacts.Count == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            package.Artifacts.Count == 0
                ? "No retained package artifacts are present."
                : $"{package.Artifacts.Count} retained package artifact(s) expose checksums and token-gated download routes.",
            "ReportPackDeliveryService",
            package.CreatedAtUtc,
            artifacts: package.Artifacts
                .OrderBy(static artifact => artifact.Format)
                .ThenBy(static artifact => artifact.ArtifactName, StringComparer.OrdinalIgnoreCase)
                .Select(artifact => DeliveryArtifact(
                    $"{artifactsId}:{SanitizeDeliveryPart(artifact.ArtifactName)}",
                    $"delivery-artifact-{artifact.Format.ToString().ToLowerInvariant()}",
                    context.Subject,
                    path: string.IsNullOrWhiteSpace(artifact.DownloadRoute) ? artifact.RetainedPath : null,
                    route: artifact.DownloadRoute,
                    generatedAt: package.CreatedAtUtc,
                    hash: artifact.ChecksumSha256,
                    retained: !string.IsNullOrWhiteSpace(artifact.DownloadRoute)))
                .ToArray()));
        edges.Add(new EvidenceEdgeDto(packageId, artifactsId, "contains", "Package manifest contains the retained PDF/XLSX/CSV artifact metadata."));
        required.Add(artifactsId);

        var evidencePacketId = NodeId(context.Subject, "evidence-packet");
        var packet = package.DeliveryEvidencePacket;
        nodes.Add(Node(
            context.Subject,
            evidencePacketId,
            "delivery-evidence-packet",
            packet is null ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            packet is null
                ? "Delivery evidence packet metadata is missing."
                : $"{packet.PacketKind} binds {packet.PackageContents.Count} package content item(s), {packet.SupportEvidenceIds.Count} support evidence id(s), and {packet.RequestHistory.Count} request-history entry(s).",
            "ReportPackDeliveryService",
            packet?.DeliveredAtUtc ?? package.CreatedAtUtc,
            artifacts: packet is null ? [] : BuildDeliveryEvidencePacketArtifacts(context.Subject, evidencePacketId, packet)) with
        {
            Metadata = BuildDeliveryNodeMetadata(attempt, package)
        });
        edges.Add(new EvidenceEdgeDto(packageId, evidencePacketId, "proves", "Delivery evidence packet binds recipient, entitlement, approval, content, and request history."));
        required.Add(evidencePacketId);

        if (packet?.ApprovalChain is { Count: > 0 })
        {
            var approvalChainId = NodeId(context.Subject, "approval-chain");
            var latestApprovalStep = packet.ApprovalChain[packet.ApprovalChain.Count - 1];
            nodes.Add(Node(
                context.Subject,
                approvalChainId,
                "approval-chain",
                EvidenceStatusDto.Ready,
                $"{packet.ApprovalChain.Count} approval step(s) gate the delivered package; latest action {latestApprovalStep.Action} by {latestApprovalStep.Actor}.",
                "ReportPackDeliveryService",
                packet.DeliveredAtUtc,
                workItemIds: packet.ApprovalChain.Select(static step =>
                    $"delivery-approval:{step.At:O}:{step.Actor}:{step.Action}").ToArray()));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, approvalChainId, "approved-by", "Delivery packet retains the report-pack approval chain."));
        }

        var auditId = NodeId(context.Subject, "audit-manifest");
        var auditArtifacts = BuildAuditArtifacts(context.Subject, auditId, package, packet);
        var auditWorkItems = BuildAuditWorkItems(packet);
        nodes.Add(Node(
            context.Subject,
            auditId,
            "audit-history",
            auditArtifacts.Count == 0 && auditWorkItems.Count == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            BuildAuditSummary(package, packet, auditArtifacts.Count, auditWorkItems.Count),
            "ReportPackDeliveryService",
            packet?.DeliveredAtUtc ?? package.CreatedAtUtc,
            artifacts: auditArtifacts,
            workItemIds: auditWorkItems));
        edges.Add(new EvidenceEdgeDto(evidencePacketId, auditId, "retained-by", "Audit manifest retains delivery package, artifact, request, and package evidence references."));
        required.Add(auditId);

        if (!string.IsNullOrWhiteSpace(package.PublicationManifestId) ||
            package.PublicationEvidenceLinks is { Count: > 0 } ||
            package.LineProvenance is { Count: > 0 })
        {
            var publicationId = NodeId(context.Subject, "publication");
            nodes.Add(Node(
                context.Subject,
                publicationId,
                "publication-manifest",
                EvidenceStatusDto.Ready,
                $"Publication {package.PublicationManifestId ?? package.ReportId.ToString("D")} contributes {package.PublicationEvidenceLinks?.Count ?? 0} evidence link(s) and {package.LineProvenance?.Count ?? 0} line-provenance pointer(s).",
                "ReportPackDeliveryService",
                package.PublicationSignedOffAtUtc ?? package.CreatedAtUtc,
                artifacts: BuildPublicationArtifacts(context.Subject, publicationId, package)));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, publicationId, "supported-by", "Delivery packet is supported by the governed report-pack publication lineage."));
        }

        if (package.LineProvenance is { Count: > 0 } ||
            package.ReportingRunSectionCount.HasValue ||
            package.ReportingRunLineageLinkedSections.HasValue)
        {
            var lineProvenanceId = NodeId(context.Subject, "line-provenance");
            var lineageReady = package.LineProvenance is { Count: > 0 } ||
                               package.ReportingRunLineageLinkedSections.GetValueOrDefault() > 0;
            nodes.Add(Node(
                context.Subject,
                lineProvenanceId,
                "report-line-provenance",
                lineageReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Missing,
                BuildLineProvenanceSummary(package),
                "ReportPackDeliveryService",
                package.CreatedAtUtc,
                artifacts: BuildLineProvenanceArtifacts(context.Subject, lineProvenanceId, package)));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, lineProvenanceId, "traces-to", "Delivery packet retains report-line provenance for the delivered contents."));
            required.Add(lineProvenanceId);
        }

        if (package.BrandingTheme is not null)
        {
            var brandingId = NodeId(context.Subject, "branding-theme");
            nodes.Add(Node(
                context.Subject,
                brandingId,
                "branding-theme",
                EvidenceStatusDto.Ready,
                BuildBrandingSummary(package.BrandingTheme),
                "ReportPackDeliveryService",
                package.CreatedAtUtc));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, brandingId, "branded-as", "Delivery packet captures the branding theme applied to stakeholder materials."));
        }

        if (!string.IsNullOrWhiteSpace(package.RestatementReasonCode) ||
            !string.IsNullOrWhiteSpace(packet?.RestatementLineage) ||
            package.RestatementChangedLines is { Count: > 0 } ||
            package.RestatementEvidenceLinks is { Count: > 0 })
        {
            var restatementId = NodeId(context.Subject, "restatement-lineage");
            nodes.Add(Node(
                context.Subject,
                restatementId,
                "restatement-lineage",
                EvidenceStatusDto.Ready,
                BuildRestatementSummary(package, packet),
                "ReportPackDeliveryService",
                package.CreatedAtUtc,
                artifacts: BuildRestatementArtifacts(context.Subject, restatementId, package)));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, restatementId, "amends", "Delivery packet retains restatement lineage and changed-line evidence when applicable."));
        }

        if (!string.IsNullOrWhiteSpace(package.ReportingRunId) ||
            package.SourceArtifacts is { Count: > 0 })
        {
            var sourceId = NodeId(context.Subject, "reporting-run-source");
            nodes.Add(Node(
                context.Subject,
                sourceId,
                "reporting-run-source",
                EvidenceStatusDto.Ready,
                BuildReportingRunSourceSummary(package),
                "ReportPackDeliveryService",
                package.CreatedAtUtc,
                artifacts: BuildReportingRunSourceArtifacts(context.Subject, sourceId, package)));
            edges.Add(new EvidenceEdgeDto(evidencePacketId, sourceId, "generated-from", "Delivery packet is supported by the generated reporting-run source artifacts."));
        }

        return Task.FromResult(new EvidenceContribution(nodes, edges, [], required, []));
    }

    private static (IReadOnlyList<ReportPackDeliveryAttemptDto> Attempts, bool SourceAvailable) ListDeliveryAttempts(
        IServiceProvider services,
        int limit)
    {
        var service = services.GetService<ReportPackDeliveryService>();
        if (service is not null)
        {
            return (service.ListAttempts(limit), true);
        }

        var store = services.GetService<IReportPackDeliveryRecordStore>();
        if (store is null)
        {
            return ([], false);
        }

        return (store.Load()
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenBy(static attempt => attempt.ReportId)
            .ThenBy(static attempt => attempt.DistributionId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .ToArray(), true);
    }

    private static ReportPackDeliveryAttemptDto? ResolveDeliveryAttempt(
        IReadOnlyList<ReportPackDeliveryAttemptDto> attempts,
        string subjectId)
    {
        var normalized = subjectId.Trim();
        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            Guid.TryParse(parts[0], out var reportId) &&
            Guid.TryParse(parts[1], out var attemptId))
        {
            return attempts.FirstOrDefault(attempt => attempt.ReportId == reportId && attempt.AttemptId == attemptId);
        }

        if (Guid.TryParse(normalized, out var attemptOnlyId))
        {
            return attempts.FirstOrDefault(attempt => attempt.AttemptId == attemptOnlyId);
        }

        return attempts.FirstOrDefault(attempt =>
            attempt.Package is not null &&
            string.Equals(attempt.Package.PackageId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static EvidenceStatusDto MapDeliveryStatus(ReportPackDeliveryStateDto state)
        => state switch
        {
            ReportPackDeliveryStateDto.Delivered => EvidenceStatusDto.Ready,
            ReportPackDeliveryStateDto.Failed or ReportPackDeliveryStateDto.Blocked => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static IReadOnlyList<string> BuildDeliveryWorkItems(ReportPackDeliveryAttemptDto attempt)
        => attempt.State == ReportPackDeliveryStateDto.Delivered
            ? []
            : [$"report-pack-delivery:{attempt.State.ToString().ToLowerInvariant()}:{attempt.AttemptId:N}"];

    private static string BuildRequestSummary(ReportPackDeliveryAttemptDto attempt)
    {
        var evidenceCount = attempt.EvidenceLinks?.Count ?? 0;
        var note = string.IsNullOrWhiteSpace(attempt.Note) ? "No operator note." : attempt.Note;
        return $"Attempt {attempt.AttemptNumber} was requested by {attempt.Actor}; {evidenceCount} delivery evidence link(s) retained. {note}";
    }

    private static IReadOnlyDictionary<string, string> BuildDeliveryNodeMetadata(
        ReportPackDeliveryAttemptDto attempt,
        ReportPackDeliveryPackageDto? package)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reportPackDeliveryAttemptId"] = attempt.AttemptId.ToString("D")
        };

        if (!string.IsNullOrWhiteSpace(package?.PackageId))
        {
            metadata["reportPackDeliveryPackageId"] = package.PackageId;
        }

        return metadata;
    }

    private static string BuildAuditSummary(
        ReportPackDeliveryPackageDto package,
        ReportPackDeliveryEvidencePacketDto? packet,
        int artifactCount,
        int auditReferenceCount)
        => packet is null
            ? $"Package {package.PackageId} has retained manifest {package.RetainedManifestPath}, but delivery packet audit metadata is missing."
            : $"Package {package.PackageId} audit trail references {auditReferenceCount} event(s), {packet.ApprovalChain.Count} approval step(s), and {artifactCount} retained manifest or evidence pointer(s).";

    private static string BuildLineProvenanceSummary(ReportPackDeliveryPackageDto package)
    {
        if (package.LineProvenance is { Count: > 0 })
        {
            var sourceKinds = package.LineProvenance
                .Select(static line => line.SourceKind)
                .Where(static sourceKind => !string.IsNullOrWhiteSpace(sourceKind))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static sourceKind => sourceKind, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return $"{package.LineProvenance.Count} delivered report line(s) retain provenance across {sourceKinds.Length} source kind(s): {string.Join(", ", sourceKinds)}.";
        }

        if (package.ReportingRunSectionCount.HasValue || package.ReportingRunLineageLinkedSections.HasValue)
        {
            var sectionCount = package.ReportingRunSectionCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            var lineageCount = package.ReportingRunLineageLinkedSections?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            return $"Generated reporting run lineage covers {lineageCount}/{sectionCount} section(s).";
        }

        return "No report-line provenance is attached to this delivery package.";
    }

    private static string BuildBrandingSummary(ReportBrandingThemeDto theme)
        => $"Branding theme {theme.ThemeId} ({theme.Name}) applies firm '{theme.FirmName}' with primary color {theme.PrimaryColor}.";

    private static string BuildRestatementSummary(
        ReportPackDeliveryPackageDto package,
        ReportPackDeliveryEvidencePacketDto? packet)
    {
        var changedLineCount = package.RestatementChangedLines?.Count ?? 0;
        var evidenceCount = package.RestatementEvidenceLinks?.Count ?? 0;
        var reason = package.RestatementReasonCode ?? packet?.AmendmentReason ?? "unspecified";
        var priorVersion = package.RestatementPriorVersionReportId?.ToString("D") ?? "unspecified";
        return $"Restatement reason {reason} references prior version {priorVersion}, {changedLineCount} changed line(s), and {evidenceCount} evidence link(s).";
    }

    private static string BuildReportingRunSourceSummary(ReportPackDeliveryPackageDto package)
    {
        var status = string.IsNullOrWhiteSpace(package.ReportingRunStatus) ? "unknown" : package.ReportingRunStatus;
        var trigger = string.IsNullOrWhiteSpace(package.ReportingRunTrigger) ? "unknown" : package.ReportingRunTrigger;
        var sectionCount = package.ReportingRunSectionCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
        var lineageCount = package.ReportingRunLineageLinkedSections?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
        return $"Reporting run {package.ReportingRunId ?? "unknown"} for template {package.ReportingTemplateId ?? "unknown"} is {status} via {trigger}; lineage covers {lineageCount}/{sectionCount} section(s).";
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildAttemptEvidenceArtifacts(
        EvidenceSubjectDto subject,
        ReportPackDeliveryAttemptDto attempt,
        string nodeId)
        => attempt.EvidenceLinks?
            .Select((link, index) => DeliveryLinkArtifact(subject, $"{nodeId}:delivery-link-{index + 1}", link, attempt.AttemptedAtUtc))
            .ToArray() ?? [];

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildDeliveryEvidencePacketArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryEvidencePacketDto packet)
        => packet.DeliveryEvidence
            .Select((link, index) => DeliveryLinkArtifact(subject, $"{nodeId}:delivery-evidence-{index + 1}", link, packet.DeliveredAtUtc))
            .ToArray();

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildAuditArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryPackageDto package,
        ReportPackDeliveryEvidencePacketDto? packet)
    {
        var artifacts = new List<EvidenceArtifactRefDto>();
        var packageRoute = string.IsNullOrWhiteSpace(package.SecureLink) ? package.PortalRoute : package.SecureLink;
        artifacts.Add(DeliveryArtifact(
            $"{nodeId}:package-manifest",
            "audit-manifest",
            subject,
            path: null,
            route: string.IsNullOrWhiteSpace(packageRoute) ? null : packageRoute,
            generatedAt: packet?.DeliveredAtUtc ?? package.CreatedAtUtc,
            hash: package.PublicationEvidenceHash,
            retained: !string.IsNullOrWhiteSpace(packageRoute)));

        if (packet?.DeliveryEvidence is { Count: > 0 })
        {
            artifacts.AddRange(packet.DeliveryEvidence.Select((link, index) =>
                DeliveryLinkArtifact(subject, $"{nodeId}:audit-evidence-{index + 1}", link, packet.DeliveredAtUtc)));
        }

        return artifacts;
    }

    private static IReadOnlyList<string> BuildAuditWorkItems(ReportPackDeliveryEvidencePacketDto? packet)
    {
        var items = new List<string>();
        if (packet?.AuditEventReferences is { Count: > 0 })
        {
            items.AddRange(packet.AuditEventReferences.Where(static item => !string.IsNullOrWhiteSpace(item)));
        }

        if (packet?.ApprovalChain is { Count: > 0 })
        {
            items.AddRange(packet.ApprovalChain.Select(static step =>
                $"delivery-approval:{step.At:O}:{step.Actor}:{step.Action}"));
        }

        return items;
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildPublicationArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryPackageDto package)
    {
        var artifacts = new List<EvidenceArtifactRefDto>();
        if (!string.IsNullOrWhiteSpace(package.PublicationRetainedManifestPath))
        {
            artifacts.Add(DeliveryArtifact(
                $"{nodeId}:publication-manifest",
                "publication-manifest",
                subject,
                path: package.PublicationRetainedManifestPath,
                route: null,
                generatedAt: package.PublicationSignedOffAtUtc ?? package.CreatedAtUtc,
                hash: package.PublicationEvidenceHash,
                retained: false));
        }

        if (package.PublicationEvidenceLinks is not null)
        {
            artifacts.AddRange(package.PublicationEvidenceLinks.Select((link, index) =>
                DeliveryLinkArtifact(subject, $"{nodeId}:publication-evidence-{index + 1}", link, package.PublicationSignedOffAtUtc ?? package.CreatedAtUtc)));
        }

        return artifacts;
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildLineProvenanceArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryPackageDto package)
    {
        if (package.LineProvenance is not { Count: > 0 } ||
            package.PublicationEvidenceLinks is not { Count: > 0 })
        {
            return [];
        }

        var lineEvidenceIds = package.LineProvenance
            .Select(static line => line.EvidenceId)
            .Where(static evidenceId => !string.IsNullOrWhiteSpace(evidenceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return package.PublicationEvidenceLinks
            .Where(link => lineEvidenceIds.Contains(link.EvidenceId))
            .Select((link, index) => DeliveryLinkArtifact(
                subject,
                $"{nodeId}:line-evidence-{index + 1}",
                link,
                package.PublicationSignedOffAtUtc ?? package.CreatedAtUtc))
            .ToArray();
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildRestatementArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryPackageDto package)
    {
        var links = new List<ReportPackEvidenceLinkDto>();
        if (package.RestatementEvidenceLinks is { Count: > 0 })
        {
            links.AddRange(package.RestatementEvidenceLinks);
        }

        if (package.RestatementChangedLines is { Count: > 0 })
        {
            links.AddRange(package.RestatementChangedLines.SelectMany(static line => line.EvidenceLinks ?? []));
        }

        return links
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select((link, index) => DeliveryLinkArtifact(subject, $"{nodeId}:restatement-evidence-{index + 1}", link, package.CreatedAtUtc))
            .ToArray();
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildReportingRunSourceArtifacts(
        EvidenceSubjectDto subject,
        string nodeId,
        ReportPackDeliveryPackageDto package)
        => package.SourceArtifacts?
            .Select((artifact, index) => DeliveryArtifact(
                $"{nodeId}:source-artifact-{index + 1}",
                "reporting-run-source-artifact",
                subject,
                path: artifact.StartsWith("/", StringComparison.OrdinalIgnoreCase) ? null : artifact,
                route: artifact.StartsWith("/", StringComparison.OrdinalIgnoreCase) ? artifact : null,
                generatedAt: package.CreatedAtUtc,
                hash: null,
                retained: artifact.StartsWith("/", StringComparison.OrdinalIgnoreCase)))
            .ToArray() ?? [];

    private static EvidenceArtifactRefDto DeliveryLinkArtifact(
        EvidenceSubjectDto subject,
        string artifactId,
        ReportPackEvidenceLinkDto link,
        DateTimeOffset generatedAt)
    {
        var target = string.IsNullOrWhiteSpace(link.Route) ? link.Label : link.Route;
        var isRoute = !string.IsNullOrWhiteSpace(target) &&
            (target.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
             target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             target.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        return DeliveryArtifact(
            artifactId,
            string.IsNullOrWhiteSpace(link.Source) ? "delivery-evidence-link" : link.Source!,
            subject,
            path: isRoute ? null : target,
            route: isRoute ? target : null,
            generatedAt: link.CapturedAtUtc ?? generatedAt,
            hash: null,
            retained: isRoute);
    }

    private static EvidenceArtifactRefDto DeliveryArtifact(
        string artifactId,
        string kind,
        EvidenceSubjectDto subject,
        string? path,
        string? route,
        DateTimeOffset generatedAt,
        string? hash,
        bool retained)
        => new(
            ArtifactId: artifactId,
            Kind: kind,
            Path: path,
            Route: route,
            GeneratedAt: generatedAt,
            Hash: string.IsNullOrWhiteSpace(hash) ? null : hash,
            Retained: retained,
            CanonicalSubjectKind: subject.SubjectKind,
            CanonicalSubjectId: subject.SubjectId);

    private static string SanitizeDeliveryPart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "artifact"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}

public sealed class ProviderTrustEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public ProviderTrustEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "provider-trust";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ProviderTrustKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.PaperReadinessKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<Dk1TrustGateReadinessService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["DK1 trust-gate readiness service is not registered."]);
        }

        var readiness = await service.GetCurrentAsync(context.CancellationToken).ConfigureAwait(false);
        var nodeId = NodeId(context.Subject, "provider-trust");
        var status = readiness.ReadyForOperatorReview && readiness.Blockers.Count == 0
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
        var node = Node(
            context.Subject,
            nodeId,
            "provider-trust",
            status,
            readiness.Detail,
            "Dk1TrustGateReadinessService",
            readiness.GeneratedAt,
            artifacts: string.IsNullOrWhiteSpace(readiness.PacketPath)
                ? []
                :
                [
                    Artifact(
                        $"{nodeId}:dk1-packet",
                        "dk1-pilot-parity-packet",
                        path: readiness.PacketPath,
                        generatedAt: readiness.GeneratedAt ?? DateTimeOffset.UtcNow)
                ],
            workItemIds: readiness.Blockers.Select(static blocker => $"provider-trust:{blocker}").ToArray());

        return new EvidenceContribution([node], [], [], [nodeId], readiness.Blockers);
    }
}

public sealed class ExportEvidenceContributor : IEvidenceContributor
{
    public string ContributorId => "analysis-export";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReportPackKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.AnalysisExportKind, StringComparison.OrdinalIgnoreCase);

    public Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var profiles = ExportProfile.GetBuiltInProfiles();
        var nodeId = NodeId(context.Subject, "analysis-export");
        var node = Node(
            context.Subject,
            nodeId,
            "analysis-export",
            profiles.Count == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            $"{profiles.Count} analysis export profile(s) are available for manifest-backed reporting evidence.",
            "ExportProfile",
            DateTimeOffset.UtcNow,
            artifacts: profiles.Select(profile => Artifact(
                $"{nodeId}:{profile.Id}",
                "export-profile",
                route: $"/api/export/analysis/{Uri.EscapeDataString(profile.Id)}",
                generatedAt: DateTimeOffset.UtcNow)).ToArray());

        return Task.FromResult(new EvidenceContribution([node], [], [], [nodeId], []));
    }
}

public sealed class SecurityMasterConflictEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public SecurityMasterConflictEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "security-master-conflict";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.SecurityMasterConflictKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<ISecurityMasterConflictService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Security Master conflict service is not registered."]);
        }

        var conflicts = await ResolveConflictsAsync(service, context.Subject.SubjectId, context.CancellationToken)
            .ConfigureAwait(false);
        if (conflicts.Count == 0)
        {
            var readyId = NodeId(context.Subject, "conflict-queue");
            var ready = Node(
                context.Subject,
                readyId,
                "security-master-conflict-queue",
                EvidenceStatusDto.Ready,
                "Security Master conflict queue has no open conflicts for this subject.",
                "SecurityMasterConflictService",
                DateTimeOffset.UtcNow,
                artifacts:
                [
                    new EvidenceArtifactRefDto(
                        $"{readyId}:conflicts-route",
                        "security-master-conflicts-route",
                        Path: null,
                        Route: UiApiRoutes.SecurityMasterConflicts,
                        GeneratedAt: DateTimeOffset.UtcNow,
                        Hash: null,
                        Retained: false)
                ]);
            return new EvidenceContribution([ready], [], [], [readyId], []);
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string>();
        var queueId = NodeId(context.Subject, "conflict-queue");
        var openCount = conflicts.Count(static conflict =>
            string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase));
        nodes.Add(Node(
            context.Subject,
            queueId,
            "security-master-conflict-queue",
            openCount == 0 ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            $"{conflicts.Count} Security Master conflict(s) are in scope; {openCount} remain open.",
            "SecurityMasterConflictService",
            conflicts.Max(static conflict => conflict.DetectedAt),
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    $"{queueId}:conflicts-route",
                    "security-master-conflicts-route",
                    Path: null,
                    Route: UiApiRoutes.SecurityMasterConflicts,
                    GeneratedAt: generatedAt,
                    Hash: null,
                    Retained: false)
            ],
            workItemIds: conflicts
                .Where(static conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
                .Select(static conflict => BuildConflictCaseId(conflict.ConflictId))
                .ToArray()));
        required.Add(queueId);

        foreach (var conflict in conflicts.OrderBy(static conflict => conflict.DetectedAt))
        {
            var conflictId = NodeId(context.Subject, $"conflict-{conflict.ConflictId:N}");
            nodes.Add(Node(
                context.Subject,
                conflictId,
                "security-master-conflict",
                MapConflictStatus(conflict.Status),
                $"Conflict {conflict.ConflictKind} on {conflict.FieldPath}: {conflict.ProviderA}='{conflict.ValueA}' vs {conflict.ProviderB}='{conflict.ValueB}' ({conflict.Status}).",
                "SecurityMasterConflictService",
                conflict.DetectedAt,
                artifacts:
                [
                    new EvidenceArtifactRefDto(
                        $"{conflictId}:resolve-route",
                        "security-master-conflict-route",
                        Path: null,
                        Route: UiApiRoutes.SecurityMasterConflictResolve.Replace(
                            "{conflictId:guid}",
                            conflict.ConflictId.ToString("D"),
                            StringComparison.Ordinal),
                        GeneratedAt: generatedAt,
                        Hash: null,
                        Retained: false)
                ],
                workItemIds: string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase)
                    ? [BuildConflictCaseId(conflict.ConflictId)]
                    : []));
            edges.Add(new EvidenceEdgeDto(queueId, conflictId, "contains", "Open conflict queue contains the conflict case evidence."));
            required.Add(conflictId);
        }

        return new EvidenceContribution(nodes, edges, [], required, []);
    }

    private static async Task<IReadOnlyList<SecurityMasterConflict>> ResolveConflictsAsync(
        ISecurityMasterConflictService service,
        string subjectId,
        CancellationToken ct)
    {
        if (Guid.TryParse(subjectId, out var conflictId))
        {
            var conflict = await service.GetConflictAsync(conflictId, ct).ConfigureAwait(false);
            return conflict is null ? [] : [conflict];
        }

        return await service.GetOpenConflictsAsync(ct).ConfigureAwait(false);
    }

    private static EvidenceStatusDto MapConflictStatus(string status)
        => status switch
        {
            _ when string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase) => EvidenceStatusDto.ReviewRequired,
            _ when string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "Dismissed", StringComparison.OrdinalIgnoreCase) => EvidenceStatusDto.Ready,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static string BuildConflictCaseId(Guid conflictId)
        => $"security-master:conflict:{conflictId:N}";
}

public sealed class OperationsApprovalEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public OperationsApprovalEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "approval";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ApprovalKind, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.AccountingRecordKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<IOperationsContinuityWorkflowService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Operations Continuity workflow service is not registered."]);
        }

        var workflow = await ResolveWorkflowAsync(service, context.Subject.SubjectId, context.CancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            return new EvidenceContribution([], [], [], [], [$"Operations approval workflow '{context.Subject.SubjectId}' was not found."]);
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var requiredEvidenceIds = new List<string>();
        var approvalId = NodeId(context.Subject, "approval");
        var workItemIds = BuildApprovalWorkItemIds(workflow);
        nodes.Add(Node(
            context.Subject,
            approvalId,
            "approval",
            MapApprovalStatus(workflow.ApprovalState),
            BuildApprovalSummary(workflow),
            "OperationsContinuityWorkflowService",
            ResolveApprovalAsOf(workflow),
            artifacts:
            [
                RouteArtifact(
                    $"{approvalId}:workflow-route",
                    "operations-approval-route",
                    UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflow.WorkflowId.ToString("D")),
                    generatedAt),
                RouteArtifact(
                    $"{approvalId}:decision-route",
                    workflow.ApprovalState == OperationsApprovalStateDto.Rejected
                        ? "operations-approval-reject-route"
                        : "operations-approval-approve-route",
                    workflow.ApprovalState == OperationsApprovalStateDto.Rejected
                        ? UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalReject, "workflowId", workflow.WorkflowId.ToString("D"))
                        : UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityApprovalApprove, "workflowId", workflow.WorkflowId.ToString("D")),
                    generatedAt)
            ],
            workItemIds: workItemIds));
        requiredEvidenceIds.Add(approvalId);

        if (workflow.Timeline.Count > 0)
        {
            var auditId = NodeId(context.Subject, "approval-audit");
            nodes.Add(Node(
                context.Subject,
                auditId,
                "approval-audit",
                HasApprovalAudit(workflow) ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
                $"{workflow.Timeline.Count} operations continuity audit event(s) are retained for the approval workflow.",
                "OperationsContinuityWorkflowService",
                workflow.Timeline.Max(static entry => entry.OccurredAtUtc),
                artifacts:
                [
                    RouteArtifact(
                        $"{auditId}:timeline-route",
                        "operations-approval-timeline-route",
                        UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflow.WorkflowId.ToString("D")),
                        generatedAt)
                ]));
            edges.Add(new EvidenceEdgeDto(approvalId, auditId, "supported-by", "Approval state is supported by retained operations continuity audit events."));
        }

        if (workflow.CloseChecklist.Count > 0)
        {
            var checklistId = NodeId(context.Subject, "close-checklist");
            var openControls = workflow.CloseChecklist.Count(static task =>
                task.RequiredApprovalCount > 0 &&
                !string.Equals(task.Status, "Acknowledged", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(task.Status, "Complete", StringComparison.OrdinalIgnoreCase));
            nodes.Add(Node(
                context.Subject,
                checklistId,
                "close-checklist",
                openControls == 0 ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
                $"{workflow.CloseChecklist.Count} close checklist task(s) are in scope; {openControls} control approval task(s) remain open.",
                "OperationsContinuityWorkflowService",
                workflow.UpdatedAtUtc,
                artifacts:
                [
                    RouteArtifact(
                        $"{checklistId}:checklist-route",
                        "operations-close-checklist-route",
                        UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityChecklist, "workflowId", workflow.WorkflowId.ToString("D")),
                        generatedAt)
                ],
                workItemIds: openControls == 0
                    ? []
                    : [$"operations-approval:checklist:{workflow.WorkflowId:N}"]));
            edges.Add(new EvidenceEdgeDto(approvalId, checklistId, "requires", "Approval depends on close checklist control approvals."));
        }

        if (!string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
        {
            var reportPackId = NodeId(context.Subject, "report-pack");
            nodes.Add(Node(
                context.Subject,
                reportPackId,
                "report-pack",
                workflow.ReportPackReadiness.IsReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
                workflow.ReportPackReadiness.IsReady
                    ? $"Report pack {workflow.ReportPackReadiness.ReportPackId} is ready for approval."
                    : workflow.ReportPackReadiness.BlockingReason ?? "Report pack is not ready for approval.",
                "OperationsContinuityWorkflowService",
                workflow.UpdatedAtUtc,
                artifacts: workflow.ReportPackReadiness.EvidenceLinks
                    .Select(link => RouteArtifact(
                        $"{reportPackId}:{SanitizeArtifactPart(link.EvidenceId)}",
                        string.IsNullOrWhiteSpace(link.Source) ? "report-pack-evidence-route" : link.Source!,
                        link.Route,
                        link.CapturedAtUtc ?? generatedAt))
                    .ToArray()));
            edges.Add(new EvidenceEdgeDto(approvalId, reportPackId, "requires", "Approval requires the governed report pack readiness evidence."));
        }

        AddAccountingRecordEvidence(context.Subject, workflow, generatedAt, approvalId, nodes, edges, requiredEvidenceIds);

        return new EvidenceContribution(nodes, edges, [], requiredEvidenceIds, []);
    }

    private static void AddAccountingRecordEvidence(
        EvidenceSubjectDto subject,
        OperationsContinuityWorkflowDto workflow,
        DateTimeOffset generatedAt,
        string approvalId,
        List<EvidenceNodeDto> nodes,
        List<EvidenceEdgeDto> edges,
        List<string> requiredEvidenceIds)
    {
        var summary = workflow.AccountingRecordSummary;
        if (summary is null)
        {
            return;
        }

        var recordId = NodeId(subject, "accounting-record");
        nodes.Add(Node(
            subject,
            recordId,
            "accounting-record",
            summary.IsAuditReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            summary.Summary,
            "OperationsContinuityWorkflowService",
            workflow.UpdatedAtUtc,
            artifacts:
            [
                RouteArtifact(
                    $"{recordId}:workflow-route",
                    "operations-accounting-record-route",
                    UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflow.WorkflowId.ToString("D")),
                    generatedAt)
            ],
            workItemIds: summary.IsAuditReady
                ? Array.Empty<string>()
                : [$"operations-accounting-record:review:{workflow.WorkflowId:N}"]));
        edges.Add(new EvidenceEdgeDto(approvalId, recordId, "requires", "Approval requires the audit-ready accounting record evidence package."));
        requiredEvidenceIds.Add(recordId);

        foreach (var category in summary.EvidenceCategories.OrderBy(static category => category.Key, StringComparer.OrdinalIgnoreCase))
        {
            var categoryId = NodeId(subject, $"accounting-record-{category.Key}");
            var workItemIds = category.IsComplete
                ? Array.Empty<string>()
                : [$"operations-accounting-record:{category.Key}:{workflow.WorkflowId:N}"];
            nodes.Add(Node(
                subject,
                categoryId,
                "accounting-record-category",
                category.IsComplete ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
                BuildAccountingRecordCategorySummary(category),
                "OperationsContinuityWorkflowService",
                workflow.UpdatedAtUtc,
                artifacts: category.EvidenceLinks
                    .Select(link => RouteArtifact(
                        $"{categoryId}:{SanitizeArtifactPart(link.EvidenceId)}",
                        string.IsNullOrWhiteSpace(link.Source) ? "operations-accounting-record-evidence-route" : link.Source!,
                        string.IsNullOrWhiteSpace(link.Route) ? category.RouteHint : link.Route,
                        link.CapturedAtUtc ?? generatedAt))
                    .ToArray(),
                workItemIds: workItemIds));
            edges.Add(new EvidenceEdgeDto(recordId, categoryId, "requires", $"Accounting record requires {category.Label} evidence."));

            if (!category.IsComplete)
            {
                requiredEvidenceIds.Add(categoryId);
            }
        }
    }

    private static string BuildAccountingRecordCategorySummary(OperationsAccountingRecordEvidenceCategoryDto category)
    {
        var requiredEvidenceItems = category.RequiredEvidence?
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray() ?? [];
        var requiredEvidence = requiredEvidenceItems.Length > 0
            ? string.Join(", ", requiredEvidenceItems)
            : "required evidence not specified";
        var closePackageLineage = requiredEvidenceItems.Any(static item =>
            item.Contains("export manifest", StringComparison.OrdinalIgnoreCase))
            ? " Close-package lineage includes retained document attachments and restatement lineage."
            : string.Empty;
        return $"{category.Label}: {category.Status} Required evidence: {requiredEvidence}.{closePackageLineage}";
    }

    private static async Task<OperationsContinuityWorkflowDto?> ResolveWorkflowAsync(
        IOperationsContinuityWorkflowService service,
        string subjectId,
        CancellationToken ct)
    {
        if (Guid.TryParse(subjectId, out var workflowId))
        {
            return await service.GetAsync(workflowId, ct).ConfigureAwait(false);
        }

        var workflows = await service.ListAsync(ct: ct).ConfigureAwait(false);
        var latest = workflows
            .OrderByDescending(static workflow => workflow.UpdatedAtUtc)
            .FirstOrDefault();
        return latest is null ? null : await service.GetAsync(latest.WorkflowId, ct).ConfigureAwait(false);
    }

    private static EvidenceStatusDto MapApprovalStatus(OperationsApprovalStateDto status)
        => status switch
        {
            OperationsApprovalStateDto.Approved => EvidenceStatusDto.Ready,
            OperationsApprovalStateDto.Rejected => EvidenceStatusDto.Blocked,
            OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned => EvidenceStatusDto.ReviewRequired,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static string BuildApprovalSummary(OperationsContinuityWorkflowDto workflow)
    {
        var approved = workflow.Approvals.Count(static approval => approval.Status == OperationsApprovalStateDto.Approved);
        var submitted = workflow.Approvals.Count(static approval =>
            approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned);
        return $"Operations workflow {workflow.WorkflowId:D} for period {workflow.PeriodId} is {workflow.ApprovalState}; {approved} approval(s) approved and {submitted} pending review.";
    }

    private static DateTimeOffset ResolveApprovalAsOf(OperationsContinuityWorkflowDto workflow)
        => workflow.Approvals
            .Select(static approval => approval.DecidedAtUtc ?? approval.SubmittedAtUtc)
            .Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .DefaultIfEmpty(workflow.UpdatedAtUtc)
            .Max();

    private static IReadOnlyList<string> BuildApprovalWorkItemIds(OperationsContinuityWorkflowDto workflow)
        => workflow.ApprovalState switch
        {
            OperationsApprovalStateDto.Approved => [],
            OperationsApprovalStateDto.Rejected => [$"operations-approval:rejected:{workflow.WorkflowId:N}"],
            OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned => [$"operations-approval:review:{workflow.WorkflowId:N}"],
            _ => [$"operations-approval:pending:{workflow.WorkflowId:N}"]
        };

    private static bool HasApprovalAudit(OperationsContinuityWorkflowDto workflow)
        => workflow.Timeline.Any(static entry => entry.EventType.StartsWith("approval-", StringComparison.OrdinalIgnoreCase));

    private static EvidenceArtifactRefDto RouteArtifact(
        string artifactId,
        string kind,
        string? route,
        DateTimeOffset generatedAt)
        => new(
            ArtifactId: artifactId,
            Kind: kind,
            Path: null,
            Route: route,
            GeneratedAt: generatedAt,
            Hash: null,
            Retained: false);

    private static string SanitizeArtifactPart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "evidence"
            : string.Join("-", value.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}

public sealed class PrivateCapitalFundEventEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public PrivateCapitalFundEventEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "private-capital-fund-event";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.PrivateCapitalFundEventKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return Empty("Manual journal entry workbench service is not registered.");
        }

        var fundProfileId = TryResolveFundProfileIdFromFundEventId(context.Subject.SubjectId);
        var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, context.Subject.LedgerBookId, context.CancellationToken)
            .ConfigureAwait(false);
        var record = activity.FundEventRecords.FirstOrDefault(item =>
            string.Equals(item.FundEventId, context.Subject.SubjectId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return Empty($"Private-capital fund event '{context.Subject.SubjectId}' was not found.");
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var rootId = NodeId(context.Subject, "fund-event");
        var evidenceId = NodeId(context.Subject, "retained-evidence");
        var approvalId = NodeId(context.Subject, "approval");
        var capitalAccountId = NodeId(context.Subject, "capital-account-subledger");
        var ledgerImpactId = NodeId(context.Subject, "ledger-impact");
        var reportOutputId = NodeId(context.Subject, "report-output");
        var nodes = new List<EvidenceNodeDto>
        {
            Node(
                context.Subject,
                rootId,
                "private-capital-fund-event",
                MapReadiness(record.Readiness),
                $"{record.FundEventType} fund event {record.FundEventId} is {record.ReadinessLabel}: {record.ReadinessReason}",
                "ManualJournalEntryWorkbenchService",
                record.FundEvent.UpdatedAtUtc,
                artifacts:
                [
                    Artifact(
                        $"{rootId}:record-route",
                        "fund-event-ledger-record-route",
                        route: PrivateCapitalActivityRoutes.BuildFundEventRecordRoute(activity.FundProfileId, activity.LedgerBookId, record.FundEventId),
                        generatedAt: generatedAt),
                    Artifact(
                        $"{rootId}:activity-route",
                        "private-capital-activity-route",
                        route: record.ActivityRoute,
                        generatedAt: generatedAt)
                ],
                workItemIds: BuildWorkItemIds(record))
        };
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string> { rootId };

        nodes.Add(Node(
            context.Subject,
            evidenceId,
            "retained-evidence",
            record.EvidenceLinkCount == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            record.EvidenceLinkCount == 0
                ? "No retained source, approval, settlement, ledger, or report evidence link is attached to this fund event."
                : $"{record.EvidenceLinkCount} retained evidence link(s) support this fund event.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: BuildEvidenceArtifacts(record, evidenceId, generatedAt)));
        edges.Add(new EvidenceEdgeDto(rootId, evidenceId, "supported-by", "Retained evidence supports the private-capital fund event."));
        required.Add(evidenceId);

        nodes.Add(Node(
            context.Subject,
            approvalId,
            "approval-state",
            MapJournalApprovalStatus(record.ApprovalState),
            record.ApprovalId is null
                ? $"Fund-event journal approval state is {record.ApprovalState}."
                : $"Fund-event journal approval {record.ApprovalId} is {record.ApprovalState}.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: record.ApprovalRoute is null
                ? []
                :
                [
                    Artifact(
                        $"{approvalId}:approval-route",
                        "approval-route",
                        route: record.ApprovalRoute,
                        generatedAt: generatedAt)
                ]));
        edges.Add(new EvidenceEdgeDto(rootId, approvalId, "approved-by", "Approval state gates posting, capital-account reliance, and stakeholder report output."));
        required.Add(approvalId);

        nodes.Add(Node(
            context.Subject,
            capitalAccountId,
            "capital-account-subledger",
            record.CapitalAccountSubledgerEntryCount == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            record.CapitalAccountSubledgerEntryCount == 0
                ? "No capital-account subledger movement is linked to this fund event."
                : $"{record.CapitalAccountSubledgerEntryCount} capital-account subledger entr{(record.CapitalAccountSubledgerEntryCount == 1 ? "y" : "ies")} move net activity from {record.CapitalAccountOpeningNetActivity} to {record.CapitalAccountEndingNetActivity} {record.Currency}.",
            "ManualJournalEntryWorkbenchService",
            record.CapitalAccountSubledgerEntries.Select(static item => (DateTimeOffset?)item.UpdatedAtUtc).DefaultIfEmpty(record.FundEvent.UpdatedAtUtc).Max(),
            artifacts:
            [
                Artifact(
                    $"{capitalAccountId}:subledger-route",
                    "capital-account-subledger-route",
                    route: PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
                        activity.FundProfileId,
                        activity.LedgerBookId,
                        record.CapitalAccountId,
                        record.InvestorId,
                        record.Currency),
                    generatedAt: generatedAt)
            ]));
        edges.Add(new EvidenceEdgeDto(rootId, capitalAccountId, "posts-to", "Capital-account subledger movement is derived from the same fund event."));
        required.Add(capitalAccountId);

        nodes.Add(Node(
            context.Subject,
            ledgerImpactId,
            "ledger-impact",
            record.LedgerImpactCount == 0
                ? EvidenceStatusDto.Missing
                : record.IsPostingReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            record.LedgerImpactCount == 0
                ? "No GL impact is linked to this fund event."
                : $"{record.LedgerImpactCount} GL impact row(s) are linked; posting readiness is {record.IsPostingReady}.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: record.LedgerImpacts
                .Select(impact => Artifact(
                    $"{ledgerImpactId}:{SanitizeNodePart(impact.LedgerImpactId)}",
                    "private-capital-ledger-impact-route",
                    route: record.ActivityRoute,
                    generatedAt: generatedAt))
                .ToArray()));
        edges.Add(new EvidenceEdgeDto(rootId, ledgerImpactId, "posts-to", "Ledger impact is derived from the same approved private-capital fund event."));
        required.Add(ledgerImpactId);

        nodes.Add(Node(
            context.Subject,
            reportOutputId,
            "report-output",
            record.ReportOutputCount == 0
                ? EvidenceStatusDto.Missing
                : record.IsReportReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            record.ReportOutputCount == 0
                ? "No retained stakeholder report output is linked to this fund event."
                : $"{record.ReportOutputCount} report output row(s) are linked; report readiness is {record.IsReportReady} and publication is {record.IsPublished}.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: record.ReportOutputs
                .Select(output => Artifact(
                    $"{reportOutputId}:{SanitizeNodePart(output.ReportOutputId)}",
                    output.IsPublished ? "published-report-output-route" : "report-output-route",
                    route: string.IsNullOrWhiteSpace(output.ReportOutputRoute)
                        ? output.ReportRoute
                        : output.ReportOutputRoute,
                    generatedAt: generatedAt,
                    hash: output.PublicationEvidenceHash,
                    retained: output.IsPublished))
                .ToArray()));
        edges.Add(new EvidenceEdgeDto(rootId, reportOutputId, "reported-by", "Report output is tied to the same fund-event ledger and capital-account activity."));
        required.Add(reportOutputId);

        var warnings = record.ValidationIssues
            .Select(static issue => $"{issue.Code}: {issue.Message}")
            .ToArray();

        return new EvidenceContribution(nodes, edges, [], required, warnings);
    }

    private static EvidenceStatusDto MapReadiness(PrivateCapitalFundEventLedgerReadinessDto readiness)
        => readiness switch
        {
            PrivateCapitalFundEventLedgerReadinessDto.Blocked => EvidenceStatusDto.Blocked,
            PrivateCapitalFundEventLedgerReadinessDto.Ready or PrivateCapitalFundEventLedgerReadinessDto.Published => EvidenceStatusDto.Ready,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static EvidenceContribution Empty(string warning)
        => new([], [], [], [], [warning]);

    private static EvidenceStatusDto MapJournalApprovalStatus(ManualJournalEntryStatusDto status)
        => status switch
        {
            ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved => EvidenceStatusDto.Ready,
            ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.NeedsFix => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static IReadOnlyList<string> BuildWorkItemIds(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var ids = new List<string>();
        if (record.Readiness is not PrivateCapitalFundEventLedgerReadinessDto.Ready and not PrivateCapitalFundEventLedgerReadinessDto.Published)
        {
            ids.Add($"private-capital-fund-event:{record.Readiness.ToString().ToLowerInvariant()}:{record.FundEventId}");
        }

        return ids;
    }

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildEvidenceArtifacts(
        PrivateCapitalFundEventLedgerRecordDto record,
        string evidenceId,
        DateTimeOffset generatedAt)
        => record.EvidenceLinks
            .Select((link, index) => BuildEvidenceArtifact(evidenceId, link, index, generatedAt))
            .Prepend(Artifact(
                $"{evidenceId}:packet-route",
                "evidence-packet-route",
                route: record.EvidenceRoute,
                generatedAt: generatedAt))
            .ToArray();

    private static EvidenceArtifactRefDto BuildEvidenceArtifact(
        string evidenceId,
        string link,
        int index,
        DateTimeOffset generatedAt)
    {
        var artifactId = $"{evidenceId}:retained-{index + 1}";
        return link.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
               link.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Artifact(artifactId, "retained-evidence-link", route: link, generatedAt: generatedAt, retained: true)
            : Artifact(artifactId, "retained-evidence-link", path: link, generatedAt: generatedAt, retained: true);
    }

    private static string? TryResolveFundProfileIdFromFundEventId(string fundEventId)
    {
        if (string.IsNullOrWhiteSpace(fundEventId))
        {
            return null;
        }

        var parts = fundEventId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 &&
               string.Equals(parts[0], "fund-event", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static string SanitizeNodePart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "artifact"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}

public sealed class PaymentIntentEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public PaymentIntentEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "payment-intent";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.PaymentIntentKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<IManualJournalEntryWorkbenchService>();
        if (service is null)
        {
            return Empty("Manual journal entry workbench service is not registered.");
        }

        var match = await ResolvePaymentIntentAsync(service, context.Subject.SubjectId, context.Subject.LedgerBookId, context.CancellationToken)
            .ConfigureAwait(false);
        if (match is null)
        {
            return Empty($"Payment intent '{context.Subject.SubjectId}' was not found.");
        }

        var (activity, workflow) = match.Value;
        var generatedAt = DateTimeOffset.UtcNow;
        var rootId = NodeId(context.Subject, "payment-intent");
        var requesterId = NodeId(context.Subject, "requester");
        var expectedCashId = NodeId(context.Subject, "expected-cash-movement");
        var approvalId = NodeId(context.Subject, "approval-chain");
        var bankEvidenceId = NodeId(context.Subject, "bank-cash-evidence");
        var reconciliationId = NodeId(context.Subject, "reconciliation-linkage");
        var auditId = NodeId(context.Subject, "audit-history");
        var executionDeferredId = NodeId(context.Subject, "execution-deferred");
        var nodes = new List<EvidenceNodeDto>
        {
            Node(
                context.Subject,
                rootId,
                "payment-intent",
                MapWorkflowStatus(workflow.Status),
                $"{workflow.PaymentIntentId} is {workflow.StatusLabel}: {workflow.ReadinessReason}",
                "ManualJournalEntryWorkbenchService",
                workflow.RequestedAtUtc,
                artifacts:
                [
                    Artifact(
                        $"{rootId}:packet-route",
                        "payment-intent-evidence-route",
                        route: workflow.EvidenceRoute,
                        generatedAt: generatedAt),
                    Artifact(
                        $"{rootId}:workbench-route",
                        "payment-intent-workbench-route",
                        route: workflow.WorkbenchRoute,
                        generatedAt: generatedAt)
                ],
                workItemIds: BuildWorkflowWorkItemIds(workflow))
        };
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string> { rootId };

        nodes.Add(Node(
            context.Subject,
            requesterId,
            "payment-requester",
            string.IsNullOrWhiteSpace(workflow.Requester) ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            string.IsNullOrWhiteSpace(workflow.Requester)
                ? "Payment intent requester is missing."
                : $"{workflow.Requester} requested payment intent {workflow.PaymentIntentId} at {workflow.RequestedAtUtc:O}.",
            "ManualJournalEntryWorkbenchService",
            workflow.RequestedAtUtc));
        edges.Add(new EvidenceEdgeDto(rootId, requesterId, "requested-by", "Payment intent evidence retains who requested the expected cash movement."));
        required.Add(requesterId);

        nodes.Add(Node(
            context.Subject,
            expectedCashId,
            "expected-cash-movement",
            workflow.ExpectedCashMovement.Amount <= 0m ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
            BuildExpectedCashSummary(workflow.ExpectedCashMovement),
            "ManualJournalEntryWorkbenchService",
            DateFrom(workflow.ExpectedCashMovement.EffectiveDate),
            artifacts: BuildExpectedCashArtifacts(workflow.ExpectedCashMovement, workflow.WorkbenchRoute, expectedCashId, generatedAt)));
        edges.Add(new EvidenceEdgeDto(rootId, expectedCashId, "expects", "Expected cash movement documents the amount, direction, date, and settlement reference before execution."));
        required.Add(expectedCashId);

        var approvalStatus = MapApprovalChainStatus(workflow.ApprovalChain);
        nodes.Add(Node(
            context.Subject,
            approvalId,
            "approval-chain",
            approvalStatus,
            workflow.ApprovalChain.Count == 0
                ? "No payment-intent approval chain is attached."
                : $"{workflow.ApprovalChain.Count} approval step(s) are attached; latest status is {workflow.ApprovalChain[^1].Status}.",
            "ManualJournalEntryWorkbenchService",
            workflow.ApprovalChain.Select(static step => step.DecidedAtUtc).Where(static value => value.HasValue).DefaultIfEmpty(workflow.RequestedAtUtc).Max(),
            artifacts: BuildApprovalArtifacts(workflow, approvalId, generatedAt),
            workItemIds: approvalStatus == EvidenceStatusDto.Ready ? [] : [$"payment-intent-approval:{workflow.PaymentIntentId}"]));
        edges.Add(new EvidenceEdgeDto(rootId, approvalId, "approved-by", "Approval chain gates the payment intent before any bank-side instruction."));
        required.Add(approvalId);

        var bankStatus = MapBankEvidenceStatus(workflow.BankEvidence);
        nodes.Add(Node(
            context.Subject,
            bankEvidenceId,
            "bank-cash-evidence",
            bankStatus,
            workflow.BankEvidence.Count == 0
                ? "No bank, cash, settlement, or retained statement evidence is attached to this payment intent."
                : $"{workflow.BankEvidence.Count} bank/cash evidence item(s) support this payment intent.",
            "ManualJournalEntryWorkbenchService",
            workflow.BankEvidence.Select(static evidence => evidence.RecordedAtUtc).Where(static value => value.HasValue).DefaultIfEmpty(workflow.RequestedAtUtc).Max(),
            artifacts: BuildBankEvidenceArtifacts(workflow.BankEvidence, workflow.ExpectedCashMovement, bankEvidenceId, generatedAt),
            workItemIds: bankStatus == EvidenceStatusDto.Ready ? [] : [$"payment-intent-cash-evidence:{workflow.PaymentIntentId}"]));
        edges.Add(new EvidenceEdgeDto(rootId, bankEvidenceId, "supported-by", "Retained bank and cash evidence supports the expected movement without initiating payment execution."));
        required.Add(bankEvidenceId);

        var reconciliationStatus = MapReconciliationStatus(workflow.ReconciliationLinks);
        nodes.Add(Node(
            context.Subject,
            reconciliationId,
            "reconciliation-linkage",
            reconciliationStatus,
            workflow.ReconciliationLinks.Count == 0
                ? "No reconciliation linkage is attached to this payment intent."
                : $"{workflow.ReconciliationLinks.Count} reconciliation link(s) tie the payment intent to settlement or exception review.",
            "ManualJournalEntryWorkbenchService",
            generatedAt,
            artifacts: BuildReconciliationArtifacts(workflow.ReconciliationLinks, reconciliationId, generatedAt),
            workItemIds: reconciliationStatus == EvidenceStatusDto.Ready ? [] : [$"payment-intent-reconciliation:{workflow.PaymentIntentId}"]));
        edges.Add(new EvidenceEdgeDto(rootId, reconciliationId, "reconciles-to", "Reconciliation linkage connects retained cash evidence to the ledger and exception workflow."));
        required.Add(reconciliationId);

        var auditStatus = workflow.AuditHistory.Count == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready;
        nodes.Add(Node(
            context.Subject,
            auditId,
            "audit-history",
            auditStatus,
            workflow.AuditHistory.Count == 0
                ? "No payment-intent audit history is retained."
                : $"{workflow.AuditHistory.Count} payment-intent audit event(s) retain request, approval, cash-evidence, reconciliation, and deferral history.",
            "ManualJournalEntryWorkbenchService",
            workflow.AuditHistory.Select(static item => item.RecordedAtUtc).DefaultIfEmpty(workflow.RequestedAtUtc).Max(),
            artifacts: BuildAuditArtifacts(workflow.AuditHistory, auditId, generatedAt)));
        edges.Add(new EvidenceEdgeDto(rootId, auditId, "retained-by", "Audit history retains the payment-intent control trail."));
        required.Add(auditId);

        nodes.Add(Node(
            context.Subject,
            executionDeferredId,
            "execution-deferred",
            string.IsNullOrWhiteSpace(workflow.ExecutionDeferredReason) ? EvidenceStatusDto.ReviewRequired : EvidenceStatusDto.Ready,
            string.IsNullOrWhiteSpace(workflow.ExecutionDeferredReason)
                ? "Payment-intent workflow does not explain why live treasury execution remains deferred."
                : workflow.ExecutionDeferredReason,
            "ManualJournalEntryWorkbenchService",
            generatedAt));
        edges.Add(new EvidenceEdgeDto(rootId, executionDeferredId, "defers", "v0.18 keeps treasury payment execution deferred while retaining intent, approval, cash evidence, and reconciliation proof."));
        required.Add(executionDeferredId);

        AddPrivateCapitalLineageNodes(context.Subject, activity, workflow, rootId, nodes, edges, generatedAt);

        return new EvidenceContribution(nodes, edges, [], required, []);
    }

    private static async Task<(PrivateCapitalActivityProjectionDto Activity, PaymentIntentWorkflowDto Workflow)?> ResolvePaymentIntentAsync(
        IManualJournalEntryWorkbenchService service,
        string paymentIntentId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        var fundProfileId = TryResolveFundProfileIdFromPaymentIntentId(paymentIntentId);
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            var activity = await service.GetPrivateCapitalActivityAsync(fundProfileId, ledgerBookId, ct)
                .ConfigureAwait(false);
            var workflow = activity.PaymentIntents.FirstOrDefault(item =>
                string.Equals(item.PaymentIntentId, paymentIntentId, StringComparison.OrdinalIgnoreCase));
            if (workflow is not null)
            {
                return (activity, workflow);
            }
        }

        var fundProfileIds = await service.ListFundProfileIdsAsync(ct).ConfigureAwait(false);
        var activities = fundProfileIds.Count == 0
            ? [await service.GetPrivateCapitalActivityAsync(ct: ct).ConfigureAwait(false)]
            : await Task.WhenAll(fundProfileIds.Select(id =>
                service.GetPrivateCapitalActivityAsync(id, ledgerBookId: null, ct))).ConfigureAwait(false);
        foreach (var activity in activities)
        {
            var workflow = activity.PaymentIntents.FirstOrDefault(item =>
                string.Equals(item.PaymentIntentId, paymentIntentId, StringComparison.OrdinalIgnoreCase));
            if (workflow is not null)
            {
                return (activity, workflow);
            }
        }

        return null;
    }

    private static void AddPrivateCapitalLineageNodes(
        EvidenceSubjectDto subject,
        PrivateCapitalActivityProjectionDto activity,
        PaymentIntentWorkflowDto workflow,
        string rootId,
        List<EvidenceNodeDto> nodes,
        List<EvidenceEdgeDto> edges,
        DateTimeOffset generatedAt)
    {
        var record = activity.FundEventRecords.FirstOrDefault(item =>
            string.Equals(item.FundEventId, workflow.FundEventId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.PaymentIntentId, workflow.PaymentIntentId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return;
        }

        var fundEventId = NodeId(subject, "fund-event");
        nodes.Add(Node(
            subject,
            fundEventId,
            "private-capital-fund-event",
            MapFundEventReadiness(record.Readiness),
            $"{record.FundEventType} fund event {record.FundEventId} supplies the payment intent, ledger, capital-account, and report-output lineage.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts:
            [
                Artifact(
                    $"{fundEventId}:record-route",
                    "fund-event-ledger-record-route",
                    route: PrivateCapitalActivityRoutes.BuildFundEventRecordRoute(activity.FundProfileId, activity.LedgerBookId, record.FundEventId),
                    generatedAt: generatedAt)
            ]));
        edges.Add(new EvidenceEdgeDto(rootId, fundEventId, "originates-from", "Payment intent originates from the private-capital fund event."));

        var capitalAccountId = NodeId(subject, "capital-account-subledger");
        nodes.Add(Node(
            subject,
            capitalAccountId,
            "capital-account-subledger",
            record.CapitalAccountSubledgerEntryCount == 0 ? EvidenceStatusDto.Missing : EvidenceStatusDto.Ready,
            record.CapitalAccountSubledgerEntryCount == 0
                ? "No capital-account subledger movement is linked to the payment intent fund event."
                : $"{record.CapitalAccountSubledgerEntryCount} capital-account subledger entr{(record.CapitalAccountSubledgerEntryCount == 1 ? "y" : "ies")} link the payment intent to investor capital.",
            "ManualJournalEntryWorkbenchService",
            record.CapitalAccountSubledgerEntries.Select(static item => (DateTimeOffset?)item.UpdatedAtUtc).DefaultIfEmpty(record.FundEvent.UpdatedAtUtc).Max(),
            artifacts:
            [
                Artifact(
                    $"{capitalAccountId}:subledger-route",
                    "capital-account-subledger-route",
                    route: PrivateCapitalActivityRoutes.BuildCapitalAccountSubledgerRoute(
                        activity.FundProfileId,
                        activity.LedgerBookId,
                        record.CapitalAccountId,
                        record.InvestorId,
                        record.Currency),
                    generatedAt: generatedAt)
            ]));
        edges.Add(new EvidenceEdgeDto(fundEventId, capitalAccountId, "posts-to", "Payment-intent lineage includes the affected capital-account subledger."));

        var ledgerImpactId = NodeId(subject, "ledger-impact");
        nodes.Add(Node(
            subject,
            ledgerImpactId,
            "ledger-impact",
            record.LedgerImpactCount == 0
                ? EvidenceStatusDto.Missing
                : record.IsPostingReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            record.LedgerImpactCount == 0
                ? "No ledger impact is linked to the payment intent fund event."
                : $"{record.LedgerImpactCount} ledger impact row(s) link payment intent to GL intent.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: record.LedgerImpacts
                .Select(impact => Artifact(
                    $"{ledgerImpactId}:{SanitizeNodePart(impact.LedgerImpactId)}",
                    "private-capital-ledger-impact-route",
                    route: record.ActivityRoute,
                    generatedAt: generatedAt))
                .ToArray()));
        edges.Add(new EvidenceEdgeDto(fundEventId, ledgerImpactId, "posts-to", "Payment-intent lineage includes the expected GL ledger impact."));

        var reportOutputId = NodeId(subject, "report-output");
        nodes.Add(Node(
            subject,
            reportOutputId,
            "report-output",
            record.ReportOutputCount == 0
                ? EvidenceStatusDto.Missing
                : record.IsReportReady ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
            record.ReportOutputCount == 0
                ? "No report output is linked to the payment intent fund event."
                : $"{record.ReportOutputCount} report output row(s) link payment intent to stakeholder reporting.",
            "ManualJournalEntryWorkbenchService",
            record.FundEvent.UpdatedAtUtc,
            artifacts: record.ReportOutputs
                .Select(output => Artifact(
                    $"{reportOutputId}:{SanitizeNodePart(output.ReportOutputId)}",
                    output.IsPublished ? "published-report-output-route" : "report-output-route",
                    route: string.IsNullOrWhiteSpace(output.ReportOutputRoute)
                        ? output.ReportRoute
                        : output.ReportOutputRoute,
                    generatedAt: generatedAt,
                    hash: output.PublicationEvidenceHash,
                    retained: output.IsPublished))
                .ToArray()));
        edges.Add(new EvidenceEdgeDto(fundEventId, reportOutputId, "reported-by", "Payment-intent lineage stays visible in stakeholder report output."));
    }

    private static EvidenceStatusDto MapWorkflowStatus(PaymentIntentWorkflowStatusDto status)
        => status switch
        {
            PaymentIntentWorkflowStatusDto.ExecutionDeferred => EvidenceStatusDto.Ready,
            PaymentIntentWorkflowStatusDto.BankReturned or PaymentIntentWorkflowStatusDto.Blocked or PaymentIntentWorkflowStatusDto.EvidenceMissing => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static EvidenceStatusDto MapFundEventReadiness(PrivateCapitalFundEventLedgerReadinessDto readiness)
        => readiness switch
        {
            PrivateCapitalFundEventLedgerReadinessDto.Blocked => EvidenceStatusDto.Blocked,
            PrivateCapitalFundEventLedgerReadinessDto.Ready or PrivateCapitalFundEventLedgerReadinessDto.Published => EvidenceStatusDto.Ready,
            _ => EvidenceStatusDto.ReviewRequired
        };

    private static EvidenceStatusDto MapApprovalChainStatus(IReadOnlyList<PaymentIntentApprovalStepDto> approvalChain)
    {
        if (approvalChain.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (approvalChain.Any(step => ContainsAny(step.Status, "rejected", "denied", "blocked", "failed")))
        {
            return EvidenceStatusDto.Blocked;
        }

        return approvalChain.All(step => ContainsAny(step.Status, "approved", "complete", "completed", "accepted"))
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto MapBankEvidenceStatus(IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence)
    {
        if (bankEvidence.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (bankEvidence.Any(evidence => ContainsAny(evidence.Status, "returned", "failed", "blocked", "rejected")))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (bankEvidence.Any(evidence => ContainsAny(evidence.Status, "missing", "pending", "needed") ||
                                         ContainsAny(evidence.EvidenceKind, "missing", "pending")))
        {
            return EvidenceStatusDto.ReviewRequired;
        }

        return EvidenceStatusDto.Ready;
    }

    private static EvidenceStatusDto MapReconciliationStatus(IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
    {
        if (reconciliationLinks.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (reconciliationLinks.Any(link => ContainsAny(link.Status, "blocked", "failed", "rejected", "break")))
        {
            return EvidenceStatusDto.Blocked;
        }

        return reconciliationLinks.Any(link => ContainsAny(link.Status, "pending", "missing", "open"))
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Ready;
    }

    private static string BuildExpectedCashSummary(PaymentIntentExpectedCashMovementDto movement)
        => $"{movement.Direction} {movement.Amount} {movement.Currency} is expected on {movement.EffectiveDate:yyyy-MM-dd} for {movement.Purpose}; payee {movement.Payee ?? "not assigned"}; scope {movement.AccountScope ?? "not assigned"}; approval policy {movement.ApprovalPolicy ?? "not assigned"}; source evidence {movement.SourceEvidenceLinks.Count} link(s); settlement reference {movement.SettlementReference ?? "not attached"}.";

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildExpectedCashArtifacts(
        PaymentIntentExpectedCashMovementDto movement,
        string workbenchRoute,
        string nodeId,
        DateTimeOffset generatedAt)
    {
        var artifacts = new List<EvidenceArtifactRefDto>
        {
            Artifact(
                $"{nodeId}:workbench-route",
                "payment-intent-workbench-route",
                route: workbenchRoute,
                generatedAt: generatedAt)
        };

        artifacts.AddRange(movement.SourceEvidenceLinks
            .Select((link, index) => BuildRouteOrPathArtifact(
                $"{nodeId}:source-evidence-{index + 1}",
                "payment-intent-source-evidence",
                link,
                generatedAt)));

        return artifacts;
    }

    private static IReadOnlyList<string> BuildWorkflowWorkItemIds(PaymentIntentWorkflowDto workflow)
        => workflow.Status == PaymentIntentWorkflowStatusDto.ExecutionDeferred
            ? []
            : [$"payment-intent:{workflow.Status.ToString().ToLowerInvariant()}:{workflow.PaymentIntentId}"];

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildApprovalArtifacts(
        PaymentIntentWorkflowDto workflow,
        string nodeId,
        DateTimeOffset generatedAt)
        => workflow.ApprovalChain
            .Where(static step => !string.IsNullOrWhiteSpace(step.EvidenceRoute))
            .Select(step => BuildRouteOrPathArtifact(
                $"{nodeId}:approval-{step.Sequence}",
                "payment-intent-approval-route",
                step.EvidenceRoute,
                step.DecidedAtUtc ?? generatedAt))
            .ToArray();

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildBankEvidenceArtifacts(
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        PaymentIntentExpectedCashMovementDto expectedCashMovement,
        string nodeId,
        DateTimeOffset generatedAt)
        => bankEvidence
            .Select((evidence, index) => BuildRouteOrPathArtifact(
                    $"{nodeId}:{SanitizeNodePart(evidence.EvidenceId)}-{index + 1}",
                    evidence.EvidenceKind,
                    evidence.EvidenceRoute,
                    evidence.RecordedAtUtc ?? generatedAt) with
            {
                Capture = BuildBankEvidenceCapture(evidence),
                ExtractedFields = BuildBankEvidenceExtractedFields(evidence, expectedCashMovement)
            })
            .ToArray();

    private static EvidenceArtifactCaptureDto BuildBankEvidenceCapture(PaymentIntentBankEvidenceDto evidence)
        => new(
            ResolveBankEvidenceCaptureChannel(evidence),
            FirstNonEmpty(evidence.TransactionType, evidence.EvidenceKind),
            evidence.RecordedAtUtc,
            null,
            FirstNonEmpty(evidence.ExternalRef, evidence.BankTransactionId?.ToString("D"), evidence.EvidenceId),
            null);

    private static IReadOnlyList<EvidenceArtifactExtractionFieldDto> BuildBankEvidenceExtractedFields(
        PaymentIntentBankEvidenceDto evidence,
        PaymentIntentExpectedCashMovementDto expectedCashMovement)
    {
        var fields = new List<EvidenceArtifactExtractionFieldDto>
        {
            BuildExtractedField(
                "evidenceStatus",
                evidence.Status,
                null,
                MapBankEvidenceStatus(evidence),
                "payment-intent-bank-evidence",
                evidence.EvidenceId,
                "Payment-intent bank evidence status retained from the cash-evidence workflow."),
            BuildExtractedField(
                "evidenceKind",
                evidence.EvidenceKind,
                null,
                EvidenceStatusDto.Ready,
                "payment-intent-bank-evidence",
                evidence.EvidenceId,
                "Payment-intent bank evidence kind retained from the cash-evidence workflow.")
        };

        if (!string.IsNullOrWhiteSpace(evidence.TransactionType))
        {
            fields.Add(BuildExtractedField(
                "transactionType",
                evidence.TransactionType,
                null,
                EvidenceStatusDto.Ready,
                "payment-intent-bank-evidence",
                evidence.EvidenceId,
                "Bank transaction type retained from the cash-evidence workflow."));
        }

        if (evidence.BankTransactionId.HasValue)
        {
            var transactionId = evidence.BankTransactionId.Value.ToString("D");
            fields.Add(BuildExtractedField(
                "bankTransactionId",
                transactionId,
                null,
                EvidenceStatusDto.Ready,
                "bank-transaction",
                transactionId,
                "Bank transaction id retained as payment-intent cash evidence."));
        }

        if (evidence.Amount.HasValue)
        {
            fields.Add(BuildExtractedField(
                "amount",
                FormatDecimal(evidence.Amount.Value),
                FormatDecimal(expectedCashMovement.Amount),
                ResolveExpectedFieldStatus(FormatDecimal(evidence.Amount.Value), FormatDecimal(expectedCashMovement.Amount)),
                "payment-intent",
                expectedCashMovement.PaymentIntentId,
                "Bank evidence amount checked against the expected cash movement."));
        }

        if (!string.IsNullOrWhiteSpace(evidence.Currency))
        {
            fields.Add(BuildExtractedField(
                "currency",
                evidence.Currency,
                expectedCashMovement.Currency,
                ResolveExpectedFieldStatus(evidence.Currency, expectedCashMovement.Currency),
                "payment-intent",
                expectedCashMovement.PaymentIntentId,
                "Bank evidence currency checked against the expected cash movement."));
        }

        if (evidence.EffectiveDate.HasValue)
        {
            fields.Add(BuildExtractedField(
                "effectiveDate",
                evidence.EffectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                expectedCashMovement.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ResolveExpectedFieldStatus(
                    evidence.EffectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    expectedCashMovement.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                "payment-intent",
                expectedCashMovement.PaymentIntentId,
                "Bank evidence effective date checked against the expected cash movement."));
        }

        if (!string.IsNullOrWhiteSpace(evidence.ExternalRef))
        {
            fields.Add(BuildExtractedField(
                "externalReference",
                evidence.ExternalRef,
                expectedCashMovement.SettlementReference,
                ResolveExpectedFieldStatus(evidence.ExternalRef, expectedCashMovement.SettlementReference),
                "settlement-reference",
                evidence.ExternalRef,
                "Bank evidence external reference checked against the expected settlement reference."));
        }

        return fields;
    }

    private static EvidenceArtifactExtractionFieldDto BuildExtractedField(
        string fieldName,
        string? extractedValue,
        string? expectedValue,
        EvidenceStatusDto validationStatus,
        string linkedRecordKind,
        string linkedRecordId,
        string validationMessage)
        => new(
            fieldName,
            string.IsNullOrWhiteSpace(extractedValue) ? null : extractedValue.Trim(),
            string.IsNullOrWhiteSpace(expectedValue) ? null : expectedValue.Trim(),
            ConfidenceFor(validationStatus),
            ReviewStateFor(validationStatus),
            validationStatus,
            validationMessage,
            linkedRecordKind,
            linkedRecordId);

    private static EvidenceStatusDto ResolveExpectedFieldStatus(string? extractedValue, string? expectedValue)
    {
        if (string.IsNullOrWhiteSpace(extractedValue))
        {
            return EvidenceStatusDto.Missing;
        }

        if (!string.IsNullOrWhiteSpace(expectedValue) &&
            !string.Equals(extractedValue.Trim(), expectedValue.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceStatusDto.ReviewRequired;
        }

        return EvidenceStatusDto.Ready;
    }

    private static EvidenceStatusDto MapBankEvidenceStatus(PaymentIntentBankEvidenceDto evidence)
    {
        if (ContainsAny(evidence.Status, "returned", "failed", "blocked", "rejected"))
        {
            return EvidenceStatusDto.Blocked;
        }

        return ContainsAny(evidence.Status, "missing", "pending", "needed") ||
               ContainsAny(evidence.EvidenceKind, "missing", "pending")
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Ready;
    }

    private static string ResolveBankEvidenceCaptureChannel(PaymentIntentBankEvidenceDto evidence)
    {
        var value = string.Join(
            ' ',
            evidence.EvidenceKind,
            evidence.EvidenceRoute,
            evidence.TransactionType,
            evidence.ExternalRef);

        if (ContainsAny(value, "sftp"))
        {
            return "SFTP";
        }

        if (ContainsAny(value, "email"))
        {
            return "Email";
        }

        if (ContainsAny(value, "portal"))
        {
            return "Portal";
        }

        return evidence.BankTransactionId.HasValue || IsApiEvidenceRoute(evidence.EvidenceRoute) || ContainsAny(value, "plaid")
            ? "API"
            : "Upload";
    }

    private static bool IsApiEvidenceRoute(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("/api/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("api:", StringComparison.OrdinalIgnoreCase));

    private static decimal? ConfidenceFor(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.Ready => 1m,
            EvidenceStatusDto.Blocked => 1m,
            EvidenceStatusDto.ReviewRequired => 0.75m,
            EvidenceStatusDto.Missing => null,
            EvidenceStatusDto.Stale => 0.5m,
            _ => null
        };

    private static string ReviewStateFor(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.Ready => "Reviewed",
            EvidenceStatusDto.Blocked => "Blocked",
            EvidenceStatusDto.ReviewRequired => "NeedsReview",
            EvidenceStatusDto.Missing => "Missing",
            EvidenceStatusDto.Stale => "Stale",
            _ => "Unknown"
        };

    private static string FormatDecimal(decimal value)
        => value.ToString("0.#############################", CultureInfo.InvariantCulture);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildReconciliationArtifacts(
        IReadOnlyList<PaymentIntentReconciliationLinkDto> links,
        string nodeId,
        DateTimeOffset generatedAt)
        => links
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceRoute))
            .Select((link, index) => BuildRouteOrPathArtifact(
                $"{nodeId}:{SanitizeNodePart(link.LinkId)}-{index + 1}",
                "payment-intent-reconciliation-route",
                link.EvidenceRoute,
                generatedAt))
            .ToArray();

    private static IReadOnlyList<EvidenceArtifactRefDto> BuildAuditArtifacts(
        IReadOnlyList<PaymentIntentAuditEventDto> events,
        string nodeId,
        DateTimeOffset generatedAt)
    {
        var artifacts = new List<EvidenceArtifactRefDto>();
        foreach (var auditEvent in events)
        {
            var index = 0;
            foreach (var link in auditEvent.EvidenceLinks)
            {
                index++;
                artifacts.Add(BuildRouteOrPathArtifact(
                    $"{nodeId}:{SanitizeNodePart(auditEvent.AuditEventId)}-{index}",
                    "payment-intent-audit-evidence-link",
                    link,
                    auditEvent.RecordedAtUtc));
            }
        }

        return artifacts.Count == 0
            ? [Artifact($"{nodeId}:audit-route", "payment-intent-audit-history", route: null, generatedAt: generatedAt)]
            : artifacts.ToArray();
    }

    private static EvidenceArtifactRefDto BuildRouteOrPathArtifact(
        string artifactId,
        string kind,
        string? routeOrPath,
        DateTimeOffset generatedAt)
        => IsRoute(routeOrPath)
            ? Artifact(artifactId, kind, route: routeOrPath, generatedAt: generatedAt)
            : Artifact(artifactId, kind, path: routeOrPath, generatedAt: generatedAt);

    private static DateTimeOffset DateFrom(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static bool ContainsAny(string? value, params string[] needles)
        => !string.IsNullOrWhiteSpace(value) &&
           needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool IsRoute(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string? TryResolveFundProfileIdFromPaymentIntentId(string paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return null;
        }

        var parts = paymentIntentId.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               string.Equals(parts[0], "payment", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    private static EvidenceContribution Empty(string warning)
        => new([], [], [], [], [warning]);

    private static string SanitizeNodePart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "artifact"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}

public sealed class EvidenceVaultEvidenceContributor : IEvidenceContributor
{
    private readonly IServiceProvider _services;

    public EvidenceVaultEvidenceContributor(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public string ContributorId => "evidence-vault";

    public bool Supports(EvidenceSubjectDto subject)
        => string.Equals(subject.SubjectKind, EvidenceSubjectResolver.EvidenceVaultKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var store = _services.GetService<IEvidenceArtifactStore>();
        if (store is null)
        {
            return new EvidenceContribution([], [], [], [], ["Evidence artifact store is not registered."]);
        }

        if (string.Equals(context.Subject.SubjectId, "lookup", StringComparison.OrdinalIgnoreCase))
        {
            return new EvidenceContribution([], [], [], [], ["Open a specific vault id to inspect retained vault artifacts."]);
        }

        var identity = await store.TryGetVaultIdentityAsync(context.Subject.SubjectId, context.CancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            return new EvidenceContribution([], [], [], [], [$"Evidence vault '{context.Subject.SubjectId}' was not found."]);
        }

        var nodes = new List<EvidenceNodeDto>();
        var edges = new List<EvidenceEdgeDto>();
        var required = new List<string>();
        var rootId = NodeId(context.Subject, "manifest");
        nodes.Add(Node(
            context.Subject,
            rootId,
            "evidence-vault-manifest",
            EvidenceStatusDto.Ready,
            $"Vault {identity.VaultId} retains {identity.StorageKind} evidence for {identity.SubjectKind}/{identity.SubjectId} with {identity.Artifacts.Count} artifact(s).",
            "EvidenceArtifactStore",
            identity.RetainedAt,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    $"{rootId}:manifest",
                    "evidence-vault-manifest-route",
                    Path: identity.ManifestPath,
                    Route: identity.ManifestRoute,
                    GeneratedAt: identity.RetainedAt,
                    Hash: identity.ContentHashSha256,
                    Retained: false,
                    CanonicalSubjectKind: identity.SubjectKind,
                    CanonicalSubjectId: identity.SubjectId)
            ]));
        required.Add(rootId);

        foreach (var artifact in identity.Artifacts.OrderBy(static artifact => artifact.ArtifactId, StringComparer.OrdinalIgnoreCase))
        {
            var artifactId = NodeId(context.Subject, $"artifact-{SanitizeNodePart(artifact.ArtifactId)}");
            nodes.Add(Node(
                context.Subject,
                artifactId,
                "retained-vault-artifact",
                EvidenceStatusDto.Ready,
                $"Retained {artifact.Kind} artifact {artifact.ArtifactId} is stored at {artifact.RelativePath} ({artifact.SizeBytes} bytes).",
                "EvidenceArtifactStore",
                artifact.RetainedAt,
                artifacts:
                [
                    new EvidenceArtifactRefDto(
                        $"{artifactId}:retained-payload",
                        artifact.Kind,
                        Path: artifact.RelativePath,
                        Route: artifact.SourceRoute,
                        GeneratedAt: artifact.RetainedAt,
                        Hash: artifact.ContentHashSha256,
                        Retained: false,
                        CanonicalSubjectKind: artifact.CanonicalSubjectKind,
                        CanonicalSubjectId: artifact.CanonicalSubjectId)
                ]));
            edges.Add(new EvidenceEdgeDto(rootId, artifactId, "retains", "Vault manifest retains the artifact payload and canonical subject linkage."));
            required.Add(artifactId);
        }

        return new EvidenceContribution(nodes, edges, [], required, []);
    }

    private static string SanitizeNodePart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "artifact"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));
}

internal static class EvidenceContributionHelpers
{
    public static string NodeId(EvidenceSubjectDto subject, string suffix)
        => $"{subject.SubjectKind}:{subject.SubjectId}:{suffix}";

    public static EvidenceNodeDto Node(
        EvidenceSubjectDto subject,
        string evidenceId,
        string kind,
        EvidenceStatusDto status,
        string summary,
        string sourceSystem,
        DateTimeOffset? asOf,
        IReadOnlyList<EvidenceArtifactRefDto>? artifacts = null,
        IReadOnlyList<string>? workItemIds = null)
        => new(
            EvidenceId: evidenceId,
            Subject: subject,
            Kind: kind,
            Status: status,
            Freshness: new EvidenceFreshnessDto(
                AsOf: asOf,
                IsStale: asOf.HasValue && DateTimeOffset.UtcNow - asOf.Value > TimeSpan.FromDays(7),
                Reason: asOf.HasValue && DateTimeOffset.UtcNow - asOf.Value > TimeSpan.FromDays(7)
                    ? "Evidence is older than seven days."
                    : null),
            SourceSystem: sourceSystem,
            Summary: summary,
            ArtifactRefs: artifacts ?? [],
            RelatedWorkItemIds: workItemIds ?? []);

    public static EvidenceArtifactRefDto Artifact(
        string artifactId,
        string kind,
        string? path = null,
        string? route = null,
        DateTimeOffset? generatedAt = null,
        string? hash = null,
        bool retained = false)
        => new(
            ArtifactId: artifactId,
            Kind: kind,
            Path: path,
            Route: route,
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            Hash: hash,
            Retained: retained);

    public static void AddLinkedNode(
        List<EvidenceNodeDto> nodes,
        List<EvidenceEdgeDto> edges,
        List<string>? requiredIds,
        EvidenceSubjectDto subject,
        string parentId,
        string suffix,
        string kind,
        EvidenceStatusDto status,
        string summary,
        string sourceSystem,
        DateTimeOffset? asOf,
        IReadOnlyList<string>? workItemIds = null,
        IReadOnlyList<EvidenceArtifactRefDto>? artifacts = null)
    {
        var nodeId = NodeId(subject, suffix);
        nodes.Add(Node(subject, nodeId, kind, status, summary, sourceSystem, asOf, artifacts, workItemIds));
        edges.Add(new EvidenceEdgeDto(parentId, nodeId, "supports", $"{kind} supports {parentId}."));
        requiredIds?.Add(nodeId);
    }

    public static EvidenceStatusDto MapStatus(TradingAcceptanceGateStatusDto status)
        => status switch
        {
            TradingAcceptanceGateStatusDto.Ready => EvidenceStatusDto.Ready,
            TradingAcceptanceGateStatusDto.Blocked => EvidenceStatusDto.Blocked,
            _ => EvidenceStatusDto.ReviewRequired
        };
}
