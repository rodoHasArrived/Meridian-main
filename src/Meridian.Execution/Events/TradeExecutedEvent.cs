using Meridian.Execution.Sdk;

namespace Meridian.Execution.Events;

/// <summary>
/// Domain event raised when an order fill is applied to the portfolio.
/// Published by both paper-trading and live-execution paths so that
/// downstream consumers (e.g. <see cref="LedgerPostingConsumer"/>) can
/// react without the portfolio holding a hard dependency on the ledger.
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
/// <param name="NewCash">Portfolio cash balance after applying the fill.</param>
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
    long FilledQuantity,
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
