using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.WalkForward;

/// <summary>
/// Metric used to rank training-window parameter sets when selecting the configuration that is
/// carried forward into each out-of-sample test window.
/// </summary>
public enum WalkForwardObjective
{
    SharpeRatio,
    SortinoRatio,
    CalmarRatio,
    TotalReturn,
    NetPnl,
    ProfitFactor
}

/// <summary>
/// Request for a walk-forward / out-of-sample validation sweep. The full period defined by
/// <see cref="BaseRequest"/> is split into rolling training/test windows; the parameter grid is
/// swept on each training window, the best parameter set (by <see cref="Objective"/>) is then run
/// once on the adjacent unseen test window, and the test-window results are stitched into
/// aggregate out-of-sample metrics.
/// </summary>
public sealed class WalkForwardRequest
{
    /// <summary>Base backtest request; its From/To span the full walk-forward period.</summary>
    public required BacktestRequest BaseRequest { get; init; }

    /// <summary>Parameter grid swept on every training window (one dictionary per candidate run).</summary>
    public required IReadOnlyList<Dictionary<string, object>> ParameterGrid { get; init; }

    /// <summary>Human-readable descriptor for the strategy under validation.</summary>
    public required string StrategyDescriptor { get; init; }

    /// <summary>Builds a strategy instance for a parameter set (used for both training and test runs).</summary>
    public required Func<IReadOnlyDictionary<string, object>, IBacktestStrategy> StrategyFactory { get; init; }

    /// <summary>Calendar days in each training window.</summary>
    public required int TrainingDays { get; init; }

    /// <summary>Calendar days in each out-of-sample test window.</summary>
    public required int TestDays { get; init; }

    /// <summary>
    /// Calendar days the window origin advances between windows. Zero (default) uses
    /// <see cref="TestDays"/>, producing contiguous, non-overlapping out-of-sample coverage.
    /// </summary>
    public int StepDays { get; init; }

    /// <summary>
    /// When true, every training window starts at the full period's start (expanding/anchored
    /// training); when false (default), training windows roll forward with a fixed length.
    /// </summary>
    public bool AnchoredTraining { get; init; }

    /// <summary>Ranking metric for training-window parameter selection.</summary>
    public WalkForwardObjective Objective { get; init; } = WalkForwardObjective.SharpeRatio;

    /// <summary>
    /// Optional custom objective; when set it overrides <see cref="Objective"/>. Higher is better.
    /// </summary>
    public Func<BacktestMetrics, double>? ObjectiveSelector { get; init; }

    /// <summary>Maximum concurrent training backtests per window.</summary>
    public int MaxConcurrency { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
}

/// <summary>One training/test window pair in a walk-forward sweep (all dates inclusive).</summary>
public sealed record WalkForwardWindow(
    int Index,
    DateOnly TrainFrom,
    DateOnly TrainTo,
    DateOnly TestFrom,
    DateOnly TestTo);

/// <summary>Outcome of a single walk-forward window: what was selected in-sample and how it did out-of-sample.</summary>
public sealed class WalkForwardWindowResult
{
    /// <summary>The window's date ranges.</summary>
    public required WalkForwardWindow Window { get; init; }

    /// <summary>Parameter set selected on the training window, or null when every training run failed.</summary>
    public IReadOnlyDictionary<string, object>? SelectedParameters { get; init; }

    /// <summary>Training-window metrics of the selected parameter set.</summary>
    public BacktestMetrics? TrainMetrics { get; init; }

    /// <summary>Objective value the selected parameters achieved in training.</summary>
    public double TrainObjective { get; init; }

    /// <summary>Full result of the out-of-sample test run, or null when it failed or was skipped.</summary>
    public BacktestResult? TestResult { get; init; }

    /// <summary>Objective value achieved on the unseen test window.</summary>
    public double TestObjective { get; init; }

    /// <summary>Number of training runs that completed successfully.</summary>
    public int TrainRunsSucceeded { get; init; }

    /// <summary>Number of training runs that failed.</summary>
    public int TrainRunsFailed { get; init; }

    /// <summary>Failure description when the window produced no out-of-sample result.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Aggregate out-of-sample metrics computed over the stitched daily-return series of all test
/// windows (capital re-normalized at each window boundary).
/// </summary>
public sealed record WalkForwardOosMetrics(
    decimal TotalReturn,
    decimal AnnualizedReturn,
    double SharpeRatio,
    decimal MaxDrawdownPercent,
    int TradingDays,
    int TotalTrades,
    decimal TotalCommissions);

/// <summary>Complete result of a walk-forward validation sweep.</summary>
public sealed class WalkForwardReport
{
    /// <summary>Per-window selection and out-of-sample outcomes, in chronological order.</summary>
    public required IReadOnlyList<WalkForwardWindowResult> Windows { get; init; }

    /// <summary>Stitched out-of-sample metrics, or null when no test window produced a result.</summary>
    public required WalkForwardOosMetrics? OosMetrics { get; init; }

    /// <summary>Mean training objective across windows that completed.</summary>
    public required double MeanTrainObjective { get; init; }

    /// <summary>Mean out-of-sample objective across windows that completed.</summary>
    public required double MeanTestObjective { get; init; }

    /// <summary>
    /// Out-of-sample / in-sample objective ratio (1.0 = no degradation; values well below 1
    /// indicate the grid is fitting noise). NaN when the training mean is not positive.
    /// </summary>
    public required double DegradationRatio { get; init; }

    /// <summary>Total wall-clock duration of the sweep.</summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>Honest caveats about the walk-forward methodology and any window failures.</summary>
    public required IReadOnlyList<BiasDisclosureItem> Disclosures { get; init; }
}

/// <summary>Progress report emitted while a walk-forward sweep runs.</summary>
public sealed class WalkForwardProgress
{
    /// <summary>Current phase: <c>training</c> or <c>testing</c>.</summary>
    public required string Phase { get; init; }

    /// <summary>Zero-based index of the window being processed.</summary>
    public int WindowIndex { get; init; }

    /// <summary>Total number of windows in the sweep.</summary>
    public int WindowCount { get; init; }

    /// <summary>Completed training runs within the current window (training phase only).</summary>
    public int CompletedRuns { get; init; }

    /// <summary>Total training runs within the current window (training phase only).</summary>
    public int TotalRuns { get; init; }

    /// <summary>Human-readable label for the current activity.</summary>
    public string CurrentLabel { get; init; } = "";
}
