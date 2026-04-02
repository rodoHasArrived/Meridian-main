namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Immutable record of a realised tax lot — a block of shares that was opened and subsequently
/// fully or partially closed.  Each close produces exactly one <see cref="ClosedLot"/>.
/// </summary>
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
    /// <summary>Total realised profit or loss for this lot.</summary>
    public decimal RealizedPnl => (ClosePrice - EntryPrice) * Quantity;

    /// <summary>Total holding duration from open to close.</summary>
    public TimeSpan HoldingPeriod => ClosedAt - OpenedAt;

    /// <summary>
    /// Returns <c>true</c> when the lot was held for at least 365 days — the IRS
    /// long-term capital gains threshold.
    /// </summary>
    public bool IsLongTerm => HoldingPeriod >= TimeSpan.FromDays(365);

    /// <summary>Per-share realised P&amp;L (close price minus entry price).</summary>
    public decimal RealizedPnlPerShare => ClosePrice - EntryPrice;
}
