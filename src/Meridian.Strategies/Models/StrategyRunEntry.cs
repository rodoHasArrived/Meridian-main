using Meridian.Backtesting.Sdk;

namespace Meridian.Strategies.Models;

/// <summary>
/// An immutable record of a single strategy run (backtest, paper, or live).
/// Stored by <see cref="Storage.StrategyRunStore"/> and used by the promotion workflow.
/// </summary>
public sealed record StrategyRunEntry(
    string RunId,
    string StrategyId,
    string StrategyName,
    RunType RunType,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    BacktestResult? Metrics)
{
    /// <summary>Creates a new run entry with a generated run ID and current timestamp.</summary>
    public static StrategyRunEntry Start(string strategyId, string strategyName, RunType runType) =>
        new(
            RunId: Guid.NewGuid().ToString("N"),
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: runType,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: null,
            Metrics: null);

    /// <summary>Returns a copy of this entry marked as ended with the provided metrics.</summary>
    public StrategyRunEntry Complete(BacktestResult? metrics) =>
        this with { EndedAt = DateTimeOffset.UtcNow, Metrics = metrics };
}
