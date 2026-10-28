using Meridian.Execution.Sdk;

namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    // Runs under _preTradeReservationGate, before registering PendingNew. The registration
    // reserves a slot before the next admission, even while the broker has not acknowledged
    // this order or its open-order endpoint still returns an older snapshot.
    private async Task<string?> EvaluateBrokerageAdmissionLimitsAsync(OrderRequest request, CancellationToken ct)
    {
        var notionalRefusal = _admissionPolicy.EvaluateBrokerageNotionalLimit(request);
        if (notionalRefusal is not null)
            return notionalRefusal;

        if (_brokerageConfiguration is not { MaxOpenOrders: > 0 } configuration)
            return null;

        var workingOrders = GetOpenOrders().Concat(GetAmendmentExposureReservations())
            .DistinctBy(order => order.OrderId).ToList();
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
                if (_brokerIdentities.TryGetActiveId(workingOrder.OrderId, out var brokerId))
                    brokerIds.TryAdd(brokerId, workingOrder.OrderId);
            }
            // A broker snapshot can still expose a predecessor after the replacement was
            // acknowledged locally. Its retained identity belongs to the same working slot.
            foreach (var alias in _brokerIdentities.GetAliasOwners())
            {
                if (clientIds.Contains(alias.Value))
                    brokerIds.TryAdd(alias.Key, alias.Value);
            }
            var matchedClientIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var brokerOrder in brokerOrders)
            {
                if (IsTerminalStatus(brokerOrder.Status))
                    continue;

                var matchingClientId = brokerIds.GetValueOrDefault(brokerOrder.OrderId);
                if (matchingClientId is null && brokerOrder.ClientOrderId is { } clientId
                    && clientIds.Contains(clientId) && !_brokerIdentities.TryGetActiveId(clientId, out _))
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
}
