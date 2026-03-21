using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Fallback fill model used when only OHLCV bar data is available.
/// Supports market, limit, stop-market, and stop-limit semantics with a
/// configurable midpoint slippage assumption.
/// </summary>
internal sealed class BarMidpointFillModel(
    ICommissionModel commissionModel,
    decimal slippageBasisPoints = 5m) : IFillModel
{
    public OrderFillResult TryFill(Order order, MarketEvent evt)
    {
        if (evt.Payload is not HistoricalBar bar)
            return new OrderFillResult(order, [], RemoveOrder: false);
        if (!bar.Symbol.Equals(order.Symbol, StringComparison.OrdinalIgnoreCase))
            return new OrderFillResult(order, [], RemoveOrder: false);
        if (order.IsComplete || order.Status is OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected)
            return new OrderFillResult(order, [], RemoveOrder: true);

        var isBuy = order.Quantity > 0;
        var triggered = order.IsTriggered || IsTriggered(order, bar, isBuy);
        var executableType = GetExecutableType(order.Type, triggered);

        if (executableType is null)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        if (!TryResolveFillPrice(bar, order, executableType.Value, isBuy, out var fillPrice))
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var remainingQuantity = order.RemainingSignedQuantity;
        var commission = commissionModel.Calculate(order.Symbol, remainingQuantity, fillPrice);
        var fill = new FillEvent(
            Guid.NewGuid(),
            order.OrderId,
            order.Symbol,
            remainingQuantity,
            fillPrice,
            commission,
            evt.Timestamp,
            order.AccountId);

        var updated = order with
        {
            FilledQuantity = order.FilledQuantity + remainingQuantity,
            Status = OrderStatus.Filled,
            IsTriggered = triggered
        };

        return new OrderFillResult(
            updated,
            [fill],
            RemoveOrder: true,
            WasTriggered: triggered && !order.IsTriggered);
    }

    private bool TryResolveFillPrice(HistoricalBar bar, Order order, OrderType executableType, bool isBuy, out decimal fillPrice)
    {
        fillPrice = 0m;

        switch (executableType)
        {
            case OrderType.Market:
                var mid = (bar.Open + bar.Close) / 2m;
                var slip = mid * (slippageBasisPoints / 10_000m);
                fillPrice = isBuy ? mid + slip : mid - slip;
                return true;

            case OrderType.Limit:
                var limitPrice = order.LimitPrice!.Value;
                if (isBuy && bar.Low > limitPrice)
                    return false;
                if (!isBuy && bar.High < limitPrice)
                    return false;
                fillPrice = limitPrice;
                return true;

            default:
                return false;
        }
    }

    private static bool IsTriggered(Order order, HistoricalBar bar, bool isBuy)
    {
        if (order.StopPrice is null)
            return order.Type is OrderType.Market or OrderType.Limit;

        return isBuy
            ? bar.High >= order.StopPrice.Value
            : bar.Low <= order.StopPrice.Value;
    }

    private static OrderType? GetExecutableType(OrderType originalType, bool triggered)
    {
        return originalType switch
        {
            OrderType.Market => OrderType.Market,
            OrderType.Limit => OrderType.Limit,
            OrderType.StopMarket when triggered => OrderType.Market,
            OrderType.StopLimit when triggered => OrderType.Limit,
            _ => null
        };
    }
}
