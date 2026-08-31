namespace Meridian.Execution.Models;

/// <summary>
/// A status change event for a specific order. Streamed via
/// <see cref="Interfaces.IOrderGateway.StreamOrderUpdatesAsync"/>.
/// Cost fields are populated on fills by gateways that model transaction costs
/// (commission, fees, and slippage as explicit cash amounts); they are <c>null</c> on
/// non-fill updates and for gateways without a cost model.
/// </summary>
public sealed record OrderStatusUpdate(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    OrderStatus Status,
    decimal FilledQuantity,
    decimal? AverageFillPrice,
    string? RejectReason,
    DateTimeOffset Timestamp,
    decimal? Commission = null,
    decimal? Fees = null,
    decimal? SlippageCost = null);
