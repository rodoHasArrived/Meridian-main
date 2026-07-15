namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Convenience base class for <see cref="IBacktestStrategy"/> implementations. Every callback is a
/// virtual no-op, so a strategy only overrides the events it actually cares about instead of
/// providing an empty stub for each of the eight interface members. Only <see cref="Name"/> is
/// abstract, since a strategy has no sensible default identifier.
/// </summary>
public abstract class BacktestStrategyBase : IBacktestStrategy
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public virtual void Initialize(IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnTrade(Trade trade, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnQuote(BboQuotePayload quote, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnOrderFill(FillEvent fill, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnDayEnd(DateOnly date, IBacktestContext ctx)
    {
    }

    /// <inheritdoc/>
    public virtual void OnFinished(IBacktestContext ctx)
    {
    }
}
