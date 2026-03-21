using Meridian.Strategies.Models;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Manages the lifecycle of a strategy that can run in live or paper execution mode.
/// Extends <see cref="IBacktestStrategy"/> so a single class can serve both backtest
/// and live contexts. Enforced by ADR-016.
/// </summary>
public interface IStrategyLifecycle
{
    /// <summary>Stable identifier for this strategy (e.g., a slug or assembly-qualified name).</summary>
    string StrategyId { get; }

    /// <summary>Current lifecycle state.</summary>
    StrategyStatus Status { get; }

    /// <summary>
    /// Starts the strategy against the provided execution context. Transitions the strategy
    /// from <see cref="StrategyStatus.Registered"/> (or <see cref="StrategyStatus.Stopped"/>)
    /// to <see cref="StrategyStatus.WarmingUp"/> and then <see cref="StrategyStatus.Running"/>.
    /// </summary>
    Task StartAsync(IExecutionContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Pauses event processing. The strategy retains its state and can be resumed
    /// by calling <see cref="StartAsync"/> again.
    /// </summary>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the strategy, finalises its state, and records the completed run.
    /// Transitions to <see cref="StrategyStatus.Stopped"/>.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
}
