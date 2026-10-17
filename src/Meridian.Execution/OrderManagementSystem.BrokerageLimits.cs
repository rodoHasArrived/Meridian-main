using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    // Runs under _preTradeReservationGate, before registering PendingNew. The registration
    // reserves a slot before the next admission, even while the broker has not acknowledged
    // this order or its open-order endpoint still returns an older snapshot.
    private async Task<string?> EvaluateBrokerageAdmissionLimitsAsync(OrderRequest request, CancellationToken ct)
    {
        var notionalRefusal = EvaluateBrokerageNotionalLimit(request);
        if (notionalRefusal is not null)
            return notionalRefusal;

        if (_brokerageConfiguration is not { MaxOpenOrders: > 0 } configuration)
            return null;

        var workingOrders = GetOpenOrders();
        var count = workingOrders.Count;
        if (count >= configuration.MaxOpenOrders)
            return $"Open order count exceeds configured maximum of {configuration.MaxOpenOrders}.";

        if (_gateway is IBrokerageGateway brokerage)
        {
            IReadOnlyList<BrokerOrder> brokerOrders;
            try
            {
                brokerOrders = await brokerage.GetOpenOrdersAsync(ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Broker returned no open-order snapshot.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return "Unable to evaluate configured open order limit: broker open-order snapshot is unavailable.";
            }

            // Count external broker orders too, without charging twice for tracked orders
            // that appear in both books. Broker ids and client ids are separate namespaces.
            var clientIds = workingOrders.Select(order => order.OrderId).ToHashSet(StringComparer.Ordinal);
            var brokerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var workingOrder in workingOrders)
            {
                if (_orderBrokerIds.TryGetValue(workingOrder.OrderId, out var brokerId))
                    brokerIds.TryAdd(brokerId, workingOrder.OrderId);
            }
            var matchedClientIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var brokerOrder in brokerOrders)
            {
                if (IsTerminalStatus(brokerOrder.Status))
                    continue;

                var matchingClientId = brokerIds.GetValueOrDefault(brokerOrder.OrderId);
                if (matchingClientId is null && brokerOrder.ClientOrderId is { } clientId
                    && clientIds.Contains(clientId) && !_orderBrokerIds.ContainsKey(clientId))
                    matchingClientId = clientId;

                // At most one broker row can overlap one local slot. Duplicate client ids
                // at different broker identities still consume separate broker capacity.
                if (matchingClientId is not null && matchedClientIds.Add(matchingClientId))
                    continue;
                count++;
            }
        }

        return count >= configuration.MaxOpenOrders
            ? $"Open order count exceeds configured maximum of {configuration.MaxOpenOrders}."
            : null;
    }

    private string? EvaluateBrokerageNotionalLimit(OrderRequest request)
    {
        if (_brokerageConfiguration is not { MaxOrderNotional: > 0m } configuration)
            return null;

        decimal? notional;
        try
        {
            notional = ResolveBrokerageOrderNotional(request);
        }
        catch (OverflowException)
        {
            return $"Order notional exceeds configured maximum of {configuration.MaxOrderNotional}.";
        }

        if (notional is null or <= 0m)
            return "Unable to evaluate order notional against the configured maximum without a positive price input.";

        return notional > configuration.MaxOrderNotional
            ? $"Order notional exceeds configured maximum of {configuration.MaxOrderNotional}."
            : null;
    }

    private static decimal? ResolveBrokerageOrderNotional(OrderRequest request)
    {
        var faceValue = OrderSizingMetadata.UsesFaceValuePercentageOfPar(request.Metadata);
        if (!faceValue && BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is { } routedNotional)
            return routedNotional;

        // The package's net limit does not establish the gross value of its individual
        // legs. Without leg pricing this configuration limit cannot authorize the order.
        if (request.Legs is { Count: > 0 })
            return null;

        var price = request.Type switch
        {
            OrderType.Limit or OrderType.StopLimit or OrderType.LimitOnOpen or OrderType.LimitOnClose => request.LimitPrice,
            OrderType.StopMarket => request.StopPrice,
            _ => request.LimitPrice ?? request.StopPrice
        };
        if (price is null or <= 0m)
            return null;

        var multiplier = faceValue ? 1m : ResolveContractMultiplier(request);
        if (!faceValue && request.OptionContract is null
            && SecurityIdentifierNormalizer.TryValidateFormat(SecurityIdentifierKind.OccOptionSymbol, request.Symbol, out _))
            multiplier = 100m;

        return Math.Abs(request.Quantity) * (faceValue ? price.Value / 100m : price.Value) * multiplier;
    }
}
