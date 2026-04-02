namespace Meridian.Backtesting.Sdk;

/// <summary>
/// A single open position lot — one acquisition event (buy or short sale).
/// Lots are always stored with a positive <see cref="Quantity"/>; the direction
/// (long vs. short) is implied by the collection (<c>Lots</c> vs. <c>ShortLots</c>).
/// </summary>
/// <param name="LotId">Stable identifier assigned at lot creation.</param>
/// <param name="Symbol">Ticker symbol (e.g., "AAPL").</param>
/// <param name="Quantity">Number of shares in this lot; always positive.</param>
/// <param name="EntryPrice">Per-share price paid (long) or received (short) when the lot was opened.</param>
/// <param name="OpenedAt">Wall-clock time when the lot was created (fill timestamp).</param>
/// <param name="OpenFillId">The <see cref="FillEvent.FillId"/> that created this lot.</param>
/// <param name="AccountId">Optional account that holds this lot.</param>
/// <param name="Notes">Optional free-form annotation (e.g., strategy tag).</param>
public sealed record OpenLot(
    Guid LotId,
    string Symbol,
    long Quantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    Guid OpenFillId,
    string? AccountId = null,
    string? Notes = null)
{
    /// <summary>
    /// Mark-to-market unrealised P&amp;L for this lot at <paramref name="currentPrice"/>.
    /// Positive = gain; negative = loss.
    /// </summary>
    public decimal UnrealizedPnl(decimal currentPrice) =>
        (currentPrice - EntryPrice) * Quantity;

    /// <summary>Gross notional value of this lot at <paramref name="currentPrice"/>.</summary>
    public decimal NotionalValue(decimal currentPrice) =>
        Quantity * currentPrice;

    /// <summary>How long this lot has been open relative to <paramref name="asOf"/>.</summary>
    public TimeSpan Age(DateTimeOffset asOf) => asOf - OpenedAt;

    /// <summary>
    /// Returns <see langword="true"/> when the lot has been held for at least 365 days
    /// (commonly the threshold for long-term capital gains treatment).
    /// </summary>
    public bool IsLongTerm(DateTimeOffset asOf) =>
        asOf - OpenedAt >= TimeSpan.FromDays(365);
}
