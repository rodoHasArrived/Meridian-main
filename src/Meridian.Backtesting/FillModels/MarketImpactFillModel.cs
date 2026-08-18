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
    int maxPartialFills = 5,
    decimal maxParticipationRate = 0m,
    FillConservatism conservatism = FillConservatism.Conservative) : IFillModel
{
    private readonly decimal _maxParticipationRate = Math.Clamp(maxParticipationRate, 0m, 1m);

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
        var newlyTriggered = triggered && !order.IsTriggered;
        var executableType = GetExecutableType(order.Type, triggered);

        if (executableType is null)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        if (!TryResolveBaseFillPrice(bar, order, executableType.Value, isBuy, newlyTriggered, out var baseFillPrice))
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var remainingAbsolute = order.RemainingQuantity;
        var fillableAbsolute = ComputeFillableQuantity(remainingAbsolute, bar.Volume);

        if (fillableAbsolute == 0)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        if (fillableAbsolute < remainingAbsolute && !order.AllowPartialFills)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        // Calculate participation rate and market impact
        var barVolume = bar.Volume > 0 ? bar.Volume : 1L;
        var participationRate = (decimal)fillableAbsolute / barVolume;
        var impactFraction = impactCoefficient * (decimal)Math.Sqrt((double)participationRate);

        // Determine number of partial fills based on order size vs. bar volume
        var numFills = participationRate > 0.05m
            ? Math.Min(maxPartialFills, (int)Math.Ceiling(participationRate * 10m))
            : 1;
        numFills = Math.Max(numFills, 1);

        var fills = new List<FillEvent>();
        var perSliceQuantity = fillableAbsolute / numFills;
        var remainder = fillableAbsolute % numFills;

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
            var commission = commissionModel
                .Quote(order.OrderId, order.Symbol, signedQuantity, fillPrice)
                .Amount;
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

    private long ComputeFillableQuantity(long remainingAbsolute, long barVolume)
    {
        if (_maxParticipationRate <= 0m)
            return remainingAbsolute;
        if (barVolume <= 0)
            return 0L;

        var barVolumeCap = (long)(barVolume * _maxParticipationRate);
        return Math.Min(remainingAbsolute, Math.Max(0L, barVolumeCap));
    }

    private bool TryResolveBaseFillPrice(HistoricalBar bar, Order order, OrderType executableType, bool isBuy, bool newlyTriggered, out decimal fillPrice)
    {
        fillPrice = 0m;

        switch (executableType)
        {
            case OrderType.Market:
                // Conservative stop-market pricing mirrors BarMidpointFillModel: a stop that
                // triggers inside this bar anchors to the worse of the stop and the open instead
                // of the midpoint, so the base fill can never beat the stop price.
                if (conservatism == FillConservatism.Conservative
                    && order.Type is OrderType.StopMarket
                    && newlyTriggered
                    && order.StopPrice is { } stop)
                {
                    var stopBase = isBuy ? Math.Max(stop, bar.Open) : Math.Min(stop, bar.Open);
                    var stopSlip = stopBase * (slippageBasisPoints / 10_000m);
                    fillPrice = isBuy ? stopBase + stopSlip : stopBase - stopSlip;
                    return true;
                }

                // Midpoint is (Open + Close) / 2 — the bar's open-to-close centre — consistent
                // with BarMidpointFillModel. See that model's summary XML doc for the rationale.
                var mid = (bar.Open + bar.Close) / 2m;
                var slip = mid * (slippageBasisPoints / 10_000m);
                fillPrice = isBuy ? mid + slip : mid - slip;
                return true;

            case OrderType.Limit:
                var limitPrice = order.LimitPrice!.Value;

                if (conservatism == FillConservatism.Conservative)
                {
                    if (order.Type == OrderType.StopLimit && newlyTriggered)
                        return TryResolveConservativeTriggerBarStopLimit(bar, order.StopPrice!.Value, limitPrice, isBuy, out fillPrice);
                    return TryResolveConservativeLimit(bar, limitPrice, isBuy, out fillPrice);
                }

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

    /// <summary>Conservative resting-limit semantics; identical rules to <see cref="BarMidpointFillModel"/>.</summary>
    private static bool TryResolveConservativeLimit(HistoricalBar bar, decimal limitPrice, bool isBuy, out decimal fillPrice)
    {
        fillPrice = 0m;

        if (isBuy)
        {
            if (bar.Open <= limitPrice)
            {
                fillPrice = bar.Open;
                return true;
            }
            if (bar.Low < limitPrice)
            {
                fillPrice = limitPrice;
                return true;
            }
            return false;
        }

        if (bar.Open >= limitPrice)
        {
            fillPrice = bar.Open;
            return true;
        }
        if (bar.High > limitPrice)
        {
            fillPrice = limitPrice;
            return true;
        }
        return false;
    }

    /// <summary>Conservative trigger-bar stop-limit semantics; identical rules to <see cref="BarMidpointFillModel"/>.</summary>
    private static bool TryResolveConservativeTriggerBarStopLimit(HistoricalBar bar, decimal stopPrice, decimal limitPrice, bool isBuy, out decimal fillPrice)
    {
        fillPrice = 0m;

        if (isBuy)
        {
            var triggerPrice = Math.Max(stopPrice, bar.Open);
            if (limitPrice < triggerPrice)
                return false;
            fillPrice = Math.Min(limitPrice, bar.High);
            return true;
        }

        var sellTriggerPrice = Math.Min(stopPrice, bar.Open);
        if (limitPrice > sellTriggerPrice)
            return false;
        fillPrice = Math.Max(limitPrice, bar.Low);
        return true;
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
