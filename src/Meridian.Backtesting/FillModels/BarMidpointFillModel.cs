using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Fallback fill model used when only OHLCV bar data is available.
/// Supports market, limit, stop-market, and stop-limit semantics with a
/// configurable midpoint slippage assumption. When <paramref name="spreadAware"/>
/// is enabled, slippage is scaled by the bar's intrabar volatility (range / midpoint),
/// simulating wider spreads in volatile conditions. The scaling formula is
/// <c>slippageBasisPoints × (1 + volatilityFactor × <paramref name="volatilityMultiplier"/>)</c>;
/// the default multiplier of 50 is an empirical calibration that maps a typical 1–2% intraday
/// bar range to a 50–100% slippage increase — adjust when calibrating against real microstructure data.
/// When <paramref name="tickSizes"/> is provided, fill prices are rounded to the
/// instrument's tick grid before being returned.
/// When <paramref name="maxParticipationRate"/> is greater than zero, the fill is
/// capped at that fraction of the bar's traded volume. Orders that exceed the cap
/// and have <see cref="Order.AllowPartialFills"/> set to <c>true</c> receive a
/// partial fill; orders with partial fills disabled are left unfilled for the bar.
/// <paramref name="conservatism"/> selects between conservative limit/stop semantics
/// (trade-through limits, gap-aware stop pricing — the default) and the legacy optimistic
/// touch-fill behaviour; see <see cref="FillConservatism"/> for the exact rules.
/// </summary>
internal sealed class BarMidpointFillModel(
    ICommissionModel commissionModel,
    decimal slippageBasisPoints = 5m,
    bool spreadAware = false,
    decimal volatilityMultiplier = 50m,
    IReadOnlyDictionary<string, decimal>? tickSizes = null,
    decimal maxParticipationRate = 0m,
    FillConservatism conservatism = FillConservatism.Conservative) : IFillModel
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

        if (!TryResolveFillPrice(bar, order, executableType.Value, isBuy, newlyTriggered, out var fillPrice))
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        fillPrice = SnapToTick(fillPrice, order.Symbol);

        // Volume-constrained participation: cap fill at (bar.Volume * maxParticipationRate).
        // When the cap is active and partial fills are disabled, leave the order working.
        var remainingAbsolute = order.RemainingQuantity;
        var fillableAbsolute = ComputeFillableQuantity(remainingAbsolute, bar.Volume);

        if (fillableAbsolute == 0)
        {
            // Volume cap prevents any fill this bar — leave the order working.
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        if (fillableAbsolute < remainingAbsolute && !order.AllowPartialFills)
        {
            // Volume cap would produce a partial fill but the order disallows it.
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var fillQuantitySigned = isBuy ? fillableAbsolute : -fillableAbsolute;
        var commission = commissionModel
            .Quote(order.OrderId, order.Symbol, fillQuantitySigned, fillPrice)
            .Amount;
        var fill = new FillEvent(
            Guid.NewGuid(),
            order.OrderId,
            order.Symbol,
            fillQuantitySigned,
            fillPrice,
            commission,
            evt.Timestamp,
            order.AccountId);

        var newFilledQuantity = order.FilledQuantity + fillQuantitySigned;
        var isFullyFilled = Math.Abs(newFilledQuantity) >= Math.Abs(order.Quantity);
        var updated = order with
        {
            FilledQuantity = newFilledQuantity,
            Status = isFullyFilled ? OrderStatus.Filled : OrderStatus.PartiallyFilled,
            IsTriggered = triggered
        };

        return new OrderFillResult(
            updated,
            [fill],
            RemoveOrder: isFullyFilled,
            WasTriggered: triggered && !order.IsTriggered);
    }

    /// <summary>
    /// Computes how many shares can be filled this bar.
    /// When <c>maxParticipationRate</c> is zero the full remaining
    /// quantity is returned (unconstrained mode, backward-compatible).
    /// </summary>
    private long ComputeFillableQuantity(long remainingAbsolute, long barVolume)
    {
        if (maxParticipationRate <= 0m)
            return remainingAbsolute;

        var barVolumeCap = (long)(barVolume * maxParticipationRate);
        return Math.Min(remainingAbsolute, barVolumeCap);
    }

    private decimal SnapToTick(decimal price, string symbol)
    {
        if (tickSizes is null || !tickSizes.TryGetValue(symbol, out var tickSize) || tickSize <= 0m)
            return price;
        return Math.Round(price / tickSize, MidpointRounding.ToEven) * tickSize;
    }

    private bool TryResolveFillPrice(HistoricalBar bar, Order order, OrderType executableType, bool isBuy, bool newlyTriggered, out decimal fillPrice)
    {
        fillPrice = 0m;

        switch (executableType)
        {
            case OrderType.Market:
                // A stop-market order that triggers inside this bar must not execute at the bar
                // midpoint — that can beat the stop price, which is impossible live. Anchor the
                // fill to the worse of the stop and the open (gaps fill at the open) and apply
                // slippage on top. Plain market orders (and stops triggered on an earlier bar)
                // keep midpoint semantics.
                if (conservatism == FillConservatism.Conservative
                    && order.Type is OrderType.StopMarket
                    && newlyTriggered
                    && order.StopPrice is { } stop)
                {
                    var stopBase = isBuy ? Math.Max(stop, bar.Open) : Math.Min(stop, bar.Open);
                    var stopSlip = stopBase * (ComputeEffectiveSlippage(bar, stopBase) / 10_000m);
                    fillPrice = isBuy ? stopBase + stopSlip : stopBase - stopSlip;
                    return true;
                }

                // Midpoint is defined as (Open + Close) / 2 — the bar's open-to-close centre —
                // rather than the OHLC midpoint ((High + Low) / 2). This models fills executing
                // somewhere in the middle of the bar's price path, not at its intrabar extreme.
                var mid = (bar.Open + bar.Close) / 2m;
                var slip = mid * (ComputeEffectiveSlippage(bar, mid) / 10_000m);
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

    private decimal ComputeEffectiveSlippage(HistoricalBar bar, decimal referencePrice)
    {
        // When spread-aware mode is enabled, scale slippage by intrabar volatility.
        // Higher bar range relative to the reference price implies wider real-world spreads.
        if (!spreadAware || referencePrice <= 0m)
            return slippageBasisPoints;

        var volatilityFactor = (bar.High - bar.Low) / referencePrice; // e.g., 0.02 for a 2% bar range
        // volatilityMultiplier is a calibration factor (default 50×); see constructor doc.
        return slippageBasisPoints * (1m + volatilityFactor * volatilityMultiplier);
    }

    /// <summary>
    /// Conservative resting-limit semantics: a bar that opens through the limit fills at the open;
    /// otherwise the bar must trade strictly through the limit — a bare touch leaves the order
    /// working (queue-position risk).
    /// </summary>
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

    /// <summary>
    /// Conservative stop-limit semantics for the bar in which the stop first triggers.
    /// The trigger price is the worse of the stop and the open (gaps trigger at the open). When the
    /// limit is marketable at that trigger the fill is priced at the worst in-range price the limit
    /// permits; when the bar opened beyond the limit the order stays working (already triggered) and
    /// is handled as a resting limit on later bars.
    /// </summary>
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
