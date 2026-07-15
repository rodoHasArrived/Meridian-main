using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;

namespace Meridian.Execution.Adapters;

/// <summary>
/// The read-only <see cref="IOrderGateway"/> surface exposed for a <b>live</b> brokerage gateway.
/// </summary>
/// <remarks>
/// <para>
/// Live order submission and cancellation are owned exclusively by
/// <see cref="OrderManagementSystem"/> (the OMS), which enforces the pre-trade gate stack —
/// placement gate, live-order readiness, operator-control circuit breaker, security-master gate,
/// and risk validation — before any order reaches a broker. To make that ownership structural
/// rather than conventional, the live <see cref="IOrderGateway"/> resolved from dependency injection
/// is <em>this</em> view rather than the raw <see cref="BrokerageGatewayAdapter"/>: read-only broker
/// metadata and the execution-report stream are delegated to the adapter (so health, capability, and
/// blotter surfaces keep working), while <see cref="SubmitAsync"/> and <see cref="CancelAsync"/> are
/// blocked. A caller that resolves <see cref="IOrderGateway"/> and attempts to place or cancel a live
/// order therefore fails fast instead of silently bypassing the OMS gates.
/// </para>
/// <para>
/// The paper-trading gateway is unaffected: paper sessions have no live broker and no gate stack to
/// bypass, so their <see cref="IOrderGateway"/> remains fully functional.
/// </para>
/// </remarks>
internal sealed class OmsGovernedBrokerageOrderGateway : IOrderGateway
{
    private const string SubmissionBlockedMessage =
        "Live order submission must go through the Order Management System (IOrderManager.PlaceOrderAsync). " +
        "The live IOrderGateway is a read-only view; direct submission would bypass the pre-trade risk and " +
        "compliance gate stack.";

    private const string CancellationBlockedMessage =
        "Live order cancellation must go through the Order Management System (IOrderManager.CancelOrderAsync). " +
        "The live IOrderGateway is a read-only view; direct cancellation would bypass OMS order tracking.";

    private readonly BrokerageGatewayAdapter _inner;

    internal OmsGovernedBrokerageOrderGateway(BrokerageGatewayAdapter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public string BrokerName => _inner.BrokerName;

    /// <inheritdoc />
    public ExecutionMode Mode => _inner.Mode;

    /// <inheritdoc />
    public OrderGatewayCapabilities Capabilities => _inner.Capabilities;

    /// <inheritdoc />
    /// <remarks>
    /// Validation is side-effect free and remains available so operator surfaces can pre-flight an
    /// order, but a successful validation does not grant a submission path outside the OMS.
    /// </remarks>
    public Task<OrderValidationResult> ValidateOrderAsync(Meridian.Execution.Sdk.OrderRequest request, CancellationToken ct = default)
        => _inner.ValidateOrderAsync(request, ct);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Always thrown. Live orders must be placed through <see cref="OrderManagementSystem"/>.
    /// </exception>
    public Task<OrderAcknowledgement> SubmitAsync(Meridian.Execution.Sdk.OrderRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(SubmissionBlockedMessage);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Always thrown. Live orders must be cancelled through <see cref="OrderManagementSystem"/>.
    /// </exception>
    public Task<bool> CancelAsync(string orderId, CancellationToken ct = default)
        => throw new InvalidOperationException(CancellationBlockedMessage);

    /// <inheritdoc />
    public IAsyncEnumerable<OrderStatusUpdate> StreamOrderUpdatesAsync(CancellationToken ct = default)
        => _inner.StreamOrderUpdatesAsync(ct);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
