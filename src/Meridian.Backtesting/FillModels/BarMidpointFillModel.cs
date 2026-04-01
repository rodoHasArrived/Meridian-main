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
/// </summary>
internal sealed class BarMidpointFillModel(
    ICommissionModel commissionModel,
    decimal slippageBasisPoints = 5m,
    bool spreadAware = false,
    decimal volatilityMultiplier = 50m,
    IReadOnlyDictionary<string, decimal>? tickSizes = null,
    decimal maxParticipationRate = 0m) : IFillModel
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
        var commission = commissionModel.Calculate(order.Symbol, fillQuantitySigned, fillPrice);
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
    /// When <paramref name="maxParticipationRate"/> is zero the full remaining
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

    private bool TryResolveFillPrice(HistoricalBar bar, Order order, OrderType executableType, bool isBuy, out decimal fillPrice)
    {
        fillPrice = 0m;

        switch (executableType)
        {
            case OrderType.Market:
                // Midpoint is defined as (Open + Close) / 2 — the bar's open-to-close centre —
                // rather than the OHLC midpoint ((High + Low) / 2). This models fills executing
                // somewhere in the middle of the bar's price path, not at its intrabar extreme.
                var mid = (bar.Open + bar.Close) / 2m;
                var effectiveSlippage = slippageBasisPoints;

                // When spread-aware mode is enabled, scale slippage by intrabar volatility.
                // Higher bar range relative to midpoint implies wider real-world spreads.
                if (spreadAware && mid > 0m)
                {
                    var range = bar.High - bar.Low;
                    var volatilityFactor = range / mid; // e.g., 0.02 for a 2% bar range
                    // volatilityMultiplier is a calibration factor (default 50×); see constructor doc.
                    effectiveSlippage = slippageBasisPoints * (1m + volatilityFactor * volatilityMultiplier);
                }

                var slip = mid * (effectiveSlippage / 10_000m);
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
