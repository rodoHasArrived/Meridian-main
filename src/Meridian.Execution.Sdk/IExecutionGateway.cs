using Meridian.Contracts.Domain;

namespace Meridian.Execution.Sdk;

/// <summary>
/// Broker adapter contract for order routing. Each broker (IB, Alpaca, etc.)
/// implements this interface to translate platform orders into broker-specific API calls.
/// </summary>
public interface IExecutionGateway
{
    /// <summary>Gets the broker/gateway identifier.</summary>
    string GatewayId { get; }

    /// <summary>Gets whether the gateway is currently connected and ready to accept orders.</summary>
    bool IsConnected { get; }

    /// <summary>Connects to the broker execution endpoint.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnects from the broker.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Submits a new order to the broker.</summary>
    Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>Requests cancellation of an existing order.</summary>
    Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>Modifies an existing order (price/quantity).</summary>
    Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default);

    /// <summary>Streams execution reports (fills, rejections, cancellations).</summary>
    IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default);
}
