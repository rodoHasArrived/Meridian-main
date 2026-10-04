namespace Meridian.Execution.Sdk;

/// <summary>Identifies which broker namespace an order-cancellation value belongs to.</summary>
public enum OrderCancellationIdentifierKind
{
    /// <summary>The client-assigned order identifier submitted with the order.</summary>
    ClientOrderId,

    /// <summary>The broker-assigned order identifier returned after submission.</summary>
    BrokerOrderId
}

/// <summary>
/// A cancellation identifier whose namespace is explicit, so a client value that happens to look
/// like a broker identifier cannot resolve to a different order.
/// </summary>
/// <param name="Value">The identifier value.</param>
/// <param name="Kind">The namespace in which <paramref name="Value"/> must be resolved.</param>
public readonly record struct OrderCancellationIdentifier(
    string Value,
    OrderCancellationIdentifierKind Kind);

/// <summary>
/// Optional execution-gateway capability for brokers that keep client and broker order identifiers
/// in distinct cancellation namespaces.
/// </summary>
public interface IExplicitOrderCancellationGateway
{
    /// <summary>Requests cancellation using the explicitly named identifier namespace.</summary>
    Task<ExecutionReport> CancelOrderAsync(
        OrderCancellationIdentifier identifier,
        CancellationToken ct = default);
}
