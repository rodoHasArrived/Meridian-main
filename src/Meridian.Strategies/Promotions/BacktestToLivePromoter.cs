using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Models;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Strategies.Promotions;

/// <summary>
/// Evaluates whether a strategy is eligible to be promoted from backtest -> paper,
/// or paper -> live, based on configurable performance thresholds.
/// </summary>
public sealed class BacktestToLivePromoter
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="result"/> meets the minimum promotion
    /// thresholds in <paramref name="criteria"/>.
    /// </summary>
    public bool MeetsPromotionThresholds(BacktestResult result, PromotionCriteria criteria)
    {
        var decision = EvaluatePromotionThresholds(result, criteria);
        return decision.Eligible;
    }

    /// <summary>
    /// Returns the typed promotion threshold decision from the F# policy kernel.
    /// </summary>
    public Interop.PromotionDecisionDto EvaluatePromotionThresholds(BacktestResult result, PromotionCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(criteria);

        return Interop.PromotionInterop.EvaluateBacktestPromotion(
            result,
            criteria.MinSharpeRatio,
            criteria.MaxAllowedDrawdownPercent,
            criteria.MinTotalReturn);
    }

    /// <summary>
    /// Creates a promotion record that pins the strategy version, parameters, and
    /// qualifying metrics for audit trail purposes.
    /// </summary>
    public StrategyPromotionRecord CreatePromotionRecord(
        BacktestResult result,
        string strategyId,
        string strategyName,
        RunType sourceRunType,
        RunType targetRunType,
        string sourceRunId,
        string? targetRunId,
        string decision,
        string? approvedBy = null,
        string? approvalReason = null,
        string? reviewNotes = null,
        string[]? approvalChecklist = null,
        string[]? evidenceReferences = null,
        string? manualOverrideId = null,
        string? auditReference = null,
        string? promotionId = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StrategyPromotionRecord(
            PromotionId: promotionId ?? Guid.NewGuid().ToString("N"),
            StrategyId: strategyId,
            StrategyName: strategyName,
            SourceRunType: sourceRunType,
            TargetRunType: targetRunType,
            SourceRunId: sourceRunId,
            TargetRunId: targetRunId,
            QualifyingSharpe: result.Metrics.SharpeRatio,
            QualifyingMaxDrawdownPercent: result.Metrics.MaxDrawdownPercent,
            QualifyingTotalReturn: result.Metrics.TotalReturn,
            Decision: decision,
            PromotedAt: DateTimeOffset.UtcNow,
            ApprovalReason: approvalReason,
            ReviewNotes: reviewNotes,
            ApprovalChecklist: approvalChecklist is { Length: > 0 } ? [.. approvalChecklist] : null,
            EvidenceReferences: evidenceReferences is { Length: > 0 } ? [.. evidenceReferences] : null,
            AuditReference: auditReference,
            ApprovedBy: approvedBy,
            ManualOverrideId: manualOverrideId);
    }
}

/// <summary>
/// Stable decision labels persisted into durable promotion history.
/// </summary>
public static class PromotionDecisionKinds
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>Minimum thresholds that a strategy must meet to be eligible for promotion.</summary>
/// <param name="MinSharpeRatio">Minimum full-period backtest Sharpe ratio.</param>
/// <param name="MaxAllowedDrawdownPercent">Maximum tolerated full-period drawdown.</param>
/// <param name="MinTotalReturn">Minimum full-period total return.</param>
/// <param name="MinOutOfSampleSharpe">Minimum stitched out-of-sample Sharpe when walk-forward
/// evidence is recorded for the run.</param>
/// <param name="MinWalkForwardDegradationRatio">Minimum out-of-sample / in-sample objective
/// ratio; values below this floor indicate the in-sample edge did not survive out of sample.</param>
/// <param name="RequireWalkForwardEvidenceForLive">When true, paper runs cannot be promoted to
/// live on a single full-period backtest alone — recorded walk-forward evidence is required.</param>
/// <param name="MaxOutOfSampleDrawdownPercent">Maximum tolerated drawdown of the stitched
/// out-of-sample equity curve when walk-forward evidence is recorded.</param>
public sealed record PromotionCriteria(
    double MinSharpeRatio,
    decimal MaxAllowedDrawdownPercent,
    decimal MinTotalReturn,
    double MinOutOfSampleSharpe = 0.0,
    double MinWalkForwardDegradationRatio = 0.5,
    bool RequireWalkForwardEvidenceForLive = true,
    decimal MaxOutOfSampleDrawdownPercent = 0.25m)
{
    /// <summary>Conservative default criteria suitable for first-time promotion to paper trading.</summary>
    public static readonly PromotionCriteria Default = new(
        MinSharpeRatio: 0.5,
        MaxAllowedDrawdownPercent: 0.25m,
        MinTotalReturn: 0.0m);
}

/// <summary>
/// An immutable audit record generated when a strategy is promoted to a higher-risk run type.
/// Should be serialised and stored alongside the strategy's run history.
/// </summary>
public sealed record StrategyPromotionRecord(
    string PromotionId,
    string StrategyId,
    string StrategyName,
    RunType SourceRunType,
    RunType TargetRunType,
    string SourceRunId,
    string? TargetRunId,
    double QualifyingSharpe,
    decimal QualifyingMaxDrawdownPercent,
    decimal QualifyingTotalReturn,
    string Decision,
    DateTimeOffset PromotedAt,
    string? ApprovalReason = null,
    string? ReviewNotes = null,
    string[]? ApprovalChecklist = null,
    string[]? EvidenceReferences = null,
    string? AuditReference = null,
    string? ApprovedBy = null,
    string? ManualOverrideId = null);
