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

/// <summary>Places the final live authorization check at the provider's actual replacement send.</summary>
public interface IGuardedOrderModificationGateway
{
    Task<ExecutionReport> ModifyOrderAsync(OrderCancellationIdentifier identifier,
        OrderModification modification, Func<CancellationToken, Task<IAsyncDisposable>> acquireDispatch,
        CancellationToken ct = default);
}
