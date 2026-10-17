namespace Meridian.Execution.Sdk;

/// <summary>Authoritative lookup of a retained client identity, including terminal orders.</summary>
public interface IBrokerageOrderRecoveryGateway
{
    /// <summary>
    /// Null means unresolved state, never permission to retry. FilledQuantity and FillPrice are
    /// the broker's cumulative filled quantity and cumulative average execution price.
    /// </summary>
    Task<ExecutionReport?> GetOrderForRecoveryAsync(string clientOrderId, CancellationToken ct = default);
}
