namespace Meridian.Execution.Models;

/// <summary>
/// A status change event for a specific order. Streamed via
/// <see cref="Interfaces.IOrderGateway.StreamOrderUpdatesAsync"/>.
/// </summary>
public sealed record OrderStatusUpdate(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    OrderStatus Status,
    long FilledQuantity,
    decimal? AverageFillPrice,
    string? RejectReason,
    DateTimeOffset Timestamp);
