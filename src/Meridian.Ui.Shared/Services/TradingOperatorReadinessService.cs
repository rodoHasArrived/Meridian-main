using System.Globalization;
using System.Text;
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
        var warnings = new List<string>();
        var workItems = new List<OperatorWorkItemDto>();

        var paperService = Resolve<PaperSessionPersistenceService>();
        if (paperService is not null)
        {
            await paperService.InitialiseAsync(ct).ConfigureAwait(false);
        }

        var auditEntries = await ResolveAuditEntriesAsync(ct).ConfigureAwait(false);
        var sessionSummaries = paperService?.GetSessions() ?? [];
        var sessions = sessionSummaries
            .Select(summary => MapSession(summary, paperService?.GetSession(summary.SessionId)))
            .ToArray();
        var activeSession = sessions.FirstOrDefault(static session => session.IsActive);
        var replay = BuildReplay(activeSession?.SessionId, auditEntries);

        if (activeSession is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "No active paper session",
                "Start or restore a paper session before treating the cockpit as operator-ready.",
                OperatorWorkItemToneDto.Critical,
                workItemId: "paper-session-missing");
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
                workItemId: BuildWorkItemId("paper-replay-missing", activeSession.SessionId));
        }
        else if (!replay.IsConsistent)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay mismatch",
                replay.MismatchReasons.FirstOrDefault() ?? $"Replay verification for {replay.SessionId} did not match persisted state.",
                OperatorWorkItemToneDto.Critical,
                replay.SessionId,
                auditReference: replay.VerificationAuditId,
                workItemId: BuildWorkItemId("paper-replay-mismatch", replay.SessionId));
        }
        else if (IsReplayCoverageStale(activeSession, replay))
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay verification stale",
                BuildReplayCoverageDetail(activeSession, replay),
                OperatorWorkItemToneDto.Warning,
                replay.SessionId,
                auditReference: replay.VerificationAuditId,
                workItemId: BuildWorkItemId("paper-replay-stale", replay.SessionId));
        }

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

        var latestRun = await ResolveLatestRunAsync(ct).ConfigureAwait(false);
        var promotionRecords = await ResolvePromotionRecordsAsync(ct).ConfigureAwait(false);
        var promotion = BuildPromotion(latestRun, promotionRecords);
        var trustGate = await ResolveTrustGateReadinessAsync(ct).ConfigureAwait(false);
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
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                "Promotion trace incomplete",
                "Promotion evidence must include decision, operator, rationale, lineage, and audit reference.",
                OperatorWorkItemToneDto.Warning,
                promotion.SourceRunId ?? promotion.TargetRunId,
                auditReference: promotion.AuditReference,
                workItemId: BuildWorkItemId("promotion-trace-incomplete", promotion.SourceRunId ?? promotion.TargetRunId));
        }

        AddTrustGateWorkItem(workItems, trustGate);

        var brokerageStatus = await ResolveBrokerageStatusAsync(fundAccountId, ct).ConfigureAwait(false);
        if (brokerageStatus is not null && brokerageStatus.Health is not WorkstationBrokerageSyncHealth.Healthy)
        {
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
                workItemId: BuildWorkItemId("brokerage-sync-attention", brokerageStatus.FundAccountId.ToString("N")));
        }

        if (latestRun is not null)
        {
            var missingSecurityCount =
                (latestRun.Portfolio?.SecurityMissingCount ?? 0)
                + (latestRun.Ledger?.SecurityMissingCount ?? 0);
            if (missingSecurityCount > 0)
            {
                AddWorkItem(
                    workItems,
                    OperatorWorkItemKindDto.SecurityMasterCoverage,
                    "Security Master coverage gap",
                    $"{missingSecurityCount} run security reference(s) are missing coverage.",
                    OperatorWorkItemToneDto.Warning,
                    latestRun.Summary.RunId,
                    workItemId: BuildWorkItemId("security-master-coverage-gap", latestRun.Summary.RunId));
            }
        }

        var acceptanceGates = BuildAcceptanceGates(
            activeSession,
            sessions,
            replay,
            controls,
            promotion,
            trustGate,
            auditEntries);
        var overallStatus = ResolveOverallStatus(acceptanceGates);

        warnings.AddRange(workItems
            .Where(static item => item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical)
            .Select(static item => item.Detail));

        _logger.LogDebug(
            "Built trading operator readiness with {OverallStatus}, {WorkItemCount} work item(s), and {WarningCount} warning(s).",
            overallStatus,
            workItems.Count,
            warnings.Count);

        return new TradingOperatorReadinessDto(
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
            OverallStatus = overallStatus,
            ReadyForPaperOperation = overallStatus == TradingAcceptanceGateStatusDto.Ready
        };
    }

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
        if (!string.IsNullOrWhiteSpace(entry.Message))
        {
            return entry.Message.Trim();
        }

        if (string.Equals(entry.Action, "OrderSubmitted", StringComparison.OrdinalIgnoreCase))
        {
            return $"Order submitted with outcome {entry.Outcome}.";
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
        var record = promotionRecords.FirstOrDefault(candidate =>
                latestRun is null ||
                string.Equals(candidate.SourceRunId, latestRun.Summary.RunId, StringComparison.Ordinal) ||
                string.Equals(candidate.TargetRunId, latestRun.Summary.RunId, StringComparison.Ordinal) ||
                string.Equals(candidate.StrategyId, latestRun.Summary.StrategyId, StringComparison.Ordinal))
            ?? promotionRecords.FirstOrDefault();

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
                ApprovalChecklist: record.ApprovalChecklist);
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
                ApprovalChecklist: promotion.ApprovalChecklist);
    }

    private static bool IsPromotionRecordTraceComplete(StrategyPromotionRecord record) =>
        !string.IsNullOrWhiteSpace(record.Decision) &&
        !string.IsNullOrWhiteSpace(record.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(record.ApprovalReason) &&
        HasApprovalChecklist(record.ApprovalChecklist) &&
        !string.IsNullOrWhiteSpace(record.SourceRunId) &&
        !string.IsNullOrWhiteSpace(record.AuditReference);

    private static bool IsPromotionTraceComplete(TradingPromotionReadinessDto? promotion) =>
        promotion is not null &&
        !string.IsNullOrWhiteSpace(promotion.ApprovalStatus) &&
        !string.IsNullOrWhiteSpace(promotion.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(promotion.Reason) &&
        HasApprovalChecklist(promotion.ApprovalChecklist) &&
        !string.IsNullOrWhiteSpace(promotion.SourceRunId) &&
        !string.IsNullOrWhiteSpace(promotion.AuditReference);

    private static bool HasApprovalChecklist(IReadOnlyList<string>? approvalChecklist)
        => approvalChecklist is { Count: > 0 } &&
           approvalChecklist.All(static item => !string.IsNullOrWhiteSpace(item));

    private static IReadOnlyList<TradingAcceptanceGateDto> BuildAcceptanceGates(
        TradingPaperSessionReadinessDto? activeSession,
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions,
        TradingReplayReadinessDto? replay,
        TradingControlReadinessDto controls,
        TradingPromotionReadinessDto? promotion,
        TradingTrustGateReadinessDto trustGate,
        IReadOnlyList<ExecutionAuditEntry> auditEntries)
        =>
        [
            BuildSessionGate(activeSession, sessions),
            BuildReplayGate(activeSession, replay),
            BuildAuditControlGate(controls, auditEntries),
            BuildPromotionGate(promotion),
            BuildTrustGateAcceptance(trustGate)
        ];

    private static TradingAcceptanceGateDto BuildSessionGate(
        TradingPaperSessionReadinessDto? activeSession,
        IReadOnlyList<TradingPaperSessionReadinessDto> sessions)
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
            Status: TradingAcceptanceGateStatusDto.ReviewRequired,
            Detail: "Promotion evidence must include decision, operator, rationale, checklist, lineage, and audit reference.",
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

    private static TradingAcceptanceGateStatusDto ResolveOverallStatus(IReadOnlyList<TradingAcceptanceGateDto> gates)
    {
        if (gates.Any(static gate => gate.Status == TradingAcceptanceGateStatusDto.Blocked))
        {
            return TradingAcceptanceGateStatusDto.Blocked;
        }

        return gates.All(static gate => gate.Status == TradingAcceptanceGateStatusDto.Ready)
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
        string? workItemId = null)
    {
        workItems.Add(new OperatorWorkItemDto(
            WorkItemId: workItemId ?? BuildWorkItemId(kind.ToString(), label),
            Kind: kind,
            Label: label,
            Detail: detail,
            Tone: tone,
            CreatedAt: DateTimeOffset.UtcNow,
            RunId: runId,
            FundAccountId: fundAccountId,
            AuditReference: auditReference));
    }

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
