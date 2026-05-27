using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Export;
using Meridian.Strategies.Services;
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
                    subject: context.Subject,
                    route: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.Summary.RunId),
                    generatedAt: generatedAt,
                    canonicalSubjectKind: context.Subject.SubjectKind,
                    canonicalSubjectId: context.Subject.SubjectId)
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
                : BuildRunLedgerArtifacts(context.Subject, ledgerId, run.Summary.RunId, run.Ledger.AsOf));

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
        EvidenceSubjectDto subject,
        string ledgerId,
        string runId,
        DateTimeOffset generatedAt)
    {
        return
        [
            Artifact(
                $"{ledgerId}:journal",
                "ledger-journal",
                subject: subject,
                route: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerJournal, "runId", runId),
                generatedAt: generatedAt,
                canonicalSubjectKind: subject.SubjectKind,
                canonicalSubjectId: subject.SubjectId),
            Artifact(
                $"{ledgerId}:trial-balance",
                "ledger-trial-balance",
                subject: subject,
                route: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", runId),
                generatedAt: generatedAt,
                canonicalSubjectKind: subject.SubjectKind,
                canonicalSubjectId: subject.SubjectId)
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
                            subject: context.Subject,
                            path: readiness.ReportPack.ManifestPath,
                            generatedAt: readiness.ReportPack.GeneratedAt ?? readiness.AsOf,
                            canonicalSubjectKind: context.Subject.SubjectKind,
                            canonicalSubjectId: context.Subject.SubjectId)
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
           string.Equals(subject.SubjectKind, EvidenceSubjectResolver.ReconciliationReviewKind, StringComparison.OrdinalIgnoreCase);

    public async Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
    {
        var service = _services.GetService<IReconciliationRunService>();
        if (service is null)
        {
            return new EvidenceContribution([], [], [], [], ["Reconciliation run service is not registered."]);
        }

        var runId = context.Subject.SubjectId;
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
            return new EvidenceContribution([], [], [], [], ["Governance report-pack repository is not registered."]);
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
            "GovernanceReportPackRepository",
            snapshot.GeneratedAt,
            artifacts: snapshot.Artifacts.Select(artifact => Artifact(
                $"{nodeId}:{artifact.ArtifactKind}:{artifact.RelativePath}",
                artifact.ArtifactKind,
                subject: context.Subject,
                path: artifact.RelativePath,
                generatedAt: snapshot.GeneratedAt,
                hash: artifact.ChecksumSha256,
                canonicalSubjectKind: context.Subject.SubjectKind,
                canonicalSubjectId: context.Subject.SubjectId)).ToArray());

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
                        subject: context.Subject,
                        path: readiness.PacketPath,
                        generatedAt: readiness.GeneratedAt ?? DateTimeOffset.UtcNow,
                        canonicalSubjectKind: context.Subject.SubjectKind,
                        canonicalSubjectId: context.Subject.SubjectId)
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
                subject: context.Subject,
                route: $"/api/export/analysis/{Uri.EscapeDataString(profile.Id)}",
                generatedAt: DateTimeOffset.UtcNow,
                canonicalSubjectKind: context.Subject.SubjectKind,
                canonicalSubjectId: context.Subject.SubjectId)).ToArray());

        return Task.FromResult(new EvidenceContribution([node], [], [], [nodeId], []));
    }
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
        EvidenceSubjectDto? subject = null,
        string? path = null,
        string? route = null,
        DateTimeOffset? generatedAt = null,
        string? hash = null,
        bool retained = true,
        string? canonicalSubjectKind = null,
        string? canonicalSubjectId = null)
        => new(
            ArtifactId: artifactId,
            Kind: kind,
            Path: path,
            Route: route,
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            Hash: hash,
            Retained: retained,
            CanonicalSubjectKind: canonicalSubjectKind ?? subject?.SubjectKind,
            CanonicalSubjectId: canonicalSubjectId ?? subject?.SubjectId);

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
