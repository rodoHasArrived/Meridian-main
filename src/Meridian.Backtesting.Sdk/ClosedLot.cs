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
    /// Positive = gain; negative = loss.
    /// For long positions: <c>(ClosePrice − EntryPrice) × Quantity</c>.
    /// For short positions the caller negates the sign before constructing the record.
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
