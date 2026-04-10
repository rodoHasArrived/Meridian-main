namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Lightweight snapshot of rolling performance metrics emitted with each
/// <see cref="BacktestProgressEvent"/> while a backtest is in progress.
/// All values are approximate (computed from a running window) and are
/// finalized when the engine completes.
/// </summary>
public readonly record struct IntermediateMetrics(
    /// <summary>Rolling annualised Sharpe ratio (excess return over risk-free 0) based on daily returns seen so far.</summary>
    double RollingSharpe,
    /// <summary>Current drawdown from the peak portfolio equity, expressed as a positive fraction (0–1).</summary>
    double CurrentDrawdownPct,
    /// <summary>Number of fills executed so far.</summary>
    int TradeCount,
    /// <summary>Calendar trading days elapsed since backtest start.</summary>
    int TradingDaysSoFar);
