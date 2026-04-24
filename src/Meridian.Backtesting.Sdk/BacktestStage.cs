namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Execution stages emitted by the native backtest engine alongside
/// <see cref="BacktestProgressEvent"/>. Stages let operators see where time is
/// spent and surface actionable bottlenecks beyond a coarse percentage.
/// </summary>
/// <remarks>
/// Not every engine pass emits every stage. Corporate-action adjustment is lazy,
/// fill simulation is interleaved with replay, and artifact persistence is the
/// caller's responsibility in the current engine. Values are defined for the
/// full trust-and-velocity plan so downstream consumers can reason about the
/// complete surface today.
/// </remarks>
public enum BacktestStage
{
    /// <summary>Arguments and request shape are being validated.</summary>
    ValidatingRequest = 0,

    /// <summary>Symbol universe, security master, and tick sizes are being resolved.</summary>
    ValidatingCoverage = 1,

    /// <summary>Per-symbol replay streams are being opened.</summary>
    LoadingData = 2,

    /// <summary>Corporate-action adjustments are being applied to loaded bars.</summary>
    ApplyingCorporateActions = 3,

    /// <summary>Multi-symbol chronological merge and strategy dispatch are in progress.</summary>
    Replaying = 4,

    /// <summary>Pending orders are being matched against the active fill model.</summary>
    SimulatingFills = 5,

    /// <summary>End-of-run metrics are being computed from captured snapshots and fills.</summary>
    ComputingMetrics = 6,

    /// <summary>Run artifacts are being persisted (optional; caller-driven today).</summary>
    PersistingArtifacts = 7,

    /// <summary>Run finished successfully.</summary>
    Completed = 8,
}
