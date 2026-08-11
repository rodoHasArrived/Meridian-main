using Meridian.Execution.Sdk;
using Meridian.Execution.Interfaces;

namespace Meridian.Execution.PaperMatching;

/// <summary>
/// The observed market data in effect for one symbol at paper-matching time: the last
/// best-bid/offer quote, the last trade print, and the last completed bar, whichever are
/// available. The observation defines the price envelope a paper fill may never leave —
/// fills are anchored to observed market data, not fabricated prices.
/// </summary>
public readonly record struct PaperMarketObservation
{
    /// <summary>Best observed bid price, when a quote has been seen.</summary>
    public decimal? BidPrice { get; init; }

    /// <summary>Best observed ask price, when a quote has been seen.</summary>
    public decimal? AskPrice { get; init; }

    /// <summary>Last observed trade price, when a trade print has been seen.</summary>
    public decimal? LastTradePrice { get; init; }

    /// <summary>Low of the last observed bar, when bar data is in effect.</summary>
    public decimal? BarLow { get; init; }

    /// <summary>High of the last observed bar, when bar data is in effect.</summary>
    public decimal? BarHigh { get; init; }

    /// <summary>Close of the last observed bar, when bar data is in effect.</summary>
    public decimal? BarClose { get; init; }

    /// <summary>True when at least one usable observed price is present.</summary>
    public bool HasAnyObservation =>
        BidPrice.HasValue || AskPrice.HasValue || LastTradePrice.HasValue
        || BarLow.HasValue || BarHigh.HasValue || BarClose.HasValue;

    /// <summary>
    /// Lower bound of the observed price envelope: the minimum across every observed
    /// price component present (bid, last trade, bar low/close).
    /// </summary>
    public decimal? EnvelopeLow => MinPresent(BidPrice, LastTradePrice, BarLow, BarClose, AskPrice);

    /// <summary>
    /// Upper bound of the observed price envelope: the maximum across every observed
    /// price component present (ask, last trade, bar high/close).
    /// </summary>
    public decimal? EnvelopeHigh => MaxPresent(AskPrice, LastTradePrice, BarHigh, BarClose, BidPrice);

    /// <summary>Quote midpoint when both sides of the book have been observed.</summary>
    public decimal? MidPrice =>
        BidPrice is { } bid && AskPrice is { } ask ? (bid + ask) / 2m : null;

    /// <summary>
    /// The price a stop on <paramref name="side"/> triggers against: the last trade, then the
    /// bar close, and only then the side the order would cross. <see langword="null"/> when
    /// nothing has been observed.
    /// <para>
    /// This is the single definition of that precedence, and it exists as one method precisely
    /// so it cannot drift. Both the matcher (which decides whether a stop fires) and the
    /// pre-trade fat-finger guard (which decides whether a stop is on the wrong side of the
    /// market and would fire on arrival) resolve through here. A guard that reconstructed the
    /// precedence separately would disagree with the engine on exactly the cases that matter —
    /// approving an already-triggered stop, or refusing one that is still resting.
    /// </para>
    /// </summary>
    public decimal? ResolveStopTriggerPrice(OrderSide side) =>
        LastTradePrice ?? BarClose ?? (side == OrderSide.Buy ? AskPrice : BidPrice);

    /// <summary>
    /// Captures the current observation for <paramref name="symbol"/> from a live feed
    /// adapter. Non-positive prices are treated as absent. Returns an empty observation
    /// when no feed is wired or nothing has been observed yet.
    /// </summary>
    public static PaperMarketObservation Capture(ILiveFeedAdapter? feed, string symbol)
    {
        if (feed is null || string.IsNullOrWhiteSpace(symbol))
        {
            return default;
        }

        decimal? bid = null;
        decimal? ask = null;
        if (feed.GetLastQuote(symbol) is { } quote)
        {
            bid = Positive(quote.BidPrice);
            ask = Positive(quote.AskPrice);
        }

        decimal? lastTrade = feed.GetLastTrade(symbol) is { } trade ? Positive(trade.Price) : null;

        decimal? barLow = null;
        decimal? barHigh = null;
        decimal? barClose = null;
        if (feed.GetLastBar(symbol) is { } bar)
        {
            barLow = Positive(bar.Low);
            barHigh = Positive(bar.High);
            barClose = Positive(bar.Close);
        }

        return new PaperMarketObservation
        {
            BidPrice = bid,
            AskPrice = ask,
            LastTradePrice = lastTrade,
            BarLow = barLow,
            BarHigh = barHigh,
            BarClose = barClose
        };
    }

    /// <summary>
    /// Builds a degenerate observation from a caller-supplied simulated price (the
    /// documented <c>Market</c>-order seam where the caller provides its own mark).
    /// The envelope collapses to that single price.
    /// </summary>
    public static PaperMarketObservation FromCallerPrice(decimal price) =>
        Positive(price) is { } positive
            ? new PaperMarketObservation { LastTradePrice = positive }
            : default;

    private static decimal? Positive(decimal value) => value > 0m ? value : null;

    private static decimal? MinPresent(params decimal?[] values)
    {
        decimal? result = null;
        foreach (var value in values)
        {
            if (value is { } present && (result is null || present < result))
            {
                result = present;
            }
        }

        return result;
    }

    private static decimal? MaxPresent(params decimal?[] values)
    {
        decimal? result = null;
        foreach (var value in values)
        {
            if (value is { } present && (result is null || present > result))
            {
                result = present;
            }
        }

        return result;
    }
}
