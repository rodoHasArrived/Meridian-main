using Meridian.Execution.Sdk;

namespace Meridian.Execution.Events;

/// <summary>
/// Canonical event for an accepted execution fill. For an OMS-tracked paper order it is published
/// after the portfolio mutation; live fills can carry zero realized P&amp;L when no portfolio
/// accounting context is attached. An order manager publishes it only for tracked orders and when an
/// <see cref="ITradeEventPublisher"/> is explicitly composed for the owning ledger scope; merely
/// enabling paper or live execution does not create an accounting book implicitly.
/// </summary>
/// <param name="FillId">Unique identifier for the fill that triggered this event.</param>
/// <param name="OrderId">The order that produced the fill.</param>
/// <param name="Symbol">Instrument symbol.</param>
/// <param name="Side">Buy or sell side.</param>
/// <param name="FilledQuantity">Number of shares/contracts filled (always positive).</param>
/// <param name="FillPrice">Price at which the fill was executed.</param>
/// <param name="Commission">Brokerage commission charged on this fill.</param>
/// <param name="RealizedPnl">
///     Realized P&amp;L produced by this fill (non-zero only when the fill closes or reduces
///     an existing position).
/// </param>
/// <param name="NewCash">Portfolio cash balance after applying the fill, or zero when unavailable.</param>
/// <param name="OccurredAt">Wall-clock timestamp of the fill.</param>
/// <param name="FinancialAccountId">
///     Optional brokerage account ID. <c>null</c> when the portfolio operates on a single
///     default account.
/// </param>
/// <param name="UsesFaceValuePercentageOfPar">
///     The gateway routed <paramref name="FilledQuantity"/> as face value and quoted
///     <paramref name="FillPrice"/> as a percentage of par (fixed income). Booking consumers
///     must not multiply the two raw: 100,000 face at 101.25 is $101,250, not $10,125,000.
/// </param>
public sealed record TradeExecutedEvent(
    Guid FillId,
    string OrderId,
    string Symbol,
    OrderSide Side,
    decimal FilledQuantity,
    decimal FillPrice,
    decimal Commission,
    decimal RealizedPnl,
    decimal NewCash,
    DateTimeOffset OccurredAt,
    string? FinancialAccountId = null,
    bool UsesFaceValuePercentageOfPar = false)
{
    /// <summary>
    /// Gross trade value, always positive. For a face-value fill the clean price is a
    /// percentage of par, so the price is scaled to a fraction of par before multiplying —
    /// the same convention pre-trade risk uses to measure the order — keeping the booked
    /// cash movement equal to the notional the risk gate approved.
    /// </summary>
    public decimal GrossValue =>
        FilledQuantity * (UsesFaceValuePercentageOfPar ? FillPrice / 100m : FillPrice);
}
