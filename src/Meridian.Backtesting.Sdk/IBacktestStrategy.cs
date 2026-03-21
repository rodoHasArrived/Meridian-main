namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Contract that all user-authored backtest strategies must implement.
/// The engine calls each callback exactly once per matching market event in chronological order.
/// </summary>
public interface IBacktestStrategy
{
    /// <summary>Human-readable strategy identifier.</summary>
    string Name { get; }

    /// <summary>
    /// Called once before replay begins. Use to initialise indicators, state, or log parameters.
    /// </summary>
    void Initialize(IBacktestContext ctx);

    /// <summary>Called for every <c>Trade</c> event in the replay stream.</summary>
    void OnTrade(Trade trade, IBacktestContext ctx);

    /// <summary>Called for every <c>BboQuotePayload</c> (best bid/offer) event.</summary>
    void OnQuote(BboQuotePayload quote, IBacktestContext ctx);

    /// <summary>Called for every <c>HistoricalBar</c> (OHLCV) event.</summary>
    void OnBar(HistoricalBar bar, IBacktestContext ctx);

    /// <summary>Called for every <c>LOBSnapshot</c> (Level-2 order book) event.</summary>
    void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx);

    /// <summary>Called immediately after each fill so the strategy can react.</summary>
    void OnOrderFill(FillEvent fill, IBacktestContext ctx);

    /// <summary>
    /// Called once per simulated trading day after all events for that date have been processed
    /// and interest/rebate accruals have been applied. Use for end-of-day rebalancing logic.
    /// </summary>
    void OnDayEnd(DateOnly date, IBacktestContext ctx);

    /// <summary>Called once after the last event has been replayed. Use to finalise state.</summary>
    void OnFinished(IBacktestContext ctx);
}
