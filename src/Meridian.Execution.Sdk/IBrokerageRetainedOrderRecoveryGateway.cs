namespace Meridian.Execution.Sdk;

/// <summary>
/// Order recovery that takes the retained order's identity, for a gateway that matches by a
/// client-supplied reference: a different instrument, side or type under the same reference is
/// refused before any route is installed for the match. Gateways that match by broker identity
/// need only <see cref="IBrokerageOrderRecoveryGateway"/>.
/// </summary>
public interface IBrokerageRetainedOrderRecoveryGateway : IBrokerageOrderRecoveryGateway
{
    /// <summary>Null means unresolved state, never permission to retry.</summary>
    Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, OrderState retainedOrder, CancellationToken ct = default);
}
