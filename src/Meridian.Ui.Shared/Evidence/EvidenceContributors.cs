using Meridian.Contracts.Api;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
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

        return new EvidenceContribution([node], [], [], [nodeId], snapshot.Warnings);
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
        var requiredEvidence = category.RequiredEvidence is { Count: > 0 }
            ? string.Join(", ", category.RequiredEvidence.Where(static item => !string.IsNullOrWhiteSpace(item)))
            : "required evidence not specified";
        return $"{category.Label}: {category.Status} Required evidence: {requiredEvidence}.";
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
