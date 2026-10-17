namespace Meridian.Execution.Sdk;

/// <summary>Optional amendment capability for brokers with distinct client and broker ID namespaces.</summary>
public interface IExplicitOrderModificationGateway
{
    /// <summary>Modifies the order in the explicitly identified namespace.</summary>
    Task<ExecutionReport> ModifyOrderAsync(
        OrderCancellationIdentifier identifier,
        OrderModification modification,
        CancellationToken ct = default);
}
