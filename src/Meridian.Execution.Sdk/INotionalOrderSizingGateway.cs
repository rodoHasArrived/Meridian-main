namespace Meridian.Execution.Sdk;

/// <summary>
/// Implemented by gateways that size orders from broker-native notional metadata — the
/// <see cref="BrokerNotionalMetadata.Keys"/> dollar amount rather than
/// <see cref="OrderRequest.Quantity"/>.
/// </summary>
/// <remarks>
/// Every rail that measures an order's economic size reads the routed notional from the
/// same metadata the gateway does. That is only correct where the gateway actually honors
/// it: a gateway that routes quantity would submit all 100,000 shares of an order carrying
/// <c>notional=1</c> while the pre-trade rules measured a one-dollar order, consuming none
/// of the notional, gross-exposure, or concentration limits. Gateways that route quantity
/// simply do not implement this interface, and the OMS refuses notional metadata on their
/// orders rather than measuring something the broker will not route.
/// </remarks>
public interface INotionalOrderSizingGateway
{
    /// <summary>
    /// Whether this gateway will route the broker-native notional metadata amount in place
    /// of <see cref="OrderRequest.Quantity"/> <em>for this order</em>.
    /// </summary>
    /// <remarks>
    /// Per-request, not per-gateway: an adapter can honour notional sizing for one asset
    /// class and route quantity for another. Alpaca does exactly that — it sends the dollar
    /// amount for equities but clears it and restores face-value quantity for fixed income,
    /// so a blanket capability would have the rails measure a treasury order at its metadata
    /// dollars while the broker receives thousands of face-value units.
    /// </remarks>
    bool RoutesNotionalMetadata(OrderRequest request);
}
