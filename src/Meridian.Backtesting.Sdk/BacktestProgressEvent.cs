namespace Meridian.Backtesting.Sdk;

/// <summary>Progress notification emitted by <c>BacktestEngine</c> during replay.</summary>
public sealed record BacktestProgressEvent(
    double ProgressFraction,        // 0.0 – 1.0
    DateOnly CurrentDate,
    decimal PortfolioValue,
    long EventsProcessed,
    string? Message = null,
    /// <summary>
    /// Rolling performance metrics available after at least 60 trading days have elapsed.
    /// <c>null</c> before that threshold or on the final completion event.
    /// </summary>
    IntermediateMetrics? LiveMetrics = null,
    /// <summary>
    /// Execution stage the engine was in when this event was emitted.
    /// Defaults to <see cref="BacktestStage.Replaying"/> so legacy callers and
    /// tests that construct events without a stage continue to observe the
    /// dominant replay-loop behaviour.
    /// </summary>
    BacktestStage Stage = BacktestStage.Replaying,
    /// <summary>
    /// Wall-clock time spent in the current <see cref="Stage"/> up to the moment
    /// this event was emitted. <see cref="TimeSpan.Zero"/> when the engine has
    /// not begun timing (e.g. externally constructed events).
    /// </summary>
    TimeSpan StageElapsed = default,
    /// <summary>
    /// Wall-clock time since the engine run started, across all stages.
    /// </summary>
    TimeSpan TotalElapsed = default,
    /// <summary>
    /// Consolidated stage telemetry for consumers that prefer a single object graph
    /// instead of individual stage and timing fields.
    /// </summary>
    BacktestStageTelemetryDto? StageTelemetry = null);
