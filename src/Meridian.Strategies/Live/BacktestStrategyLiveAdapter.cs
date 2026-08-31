using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Strategies.Live;

/// <summary>
/// Adapts any <see cref="IBacktestStrategy"/> into an <see cref="Interfaces.ILiveStrategy"/> so
/// the exact strategy code validated by the backtest engine can be promoted to paper or live
/// execution without a rewrite. The adapter contributes the lifecycle state machine; every
/// market-event callback is forwarded to the wrapped strategy unchanged.
/// </summary>
public sealed class BacktestStrategyLiveAdapter : LiveStrategyBase
{
    private readonly IBacktestStrategy _inner;
    private readonly string _strategyId;

    /// <summary>
    /// Creates a live adapter over a backtest strategy.
    /// </summary>
    /// <param name="strategyId">Stable strategy identifier used by the run and lifecycle records.</param>
    /// <param name="inner">The backtest strategy whose callbacks drive trading decisions.</param>
    public BacktestStrategyLiveAdapter(string strategyId, IBacktestStrategy inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _strategyId = strategyId;
    }

    /// <inheritdoc/>
    public override string StrategyId => _strategyId;

    /// <inheritdoc/>
    public override string Name => _inner.Name;

    /// <inheritdoc/>
    public override void Initialize(IBacktestContext ctx) => _inner.Initialize(ctx);

    /// <inheritdoc/>
    public override void OnTrade(Trade trade, IBacktestContext ctx) => _inner.OnTrade(trade, ctx);

    /// <inheritdoc/>
    public override void OnQuote(BboQuotePayload quote, IBacktestContext ctx) => _inner.OnQuote(quote, ctx);

    /// <inheritdoc/>
    public override void OnBar(HistoricalBar bar, IBacktestContext ctx) => _inner.OnBar(bar, ctx);

    /// <inheritdoc/>
    public override void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => _inner.OnOrderBook(snapshot, ctx);

    /// <inheritdoc/>
    public override void OnOrderFill(FillEvent fill, IBacktestContext ctx) => _inner.OnOrderFill(fill, ctx);

    /// <inheritdoc/>
    public override void OnDayEnd(DateOnly date, IBacktestContext ctx) => _inner.OnDayEnd(date, ctx);

    /// <inheritdoc/>
    public override void OnFinished(IBacktestContext ctx) => _inner.OnFinished(ctx);
}
