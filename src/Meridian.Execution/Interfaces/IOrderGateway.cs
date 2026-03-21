using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using GatewayExecutionMode = Meridian.Execution.Models.ExecutionMode;

namespace Meridian.Execution.Interfaces;

/// <summary>
/// Broker-agnostic abstraction for submitting, cancelling, and monitoring orders.
/// Implementations include <c>PaperTradingGateway</c> (simulated) and future live
/// broker adapters (Interactive Brokers, Alpaca). Enforced by ADR-015.
/// </summary>
public interface IOrderGateway : IAsyncDisposable
{
    /// <summary>Human-readable name of the broker or simulator (e.g., "Paper", "Alpaca").</summary>
    string BrokerName { get; }

    /// <summary>Indicates whether this gateway routes paper or live orders.</summary>
    GatewayExecutionMode Mode { get; }

    /// <summary>
    /// Provider-independent capability matrix for this gateway. Provider-specific
    /// nuances can be exposed via metadata without leaking provider types.
    /// </summary>
    OrderGatewayCapabilities Capabilities { get; }

    /// <summary>
    /// Validates a request against provider-independent constraints and any provider-specific
    /// implementation rules published through <see cref="OrderGatewayCapabilities"/>.
    /// </summary>
    Task<OrderValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Submits an order to the gateway. Returns an acknowledgement immediately; fills
    /// arrive asynchronously via <see cref="StreamOrderUpdatesAsync"/>.
    /// </summary>
    Task<OrderAcknowledgement> SubmitAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Requests cancellation of a working order. Returns <c>true</c> if the cancellation
    /// was accepted, <c>false</c> if the order was already terminal.
    /// </summary>
    Task<bool> CancelAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// Streams all order lifecycle events (fills, partial fills, cancellations, rejections)
    /// for this session. Callers should consume this stream on a dedicated background task.
    /// </summary>
    IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(CancellationToken ct = default);
}
