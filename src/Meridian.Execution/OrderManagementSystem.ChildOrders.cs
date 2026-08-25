using Meridian.Execution.Logging;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Execution;

// Gateway-created child orders (bracket TP/SL legs) join the tracked book here so their
// execution reports land on registered state and the kill-switch sweep can cancel them.
public sealed partial class OrderManagementSystem
{
    /// <summary>
    /// Registers the broker-created child orders a gateway acknowledgement carries (bracket/OCO
    /// take-profit and stop-loss legs) as tracked orders under the parent's accounting scope.
    /// <para>
    /// Without this the children exist only at the broker: their execution reports are dropped as
    /// "not tracked by this OMS" and a kill-switch sweep can truthfully report the in-memory book
    /// empty while live TP/SL legs rest at the broker. Each child is keyed the way
    /// <see cref="ProcessGatewayReportAsync"/> resolves reports — client order id first, broker
    /// order id otherwise — so later stream reports land on the registered state.
    /// </para>
    /// </summary>
    private void RegisterGatewayChildOrders(string parentOrderId, OrderState parent, ExecutionReport report)
    {
        if (report.ChildOrders is not { Count: > 0 } childOrders)
        {
            return;
        }

        foreach (var child in childOrders)
        {
            var childOrderId = string.IsNullOrWhiteSpace(child.ClientOrderId) ? child.OrderId : child.ClientOrderId;
            if (string.IsNullOrWhiteSpace(childOrderId) || IsTerminal(child.Status))
            {
                continue;
            }

            // A child already tracked keeps whatever state its own reports have built — including
            // a terminal one. TryRegisterOrder would resurrect a terminal entry, which is correct
            // for a reused client order id but wrong for a re-sighted child leg.
            if (_orders.ContainsKey(childOrderId))
            {
                continue;
            }

            var childState = new OrderState
            {
                OrderId = childOrderId,
                Symbol = string.IsNullOrWhiteSpace(child.Symbol) ? parent.Symbol : child.Symbol,
                Side = child.Side,
                Type = child.Type,
                Quantity = child.Quantity,
                FilledQuantity = child.FilledQuantity,
                LimitPrice = child.LimitPrice,
                StopPrice = child.StopPrice,
                Status = child.Status,
                CreatedAt = child.CreatedAt == default ? DateTimeOffset.UtcNow : child.CreatedAt,
                StrategyId = parent.StrategyId,
                // The parent's scope, deliberately: a bracket's exit legs settle into the same
                // fund account and derivative identity the entry was admitted under.
                FundAccountId = parent.FundAccountId,
                ContractMultiplier = parent.ContractMultiplier,
                OptionContract = parent.OptionContract
            };

            if (!TryRegisterOrder(childOrderId, childState))
            {
                continue;
            }

            // The same per-order side tables the parent got at registration, so child fills book
            // into the right session, fund account, and contract scale.
            if (_orderSessionIds.TryGetValue(parentOrderId, out var parentSessionId))
            {
                _orderSessionIds[childOrderId] = parentSessionId;
            }

            if (_orderFinancialAccountIds.TryGetValue(parentOrderId, out var parentFinancialAccountId))
            {
                _orderFinancialAccountIds[childOrderId] = parentFinancialAccountId;
            }

            if (_orderContractMultipliers.TryGetValue(parentOrderId, out var parentMultiplier))
            {
                _orderContractMultipliers[childOrderId] = parentMultiplier;
            }

            // Deliberately the broker-reported leg symbol, not childState.Symbol: the registered
            // state falls back to the parent's admitted request for a blank leg symbol, and this
            // diagnostic must not carry request-derived data — broker-echoed identifiers only.
            _logger.LogInformation(
                "Registered broker child order {ChildOrderId} ({Symbol}, {Status}) under parent {ParentOrderId}",
                LogSanitizer.Sanitize(childOrderId),
                LogSanitizer.Sanitize(child.Symbol),
                childState.Status,
                LogSanitizer.Sanitize(parentOrderId));
        }

        TrimRetainedOrdersIfNeeded();
    }
}
