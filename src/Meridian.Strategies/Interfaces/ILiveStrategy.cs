namespace Meridian.Strategies.Interfaces;

/// <summary>
/// A strategy that can participate in both the backtest engine and live execution.
/// Combines the <see cref="IBacktestStrategy"/> replay callbacks with the
/// <see cref="IStrategyLifecycle"/> live-mode management contract.
/// </summary>
/// <remarks>
/// Not all strategies need live execution capability. Strategies that implement
/// this interface are intended to support both backtesting and live execution,
/// while strategies that only implement <see cref="IBacktestStrategy"/> remain
/// backtest-only.
/// </remarks>
public interface ILiveStrategy : IBacktestStrategy, IStrategyLifecycle { }
