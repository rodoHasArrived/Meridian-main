using Meridian.Backtesting.Sdk;
using Meridian.Backtesting.Sdk.Strategies;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Strategies.Live.Strategies;

/// <summary>Lifecycle wrapper over the same deterministic strategy used by backtests.</summary>
public sealed class MovingAverageCrossoverLiveStrategy : LiveStrategyBase, ILiveOrderOutcomeObserver
{
    public const string CatalogId = "moving-average-crossover";
    private readonly string _strategyId;
    private readonly MovingAverageCrossoverStrategy _inner;

    public MovingAverageCrossoverLiveStrategy(string? strategyId = null, int fastPeriod = 10,
        int slowPeriod = 30, long quantity = 10) : this(strategyId, fastPeriod, slowPeriod, quantity, barBased: false) { }

    public MovingAverageCrossoverLiveStrategy(string? strategyId, int fastPeriod, int slowPeriod, long quantity, bool barBased)
    {
        _strategyId = string.IsNullOrWhiteSpace(strategyId) ? CatalogId : strategyId;
        _inner = new(fastPeriod, slowPeriod, quantity, barBased);
    }
    public override string StrategyId => _strategyId;
    public override string Name => _inner.Name;
    public override void Initialize(IBacktestContext ctx) => _inner.Initialize(ctx);
    public override void OnTrade(Trade trade, IBacktestContext ctx) => _inner.OnTrade(trade, ctx);
    public override void OnBar(HistoricalBar bar, IBacktestContext ctx) => _inner.OnBar(bar, ctx);
    public override void OnOrderFill(FillEvent fill, IBacktestContext ctx) => _inner.OnOrderFill(fill, ctx);
    public void OnOrderTerminated(Guid orderId, LiveOrderOutcome outcome) => _inner.OrderTerminated(orderId);
}
