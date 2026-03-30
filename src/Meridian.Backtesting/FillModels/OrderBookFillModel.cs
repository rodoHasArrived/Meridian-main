using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Realistic fill model that walks stored <see cref="LOBSnapshot"/> bid/ask levels.
/// Buy orders consume ask levels in ascending price order; sell orders consume bid levels
/// in descending order. Supports partial fills, stop triggers, and time-in-force semantics.
/// When <paramref name="tickSizes"/> is provided, fill prices are rounded to the
/// instrument's tick grid before being returned.
/// </summary>
internal sealed class OrderBookFillModel(
    ICommissionModel commissionModel,
    IReadOnlyDictionary<string, decimal>? tickSizes = null) : IFillModel
{
    public OrderFillResult TryFill(Order order, MarketEvent evt)
    {
        if (evt.Payload is not LOBSnapshot lob)
            return new OrderFillResult(order, [], RemoveOrder: false);
        if (!lob.Symbol.Equals(order.Symbol, StringComparison.OrdinalIgnoreCase))
            return new OrderFillResult(order, [], RemoveOrder: false);
        if (order.IsComplete || order.Status is OrderStatus.Cancelled or OrderStatus.Expired or OrderStatus.Rejected)
            return new OrderFillResult(order, [], RemoveOrder: true);

        var isBuy = order.Quantity > 0;
        var triggered = order.IsTriggered || IsTriggered(order, lob, isBuy);
        var executableType = GetExecutableType(order.Type, triggered);
        if (executableType is null)
        {
            return new OrderFillResult(
                order with { IsTriggered = triggered },
                [],
                RemoveOrder: false,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var remainingSigned = order.RemainingSignedQuantity;
        var remainingAbsolute = order.RemainingQuantity;

        var levels = isBuy
            ? lob.Asks.OrderBy(l => l.Price).ToList()
            : lob.Bids.OrderByDescending(l => l.Price).ToList();

        var executableLevels = FilterExecutableLevels(levels, executableType.Value, order, isBuy);
        if (order.TimeInForce == TimeInForce.FillOrKill &&
            executableLevels.Sum(static level => (long)level.Size) < remainingAbsolute)
        {
            return new OrderFillResult(
                order with { Status = OrderStatus.Cancelled, IsTriggered = triggered },
                [],
                RemoveOrder: true,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var allowPartial = order.AllowPartialFills && order.TimeInForce != TimeInForce.FillOrKill;
        var fills = new List<FillEvent>();
        foreach (var level in executableLevels)
        {
            if (remainingAbsolute == 0)
                break;

            var levelQuantity = (long)Math.Truncate(level.Size);
            if (levelQuantity <= 0)
                continue;

            var fillQuantity = allowPartial
                ? Math.Min(remainingAbsolute, levelQuantity)
                : remainingAbsolute <= levelQuantity ? remainingAbsolute : 0;

            if (fillQuantity == 0)
                break;

            var signedQuantity = isBuy ? fillQuantity : -fillQuantity;
            var fillPrice = SnapToTick(level.Price, order.Symbol);
            var commission = commissionModel.Calculate(order.Symbol, signedQuantity, fillPrice);
            fills.Add(new FillEvent(
                Guid.NewGuid(),
                order.OrderId,
                order.Symbol,
                signedQuantity,
                fillPrice,
                commission,
                evt.Timestamp,
                order.AccountId));

            remainingAbsolute -= fillQuantity;
        }

        if (fills.Count == 0)
        {
            var shouldCancel =
                order.TimeInForce is TimeInForce.ImmediateOrCancel or TimeInForce.FillOrKill;

            return new OrderFillResult(
                order with
                {
                    Status = shouldCancel ? OrderStatus.Cancelled : order.Status,
                    IsTriggered = triggered
                },
                [],
                RemoveOrder: shouldCancel,
                WasTriggered: triggered && !order.IsTriggered);
        }

        var totalFilled = fills.Sum(static fill => fill.FilledQuantity);
        var updatedOrder = order with
        {
            FilledQuantity = order.FilledQuantity + totalFilled,
            Status = remainingAbsolute == 0 ? OrderStatus.Filled : OrderStatus.PartiallyFilled,
            IsTriggered = triggered
        };

        var removeOrder = updatedOrder.IsComplete || order.TimeInForce == TimeInForce.ImmediateOrCancel;
        if (order.TimeInForce == TimeInForce.ImmediateOrCancel && !updatedOrder.IsComplete)
        {
            updatedOrder = updatedOrder with { Status = OrderStatus.Cancelled };
        }

        return new OrderFillResult(
            updatedOrder,
            fills,
            removeOrder,
            WasTriggered: triggered && !order.IsTriggered);
    }

    private decimal SnapToTick(decimal price, string symbol)
    {
        if (tickSizes is null || !tickSizes.TryGetValue(symbol, out var tickSize) || tickSize <= 0m)
            return price;
        return Math.Round(price / tickSize, MidpointRounding.ToEven) * tickSize;
    }

    private static IReadOnlyList<OrderBookLevel> FilterExecutableLevels(
        IReadOnlyList<OrderBookLevel> levels,
        OrderType executableType,
        Order order,
        bool isBuy)
    {
        if (executableType == OrderType.Market)
        {
            // All levels are executable — return the input directly, no allocation.
            return levels;
        }

        // Limit order: filter levels within the limit price.
        var filtered = new List<OrderBookLevel>();
        foreach (var level in levels)
        {
            if (isBuy ? level.Price <= order.LimitPrice!.Value : level.Price >= order.LimitPrice!.Value)
                filtered.Add(level);
        }
        return filtered;
    }

    private static bool IsTriggered(Order order, LOBSnapshot lob, bool isBuy)
    {
        if (order.StopPrice is null)
            return order.Type is OrderType.Market or OrderType.Limit;

        // Use O(n) min/max scan instead of sorting the whole order book.
        if (isBuy)
        {
            var bestAsk = lob.Asks.Count > 0 ? lob.Asks.Min(static l => l.Price) : (decimal?)null;
            return bestAsk.HasValue && bestAsk.Value >= order.StopPrice.Value;
        }
        else
        {
            var bestBid = lob.Bids.Count > 0 ? lob.Bids.Max(static l => l.Price) : (decimal?)null;
            return bestBid.HasValue && bestBid.Value <= order.StopPrice.Value;
        }
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
