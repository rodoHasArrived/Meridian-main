using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Strategies.Live.Strategies;

/// <summary>
/// Concrete live strategy implementing a classic fast/slow simple-moving-average crossover:
/// enters a long position when the fast average crosses above the slow average and exits when
/// it crosses back below. Prices are taken from live trades (or bar closes when the feed is
/// bar-based). One position per symbol; no shorting.
/// </summary>
public sealed class MovingAverageCrossoverLiveStrategy : LiveStrategyBase
{
    public const string CatalogId = "moving-average-crossover";

    private readonly string _strategyId;
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly long _quantity;
    private readonly Dictionary<string, SymbolState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a moving-average crossover strategy.
    /// </summary>
    /// <param name="strategyId">Stable strategy identifier; defaults to <see cref="CatalogId"/>.</param>
    /// <param name="fastPeriod">Fast SMA length in observations (default 10).</param>
    /// <param name="slowPeriod">Slow SMA length in observations (default 30).</param>
    /// <param name="quantity">Shares per entry order (default 10).</param>
    public MovingAverageCrossoverLiveStrategy(
        string? strategyId = null,
        int fastPeriod = 10,
        int slowPeriod = 30,
        long quantity = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fastPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (slowPeriod <= fastPeriod)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slowPeriod),
                $"Slow period ({slowPeriod}) must be greater than fast period ({fastPeriod}).");
        }

        _strategyId = string.IsNullOrWhiteSpace(strategyId) ? CatalogId : strategyId;
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _quantity = quantity;
    }

    /// <inheritdoc/>
    public override string StrategyId => _strategyId;

    /// <inheritdoc/>
    public override string Name => $"SMA Crossover {_fastPeriod}/{_slowPeriod}";

    /// <inheritdoc/>
    public override void OnTrade(Trade trade, IBacktestContext ctx) =>
        Observe(trade.Symbol, trade.Price, ctx);

    /// <inheritdoc/>
    public override void OnBar(HistoricalBar bar, IBacktestContext ctx) =>
        Observe(bar.Symbol, bar.Close, ctx);

    private void Observe(string symbol, decimal price, IBacktestContext ctx)
    {
        if (price <= 0m || !ctx.Universe.Contains(symbol))
        {
            return;
        }

        if (!_states.TryGetValue(symbol, out var state))
        {
            state = new SymbolState(_slowPeriod);
            _states[symbol] = state;
        }

        state.Push(price);
        if (!state.IsWarm)
        {
            return;
        }

        var fast = state.Average(_fastPeriod);
        var slow = state.Average(_slowPeriod);
        var heldQuantity = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;

        if (fast > slow && heldQuantity <= 0 && !state.EntryPending)
        {
            state.EntryPending = true;
            ctx.PlaceMarketOrder(symbol, _quantity);
        }
        else if (fast < slow && heldQuantity > 0 && !state.ExitPending)
        {
            state.ExitPending = true;
            ctx.PlaceMarketOrder(symbol, -heldQuantity);
        }
    }

    /// <inheritdoc/>
    public override void OnOrderFill(FillEvent fill, IBacktestContext ctx)
    {
        if (!_states.TryGetValue(fill.Symbol, out var state))
        {
            return;
        }

        if (fill.FilledQuantity > 0)
        {
            state.EntryPending = false;
        }
        else if (fill.FilledQuantity < 0)
        {
            state.ExitPending = false;
        }
    }

    private sealed class SymbolState(int capacity)
    {
        private readonly Queue<decimal> _window = new(capacity);

        public bool EntryPending { get; set; }

        public bool ExitPending { get; set; }

        public bool IsWarm => _window.Count >= capacity;

        public void Push(decimal price)
        {
            _window.Enqueue(price);
            while (_window.Count > capacity)
            {
                _window.Dequeue();
            }
        }

        public decimal Average(int period)
        {
            var count = Math.Min(period, _window.Count);
            return count == 0 ? 0m : _window.TakeLast(count).Average();
        }
    }
}
