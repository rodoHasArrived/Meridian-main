using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Strategies.Services;

/// <summary>
/// Orchestrates the strategy promotion workflow: evaluates eligibility,
/// records promotion decisions, and creates new run entries for the target mode.
/// Bridges the gap between promotion evaluation (F# policy) and operational execution.
/// </summary>
public sealed class PromotionService
{
    private readonly IStrategyRepository _repository;
    private readonly BacktestToLivePromoter _promoter;
    private readonly ILogger<PromotionService> _logger;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly IPromotionRecordStore _promotionRecordStore;

    public PromotionService(
        IStrategyRepository repository,
        BacktestToLivePromoter promoter,
        IPromotionRecordStore promotionRecordStore,
        ILogger<PromotionService> logger,
        ExecutionOperatorControlService? operatorControls = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _promoter = promoter ?? throw new ArgumentNullException(nameof(promoter));
        _promotionRecordStore = promotionRecordStore ?? throw new ArgumentNullException(nameof(promotionRecordStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _brokerageConfiguration = brokerageConfiguration;
        _ = _promotionRecordStore.LoadAsync();
    }

    /// <summary>
    /// Evaluates whether a completed run is eligible for promotion to the next mode.
    /// </summary>
    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string runId,
        PromotionCriteria? criteria = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await FindRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            return PromotionEvaluationResult.NotFound(runId);
        }

        if (!run.EndedAt.HasValue)
        {
            return PromotionEvaluationResult.NotReady(runId, "Run has not completed yet.");
        }

        if (run.RunType == RunType.Live)
        {
            return PromotionEvaluationResult.NotReady(runId, "Live runs cannot be promoted further.");
        }

        if (run.Metrics is null)
        {
            return PromotionEvaluationResult.NotReady(runId, "Run has no metrics available for evaluation.");
        }

        var effectiveCriteria = criteria ?? PromotionCriteria.Default;
        var metrics = run.Metrics!.Metrics;
        var targetMode = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
        var controlsSnapshot = _operatorControls?.GetSnapshot();
        var brokerageValidation = targetMode == RunType.Live
            ? BrokerageValidationEvaluator.Evaluate(_brokerageConfiguration)
            : null;
        var hasConflictingOverride = false;
        var hasLivePromotionOverride = false;
        if (targetMode == RunType.Live && controlsSnapshot is not null)
        {
            foreach (var overrideEntry in controlsSnapshot.ManualOverrides)
            {
                var matchesStrategy = string.IsNullOrWhiteSpace(overrideEntry.StrategyId) ||
                                      string.Equals(overrideEntry.StrategyId, run.StrategyId, StringComparison.OrdinalIgnoreCase);
                var matchesRun = string.IsNullOrWhiteSpace(overrideEntry.RunId) ||
                                 string.Equals(overrideEntry.RunId, run.RunId, StringComparison.OrdinalIgnoreCase);
                if (!matchesStrategy || !matchesRun)
                {
                    continue;
                }

                if (string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.ForceBlockOrders, StringComparison.OrdinalIgnoreCase))
                {
                    hasConflictingOverride = true;
                }

                if (string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.AllowLivePromotion, StringComparison.OrdinalIgnoreCase))
                {
                    hasLivePromotionOverride = true;
                }
            }
        }

        var policyInput = new Meridian.FSharp.Promotion.PromotionPolicy.PromotionPolicyInput(
            run.EndedAt.HasValue,
            run.Metrics is not null,
            metrics.SharpeRatio,
            metrics.MaxDrawdownPercent,
            metrics.TotalReturn,
            effectiveCriteria.MinSharpeRatio,
            effectiveCriteria.MaxAllowedDrawdownPercent,
            effectiveCriteria.MinTotalReturn,
            targetMode == RunType.Live,
            controlsSnapshot is not null || targetMode != RunType.Live,
            controlsSnapshot is not null || targetMode != RunType.Live,
            _brokerageConfiguration?.LiveExecutionEnabled ?? false,
            controlsSnapshot?.CircuitBreaker.IsOpen ?? false,
            hasConflictingOverride,
            targetMode != RunType.Live || hasLivePromotionOverride,
            ExecutionManualOverrideKinds.AllowLivePromotion);
        var policyDecision = Interop.PromotionInterop.EvaluatePromotionPolicy(policyInput);
        var hasBrokerageGap = brokerageValidation?.HasBlockingGap == true;
        var eligible = policyDecision.Eligible && !hasBrokerageGap;
        var requiresManualOverride =
            string.Equals(policyDecision.Outcome, "requires_manual_override", StringComparison.OrdinalIgnoreCase) ||
            (targetMode == RunType.Live && hasBrokerageGap);
        var requiresHumanApproval =
            requiresManualOverride ||
            string.Equals(policyDecision.Outcome, "requires_human_review", StringComparison.OrdinalIgnoreCase);
        var blockingReasons = new List<string>();
        if (policyDecision.Reasons.Length > 0)
        {
            blockingReasons.AddRange(policyDecision.Reasons);
        }

        if (targetMode == RunType.Live && brokerageValidation is not null && brokerageValidation.Findings.Count > 0)
        {
            blockingReasons.AddRange(brokerageValidation.Findings);
        }

        var requiredManualOverrideKind = string.IsNullOrWhiteSpace(policyDecision.RequiredManualOverrideKind)
            ? requiresManualOverride && targetMode == RunType.Live
                ? ExecutionManualOverrideKinds.AllowLivePromotion
                : null
            : policyDecision.RequiredManualOverrideKind;
        var blockingReasonSet = blockingReasons.Count > 0
            ? blockingReasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : null;
        var reason = policyDecision.Outcome switch
        {
            "approved" when hasBrokerageGap && brokerageValidation is not null => brokerageValidation.Summary,
            "approved" => "Meets all promotion policy gates.",
            "requires_human_review" => "Promotion requires human governance review.",
            "requires_manual_override" => "Promotion requires a manual override.",
            "blocked" => "Promotion is blocked by policy.",
            _ when hasBrokerageGap && brokerageValidation is not null => brokerageValidation.Summary,
            _ => "Promotion policy decision unavailable."
        };

        _logger.LogInformation(
            "Promotion evaluation for run {RunId}: eligible={Eligible}, target={Target}, sharpe={Sharpe:F3}",
            runId, eligible, targetMode, metrics.SharpeRatio);

        return new PromotionEvaluationResult(
            RunId: runId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            SourceMode: run.RunType,
            TargetMode: targetMode,
            IsEligible: eligible,
            SharpeRatio: metrics.SharpeRatio,
            MaxDrawdownPercent: metrics.MaxDrawdownPercent,
            TotalReturn: metrics.TotalReturn,
            Reason: reason,
            Found: true,
            Ready: true,
            RequiresHumanApproval: requiresHumanApproval,
            RequiresManualOverride: requiresManualOverride,
            RequiredManualOverrideKind: requiredManualOverrideKind,
            BlockingReasons: blockingReasonSet);
    }

    /// <summary>
    /// Approves a promotion: creates a new run entry for the target mode and records the audit trail.
    /// </summary>
    public async Task<PromotionDecisionResult> ApproveAsync(
        PromotionApprovalRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = await FindRunAsync(request.RunId, ct).ConfigureAwait(false);
        if (run?.Metrics is null || !run.EndedAt.HasValue)
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Run not found, incomplete, or has no metrics.");
        }

        var targetRunType = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;

        if (targetRunType == RunType.Live && _operatorControls is not null)
        {
            var controlDecision = _operatorControls.EvaluateLivePromotion(run.RunId, run.StrategyId, request.ManualOverrideId);
            if (!controlDecision.IsAllowed)
            {
                return new PromotionDecisionResult(
                    Success: false,
                    PromotionId: null,
                    NewRunId: null,
                    Reason: controlDecision.RejectReason ?? "Promotion blocked by execution controls.");
            }
        }

        var auditReference = Guid.NewGuid().ToString("N");

        // Create durable promotion record
        var promotionRecord = _promoter.CreatePromotionRecord(
            run.Metrics,
            run.StrategyId,
            run.StrategyName,
            sourceRunId: run.RunId,
            targetRunId: null,
            targetRunType,
            decision: "Approved",
            approvedBy: request.ApprovedBy,
            manualOverrideId: request.ManualOverrideId,
            approvalReason: request.ApprovalReason,
            reviewNotes: request.ReviewNotes,
            auditReference: auditReference);

        // Create new run entry for the target mode, inheriting parameters
        var newRun = new StrategyRunEntry(
            RunId: Guid.NewGuid().ToString("N"),
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            RunType: targetRunType,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: null,
            Metrics: null,
            PortfolioId: $"{run.StrategyId}-{targetRunType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: $"{run.StrategyId}-{targetRunType.ToString().ToLowerInvariant()}-ledger",
            AuditReference: promotionRecord.PromotionId,
            Engine: targetRunType == RunType.Paper ? "BrokerPaper" : "BrokerLive",
            ParameterSet: run.ParameterSet,
            ParentRunId: run.RunId,
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName);

        await _repository.RecordRunAsync(newRun, ct).ConfigureAwait(false);
        await _promotionRecordStore.AppendAsync(
            promotionRecord with
            {
                TargetRunId = newRun.RunId
            },
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Promoted strategy {StrategyId} from {Source} to {Target}: promotionId={PromotionId}, newRunId={NewRunId}",
            run.StrategyId, run.RunType, targetRunType, promotionRecord.PromotionId, newRun.RunId);

        // Record promotion approval in the execution audit trail
        if (_auditTrail is not null)
        {
            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Promotion",
                Action: "PromotionApproved",
                Outcome: "Approved",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: request.ApprovedBy,
                RunId: request.RunId,
                CorrelationId: promotionRecord.PromotionId,
                Message: request.ApprovalReason), ct).ConfigureAwait(false);
        }

        return new PromotionDecisionResult(
            Success: true,
            PromotionId: promotionRecord.PromotionId,
            NewRunId: newRun.RunId,
            Reason: $"Strategy promoted from {run.RunType} to {targetRunType}.",
            AuditReference: auditReference,
            ApprovedBy: request.ApprovedBy);
    }

    /// <summary>
    /// Rejects a promotion with a recorded reason.
    /// </summary>
    public async Task<PromotionDecisionResult> RejectAsync(
        PromotionRejectionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = await FindRunAsync(request.RunId, ct).ConfigureAwait(false);
        var auditReference = Guid.NewGuid().ToString("N");
        var rejectionRecord = new StrategyPromotionRecord(
            PromotionId: Guid.NewGuid().ToString("N"),
            StrategyId: run?.StrategyId ?? "unknown",
            StrategyName: run?.StrategyName ?? "unknown",
            SourceRunId: request.RunId,
            TargetRunId: null,
            SourceRunType: run?.RunType ?? RunType.Backtest,
            TargetRunType: run?.RunType == RunType.Paper ? RunType.Live : RunType.Paper,
            QualifyingSharpe: run?.Metrics?.Metrics.SharpeRatio ?? 0,
            QualifyingMaxDrawdownPercent: run?.Metrics?.Metrics.MaxDrawdownPercent ?? 0m,
            QualifyingTotalReturn: run?.Metrics?.Metrics.TotalReturn ?? 0m,
            PromotedAt: DateTimeOffset.UtcNow,
            Decision: "Rejected",
            ApprovalReason: request.Reason,
            ReviewNotes: request.ReviewNotes,
            AuditReference: auditReference,
            ApprovedBy: request.RejectedBy,
            ManualOverrideId: request.ManualOverrideId);
        await _promotionRecordStore.AppendAsync(rejectionRecord, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Promotion rejected for run {RunId}: {Reason}",
            request.RunId, request.Reason);

        return new PromotionDecisionResult(
            Success: true,
            PromotionId: rejectionRecord.PromotionId,
            NewRunId: null,
            Reason: $"Promotion rejected: {request.Reason}",
            AuditReference: auditReference,
            ApprovedBy: request.RejectedBy);
    }

    /// <summary>Returns the full promotion audit trail.</summary>
    public IReadOnlyList<StrategyPromotionRecord> GetPromotionHistory()
        => _promotionRecordStore.GetHistoryAsync().GetAwaiter().GetResult();

    private async Task<StrategyRunEntry?> FindRunAsync(string runId, CancellationToken ct)
    {
        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                return run;
            }
        }

        return null;
    }
}

// --- Request/response DTOs ---

/// <summary>Result of evaluating a run for promotion eligibility.</summary>
public sealed record PromotionEvaluationResult(
    string RunId,
    string? StrategyId,
    string? StrategyName,
    RunType? SourceMode,
    RunType? TargetMode,
    bool IsEligible,
    double SharpeRatio,
    decimal MaxDrawdownPercent,
    decimal TotalReturn,
    string Reason,
    bool Found = true,
    bool Ready = true,
    bool RequiresHumanApproval = false,
    bool RequiresManualOverride = false,
    string? RequiredManualOverrideKind = null,
    IReadOnlyList<string>? BlockingReasons = null)
{
    public static PromotionEvaluationResult NotFound(string runId) => new(
        RunId: runId, StrategyId: null, StrategyName: null,
        SourceMode: null, TargetMode: null, IsEligible: false,
        SharpeRatio: 0, MaxDrawdownPercent: 0, TotalReturn: 0,
        Reason: "Run not found.", Found: false, Ready: false);

    public static PromotionEvaluationResult NotReady(string runId, string reason) => new(
        RunId: runId, StrategyId: null, StrategyName: null,
        SourceMode: null, TargetMode: null, IsEligible: false,
        SharpeRatio: 0, MaxDrawdownPercent: 0, TotalReturn: 0,
        Reason: reason, Found: true, Ready: false);
}

/// <summary>Request to approve a strategy promotion.</summary>
public sealed record PromotionApprovalRequest(
    string RunId,
    string? ReviewNotes = null,
    string? ApprovedBy = null,
    string? ApprovalReason = null,
    string? ManualOverrideId = null);

/// <summary>Request to reject a strategy promotion.</summary>
public sealed record PromotionRejectionRequest(
    string RunId,
    string Reason,
    string? ReviewNotes = null,
    string? RejectedBy = null,
    string? ManualOverrideId = null);

/// <summary>Result of a promotion approval or rejection.</summary>
public sealed record PromotionDecisionResult(
    bool Success,
    string? PromotionId,
    string? NewRunId,
    string Reason,
    string? AuditReference = null,
    string? ApprovedBy = null);
