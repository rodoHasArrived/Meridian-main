namespace Meridian.Execution.Sdk;

/// <summary>
/// Optional typed execution-mode metadata for gateways that implement <see cref="IExecutionGateway"/>.
/// </summary>
public interface IExecutionGatewayModeProvider
{
    /// <summary>
    /// Identifies whether the gateway routes simulated or live orders.
    /// </summary>
    ExecutionMode ExecutionMode { get; }
}
