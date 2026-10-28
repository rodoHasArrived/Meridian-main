using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Meridian.Execution.Sdk;

/// <summary>A validated order whose sizing semantics were resolved by the active gateway.</summary>
public sealed class GatewayResolvedOrder
{
    private GatewayResolvedOrder(ValidatedOrder order, OrderSizing sizing)
    {
        Order = order;
        Sizing = sizing;
    }

    public ValidatedOrder Order { get; }
    public OrderRequest Request => Order.Request;
    public OrderSizing Sizing { get; }

    /// <summary>
    /// Resolves capabilities once per submission. A metadata label alone never establishes
    /// sizing authority; unsupported native notional is refused before risk can measure it.
    /// </summary>
    public static bool TryResolve(
        ValidatedOrder order,
        IExecutionGateway gateway,
        [NotNullWhen(true)] out GatewayResolvedOrder? resolved,
        out string? rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(gateway);
        var request = order.Request;
        var notional = BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity);
        if (notional is not null
            && (gateway as INotionalOrderSizingGateway)?.RoutesNotionalMetadata(request) is not true)
        {
            resolved = null;
            rejectionReason = $"Broker-native notional sizing metadata is not supported by {gateway.GatewayId}; this gateway "
                + "routes order quantity. Remove the notional metadata and size the order in quantity.";
            return false;
        }

        OrderSizing sizing = (gateway as IFaceValueOrderSizingGateway)?.UsesFaceValuePercentageOfPar(request) is true
            ? new OrderSizing.FaceValuePercentageOfPar()
            : notional is { } amount
                ? new OrderSizing.BrokerNotional(amount)
                : new OrderSizing.Quantity();
        resolved = new GatewayResolvedOrder(order, sizing);
        rejectionReason = null;
        return true;
    }

    /// <summary>
    /// Compatibility boundary for existing risk rules. Caller markers are removed and only
    /// the resolved sizing can stamp one; the gateway request itself is never stamped.
    /// </summary>
    public OrderRequest ToRiskRequest()
    {
        var metadata = Request.Metadata is { } source ? new Dictionary<string, string>(source) : null;
        if (metadata is not null)
        {
            foreach (var key in metadata.Keys.Where(key => string.Equals(
                key, OrderSizingMetadata.FaceValuePercentageOfParKey, StringComparison.OrdinalIgnoreCase)).ToArray())
                metadata.Remove(key);
        }

        if (Sizing is OrderSizing.FaceValuePercentageOfPar)
        {
            metadata ??= new Dictionary<string, string>();
            metadata[OrderSizingMetadata.FaceValuePercentageOfParKey] = bool.TrueString;
        }

        return Request with
        {
            Metadata = metadata is null ? null : new ReadOnlyDictionary<string, string>(metadata)
        };
    }
}

/// <summary>Closed gateway-resolved sizing cases; constructed only during resolution.</summary>
public abstract class OrderSizing
{
    private OrderSizing() { }

    public sealed class Quantity : OrderSizing
    {
        internal Quantity() { }
    }

    public sealed class BrokerNotional : OrderSizing
    {
        internal BrokerNotional(decimal amount) => Amount = amount;
        public decimal Amount { get; }
    }

    public sealed class FaceValuePercentageOfPar : OrderSizing
    {
        internal FaceValuePercentageOfPar() { }
    }
}
