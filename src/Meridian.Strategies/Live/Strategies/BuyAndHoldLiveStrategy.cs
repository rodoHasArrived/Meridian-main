using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Strategies.Live.Strategies;

/// <summary>
/// Minimal concrete live strategy: buys a fixed quantity of each universe symbol on its first
/// observed price and holds. Useful as a promotion smoke-test strategy and as the simplest
/// reference implementation of the live strategy surface.
/// </summary>
public sealed class BuyAndHoldLiveStrategy : LiveStrategyBase
{
    public const string CatalogId = "buy-and-hold";

    private readonly string _strategyId;
    private readonly long _quantityPerSymbol;
    private readonly HashSet<string> _entered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a buy-and-hold strategy.
    /// </summary>
    /// <param name="strategyId">Stable strategy identifier; defaults to <see cref="CatalogId"/>.</param>
    /// <param name="quantityPerSymbol">Shares to buy per symbol on first tick (default 10).</param>
    public BuyAndHoldLiveStrategy(string? strategyId = null, long quantityPerSymbol = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantityPerSymbol);
        _strategyId = string.IsNullOrWhiteSpace(strategyId) ? CatalogId : strategyId;
        _quantityPerSymbol = quantityPerSymbol;
    }

    /// <inheritdoc/>
    public override string StrategyId => _strategyId;

    /// <inheritdoc/>
    public override string Name => "Buy & Hold";

    /// <inheritdoc/>
    public override void OnTrade(Trade trade, IBacktestContext ctx) =>
        EnterIfFlat(trade.Symbol, ctx);

    /// <inheritdoc/>
    public override void OnBar(HistoricalBar bar, IBacktestContext ctx) =>
        EnterIfFlat(bar.Symbol, ctx);

    private void EnterIfFlat(string symbol, IBacktestContext ctx)
    {
        if (!ctx.Universe.Contains(symbol) || !_entered.Add(symbol))
        {
            return;
        }

        ctx.PlaceMarketOrder(symbol, _quantityPerSymbol);
    }
}
