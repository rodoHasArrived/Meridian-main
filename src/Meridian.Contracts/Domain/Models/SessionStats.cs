namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Per-symbol intraday session statistics derived from observed trades.
/// Captures the conventional "today" reference points a watchlist or quote display needs:
/// open, high, low, last, total volume, and a volume-weighted average price.
/// </summary>
/// <remarks>
/// The session boundary is the calendar date in UTC. When a trade arrives whose UTC date
/// differs from <see cref="SessionDate"/>, the collector resets the state and starts a
/// new session anchored to the new date's first observed trade. This keeps the model
/// honest in the absence of an exchange-local trading calendar; consumers that need a
/// different anchor (e.g. America/New_York midnight) can plug in a different collector.
/// </remarks>
public sealed record SessionStats(
    string Symbol,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Last,
    long Volume,
    decimal Vwap,
    long TradeCount,
    DateTimeOffset FirstTradeAt,
    DateTimeOffset LastTradeAt)
{
    /// <summary>
    /// Absolute change in price from the session open to the most recent trade.
    /// </summary>
    public decimal Change => Last - Open;

    /// <summary>
    /// Percentage change from the session open to the most recent trade. Returns
    /// <c>null</c> when the open price is zero (cannot compute a percentage).
    /// </summary>
    public decimal? ChangePercent => Open == 0m ? null : (Last - Open) / Open * 100m;
}
