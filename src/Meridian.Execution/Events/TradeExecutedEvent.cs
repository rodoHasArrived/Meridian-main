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
    string? FinancialAccountId = null)
{
    /// <summary>Gross trade value (fill quantity × fill price, always positive).</summary>
    public decimal GrossValue => FilledQuantity * FillPrice;
}
