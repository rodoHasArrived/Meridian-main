using System.Globalization;
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
                OperatorWorkItemToneDto.Critical);
        }
        else if (replay is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "Paper replay verification required",
                $"Run replay verification for paper session {activeSession.SessionId}.",
                OperatorWorkItemToneDto.Warning,
                activeSession.SessionId);
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
                auditReference: replay.VerificationAuditId);
        }

        if (auditEntries.Count == 0)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PaperReplay,
                "No audit evidence",
                "No execution audit entries are visible for the paper acceptance lane.",
                OperatorWorkItemToneDto.Warning);
        }

        var controls = BuildControls(Resolve<ExecutionOperatorControlService>());
        if (controls.CircuitBreakerOpen)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                "Execution circuit breaker open",
                controls.CircuitBreakerReason ?? "Execution is blocked by an operator control.",
                OperatorWorkItemToneDto.Critical);
        }

        if (controls.ManualOverrideCount > 0)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                "Manual overrides require review",
                $"{controls.ManualOverrideCount} execution manual override(s) must be reviewed before promotion acceptance.",
                OperatorWorkItemToneDto.Warning);
        }

        var latestRun = await ResolveLatestRunAsync(ct).ConfigureAwait(false);
        var promotionRecords = await ResolvePromotionRecordsAsync(ct).ConfigureAwait(false);
        var promotion = BuildPromotion(latestRun, promotionRecords);
        if (promotion is null)
        {
            AddWorkItem(
                workItems,
                OperatorWorkItemKindDto.PromotionReview,
                "Promotion decision required",
                "Evaluate and record the paper promotion decision before accepting the cockpit.",
                OperatorWorkItemToneDto.Warning,
                latestRun?.Summary.RunId);
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
                auditReference: promotion.AuditReference);
        }

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
                fundAccountId: brokerageStatus.FundAccountId);
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
                    latestRun.Summary.RunId);
            }
        }

        warnings.AddRange(workItems
            .Where(static item => item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical)
            .Select(static item => item.Detail));

        _logger.LogDebug(
            "Built trading operator readiness with {WorkItemCount} work item(s) and {WarningCount} warning(s).",
            workItems.Count,
            warnings.Count);

        return new TradingOperatorReadinessDto(
            AsOf: asOf,
            ActiveSession: activeSession,
            Sessions: sessions,
            Replay: replay,
            Controls: controls,
            Promotion: promotion,
            BrokerageSync: brokerageStatus,
            WorkItems: workItems
                .OrderByDescending(static item => item.Tone)
                .ThenBy(static item => item.CreatedAt)
                .ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
            MismatchReasons: isConsistent
                ? Array.Empty<string>()
                : [replayAudit.Message ?? "Replay verification recorded a mismatch."]);
    }

    private TradingControlReadinessDto BuildControls(ExecutionOperatorControlService? controlService)
    {
        if (controlService is null)
        {
            return new TradingControlReadinessDto(
                CircuitBreakerOpen: false,
                CircuitBreakerReason: null,
                CircuitBreakerChangedBy: null,
                CircuitBreakerChangedAt: null,
                ManualOverrideCount: 0,
                SymbolLimitCount: 0,
                DefaultMaxPositionSize: null);
        }

        var snapshot = controlService.GetSnapshot();
        return new TradingControlReadinessDto(
            CircuitBreakerOpen: snapshot.CircuitBreaker.IsOpen,
            CircuitBreakerReason: snapshot.CircuitBreaker.Reason,
            CircuitBreakerChangedBy: snapshot.CircuitBreaker.ChangedBy,
            CircuitBreakerChangedAt: snapshot.CircuitBreaker.ChangedAt,
            ManualOverrideCount: snapshot.ManualOverrides.Count,
            SymbolLimitCount: snapshot.SymbolPositionLimits.Count,
            DefaultMaxPositionSize: snapshot.DefaultMaxPositionSize);
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
                ApprovedBy: record.ApprovedBy);
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
                ApprovedBy: promotion.ApprovedBy);
    }

    private static bool IsPromotionRecordTraceComplete(StrategyPromotionRecord record) =>
        !string.IsNullOrWhiteSpace(record.Decision) &&
        !string.IsNullOrWhiteSpace(record.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(record.ApprovalReason) &&
        !string.IsNullOrWhiteSpace(record.SourceRunId) &&
        !string.IsNullOrWhiteSpace(record.AuditReference);

    private static bool IsPromotionTraceComplete(TradingPromotionReadinessDto? promotion) =>
        promotion is not null &&
        !string.IsNullOrWhiteSpace(promotion.ApprovalStatus) &&
        !string.IsNullOrWhiteSpace(promotion.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(promotion.Reason) &&
        !string.IsNullOrWhiteSpace(promotion.SourceRunId) &&
        !string.IsNullOrWhiteSpace(promotion.AuditReference);

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
            PortfolioValue: detail?.Portfolio?.PortfolioValue);

    private static void AddWorkItem(
        ICollection<OperatorWorkItemDto> workItems,
        OperatorWorkItemKindDto kind,
        string label,
        string detail,
        OperatorWorkItemToneDto tone,
        string? runId = null,
        Guid? fundAccountId = null,
        string? auditReference = null)
    {
        workItems.Add(new OperatorWorkItemDto(
            WorkItemId: $"operator-{Guid.NewGuid():N}",
            Kind: kind,
            Label: label,
            Detail: detail,
            Tone: tone,
            CreatedAt: DateTimeOffset.UtcNow,
            RunId: runId,
            FundAccountId: fundAccountId,
            AuditReference: auditReference));
    }

    private T? Resolve<T>() where T : class
        => _services.GetService(typeof(T)) as T;
}
