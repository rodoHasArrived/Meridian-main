namespace Meridian.Backtesting.Sdk.Strategies;

/// <summary>One deterministic long-only decision implementation for replay and paper execution.</summary>
public sealed class MovingAverageCrossoverStrategy : BacktestStrategyBase, IBacktestOrderTerminationObserver
{
    public const string Version = "sma-crossover-v1";
    private readonly int _fast;
    private readonly int _slow;
    private readonly long _quantity;
    private readonly bool _bars;
    private readonly Dictionary<string, Queue<decimal>> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, (string Symbol, long Remaining)> _pending = [];
    private readonly HashSet<string> _submitting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastObservation = new(StringComparer.OrdinalIgnoreCase);

    public MovingAverageCrossoverStrategy(int fastPeriod = 10, int slowPeriod = 30, long quantity = 10, bool barBased = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fastPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (slowPeriod <= fastPeriod)
            throw new ArgumentOutOfRangeException(nameof(slowPeriod));
        _fast = fastPeriod;
        _slow = slowPeriod;
        _quantity = quantity;
        _bars = barBased;
    }
    public override string Name => $"SMA Crossover {_fast}/{_slow}";
    public string InputMode => _bars ? "bars" : "trades";
    public override void OnTrade(Trade trade, IBacktestContext ctx)
    { if (!_bars) Observe(trade.Symbol, trade.Price, trade.Timestamp, ctx); }
    public override void OnBar(HistoricalBar bar, IBacktestContext ctx)
    { if (_bars) Observe(bar.Symbol, bar.Close, ctx.CurrentTime, ctx); }

    private void Observe(string symbol, decimal price, DateTimeOffset at, IBacktestContext ctx)
    {
        if (price <= 0m || !ctx.Universe.Contains(symbol) || (_lastObservation.TryGetValue(symbol, out var last) && at <= last))
            return;
        _lastObservation[symbol] = at;
        if (!_windows.TryGetValue(symbol, out var window))
            _windows[symbol] = window = new();
        window.Enqueue(price);
        while (window.Count > _slow)
            window.Dequeue();
        if (window.Count < _slow || _submitting.Contains(symbol) || _pending.Values.Any(order => order.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            return;
        var fast = window.TakeLast(_fast).Average();
        var slow = window.Average();
        var held = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;
        var quantity = fast > slow && held == 0 ? _quantity : fast < slow && held > 0 ? -held : 0L;
        if (quantity == 0)
            return;
        _submitting.Add(symbol);
        try
        { var id = ctx.PlaceMarketOrder(symbol, quantity); _pending.Add(id, (symbol, quantity)); }
        finally { _submitting.Remove(symbol); }
    }

    public override void OnOrderFill(FillEvent fill, IBacktestContext ctx)
    {
        if (!_pending.TryGetValue(fill.OrderId, out var order))
            return;
        var remaining = order.Remaining - fill.FilledQuantity;
        if (remaining == 0)
            _pending.Remove(fill.OrderId);
        else if (Math.Sign(remaining) == Math.Sign(order.Remaining))
            _pending[fill.OrderId] = (order.Symbol, remaining);
        else
            throw new InvalidDataException("Fill exceeds the strategy's retained pending quantity.");
    }
    public void OrderTerminated(Guid orderId) => _pending.Remove(orderId);
    public void OnOrderTerminated(Guid orderId) => OrderTerminated(orderId);
}

/// <summary>A terminal order without a complete fill must release a strategy's pending intent.</summary>
public interface IBacktestOrderTerminationObserver
{
    void OnOrderTerminated(Guid orderId);
}
