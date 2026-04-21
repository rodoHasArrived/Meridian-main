using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Microsoft.Extensions.Logging;

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
    private readonly List<StrategyPromotionRecord> _promotionHistory = [];
    private readonly Lock _lock = new();

    public PromotionService(
        IStrategyRepository repository,
        BacktestToLivePromoter promoter,
        ILogger<PromotionService> logger,
        ExecutionOperatorControlService? operatorControls = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _promoter = promoter ?? throw new ArgumentNullException(nameof(promoter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _brokerageConfiguration = brokerageConfiguration;
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
        var targetMode = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
        var eligible = _promoter.MeetsPromotionThresholds(run.Metrics, effectiveCriteria);

        // Governance checks for live promotion
        var blockingReasons = new List<string>();
        var requiresHumanApproval = false;
        var requiresManualOverride = false;
        string? requiredManualOverrideKind = null;

        if (targetMode == RunType.Live)
        {
            var brokerageValidation = BrokerageValidationEvaluator.Evaluate(_brokerageConfiguration);
            if (brokerageValidation.HasBlockingGap)
            {
                blockingReasons.AddRange(brokerageValidation.Findings);
                requiresHumanApproval = true;
                requiresManualOverride = true;
                requiredManualOverrideKind = ExecutionManualOverrideKinds.AllowLivePromotion;
            }
        }

        _logger.LogInformation(
            "Promotion evaluation for run {RunId}: eligible={Eligible}, target={Target}, sharpe={Sharpe:F3}",
            runId, eligible, targetMode, run.Metrics.Metrics.SharpeRatio);

        return new PromotionEvaluationResult(
            RunId: runId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            SourceMode: run.RunType,
            TargetMode: targetMode,
            IsEligible: eligible,
            SharpeRatio: run.Metrics.Metrics.SharpeRatio,
            MaxDrawdownPercent: run.Metrics.Metrics.MaxDrawdownPercent,
            TotalReturn: run.Metrics.Metrics.TotalReturn,
            Reason: eligible
                ? "Meets all promotion thresholds."
                : "Does not meet minimum promotion criteria.",
            Found: true,
            Ready: true,
            RequiresHumanApproval: requiresHumanApproval,
            RequiresManualOverride: requiresManualOverride,
            RequiredManualOverrideKind: requiredManualOverrideKind,
            BlockingReasons: blockingReasons.Count > 0 ? blockingReasons : null);
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

        // Create audit record
        var promotionRecord = _promoter.CreatePromotionRecord(
            run.Metrics,
            run.StrategyId,
            run.StrategyName,
            targetRunType,
            approvedBy: request.ApprovedBy,
            manualOverrideId: request.ManualOverrideId);

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

        lock (_lock)
        {
            _promotionHistory.Add(promotionRecord);
        }

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
            AuditReference: promotionRecord.AuditReference,
            ApprovedBy: request.ApprovedBy);
    }

    /// <summary>
    /// Rejects a promotion with a recorded reason.
    /// </summary>
    public Task<PromotionDecisionResult> RejectAsync(
        PromotionRejectionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation(
            "Promotion rejected for run {RunId}: {Reason}",
            request.RunId, request.Reason);

        return Task.FromResult(new PromotionDecisionResult(
            Success: true,
            PromotionId: null,
            NewRunId: null,
            Reason: $"Promotion rejected: {request.Reason}"));
    }

    /// <summary>Returns the full promotion audit trail.</summary>
    public IReadOnlyList<StrategyPromotionRecord> GetPromotionHistory()
    {
        lock (_lock)
        {
            return [.. _promotionHistory];
        }
    }

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
    string Reason);

/// <summary>Result of a promotion approval or rejection.</summary>
public sealed record PromotionDecisionResult(
    bool Success,
    string? PromotionId,
    string? NewRunId,
    string Reason,
    string? AuditReference = null,
    string? ApprovedBy = null);
