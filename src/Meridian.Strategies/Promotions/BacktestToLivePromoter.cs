using Meridian.Backtesting.Sdk;
using Interop = Meridian.FSharp.Interop;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Promotions;

/// <summary>
/// Evaluates whether a strategy is eligible to be promoted from backtest → paper,
/// or paper → live, based on configurable performance thresholds.
/// </summary>
public sealed class BacktestToLivePromoter
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="result"/> meets the minimum promotion
    /// thresholds in <paramref name="criteria"/>.
    /// </summary>
    public bool MeetsPromotionThresholds(BacktestResult result, PromotionCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(criteria);

        var decision = Interop.PromotionInterop.EvaluateBacktestPromotion(
            result,
            criteria.MinSharpeRatio,
            criteria.MaxAllowedDrawdownPercent,
            criteria.MinTotalReturn);
        return decision.Eligible;
    }

    /// <summary>
    /// Creates a promotion record that pins the strategy version, parameters, and
    /// qualifying metrics for audit trail purposes.
    /// </summary>
    public StrategyPromotionRecord CreatePromotionRecord(
        BacktestResult result,
        string strategyId,
        string strategyName,
        RunType targetRunType)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StrategyPromotionRecord(
            PromotionId: Guid.NewGuid().ToString("N"),
            StrategyId: strategyId,
            StrategyName: strategyName,
            SourceRunType: targetRunType == RunType.Paper ? RunType.Backtest : RunType.Paper,
            TargetRunType: targetRunType,
            QualifyingSharpe: result.Metrics.SharpeRatio,
            QualifyingMaxDrawdownPercent: result.Metrics.MaxDrawdownPercent,
            QualifyingTotalReturn: result.Metrics.TotalReturn,
            PromotedAt: DateTimeOffset.UtcNow);
    }
}

/// <summary>Minimum thresholds that a strategy must meet to be eligible for promotion.</summary>
public sealed record PromotionCriteria(
    double MinSharpeRatio,
    decimal MaxAllowedDrawdownPercent,
    decimal MinTotalReturn)
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
    double QualifyingSharpe,
    decimal QualifyingMaxDrawdownPercent,
    decimal QualifyingTotalReturn,
    DateTimeOffset PromotedAt);
