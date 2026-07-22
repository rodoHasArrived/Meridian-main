using Meridian.Backtesting.Sdk;

namespace Meridian.Strategies.Live;

/// <summary>
/// Supplies <see cref="IBacktestStrategy"/> implementations for promoted runs whose strategy id
/// is not covered by a hand-written live strategy. Sources registered in DI are composed into the
/// live catalog as fallbacks: any strategy they resolve reaches paper/live execution through
/// <see cref="BacktestStrategyLiveAdapter"/>, which contributes the lifecycle state machine.
/// </summary>
public interface IBacktestStrategyLiveSource
{
    /// <summary>
    /// Attempts to create the backtest strategy for a promoted run. Returning <c>false</c> with a
    /// <paramref name="failureReason"/> lets the catalog surface an actionable deferral message.
    /// </summary>
    bool TryCreate(
        LiveStrategyCreationContext context,
        out IBacktestStrategy? strategy,
        out string? failureReason);
}
