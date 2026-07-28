namespace Meridian.Execution.Sdk;

/// <summary>
/// Manages the full order lifecycle: creation, submission, tracking, and completion.
/// Central coordination point between strategies, risk checks, and execution gateways.
/// </summary>
public interface IOrderManager
{
    /// <summary>Places a new order after risk validation.</summary>
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>Cancels an open order.</summary>
    Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>Modifies an open order.</summary>
    Task<OrderResult> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default);

    /// <summary>Gets all currently open orders.</summary>
    IReadOnlyList<OrderState> GetOpenOrders();

    /// <summary>
    /// Orders whose exposure must still be reserved by pre-trade risk: the open book plus
    /// any fill whose accounting handoff has not yet reached the portfolio. Without the
    /// latter, a fill briefly belongs to neither the order book nor the position book, and
    /// a concurrent validation in that window measures understated exposure.
    /// Implementations without handoff tracking return the open book.
    /// </summary>
    IReadOnlyList<OrderState> GetExposureReservingOrders() => GetOpenOrders();

    /// <summary>Gets order state by ID.</summary>
    OrderState? GetOrder(string orderId);

    /// <summary>Cancels all open orders.</summary>
    Task CancelAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the most recently completed orders (filled, cancelled, rejected, expired) for display
    /// in the trading cockpit fills feed. Ordered by completion time, most recent first.
    /// </summary>
    IReadOnlyList<OrderState> GetCompletedOrders(int take = 20);
}
