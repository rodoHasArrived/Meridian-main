namespace Meridian.Execution.Sdk;

/// <summary>
/// Implemented by gateways whose order quantity can represent face value while the order price is
/// quoted as a percentage of par. The capability is per order because the same asset-class label
/// can carry different quantity semantics at different brokers.
/// </summary>
public interface IFaceValueOrderSizingGateway
{
    /// <summary>
    /// Whether this gateway will route <see cref="OrderRequest.Quantity"/> as face value and treat
    /// the order price as a percentage of par for this request.
    /// </summary>
    bool UsesFaceValuePercentageOfPar(OrderRequest request);
}
