namespace Meridian.Execution.Models;

/// <summary>
/// Confirmation returned immediately after an order is accepted by the gateway.
/// This does not indicate a fill — fills are delivered via <see cref="OrderStatusUpdate"/>.
/// </summary>
public sealed record OrderAcknowledgement(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    OrderStatus Status,
    DateTimeOffset AcknowledgedAt);
