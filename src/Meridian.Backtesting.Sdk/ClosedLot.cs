namespace Meridian.Backtesting.Sdk;

/// <summary>
/// An immutable record of a lot that has been fully or partially closed.
/// Created by <c>SimulatedPortfolio</c> whenever <see cref="OpenLot"/> shares are
/// matched against a sell or cover-short fill.
/// </summary>
/// <param name="LotId">The <see cref="OpenLot.LotId"/> of the original opening lot.</param>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="Quantity">Number of shares closed in this record; always positive.</param>
/// <param name="EntryPrice">Per-share entry price from the original open lot.</param>
/// <param name="OpenedAt">Timestamp of the original fill that created the open lot.</param>
/// <param name="OpenFillId">Fill ID of the original opening transaction.</param>
/// <param name="ClosePrice">Per-share price received (long close) or paid (short cover) to close.</param>
/// <param name="ClosedAt">Timestamp of the fill that closed this lot.</param>
/// <param name="CloseFillId">Fill ID of the closing transaction.</param>
/// <param name="AccountId">Optional account in which the lot was held.</param>
public sealed record ClosedLot(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    Guid OpenFillId,
    decimal ClosePrice,
    DateTimeOffset ClosedAt,
    Guid CloseFillId,
    string? AccountId = null)
{
    /// <summary>
    /// Realised P&amp;L for this closed lot.
    /// Always positive for a gain, negative for a loss.
    /// Formula: <c>(ClosePrice − EntryPrice) × Quantity</c>.
    /// <para>
    /// For <b>long</b> positions <c>EntryPrice</c> is the acquisition cost and <c>ClosePrice</c>
    /// is the sale price, so the formula yields a profit when the stock appreciated.
    /// </para>
    /// <para>
    /// For <b>short</b> positions the price arguments are intentionally swapped by the
    /// constructor: <c>EntryPrice</c> receives the cover (buy-back) price and
    /// <c>ClosePrice</c> receives the original short-sale price.  The formula therefore
    /// yields a profit when the cover price is lower than the short-sale price, matching
    /// the sign convention of the portfolio's realised P&amp;L calculation.
    /// </para>
    /// </summary>
    public decimal RealizedPnl => (ClosePrice - EntryPrice) * Quantity;

    /// <summary>Calendar time between opening and closing this lot.</summary>
    public TimeSpan HoldingPeriod => ClosedAt - OpenedAt;

    /// <summary>
    /// Returns <see langword="true"/> when the holding period is at least 365 days
    /// (commonly the threshold for long-term capital gains treatment).
    /// </summary>
    public bool IsLongTerm => HoldingPeriod >= TimeSpan.FromDays(365);

    /// <summary>Per-share realised P&amp;L: <c>ClosePrice − EntryPrice</c>.</summary>
    public decimal RealizedPnlPerShare => ClosePrice - EntryPrice;
}
