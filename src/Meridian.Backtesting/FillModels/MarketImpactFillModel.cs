using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Fill model that simulates realistic market impact for large orders.
/// Uses a square-root market impact formula: impact = impactCoefficient * sqrt(orderQty / barVolume).
/// Fills are split into multiple partial fills to simulate time-slicing through bar volume.
/// Falls back to <see cref="BarMidpointFillModel"/> semantics for order types and triggers.
/// </summary>
internal sealed class MarketImpactFillModel(
    ICommissionModel commissionModel,
    decimal impactCoefficient = 0.1m,
    decimal slippageBasisPoints = 5m,
    int maxPartialFills = 5) : IFillModel
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

        if (!TryResolveBaseFillPrice(bar, order, executableType.Value, isBuy, out var baseFillPrice))
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var remainingAbsolute = order.RemainingQuantity;
        var barVolume = bar.Volume > 0 ? bar.Volume : 1L;

        // Calculate participation rate and market impact
        var participationRate = (decimal)remainingAbsolute / barVolume;
        var impactFraction = impactCoefficient * (decimal)Math.Sqrt((double)participationRate);

        // Determine number of partial fills based on order size vs. bar volume
        var numFills = participationRate > 0.05m
            ? Math.Min(maxPartialFills, (int)Math.Ceiling(participationRate * 10m))
            : 1;
        numFills = Math.Max(numFills, 1);

        var fills = new List<FillEvent>();
        var perSliceQuantity = remainingAbsolute / numFills;
        var remainder = remainingAbsolute % numFills;

        for (var i = 0; i < numFills; i++)
        {
            var sliceQty = perSliceQuantity + (i < remainder ? 1 : 0);
            if (sliceQty == 0)
                continue;

            // Impact increases with each slice (cumulative participation)
            var cumulativeParticipation = (decimal)(i + 1) / numFills;
            var sliceImpact = impactFraction * cumulativeParticipation;

            var fillPrice = isBuy
                ? baseFillPrice * (1m + sliceImpact)
                : baseFillPrice * (1m - sliceImpact);

            // Ensure limit orders don't exceed limit price
            if (executableType == OrderType.Limit)
            {
                if (isBuy && fillPrice > order.LimitPrice!.Value)
                    continue;
                if (!isBuy && fillPrice < order.LimitPrice!.Value)
                    continue;
            }

            var signedQuantity = isBuy ? sliceQty : -sliceQty;
            var commission = commissionModel.Calculate(order.Symbol, signedQuantity, fillPrice);
            fills.Add(new FillEvent(
                Guid.NewGuid(),
                order.OrderId,
                order.Symbol,
                signedQuantity,
                Math.Round(fillPrice, 4),
                commission,
                evt.Timestamp,
                order.AccountId));
        }

        if (fills.Count == 0)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var totalFilled = fills.Sum(static f => f.FilledQuantity);
        var updated = order with
        {
            FilledQuantity = order.FilledQuantity + totalFilled,
            Status = order.RemainingQuantity - Math.Abs(totalFilled) == 0
                ? OrderStatus.Filled
                : OrderStatus.PartiallyFilled,
            IsTriggered = triggered
        };

        return new OrderFillResult(
            updated,
            fills,
            RemoveOrder: updated.IsComplete,
            WasTriggered: triggered && !order.IsTriggered);
    }

    private bool TryResolveBaseFillPrice(HistoricalBar bar, Order order, OrderType executableType, bool isBuy, out decimal fillPrice)
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
