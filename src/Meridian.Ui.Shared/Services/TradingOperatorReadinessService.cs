using System.Globalization;
using System.Text;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared operator-readiness model consumed by the Trading cockpit.
/// </summary>
public sealed class TradingOperatorReadinessService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TradingOperatorReadinessService> _logger;

    public TradingOperatorReadinessService(
        IServiceProvider services,
        ILogger<TradingOperatorReadinessService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TradingOperatorReadinessDto> GetAsync(Guid? fundAccountId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var asOf = DateTimeOffset.UtcNow;
        var workItems = new List<OperatorWorkItemDto>();

        var paperService = Resolve<PaperSessionPersistenceService>();
        if (paperService is not null)
        {
            await paperService.InitialiseAsync(ct).ConfigureAwait(false);
        }

        var auditEntries = await ResolveAuditEntriesAsync(ct).ConfigureAwait(false);
        var latestRun = await ResolveLatestRunAsync(ct).ConfigureAwait(false);
        var promotionRecords = await ResolvePromotionRecordsAsync(ct).ConfigureAwait(false);
        var promotion = BuildPromotion(latestRun, promotionRecords);
        var paperReadiness = BuildPaperSessionReadiness(paperService, latestRun, auditEntries, workItems);
        var sessions = paperReadiness.Sessions;
        var activeSession = paperReadiness.ActiveSession;
        var replay = paperReadiness.Replay;

        if (auditEntries.Count == 0)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "No audit evidence",
                "No execution audit entries are visible for the paper acceptance lane.",
                OperatorWorkItemToneDto.Warning,
                workItemId: "execution-audit-empty");
        }

        var controls = BuildControls(Resolve<ExecutionOperatorControlService>(), auditEntries);
        var riskRuleStatuses = await ResolveRiskRuleStatusesAsync(ct).ConfigureAwait(false);
        AddExecutionControlWorkItems(workItems, controls, riskRuleStatuses);

        var trustGate = await ResolveTrustGateReadinessAsync(ct).ConfigureAwait(false);
        var reportPack = await ResolveReportPackReadinessAsync(latestRun, promotion, ct).ConfigureAwait(false);
        var reconciliationGate = await ResolveReconciliationGateAsync(latestRun, ct).ConfigureAwait(false);
        AddPromotionGovernanceWorkItems(workItems, latestRun, promotion, trustGate, reportPack, reconciliationGate);

        var brokerageStatus = await ResolveBrokerageStatusAsync(fundAccountId, ct).ConfigureAwait(false);
        AddBrokerageSyncWorkItem(workItems, brokerageStatus);
        AddSecurityMasterCoverageWorkItem(workItems, latestRun);

        var acceptanceGates = BuildAcceptanceGates(
            activeSession,
            sessions,
            latestRun,
            replay,
            controls,
            promotion,
            trustGate,
            reportPack,
            reconciliationGate,
            brokerageStatus,
            riskRuleStatuses,
            auditEntries);
        var overallStatus = EvaluateOverallPosture(acceptanceGates);
        var evidenceCompleteness = BuildEvidenceCompleteness(acceptanceGates, workItems);
        var warnings = BuildWarnings(workItems);
        var snapshotVersion = BuildSnapshotVersion(
            latestRun?.Summary.RunId,
            replay?.VerificationAuditId,
            promotion?.AuditReference,
            trustGate.Status,
            trustGate.OperatorSignoffStatus);

        _logger.LogDebug(
            "Built trading operator readiness snapshot {SnapshotVersion} at {SnapshotMaterializedAt:o} with {OverallStatus}, {WorkItemCount} work item(s), and {WarningCount} warning(s).",
            snapshotVersion,
            asOf,
            overallStatus,
            workItems.Count,
            warnings.Count);

        var readiness = new TradingOperatorReadinessDto(
            AsOf: asOf,
            ActiveSession: activeSession,
            Sessions: sessions,
            Replay: replay,
            Controls: controls,
            Promotion: promotion,
            TrustGate: trustGate,
            BrokerageSync: brokerageStatus,
            WorkItems: workItems
                .OrderByDescending(static item => item.Tone)
                .ThenBy(static item => item.CreatedAt)
                .ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            AcceptanceGates = acceptanceGates,
            EvidenceCompleteness = evidenceCompleteness,
            OverallStatus = overallStatus,
            ReadyForPaperOperation = overallStatus == TradingAcceptanceGateStatusDto.Ready,
            ReportPack = reportPack,
            SnapshotMaterializedAt = asOf,
            SnapshotVersion = snapshotVersion,
            ProviderPromotionChecklist = BuildProviderPromotionChecklist(trustGate, replay, asOf)
        };

        ValidateRequiredReadinessFields(readiness);
        return readiness;
    }

    private static void ValidateRequiredReadinessFields(TradingOperatorReadinessDto readiness)
    {
        if (!Enum.IsDefined(readiness.OverallStatus))
        {
            throw new InvalidOperationException("Trading readiness projection is missing OverallStatus.");
        }

        if (string.IsNullOrWhiteSpace(readiness.SnapshotVersion))
        {
            throw new InvalidOperationException("Trading readiness projection is missing SnapshotVersion.");
        }

        if (readiness.AcceptanceGates is null || readiness.AcceptanceGates.Count == 0)
        {
            throw new InvalidOperationException("Trading readiness projection is missing AcceptanceGates.");
        }

        if (readiness.EvidenceCompleteness is null)
        {
            throw new InvalidOperationException("Trading readiness projection is missing EvidenceCompleteness.");
        }

        foreach (var gate in readiness.AcceptanceGates)
        {
            if (string.IsNullOrWhiteSpace(gate.GateId) || !Enum.IsDefined(gate.Status))
            {
                throw new InvalidOperationException("Trading readiness projection contains an incomplete acceptance gate.");
            }
        }
    }

    private static string BuildSnapshotVersion(
        string? runId,
        string? replayAuditReference,
        string? promotionAuditReference,
        string trustGateStatus,
        string? trustGateOperatorSignoffStatus)
        => string.Join(
            '|',
            runId ?? "none",
            replayAuditReference ?? "none",
            promotionAuditReference ?? "none",
            trustGateStatus,
            trustGateOperatorSignoffStatus ?? "none");

    private sealed record PaperSessionReadiness(
        IReadOnlyList<TradingPaperSessionReadinessDto> Sessions,
        TradingPaperSessionReadinessDto? ActiveSession,
        TradingReplayReadinessDto? Replay);

    private static PaperSessionReadiness BuildPaperSessionReadiness(
        PaperSessionPersistenceService? paperService,
        StrategyRunDetail? latestRun,
        IReadOnlyList<ExecutionAuditEntry> auditEntries,
        ICollection<OperatorWorkItemDto> workItems)
    {
        var sessionSummaries = paperService?.GetSessions() ?? [];
        var sessions = sessionSummaries
            .Select(summary => MapSession(summary, paperService?.GetSession(summary.SessionId)))
            .ToArray();
        var activeSession = SelectActiveSession(sessions, latestRun);
        var replay = BuildReplay(activeSession?.SessionId, auditEntries);

        if (activeSession is null)
        {
            var mismatchDetail = BuildActiveSessionMismatchDetail(sessions, latestRun);
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                mismatchDetail is null ? "No active paper session" : "No matching paper session",
                mismatchDetail
                    ?? "Start or restore a paper session before treating the cockpit as operator-ready.",
                OperatorWorkItemToneDto.Critical,
                latestRun?.Summary.RunId,
                workItemId: mismatchDetail is null
                    ? "paper-session-missing"
                    : BuildWorkItemId("paper-session-mismatch", latestRun?.Summary.RunId ?? latestRun?.Summary.StrategyId));
        }
        else if (replay is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay verification required",
                $"Run replay verification for paper session {activeSession.SessionId}.",
                OperatorWorkItemToneDto.Warning,
                activeSession.SessionId,
                workItemId: BuildWorkItemId("paper-replay-missing", activeSession.SessionId),
                scope: activeSession.SessionId);
        }
        else if (!replay.IsConsistent)
        {
            PrometheusMetrics.RecordRunContinuityStaleProjection("api", "replay-mismatch");
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay mismatch",
                replay.MismatchReasons.FirstOrDefault() ?? $"Replay verification for {replay.SessionId} did not match persisted state.",
                OperatorWorkItemToneDto.Critical,
                replay.SessionId,
                auditReference: replay.VerificationAuditId,
                workItemId: BuildWorkItemId("paper-replay-mismatch", replay.SessionId),
                scope: replay.SessionId);
        }
        else if (IsReplayCoverageStale(activeSession, replay))
        {
            PrometheusMetrics.RecordRunContinuityStaleProjection("api", "replay-stale");
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay verification stale",
                BuildReplayCoverageDetail(activeSession, replay),
                OperatorWorkItemToneDto.Warning,
                replay.SessionId,
                auditReference: replay.VerificationAuditId,
                workItemId: BuildWorkItemId("paper-replay-stale", replay.SessionId),
                scope: replay.SessionId);
        }

        return new PaperSessionReadiness(sessions, activeSession, replay);
    }

    private static void AddExecutionControlWorkItems(
        ICollection<OperatorWorkItemDto> workItems,
        TradingControlReadinessDto controls,
        IReadOnlyList<RiskRuleStatusDto> riskRuleStatuses)
    {
        if (controls.CircuitBreakerOpen)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ExecutionControl,
                "Execution circuit breaker open",
                controls.CircuitBreakerReason ?? "Execution is blocked by an operator control.",
                OperatorWorkItemToneDto.Critical,
                workItemId: "execution-circuit-breaker-open");
        }

        if (controls.ManualOverrideCount > 0)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ExecutionControl,
                "Manual overrides require review",
                $"{controls.ManualOverrideCount} execution manual override(s) must be reviewed before promotion acceptance.",
                OperatorWorkItemToneDto.Warning,
                workItemId: "execution-manual-overrides-open");
        }

        if (controls.UnexplainedEvidenceCount > 0)
        {
            var firstUnexplained = controls.RecentEvidence.FirstOrDefault(static evidence => !evidence.IsExplained);
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ExecutionControl,
                "Risk evidence incomplete",
                controls.ExplainabilityWarnings.FirstOrDefault()
                    ?? "Risk/control audit evidence is missing actor, scope, or rationale.",
                OperatorWorkItemToneDto.Warning,
                firstUnexplained?.RunId,
                auditReference: firstUnexplained?.AuditId,
                workItemId: "execution-evidence-incomplete");
        }

        var constrainedRiskRule = riskRuleStatuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Constrained", StringComparison.OrdinalIgnoreCase));
        if (constrainedRiskRule is not null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ExecutionControl,
                $"{constrainedRiskRule.RuleName} is constraining trading",
                constrainedRiskRule.Summary,
                OperatorWorkItemToneDto.Critical,
                workItemId: BuildWorkItemId("risk-rule-constrained", constrainedRiskRule.RuleName));
            return;
        }

        var observedRiskRule = riskRuleStatuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Observe", StringComparison.OrdinalIgnoreCase));
        if (observedRiskRule is not null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ExecutionControl,
                $"{observedRiskRule.RuleName} needs review",
                observedRiskRule.Summary,
                OperatorWorkItemToneDto.Warning,
                workItemId: BuildWorkItemId("risk-rule-observe", observedRiskRule.RuleName));
        }
    }

    private static void AddPromotionGovernanceWorkItems(
        ICollection<OperatorWorkItemDto> workItems,
        StrategyRunDetail? latestRun,
        TradingPromotionReadinessDto? promotion,
        TradingTrustGateReadinessDto trustGate,
        TradingReportPackReadinessDto reportPack,
        ReconciliationGateEvaluation? reconciliationGate)
    {
        if (promotion is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                "Promotion decision required",
                "Evaluate and record the paper promotion decision before accepting the cockpit.",
                OperatorWorkItemToneDto.Warning,
                latestRun?.Summary.RunId,
                workItemId: BuildWorkItemId("promotion-decision-missing", latestRun?.Summary.RunId));
        }
        else if (promotion.RequiresReview || !IsPromotionTraceComplete(promotion))
        {
            PrometheusMetrics.RecordRunContinuityMissingLineage("api", "promotion-trace-incomplete");
            var missingFields = GetMissingPromotionTraceFields(promotion);
            var hasDecision = !string.IsNullOrWhiteSpace(promotion.ApprovalStatus);
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                hasDecision ? "Promotion trace incomplete" : "Promotion decision required",
                hasDecision
                    ? $"Promotion evidence is incomplete. Missing: {string.Join(", ", missingFields)}."
                    : $"Record the paper promotion decision before accepting the cockpit. Missing: {string.Join(", ", missingFields)}.",
                hasDecision ? OperatorWorkItemToneDto.Critical : OperatorWorkItemToneDto.Warning,
                promotion.SourceRunId ?? promotion.TargetRunId,
                auditReference: promotion.AuditReference,
                workItemId: BuildWorkItemId("promotion-trace-incomplete", promotion.SourceRunId ?? promotion.TargetRunId));
        }

        AddTrustGateWorkItem(workItems, trustGate);
        AddReportPackWorkItem(workItems, reportPack, latestRun?.Summary.RunId);
        if (reportPack.Status != TradingAcceptanceGateStatusDto.Ready)
        {
            PrometheusMetrics.RecordRunContinuityMissingLineage("api", "report-pack-lineage");
        }

        AddReconciliationGateWorkItem(workItems, reconciliationGate, latestRun?.Summary.RunId);
    }

    private static void AddBrokerageSyncWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        WorkstationBrokerageSyncStatusDto? brokerageStatus)
    {
        if (brokerageStatus is null || brokerageStatus.Health is WorkstationBrokerageSyncHealth.Healthy)
        {
            return;
        }

        AddWorkItem(
            workItems,
            OperatorWorkItemKindDto.BrokerageSync,
            "Brokerage sync attention",
            brokerageStatus.Warnings.FirstOrDefault()
                ?? brokerageStatus.LastError
                ?? "Brokerage sync is not healthy.",
            brokerageStatus.Health is WorkstationBrokerageSyncHealth.Failed
                ? OperatorWorkItemToneDto.Critical
                : OperatorWorkItemToneDto.Warning,
            fundAccountId: brokerageStatus.FundAccountId,
            workItemId: BuildWorkItemId("brokerage-sync-attention", brokerageStatus.FundAccountId.ToString("N")),
            workspaceOverride: "Settings",
            targetRouteOverride: ProviderNavigationRouteMapper.ResolveProviderConnectionSettingsRoute(brokerageStatus.ProviderId),
            targetPageTagOverride: "ProviderConnectionCenter");
    }

    private static void AddSecurityMasterCoverageWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        StrategyRunDetail? latestRun)
    {
        if (latestRun is null)
        {
            return;
        }

        var missingSecurityCount =
            (latestRun.Portfolio?.SecurityMissingCount ?? 0)
            + (latestRun.Ledger?.SecurityMissingCount ?? 0);
        if (missingSecurityCount == 0)
        {
            return;
        }

        AddWorkItem(
            workItems,
            OperatorWorkItemKindDto.SecurityMasterCoverage,
            "Security Master coverage gap",
            $"{missingSecurityCount} run security reference(s) are missing coverage.",
            OperatorWorkItemToneDto.Warning,
            latestRun.Summary.RunId,
            workItemId: BuildWorkItemId("security-master-coverage-gap", latestRun.Summary.RunId));
    }

    private static IReadOnlyList<string> BuildWarnings(IReadOnlyList<OperatorWorkItemDto> workItems)
        => workItems
            .Where(static item => item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical)
            .Select(static item => item.Detail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<StrategyRunDetail?> ResolveLatestRunAsync(CancellationToken ct)
    {
        var readService = Resolve<StrategyRunReadService>();
        if (readService is null)
        {
            return null;
        }

        var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
        var latest = runs.FirstOrDefault();
        return latest is null
            ? null
            : await readService.GetRunDetailAsync(latest.RunId, ct).ConfigureAwait(false);
    }

    private async Task<WorkstationBrokerageSyncStatusDto?> ResolveBrokerageStatusAsync(
        Guid? fundAccountId,
        CancellationToken ct)
    {
        if (!fundAccountId.HasValue)
        {
            return null;
        }

        var syncService = Resolve<BrokeragePortfolioSyncService>();
        return syncService is null
            ? null
            : await syncService.GetStatusAsync(fundAccountId.Value, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ExecutionAuditEntry>> ResolveAuditEntriesAsync(CancellationToken ct)
    {
        var auditTrail = Resolve<ExecutionAuditTrailService>();
        return auditTrail is null
            ? Array.Empty<ExecutionAuditEntry>()
            : await auditTrail.GetRecentAsync(100, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RiskRuleStatusDto>> ResolveRiskRuleStatusesAsync(CancellationToken ct)
    {
        var runtime = Resolve<RiskRuleRuntimeService>();
        if (runtime is null)
        {
            _logger.LogWarning("RiskRuleRuntimeService is not registered; risk rule statuses will be unavailable.");
            return Array.Empty<RiskRuleStatusDto>();
        }

        return await runtime.GetAllStatusesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<StrategyPromotionRecord>> ResolvePromotionRecordsAsync(CancellationToken ct)
    {
        var promotionService = Resolve<PromotionService>();
        return promotionService is null
            ? Array.Empty<StrategyPromotionRecord>()
            : await promotionService.GetPromotionHistoryAsync(ct).ConfigureAwait(false);
    }

    private async Task<TradingTrustGateReadinessDto> ResolveTrustGateReadinessAsync(CancellationToken ct)
    {
        var trustGateService = Resolve<Dk1TrustGateReadinessService>();
        return trustGateService is null
            ? Dk1TrustGateReadinessService.CreateUnavailable("DK1 trust-gate packet service is not registered.")
            : await trustGateService.GetCurrentAsync(ct).ConfigureAwait(false);
    }

    private async Task<TradingReportPackReadinessDto> ResolveReportPackReadinessAsync(
        StrategyRunDetail? latestRun,
        TradingPromotionReadinessDto? promotion,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (latestRun is null)
        {
            return new TradingReportPackReadinessDto(
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "A retained strategy run is required before a governed report pack can satisfy readiness.",
                FundProfileId: null,
                ReportId: null,
                GeneratedAt: null,
                RelatedRunIds: [],
                ArtifactCount: 0,
                WarningCount: 0,
                ManifestPath: null);
        }

        var candidateRunIds = BuildReportPackRunCandidates(latestRun, promotion);
        var primaryRunId = candidateRunIds[0];
        var fundProfileId = latestRun.Summary.FundProfileId;
        var repository = Resolve<IGovernanceReportPackRepository>();
        if (repository is null)
        {
            return new TradingReportPackReadinessDto(
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: string.IsNullOrWhiteSpace(fundProfileId)
                    ? $"Governance report-pack repository is not registered, and latest run {primaryRunId} has no fund profile."
                    : $"Governance report-pack repository is not registered for fund {fundProfileId}.",
                FundProfileId: fundProfileId,
                ReportId: null,
                GeneratedAt: null,
                RelatedRunIds: [],
                ArtifactCount: 0,
                WarningCount: 0,
                ManifestPath: null);
        }

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            var history = await repository.GetHistoryAsync(fundProfileId, limit: 10, ct).ConfigureAwait(false);
            foreach (var item in history.OrderByDescending(static item => item.GeneratedAt))
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = await repository.GetAsync(item.ReportId, ct).ConfigureAwait(false);
                if (snapshot is null)
                {
                    continue;
                }

                var readiness = BuildReportPackReadinessForRuns(snapshot, candidateRunIds, item.RelativeManifestPath);
                if (readiness is not null)
                {
                    return readiness;
                }
            }
        }

        foreach (var runId in candidateRunIds)
        {
            var runLinkedSnapshot = await repository.FindLatestByRunIdAsync(runId, ct).ConfigureAwait(false);
            if (runLinkedSnapshot is not null)
            {
                return BuildReportPackReadinessForRuns(runLinkedSnapshot, candidateRunIds, manifestPath: null)!;
            }
        }

        return new TradingReportPackReadinessDto(
            Status: TradingAcceptanceGateStatusDto.ReviewRequired,
            Detail: string.IsNullOrWhiteSpace(fundProfileId)
                ? $"No governed report pack links to latest run {primaryRunId}."
                : $"No governed report pack links fund {fundProfileId} to latest run {primaryRunId}.",
            FundProfileId: fundProfileId,
            ReportId: null,
            GeneratedAt: null,
            RelatedRunIds: [],
            ArtifactCount: 0,
            WarningCount: 0,
            ManifestPath: null);
    }

    private static IReadOnlyList<string> BuildReportPackRunCandidates(
        StrategyRunDetail latestRun,
        TradingPromotionReadinessDto? promotion)
        => new[]
            {
                latestRun.Summary.RunId,
                latestRun.Summary.ParentRunId,
                promotion?.SourceRunId,
                promotion?.TargetRunId
            }
            .Where(static runId => !string.IsNullOrWhiteSpace(runId))
            .Select(static runId => runId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static TradingReportPackReadinessDto? BuildReportPackReadinessForRuns(
        FundReportPackSnapshotDto snapshot,
        IReadOnlyList<string> candidateRunIds,
        string? manifestPath)
    {
        var relatedRunIds = snapshot.Provenance.RelatedRunIds;
        var matchedRunId = candidateRunIds.FirstOrDefault(runId =>
            relatedRunIds.Contains(runId, StringComparer.OrdinalIgnoreCase));
        if (matchedRunId is null)
        {
            return null;
        }

        var status = snapshot.Warnings.Count == 0 && snapshot.Artifacts.Count > 0
            ? TradingAcceptanceGateStatusDto.Ready
            : TradingAcceptanceGateStatusDto.ReviewRequired;
        var detail = status == TradingAcceptanceGateStatusDto.Ready
            ? $"Report pack {snapshot.ReportId:D} retains {snapshot.Artifacts.Count} artifact(s) and links to run {matchedRunId}."
            : $"Report pack {snapshot.ReportId:D} links to run {matchedRunId}, but {snapshot.Warnings.Count} warning(s) require review.";

        return new TradingReportPackReadinessDto(
            Status: status,
            Detail: detail,
            FundProfileId: snapshot.FundProfileId,
            ReportId: snapshot.ReportId,
            GeneratedAt: snapshot.GeneratedAt,
            RelatedRunIds: relatedRunIds,
            ArtifactCount: snapshot.Artifacts.Count,
            WarningCount: snapshot.Warnings.Count,
            ManifestPath: manifestPath);
    }

    private static TradingReplayReadinessDto? BuildReplay(
        string? sessionId,
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
    {
        var replayAudit = auditEntries.FirstOrDefault(entry =>
            (string.Equals(entry.Action, "ReplayPaperSession", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.Action, "VerifyReplay", StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(sessionId) ||
             string.Equals(GetMetadata(entry, "sessionId"), sessionId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.CorrelationId, sessionId, StringComparison.OrdinalIgnoreCase)));

        if (replayAudit is null)
        {
            return null;
        }

        var mismatchCount = ParseIntMetadata(replayAudit, "mismatchCount") ?? 0;
        var isConsistent = ParseBoolMetadata(replayAudit, "isConsistent")
            ?? (mismatchCount == 0 && !string.Equals(replayAudit.Outcome, "AttentionRequired", StringComparison.OrdinalIgnoreCase));

        return new TradingReplayReadinessDto(
            SessionId: GetMetadata(replayAudit, "sessionId") ?? sessionId ?? replayAudit.CorrelationId ?? string.Empty,
            ReplaySource: GetMetadata(replayAudit, "replaySource") ?? "ExecutionAudit",
            IsConsistent: isConsistent,
            ComparedFillCount: ParseIntMetadata(replayAudit, "comparedFillCount") ?? 0,
            ComparedOrderCount: ParseIntMetadata(replayAudit, "comparedOrderCount") ?? 0,
            ComparedLedgerEntryCount: ParseIntMetadata(replayAudit, "comparedLedgerEntryCount") ?? 0,
            VerifiedAt: replayAudit.OccurredAt,
            LastPersistedFillAt: ParseDateTimeOffsetMetadata(replayAudit, "lastPersistedFillAt"),
            LastPersistedOrderUpdateAt: ParseDateTimeOffsetMetadata(replayAudit, "lastPersistedOrderUpdateAt"),
            VerificationAuditId: replayAudit.AuditId,
            MismatchReasons: BuildReplayMismatchReasons(isConsistent, replayAudit));
    }

    private static IReadOnlyList<string> BuildReplayMismatchReasons(
        bool isConsistent,
        ExecutionAuditEntry replayAudit)
    {
        if (isConsistent)
        {
            return [];
        }

        return
        [
            GetMetadata(replayAudit, "primaryMismatchReason")
                ?? replayAudit.Message
                ?? "Replay verification recorded a mismatch."
        ];
    }

    private TradingControlReadinessDto BuildControls(
        ExecutionOperatorControlService? controlService,
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
    {
        var evidence = BuildControlEvidence(auditEntries);
        var unexplained = evidence.Where(static item => !item.IsExplained).ToArray();
        var explainabilityWarnings = unexplained
            .Take(3)
            .Select(static item =>
                $"{item.Action} audit {item.AuditId} is missing {string.Join(", ", item.MissingFields)}.")
            .ToArray();

        if (controlService is null)
        {
            return new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: null)
            {
                RecentEvidence = evidence,
                ExplainableEvidenceCount = evidence.Count - unexplained.Length,
                UnexplainedEvidenceCount = unexplained.Length,
                ExplainabilityWarnings = explainabilityWarnings
            };
        }

        var snapshot = controlService.GetSnapshot();
        return new TradingControlReadinessDto(
            CircuitBreakerOpen: snapshot.CircuitBreaker.IsOpen,
            CircuitBreakerReason: snapshot.CircuitBreaker.Reason,
            CircuitBreakerChangedBy: snapshot.CircuitBreaker.ChangedBy,
            CircuitBreakerChangedAt: snapshot.CircuitBreaker.ChangedAt,
            ManualOverrideCount: snapshot.ManualOverrides.Count,
            SymbolLimitCount: snapshot.SymbolPositionLimits.Count,
            DefaultMaxPositionSize: snapshot.DefaultMaxPositionSize)
        {
            RecentEvidence = evidence,
            ExplainableEvidenceCount = evidence.Count - unexplained.Length,
            UnexplainedEvidenceCount = unexplained.Length,
            ExplainabilityWarnings = explainabilityWarnings
        };
    }

    private static IReadOnlyList<TradingControlEvidenceDto> BuildControlEvidence(
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
        => auditEntries
            .Where(IsControlEvidence)
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(10)
            .Select(MapControlEvidence)
            .ToArray();

    private static bool IsControlEvidence(ExecutionAuditEntry entry)
    {
        if (string.Equals(entry.Category, "Control", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(entry.Category, "Order", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(entry.Action, "OrderRejected", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Action, "GatewayConnectFailed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.Action, "OrderSubmitted", StringComparison.OrdinalIgnoreCase);
    }

    private static TradingControlEvidenceDto MapControlEvidence(ExecutionAuditEntry entry)
    {
        var actor = string.IsNullOrWhiteSpace(entry.Actor) ? null : entry.Actor.Trim();
        var scope = ResolveEvidenceScope(entry);
        var reason = ResolveEvidenceReason(entry);
        var missingFields = new List<string>(capacity: 3);

        if (string.IsNullOrWhiteSpace(actor))
        {
            missingFields.Add("actor");
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            missingFields.Add("scope");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            missingFields.Add("reason");
        }

        return new TradingControlEvidenceDto(
            AuditId: entry.AuditId,
            Category: entry.Category,
            Action: entry.Action,
            Outcome: entry.Outcome,
            OccurredAt: entry.OccurredAt,
            Actor: actor,
            Scope: string.IsNullOrWhiteSpace(scope) ? "unscoped" : scope,
            Reason: string.IsNullOrWhiteSpace(reason) ? "No rationale was recorded." : reason,
            IsExplained: missingFields.Count == 0,
            MissingFields: missingFields,
            RunId: entry.RunId,
            Symbol: entry.Symbol,
            OrderId: entry.OrderId,
            CorrelationId: entry.CorrelationId);
    }

    private static string? ResolveEvidenceScope(ExecutionAuditEntry entry)
    {
        // Prefer the explicit Scope field first.
        if (!string.IsNullOrWhiteSpace(entry.Scope))
        {
            return entry.Scope.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.RunId) && !string.IsNullOrWhiteSpace(entry.Symbol))
        {
            return $"run:{entry.RunId}/symbol:{entry.Symbol}";
        }

        if (!string.IsNullOrWhiteSpace(entry.RunId))
        {
            return $"run:{entry.RunId}";
        }

        if (!string.IsNullOrWhiteSpace(entry.Symbol))
        {
            return $"symbol:{entry.Symbol}";
        }

        if (!string.IsNullOrWhiteSpace(entry.OrderId))
        {
            return $"order:{entry.OrderId}";
        }

        var overrideId = GetMetadata(entry, "overrideId");
        if (!string.IsNullOrWhiteSpace(overrideId))
        {
            return $"override:{overrideId}";
        }

        if (entry.Action.Contains("CircuitBreaker", StringComparison.OrdinalIgnoreCase))
        {
            return "global-circuit-breaker";
        }

        if (entry.Action.Contains("DefaultPositionLimit", StringComparison.OrdinalIgnoreCase))
        {
            return "default-position-limit";
        }

        if (entry.Action.Contains("SymbolPositionLimit", StringComparison.OrdinalIgnoreCase))
        {
            return "symbol-position-limit";
        }

        return string.IsNullOrWhiteSpace(entry.CorrelationId) ? null : $"correlation:{entry.CorrelationId}";
    }

    private static string? ResolveEvidenceReason(ExecutionAuditEntry entry)
    {
        // Prefer the explicit Reason field first.
        if (!string.IsNullOrWhiteSpace(entry.Reason))
        {
            return entry.Reason.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.Message))
        {
            return entry.Message.Trim();
        }

        var explicitReason =
            GetMetadata(entry, "reason")
            ?? GetMetadata(entry, "rationale")
            ?? GetMetadata(entry, "approvalReason")
            ?? GetMetadata(entry, "rejectionReason");
        if (!string.IsNullOrWhiteSpace(explicitReason))
        {
            return explicitReason.Trim();
        }

        if (string.Equals(entry.Category, "Order", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var kind = GetMetadata(entry, "kind");
        return string.IsNullOrWhiteSpace(kind)
            ? null
            : $"{entry.Action} for {kind}.";
    }

    private static TradingPromotionReadinessDto? BuildPromotion(
        StrategyRunDetail? latestRun,
        IReadOnlyList<StrategyPromotionRecord> promotionRecords)
    {
        var record = latestRun is null
            ? promotionRecords.FirstOrDefault()
            : promotionRecords.FirstOrDefault(candidate =>
                IsPromotionRecordLinkedToRun(candidate, latestRun.Summary.RunId));

        if (record is not null)
        {
            return new TradingPromotionReadinessDto(
                State: record.Decision,
                Reason: record.ApprovalReason ?? record.ReviewNotes ?? "Promotion decision recorded.",
                RequiresReview: !IsPromotionRecordTraceComplete(record),
                SourceRunId: record.SourceRunId,
                TargetRunId: record.TargetRunId,
                SuggestedNextMode: record.TargetRunType.ToString(),
                AuditReference: record.AuditReference,
                ApprovalStatus: record.Decision,
                ManualOverrideId: record.ManualOverrideId,
                ApprovedBy: record.ApprovedBy,
                ApprovalChecklist: record.ApprovalChecklist ?? []);
        }

        var promotion = latestRun?.Promotion ?? latestRun?.Summary.Promotion;
        return promotion is null
            ? null
            : new TradingPromotionReadinessDto(
                State: promotion.State.ToString(),
                Reason: promotion.Reason,
                RequiresReview: promotion.RequiresReview,
                SourceRunId: promotion.SourceRunId ?? latestRun?.Summary.RunId,
                TargetRunId: promotion.TargetRunId,
                SuggestedNextMode: promotion.SuggestedNextMode?.ToString(),
                AuditReference: promotion.AuditReference,
                ApprovalStatus: promotion.ApprovalStatus,
                ManualOverrideId: promotion.ManualOverrideId,
                ApprovedBy: promotion.ApprovedBy,
                ApprovalChecklist: promotion.ApprovalChecklist ?? []);
    }

    private static bool IsPromotionRecordLinkedToRun(StrategyPromotionRecord record, string runId) =>
        string.Equals(record.SourceRunId, runId, StringComparison.Ordinal) ||
        string.Equals(record.TargetRunId, runId, StringComparison.Ordinal);

    private static bool IsPromotionRecordTraceComplete(StrategyPromotionRecord record) =>
        !string.IsNullOrWhiteSpace(record.Decision) &&
        !string.IsNullOrWhiteSpace(record.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(record.ApprovalReason) &&
        HasApprovalChecklist(record.ApprovalChecklist) &&
        !string.IsNullOrWhiteSpace(record.SourceRunId) &&
        !string.IsNullOrWhiteSpace(record.AuditReference);

    private static bool IsPromotionTraceComplete(TradingPromotionReadinessDto? promotion) =>
        GetMissingPromotionTraceFields(promotion).Count == 0;

    private static IReadOnlyList<string> GetMissingPromotionTraceFields(TradingPromotionReadinessDto? promotion)
    {
        if (promotion is null)
        {
            return ["promotion"];
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(promotion.ApprovalStatus))
        {
            missing.Add("decision");
        }

        if (string.IsNullOrWhiteSpace(promotion.ApprovedBy))
        {
            missing.Add("operator");
        }

        if (string.IsNullOrWhiteSpace(promotion.Reason))
        {
            missing.Add("rationale");
        }

        if (!HasApprovalChecklist(promotion.ApprovalChecklist))
        {
            missing.Add("checklist");
        }

        if (string.IsNullOrWhiteSpace(promotion.SourceRunId))
        {
            missing.Add("sourceRunId");
        }

        if (string.Equals(promotion.ApprovalStatus, PromotionDecisionKinds.Approved, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(promotion.TargetRunId))
        {
            missing.Add("targetRunId");
        }

        if (string.Equals(promotion.ApprovalStatus, PromotionDecisionKinds.Rejected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(promotion.TargetRunId))
        {
            missing.Add("targetRunId must be empty for rejected decisions");
        }

        if (string.IsNullOrWhiteSpace(promotion.AuditReference))
        {
            missing.Add("auditReference");
        }

        return missing;
    }

    private static bool HasApprovalChecklist(IReadOnlyList<string>? approvalChecklist)
        => approvalChecklist is { Count: > 0 } &&
           approvalChecklist.All(static item => !string.IsNullOrWhiteSpace(item));

    private static TradingPaperSessionReadinessDto? SelectActiveSession(
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions,
        StrategyRunDetail? latestRun)
    {
        var expectedStrategyId = latestRun?.Summary.StrategyId;
        if (string.IsNullOrWhiteSpace(expectedStrategyId))
        {
            return sessions.FirstOrDefault(static session => session.IsActive);
        }

        foreach (var session in sessions)
        {
            if (session.IsActive &&
                string.Equals(session.StrategyId, expectedStrategyId, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    private static string? BuildActiveSessionMismatchDetail(
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions,
        StrategyRunDetail? latestRun)
    {
        var expectedStrategyId = latestRun?.Summary.StrategyId;
        if (string.IsNullOrWhiteSpace(expectedStrategyId) || sessions.All(static session => !session.IsActive))
        {
            return null;
        }

        return $"Active paper session(s) exist, but none match latest run {latestRun!.Summary.RunId} for strategy {expectedStrategyId}. Start or restore the promoted strategy session before accepting cockpit readiness.";
    }

    private static IReadOnlyList<TradingAcceptanceGateDto> BuildAcceptanceGates(
        TradingPaperSessionReadinessDto? activeSession,
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions,
        StrategyRunDetail? latestRun,
        TradingReplayReadinessDto? replay,
        TradingControlReadinessDto controls,
        TradingPromotionReadinessDto? promotion,
        TradingTrustGateReadinessDto trustGate,
        TradingReportPackReadinessDto reportPack,
        ReconciliationGateEvaluation? reconciliationGate,
        WorkstationBrokerageSyncStatusDto? brokerageStatus,
        IReadOnlyList<RiskRuleStatusDto> riskRuleStatuses,
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
    {
        var gates = new List<TradingAcceptanceGateDto>
        {
            BuildSessionGate(activeSession, sessions, latestRun),
            BuildReplayGate(activeSession, replay),
            BuildAuditControlGate(controls, auditEntries)
        };

        gates.Add(BuildRiskRuleGate(riskRuleStatuses));
        gates.Add(BuildPromotionGate(promotion));
        gates.Add(BuildTrustGateAcceptance(trustGate));
        gates.Add(BuildReportPackGate(reportPack));
        gates.Add(BuildReconciliationGate(reconciliationGate));
        gates.Add(BuildBrokerageSyncGate(brokerageStatus));
        return gates.Select(AttachExplainability).ToArray();
    }

    private static TradingAcceptanceGateDto AttachExplainability(TradingAcceptanceGateDto gate)
    {
        var requiredNextAction = gate.RequiredNextAction;
        if (string.IsNullOrWhiteSpace(requiredNextAction))
        {
            requiredNextAction = gate.Status switch
            {
                TradingAcceptanceGateStatusDto.Ready => "No immediate action required.",
                TradingAcceptanceGateStatusDto.ReviewRequired => "Review the gate evidence and resolve outstanding operator actions.",
                TradingAcceptanceGateStatusDto.Blocked => "Resolve blocking evidence before accepting cockpit readiness.",
                _ => "Collect gate evidence and re-evaluate readiness."
            };
        }

        return gate with
        {
            Reason = string.IsNullOrWhiteSpace(gate.Reason) ? gate.Detail : gate.Reason,
            LastEvidenceAt = gate.LastEvidenceAt ?? DateTimeOffset.UtcNow,
            RequiredNextAction = requiredNextAction
        };
    }


    private static TradingAcceptanceGateDto BuildBrokerageSyncGate(WorkstationBrokerageSyncStatusDto? brokerageStatus)
    {
        if (brokerageStatus is null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "brokerage-sync",
                Label: "Brokerage sync",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: "Brokerage sync gate is not account-scoped for this readiness query.");
        }

        var status = brokerageStatus.Health switch
        {
            WorkstationBrokerageSyncHealth.Healthy => TradingAcceptanceGateStatusDto.Ready,
            WorkstationBrokerageSyncHealth.Degraded => TradingAcceptanceGateStatusDto.ReviewRequired,
            WorkstationBrokerageSyncHealth.Failed => TradingAcceptanceGateStatusDto.Blocked,
            _ => TradingAcceptanceGateStatusDto.ReviewRequired
        };

        return new TradingAcceptanceGateDto(
            GateId: "brokerage-sync",
            Label: "Brokerage sync",
            Status: status,
            Detail: brokerageStatus.Warnings.FirstOrDefault()
                ?? brokerageStatus.LastError
                ?? $"Brokerage sync health is {brokerageStatus.Health}.");
    }

    private static TradingAcceptanceGateDto BuildSessionGate(
        TradingPaperSessionReadinessDto? activeSession,
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions,
        StrategyRunDetail? latestRun)
    {
        if (activeSession is { IsActive: true })
        {
            return new TradingAcceptanceGateDto(
                GateId: "session",
                Label: "Session active",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: $"Active paper session {activeSession.SessionId} retains {activeSession.OrderCount} order(s) and {activeSession.PositionCount} position(s).",
                SessionId: activeSession.SessionId);
        }

        var mismatchDetail = BuildActiveSessionMismatchDetail(sessions, latestRun);
        if (mismatchDetail is not null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "session",
                Label: "Session active",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: mismatchDetail,
                RunId: latestRun?.Summary.RunId);
        }

        if (sessions.Count > 0)
        {
            return new TradingAcceptanceGateDto(
                GateId: "session",
                Label: "Session restore",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "Restore a retained paper session before treating the cockpit as operator-ready.",
                SessionId: sessions[0].SessionId);
        }

        return new TradingAcceptanceGateDto(
            GateId: "session",
            Label: "Session active",
            Status: TradingAcceptanceGateStatusDto.Blocked,
            Detail: "Create a paper session so orders, fills, and portfolio state have a durable acceptance scope.");
    }

    private static TradingAcceptanceGateDto BuildReplayGate(
        TradingPaperSessionReadinessDto? activeSession,
        TradingReplayReadinessDto? replay)
    {
        if (activeSession is not { IsActive: true })
        {
            return new TradingAcceptanceGateDto(
                GateId: "replay",
                Label: "Replay verified",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: "An active paper session is required before replay evidence can satisfy cockpit readiness.",
                SessionId: activeSession?.SessionId,
                AuditReference: replay?.VerificationAuditId);
        }

        if (replay is null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "replay",
                Label: "Replay verified",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: $"Run replay verification for paper session {activeSession.SessionId}.",
                SessionId: activeSession.SessionId);
        }

        if (!replay.IsConsistent)
        {
            return new TradingAcceptanceGateDto(
                GateId: "replay",
                Label: "Replay verified",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: replay.MismatchReasons.FirstOrDefault() ?? "Replay verification recorded a mismatch.",
                SessionId: replay.SessionId,
                AuditReference: replay.VerificationAuditId);
        }

        if (IsReplayCoverageStale(activeSession, replay))
        {
            return new TradingAcceptanceGateDto(
                GateId: "replay",
                Label: "Replay verified",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: BuildReplayCoverageDetail(activeSession, replay),
                SessionId: replay.SessionId,
                AuditReference: replay.VerificationAuditId);
        }

        return new TradingAcceptanceGateDto(
            GateId: "replay",
            Label: "Replay verified",
            Status: TradingAcceptanceGateStatusDto.Ready,
            Detail: $"Compared {replay.ComparedFillCount} fill(s), {replay.ComparedOrderCount} order(s), and {replay.ComparedLedgerEntryCount} ledger entr{(replay.ComparedLedgerEntryCount == 1 ? "y" : "ies")}.",
            SessionId: replay.SessionId,
            AuditReference: replay.VerificationAuditId);
    }

    private static bool IsReplayCoverageStale(
        TradingPaperSessionReadinessDto activeSession,
        TradingReplayReadinessDto replay)
        => !string.Equals(activeSession.SessionId, replay.SessionId, StringComparison.OrdinalIgnoreCase) ||
           activeSession.FillCount != replay.ComparedFillCount ||
           activeSession.OrderCount != replay.ComparedOrderCount ||
           activeSession.LedgerEntryCount != replay.ComparedLedgerEntryCount;

    private static string BuildReplayCoverageDetail(
        TradingPaperSessionReadinessDto activeSession,
        TradingReplayReadinessDto replay)
    {
        if (!string.Equals(activeSession.SessionId, replay.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return $"Replay verification covers session {replay.SessionId}, but the active paper session is {activeSession.SessionId}.";
        }

        var differences = new List<string>(capacity: 3);
        if (activeSession.FillCount != replay.ComparedFillCount)
        {
            differences.Add($"fills active={activeSession.FillCount}, verified={replay.ComparedFillCount}");
        }

        if (activeSession.OrderCount != replay.ComparedOrderCount)
        {
            differences.Add($"orders active={activeSession.OrderCount}, verified={replay.ComparedOrderCount}");
        }

        if (activeSession.LedgerEntryCount != replay.ComparedLedgerEntryCount)
        {
            differences.Add($"ledger active={activeSession.LedgerEntryCount}, verified={replay.ComparedLedgerEntryCount}");
        }

        return differences.Count == 0
            ? $"Replay verification for paper session {activeSession.SessionId} is no longer aligned with the active session."
            : $"Replay verification for paper session {activeSession.SessionId} is stale ({string.Join("; ", differences)}). Run replay verification again before accepting cockpit readiness.";
    }

    private static TradingAcceptanceGateDto BuildAuditControlGate(
        TradingControlReadinessDto controls,
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
    {
        if (controls.CircuitBreakerOpen)
        {
            return new TradingAcceptanceGateDto(
                GateId: "audit-controls",
                Label: "Risk state explainable",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: controls.CircuitBreakerReason ?? "Execution is blocked by an open circuit breaker.");
        }

        if (auditEntries.Count == 0)
        {
            return new TradingAcceptanceGateDto(
                GateId: "audit-controls",
                Label: "Risk state explainable",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "Execution actions need visible audit and control evidence before daily operation.");
        }

        if (controls.UnexplainedEvidenceCount > 0)
        {
            var firstUnexplained = controls.RecentEvidence.FirstOrDefault(static evidence => !evidence.IsExplained);
            return new TradingAcceptanceGateDto(
                GateId: "audit-controls",
                Label: "Risk state explainable",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: $"{controls.UnexplainedEvidenceCount} risk/control audit entr{(controls.UnexplainedEvidenceCount == 1 ? "y is" : "ies are")} missing actor, scope, or rationale.",
                RunId: firstUnexplained?.RunId,
                AuditReference: firstUnexplained?.AuditId);
        }

        if (controls.ManualOverrideCount > 0)
        {
            return new TradingAcceptanceGateDto(
                GateId: "audit-controls",
                Label: "Risk state explainable",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: $"{controls.ManualOverrideCount} manual override(s) require operator review; {auditEntries.Count} audit entr{(auditEntries.Count == 1 ? "y is" : "ies are")} visible.");
        }

        return new TradingAcceptanceGateDto(
            GateId: "audit-controls",
            Label: "Risk state explainable",
            Status: TradingAcceptanceGateStatusDto.Ready,
            Detail: $"{auditEntries.Count} execution audit entr{(auditEntries.Count == 1 ? "y is" : "ies are")} visible and no blocking controls are active.");
    }

    private static TradingAcceptanceGateDto BuildRiskRuleGate(IReadOnlyList<RiskRuleStatusDto> statuses)
    {
        if (statuses.Count == 0)
        {
            return new TradingAcceptanceGateDto(
                GateId: "risk-rules",
                Label: "Risk rules healthy",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "Runtime risk-rule status is unavailable.");
        }

        var constrained = statuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Constrained", StringComparison.OrdinalIgnoreCase));
        if (constrained is not null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "risk-rules",
                Label: "Risk rules healthy",
                Status: TradingAcceptanceGateStatusDto.Blocked,
                Detail: $"{constrained.RuleName}: {constrained.Summary}");
        }

        var observed = statuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Observe", StringComparison.OrdinalIgnoreCase));
        if (observed is not null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "risk-rules",
                Label: "Risk rules healthy",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: $"{observed.RuleName}: {observed.Summary}");
        }

        return new TradingAcceptanceGateDto(
            GateId: "risk-rules",
            Label: "Risk rules healthy",
            Status: TradingAcceptanceGateStatusDto.Ready,
            Detail: "All runtime risk rules report healthy status.");
    }

    private static TradingAcceptanceGateDto BuildPromotionGate(TradingPromotionReadinessDto? promotion)
    {
        if (promotion is null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "promotion",
                Label: "Promotion trace complete",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "Evaluate and record the paper promotion decision before accepting the cockpit.");
        }

        if (!promotion.RequiresReview && IsPromotionTraceComplete(promotion))
        {
            return new TradingAcceptanceGateDto(
                GateId: "promotion",
                Label: "Promotion trace complete",
                Status: TradingAcceptanceGateStatusDto.Ready,
                Detail: $"{promotion.ApprovalStatus} by {promotion.ApprovedBy}: {promotion.Reason}.",
                RunId: promotion.SourceRunId ?? promotion.TargetRunId,
                AuditReference: promotion.AuditReference);
        }

        return new TradingAcceptanceGateDto(
            GateId: "promotion",
            Label: "Promotion trace complete",
            Status: string.IsNullOrWhiteSpace(promotion.ApprovalStatus)
                ? TradingAcceptanceGateStatusDto.ReviewRequired
                : TradingAcceptanceGateStatusDto.Blocked,
            Detail: string.IsNullOrWhiteSpace(promotion.ApprovalStatus)
                ? $"Promotion decision is pending. Missing: {string.Join(", ", GetMissingPromotionTraceFields(promotion))}."
                : $"Promotion evidence is incomplete. Missing: {string.Join(", ", GetMissingPromotionTraceFields(promotion))}.",
            RunId: promotion.SourceRunId ?? promotion.TargetRunId,
            AuditReference: promotion.AuditReference);
    }

    private static TradingAcceptanceGateDto BuildTrustGateAcceptance(TradingTrustGateReadinessDto trustGate)
    {
        var status = ResolveTrustGateAcceptanceStatus(trustGate);
        return new TradingAcceptanceGateDto(
            GateId: "dk1-trust",
            Label: "DK1 trust gate",
            Status: status,
            Detail: trustGate.Detail,
            AuditReference: trustGate.PacketPath);
    }

    private static TradingAcceptanceGateDto BuildReportPackGate(TradingReportPackReadinessDto reportPack)
        => new(
            GateId: "report-pack",
            Label: "Report pack lineage",
            Status: reportPack.Status,
            Detail: reportPack.Detail,
            RunId: reportPack.RelatedRunIds.FirstOrDefault(),
            AuditReference: reportPack.ReportId?.ToString("D") ?? reportPack.ManifestPath);

    private static TradingAcceptanceGateStatusDto ResolveTrustGateAcceptanceStatus(TradingTrustGateReadinessDto trustGate)
    {
        if (string.Equals(trustGate.Status, "packet-unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return TradingAcceptanceGateStatusDto.ReviewRequired;
        }

        if (!trustGate.ReadyForOperatorReview || trustGate.Blockers.Count > 0)
        {
            return TradingAcceptanceGateStatusDto.Blocked;
        }

        if (trustGate.OperatorSignoffRequired && !IsOperatorSignoffComplete(trustGate.OperatorSignoffStatus))
        {
            return TradingAcceptanceGateStatusDto.ReviewRequired;
        }

        return TradingAcceptanceGateStatusDto.Ready;
    }


    private async Task<ReconciliationGateEvaluation?> ResolveReconciliationGateAsync(StrategyRunDetail? latestRun, CancellationToken ct)
    {
        if (latestRun is null)
        {
            return null;
        }

        var governance = Resolve<ReconciliationGovernanceService>();
        if (governance is null)
        {
            return null;
        }

        return await governance.EvaluateGateAsync(latestRun.Summary.RunId, new ReconciliationPolicyThresholds(), waiverRequested: false, secondaryApprovalSigned: false, ct).ConfigureAwait(false);
    }

    private static TradingAcceptanceGateDto BuildReconciliationGate(ReconciliationGateEvaluation? evaluation)
    {
        if (evaluation is null)
        {
            return new TradingAcceptanceGateDto(
                GateId: "reconciliation",
                Label: "Reconciliation policy",
                Status: TradingAcceptanceGateStatusDto.ReviewRequired,
                Detail: "Reconciliation policy evaluation is not available.");
        }

        return new TradingAcceptanceGateDto(
            GateId: "reconciliation",
            Label: "Reconciliation policy",
            Status: evaluation.Status,
            Detail: evaluation.Detail);
    }

    private static void AddReconciliationGateWorkItem(ICollection<OperatorWorkItemDto> workItems, ReconciliationGateEvaluation? evaluation, string? runId)
    {
        if (evaluation is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ReconciliationBreak,
                "Reconciliation policy unavailable",
                "Reconciliation policy evaluation is not available.",
                OperatorWorkItemToneDto.Warning,
                runId,
                workItemId: BuildWorkItemId("reconciliation-policy", runId));
            return;
        }

        if (evaluation.Status == TradingAcceptanceGateStatusDto.Ready)
        {
            return;
        }

        AddWorkItem(
            workItems,
            OperatorWorkItemKindDto.ReconciliationBreak,
            "Reconciliation policy requires attention",
            evaluation.Detail,
            evaluation.Status == TradingAcceptanceGateStatusDto.Blocked ? OperatorWorkItemToneDto.Critical : OperatorWorkItemToneDto.Warning,
            runId,
            workItemId: BuildWorkItemId("reconciliation-policy", runId));
    }

    private ProviderPromotionChecklistDto BuildProviderPromotionChecklist(
        TradingTrustGateReadinessDto trustGate,
        TradingReplayReadinessDto? replay,
        DateTimeOffset asOf)
    {
        var contractCompatibilityValidated = ResolveContractCompatibilityValidated();
        var focusedAdapterTestsValidated = string.Equals(trustGate.Status, "ready-for-operator-review", StringComparison.OrdinalIgnoreCase) &&
                                           trustGate.ReadySampleCount >= trustGate.RequiredSampleCount &&
                                           trustGate.ValidatedEvidenceDocumentCount > 0;
        var replayEvidenceGenerated = replay is { IsConsistent: true };
        var degradationCalibrationOutputValidated = string.Equals(trustGate.PromotionPosture, "candidate-approved", StringComparison.OrdinalIgnoreCase);

        var blockers = new List<string>();
        if (!contractCompatibilityValidated)
        {
            blockers.Add("Contract compatibility validation packet is missing or not approved.");
        }

        if (!focusedAdapterTestsValidated)
        {
            blockers.Add("Focused adapter validation evidence is not ready in the provider promotion packet.");
        }

        if (!replayEvidenceGenerated)
        {
            blockers.Add("Replay evidence is missing or inconsistent for the active paper session.");
        }

        if (!degradationCalibrationOutputValidated)
        {
            blockers.Add("Provider degradation calibration output is missing or not candidate-approved.");
        }

        var ready = blockers.Count == 0;
        return new ProviderPromotionChecklistDto(
            ContractCompatibilityValidated: contractCompatibilityValidated,
            FocusedAdapterTestsValidated: focusedAdapterTestsValidated,
            ReplayEvidenceGenerated: replayEvidenceGenerated,
            DegradationCalibrationOutputValidated: degradationCalibrationOutputValidated,
            ReadyForPaperEnablement: ready,
            ReadyForLiveEnablement: ready,
            Blockers: blockers,
            EvidenceBundlePath: trustGate.PacketPath,
            EvaluatedAt: asOf);
    }

    private bool ResolveContractCompatibilityValidated()
    {
        try
        {
            var root = Directory.GetCurrentDirectory();
            var contractRoot = Path.Combine(root, "artifacts", "contract-review");
            if (!Directory.Exists(contractRoot))
            {
                return false;
            }

            var latest = Directory.EnumerateFiles(contractRoot, "contract-review-packet.json", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(latest.FullName));
            return doc.RootElement.TryGetProperty("readyForCadenceReview", out var ready) && ready.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the aggregate readiness status using deterministic precedence:
    /// <c>Blocked</c> overrides all other signals, <c>ReviewRequired</c> overrides <c>Ready</c>, and
    /// <c>Ready</c> is returned only when every gate reports ready.
    /// This keeps stale replay/trust/promotion signals from being masked by otherwise-green gates.
    /// </summary>
    private static TradingAcceptanceGateStatusDto EvaluateOverallPosture(
        IReadOnlyList<TradingAcceptanceGateDto> gates)
    {
        var canonicalGateOrder = new[]
        {
            "replay",
            "reconciliation",
            "audit-controls",
            "risk-rules",
            "promotion",
            "dk1-trust",
            "report-pack",
            "session",
            "brokerage-sync"
        };

        var ordered = canonicalGateOrder
            .Select(gateId => gates.FirstOrDefault(gate => string.Equals(gate.GateId, gateId, StringComparison.OrdinalIgnoreCase)))
            .Where(static gate => gate is not null)
            .Cast<TradingAcceptanceGateDto>()
            .ToArray();

        if (ordered.Length == 0)
        {
            return TradingAcceptanceGateStatusDto.Unknown;
        }

        if (ordered.Any(static gate => gate.Status == TradingAcceptanceGateStatusDto.Blocked))
        {
            return TradingAcceptanceGateStatusDto.Blocked;
        }

        return ordered.All(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready)
            ? TradingAcceptanceGateStatusDto.Ready
            : TradingAcceptanceGateStatusDto.ReviewRequired;
    }

    private static void AddTrustGateWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        TradingTrustGateReadinessDto trustGate)
    {
        if (string.Equals(trustGate.Status, "packet-unavailable", StringComparison.OrdinalIgnoreCase))
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ProviderTrustGate,
                "DK1 trust packet unavailable",
                trustGate.Detail,
                OperatorWorkItemToneDto.Warning,
                workItemId: "dk1-trust-packet-unavailable");
            return;
        }

        if (!trustGate.ReadyForOperatorReview || trustGate.Blockers.Count > 0)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ProviderTrustGate,
                "DK1 trust packet blocked",
                trustGate.Detail,
                OperatorWorkItemToneDto.Critical,
                auditReference: trustGate.PacketPath,
                workItemId: "dk1-trust-packet-blocked");
            return;
        }

        if (trustGate.OperatorSignoffRequired && !IsOperatorSignoffComplete(trustGate.OperatorSignoffStatus))
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.ProviderTrustGate,
                "DK1 operator sign-off pending",
                trustGate.Detail,
                OperatorWorkItemToneDto.Warning,
                auditReference: trustGate.PacketPath,
                workItemId: "dk1-operator-signoff-pending");
        }
    }

    private static void AddReportPackWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        TradingReportPackReadinessDto reportPack,
        string? latestRunId)
    {
        if (reportPack.Status == TradingAcceptanceGateStatusDto.Ready)
        {
            return;
        }

        AddWorkItem(
            workItems,
            OperatorWorkItemKindDto.ReportPackApproval,
            reportPack.Status == TradingAcceptanceGateStatusDto.Blocked
                ? "Report pack blocked"
                : "Report pack lineage requires review",
            reportPack.Detail,
            reportPack.Status == TradingAcceptanceGateStatusDto.Blocked
                ? OperatorWorkItemToneDto.Critical
                : OperatorWorkItemToneDto.Warning,
            latestRunId,
            auditReference: reportPack.ReportId?.ToString("D") ?? reportPack.ManifestPath,
            workItemId: BuildWorkItemId("report-pack-lineage", latestRunId ?? reportPack.FundProfileId));
    }

    private static EvidenceCompletenessSummaryDto BuildEvidenceCompleteness(
        IReadOnlyList<TradingAcceptanceGateDto> gates,
        IReadOnlyList<OperatorWorkItemDto> workItems)
    {
        var readyGateCount = gates.Count(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready);
        var totalGateCount = gates.Count;
        var scorePercent = totalGateCount == 0
            ? 0
            : (int)Math.Round(readyGateCount * 100m / totalGateCount, MidpointRounding.AwayFromZero);
        var criticalCount = workItems.Count(static item => item.Tone == OperatorWorkItemToneDto.Critical);
        var warningCount = workItems.Count(static item => item.Tone == OperatorWorkItemToneDto.Warning);
        var status = EvaluateOverallPosture(gates);

        return new EvidenceCompletenessSummaryDto(
            Status: status,
            ReadyGateCount: readyGateCount,
            TotalGateCount: totalGateCount,
            CriticalWorkItemCount: criticalCount,
            WarningWorkItemCount: warningCount,
            ScorePercent: scorePercent,
            BlockingGateIds: gates
                .Where(static gate => gate.Status == TradingAcceptanceGateStatusDto.Blocked)
                .Select(static gate => gate.GateId)
                .ToArray(),
            ReviewGateIds: gates
                .Where(static gate => gate.Status == TradingAcceptanceGateStatusDto.ReviewRequired)
                .Select(static gate => gate.GateId)
                .ToArray(),
            MissingEvidenceIds: workItems
                .Where(static item => item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical)
                .Select(static item => item.AuditReference ?? item.RunId ?? item.WorkItemId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ReadyGateIds: gates
                .Where(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready)
                .Select(static gate => gate.GateId)
                .ToArray());
    }

    private static bool IsOperatorSignoffComplete(string status) =>
        string.Equals(status, "signed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static string? GetMetadata(ExecutionAuditEntry entry, string key)
    {
        return entry.Metadata is not null && entry.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool? ParseBoolMetadata(ExecutionAuditEntry entry, string key) =>
        bool.TryParse(GetMetadata(entry, key), out var value) ? value : null;

    private static int? ParseIntMetadata(ExecutionAuditEntry entry, string key) =>
        int.TryParse(GetMetadata(entry, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ParseDateTimeOffsetMetadata(ExecutionAuditEntry entry, string key) =>
        DateTimeOffset.TryParse(GetMetadata(entry, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private static TradingPaperSessionReadinessDto MapSession(
        PaperSessionSummaryDto summary,
        PaperSessionDetailDto? detail)
        => new(
            SessionId: summary.SessionId,
            StrategyId: summary.StrategyId,
            StrategyName: summary.StrategyName,
            IsActive: summary.IsActive,
            InitialCash: summary.InitialCash,
            CreatedAt: summary.CreatedAt,
            ClosedAt: summary.ClosedAt,
            SymbolCount: detail?.Symbols.Count ?? 0,
            OrderCount: detail?.OrderHistory?.Count ?? 0,
            PositionCount: detail?.Portfolio?.Positions.Count ?? 0,
            PortfolioValue: detail?.Portfolio?.PortfolioValue)
        {
            FillCount = detail?.FillCount ?? 0,
            LedgerEntryCount = detail?.LedgerEntryCount ?? 0,
            LastFillAt = detail?.LastFillAt,
            LastOrderUpdatedAt = detail?.LastOrderUpdatedAt
        };

    private static void AddWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        OperatorWorkItemKindDto kind,
        string label,
        string detail,
        OperatorWorkItemToneDto tone,
        string? runId = null,
        Guid? fundAccountId = null,
        string? auditReference = null,
        string? workItemId = null,
        string? scope = null,
        string? workspaceOverride = null,
        string? targetRouteOverride = null,
        string? targetPageTagOverride = null)
    {
        var navigation = ResolveWorkItemNavigation(kind, fundAccountId);
        workItems.Add(new OperatorWorkItemDto(
            WorkItemId: workItemId ?? BuildWorkItemId(kind.ToString(), label),
            Kind: kind,
            Label: label,
            Detail: detail,
            Tone: tone,
            CreatedAt: DateTimeOffset.UtcNow,
            RunId: runId,
            FundAccountId: fundAccountId,
            AuditReference: auditReference,
            Workspace: workspaceOverride ?? navigation.Workspace,
            TargetRoute: targetRouteOverride ?? navigation.TargetRoute,
            TargetPageTag: targetPageTagOverride ?? navigation.TargetPageTag,
            Scope: scope));
    }

    private static (string Workspace, string TargetRoute, string TargetPageTag) ResolveWorkItemNavigation(
        OperatorWorkItemKindDto kind,
        Guid? fundAccountId)
        => kind switch
        {
            OperatorWorkItemKindDto.SecurityMasterCoverage => (
                "Data",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                "DataShell"),
            OperatorWorkItemKindDto.ReconciliationBreak => (
                "Accounting",
                UiApiRoutes.ReconciliationBreakQueue,
                "AccountingShell"),
            OperatorWorkItemKindDto.ReportPackApproval => (
                "Reporting",
                UiApiRoutes.FundReportPacks,
                "ReportingShell"),
            OperatorWorkItemKindDto.BrokerageSync => (
                "Trading",
                fundAccountId.HasValue
                    ? UiApiRoutes.WithParam(UiApiRoutes.FundAccountBrokerageSyncStatus, "accountId", fundAccountId.Value.ToString())
                    : UiApiRoutes.FundAccountBrokerageSyncAccounts,
                "AccountPortfolio"),
            _ => (
                "Trading",
                UiApiRoutes.WorkstationTradingReadiness,
                "TradingShell")
        };

    private static string BuildWorkItemId(string prefix, string? scope = null)
    {
        var normalizedPrefix = NormalizeWorkItemToken(prefix);
        var normalizedScope = NormalizeWorkItemToken(scope);
        return string.IsNullOrEmpty(normalizedScope)
            ? normalizedPrefix
            : $"{normalizedPrefix}-{normalizedScope}";
    }

    private static string NormalizeWorkItemToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    private T? Resolve<T>() where T : class
        => _services.GetService(typeof(T)) as T;
}
