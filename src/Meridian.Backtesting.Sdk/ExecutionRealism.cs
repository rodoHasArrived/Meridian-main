namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Controls when an order becomes eligible for execution relative to the market event that
/// caused the strategy to place it.
/// </summary>
public enum FillTiming
{
    /// <summary>
    /// An order can only fill against market events strictly later than the event that was being
    /// dispatched when the order was placed. For daily bars this is classic next-bar execution:
    /// a signal computed on bar N fills no earlier than bar N+1. This is the trustworthy default —
    /// it removes the look-ahead bias of acting on a bar's close and filling inside that same bar.
    /// </summary>
    NextBar,

    /// <summary>
    /// Legacy behaviour: an order may fill against the same event that generated the signal.
    /// A strategy reacting to bar N's close can fill inside bar N, which embeds look-ahead bias.
    /// Only use for continuity with historical results; runs using this mode are flagged in the
    /// bias-disclosure report.
    /// </summary>
    SameBar
}

/// <summary>
/// Controls how optimistic the bar-based fill models are about limit and stop executions.
/// </summary>
public enum FillConservatism
{
    /// <summary>
    /// Conservative limit/stop semantics (default):
    /// <list type="bullet">
    ///   <item>A resting limit order fills at the bar open when the bar opens through the limit,
    ///   and otherwise requires the bar to trade strictly through the limit price — a bare touch
    ///   does not fill (queue-position risk).</item>
    ///   <item>A stop-market order fills no better than its stop price: the fill base is
    ///   max(stop, open) for buys and min(stop, open) for sells, so gaps through the stop fill at
    ///   the (worse) opening price, with slippage applied on top.</item>
    ///   <item>A stop-limit order newly triggered inside a bar fills at the worst in-range price
    ///   permitted by its limit, and does not fill at all on the trigger bar when the bar opened
    ///   beyond the limit.</item>
    /// </list>
    /// </summary>
    Conservative,

    /// <summary>
    /// Legacy optimistic semantics: limit orders fill at the limit price on a bare touch of the
    /// bar range, and triggered stop-market orders execute at the bar midpoint, which can be a
    /// better price than the stop itself. Runs using this mode are flagged in the bias-disclosure
    /// report.
    /// </summary>
    Optimistic
}

/// <summary>
/// Controls what happens to open positions in a symbol whose market data ends before the backtest
/// range does (a delisting, halt, or data gap — the engine cannot distinguish them).
/// </summary>
public enum DelistingPolicy
{
    /// <summary>
    /// Default: once a symbol has produced no events for <see cref="BacktestRequest.DelistingGraceDays"/>
    /// days while a position is open, the position is force-liquidated at the last observed price,
    /// adjusted by <see cref="BacktestRequest.DelistingHaircutPercent"/> against the position
    /// (longs receive less, shorts cover higher). Pending orders for the symbol are cancelled.
    /// Every forced liquidation is listed in the bias-disclosure report.
    /// </summary>
    LiquidateAtLastPrice,

    /// <summary>
    /// Legacy behaviour: keep the position open and marked at the last observed (stale) price
    /// until the end of the run. This silently overstates equity for delisted names and is
    /// flagged as a warning in the bias-disclosure report.
    /// </summary>
    Hold
}
