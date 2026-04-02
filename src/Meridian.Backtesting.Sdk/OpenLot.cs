namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Represents an open (unrealised) tax lot — a block of shares acquired in a single fill.
/// Lots are always positive-quantity records. Short positions use a parallel short-lots
/// collection (see <see cref="ClosedLot"/> for closed counterparts).
/// </summary>
public sealed record OpenLot(
    Guid LotId,
    string Symbol,
    long Quantity,           // always positive; lots are never negative
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    Guid OpenFillId,
    string? AccountId = null,
    string? Notes = null)
{
    /// <summary>Mark-to-market unrealised P&amp;L for this lot at the given price.</summary>
    public decimal UnrealizedPnl(decimal currentPrice) =>
        (currentPrice - EntryPrice) * Quantity;

    /// <summary>Current notional value of this lot at the given price.</summary>
    public decimal NotionalValue(decimal currentPrice) =>
        Quantity * currentPrice;

    /// <summary>How long this lot has been open as of the given point in time.</summary>
    public TimeSpan Age(DateTimeOffset asOf) => asOf - OpenedAt;

    /// <summary>
    /// Returns <c>true</c> when the lot has been held for at least 365 days — the IRS
    /// long-term capital gains threshold.
    /// </summary>
    public bool IsLongTerm(DateTimeOffset asOf) =>
        asOf - OpenedAt >= TimeSpan.FromDays(365);
}
