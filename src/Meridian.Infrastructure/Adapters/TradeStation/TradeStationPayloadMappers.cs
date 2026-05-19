using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.TradeStation;

public static class TradeStationPayloadMappers
{
    public static BrokerageExternalAccountDto MapAccount(TradeStationAccountPayload payload, DateTimeOffset retrievedAt)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new BrokerageExternalAccountDto(
            ProviderId: "tradestation",
            AccountId: Required(payload.AccountId, nameof(payload.AccountId)),
            DisplayName: string.IsNullOrWhiteSpace(payload.DisplayName) ? payload.AccountId : payload.DisplayName,
            Status: string.IsNullOrWhiteSpace(payload.Status) ? "unknown" : payload.Status.Trim().ToLowerInvariant(),
            Currency: string.IsNullOrWhiteSpace(payload.Currency) ? "USD" : payload.Currency.Trim().ToUpperInvariant(),
            RetrievedAt: retrievedAt,
            Metadata: payload.Metadata);
    }

    public static BrokeragePositionSnapshotDto MapPosition(TradeStationPositionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new BrokeragePositionSnapshotDto(
            Symbol: Required(payload.Symbol, nameof(payload.Symbol)).ToUpperInvariant(),
            Quantity: payload.Quantity,
            AverageEntryPrice: payload.AverageEntryPrice,
            MarketPrice: payload.MarketPrice,
            MarketValue: payload.MarketValue,
            UnrealizedPnl: payload.UnrealizedPnl,
            AssetClass: string.IsNullOrWhiteSpace(payload.AssetClass) ? "equity" : payload.AssetClass.Trim().ToLowerInvariant(),
            Description: payload.Description,
            PositionId: payload.PositionId,
            Currency: string.IsNullOrWhiteSpace(payload.Currency) ? "USD" : payload.Currency.Trim().ToUpperInvariant(),
            Metadata: payload.Metadata);
    }

    public static BrokerageOrderSnapshotDto MapOrder(TradeStationOrderPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new BrokerageOrderSnapshotDto(
            OrderId: Required(payload.OrderId, nameof(payload.OrderId)),
            ClientOrderId: payload.ClientOrderId,
            Symbol: Required(payload.Symbol, nameof(payload.Symbol)).ToUpperInvariant(),
            Side: MapSide(payload.Side),
            Type: MapType(payload.Type),
            Status: MapStatus(payload.Status),
            Quantity: payload.Quantity,
            FilledQuantity: payload.FilledQuantity,
            LimitPrice: payload.LimitPrice,
            StopPrice: payload.StopPrice,
            CreatedAt: payload.CreatedAt,
            UpdatedAt: payload.UpdatedAt);
    }

    public static BrokerageFillSnapshotDto MapFill(TradeStationFillPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new BrokerageFillSnapshotDto(
            FillId: Required(payload.FillId, nameof(payload.FillId)),
            OrderId: payload.OrderId,
            Symbol: Required(payload.Symbol, nameof(payload.Symbol)).ToUpperInvariant(),
            Side: MapSide(payload.Side),
            Quantity: payload.Quantity,
            Price: payload.Price,
            FilledAt: payload.FilledAt,
            Venue: payload.Venue,
            Commission: payload.Commission);
    }

    public static OrderSide MapSide(string? side)
        => Normalize(side) switch
        {
            "buy" or "buytocover" => OrderSide.Buy,
            "sell" or "sellshort" => OrderSide.Sell,
            _ => throw new InvalidOperationException($"Unsupported TradeStation side '{side ?? "<null>"}'.")
        };

    public static OrderType MapType(string? type)
        => Normalize(type) switch
        {
            "market" => OrderType.Market,
            "limit" => OrderType.Limit,
            "stopmarket" or "stop" => OrderType.StopMarket,
            "stoplimit" => OrderType.StopLimit,
            "marketonopen" => OrderType.MarketOnOpen,
            "marketonclose" => OrderType.MarketOnClose,
            "limitonopen" => OrderType.LimitOnOpen,
            "limitonclose" => OrderType.LimitOnClose,
            "trailingstop" => OrderType.TrailingStop,
            _ => throw new InvalidOperationException($"Unsupported TradeStation order type '{type ?? "<null>"}'.")
        };

    public static OrderStatus MapStatus(string? status)
        => Normalize(status) switch
        {
            "new" or "received" => OrderStatus.PendingNew,
            "accepted" or "working" => OrderStatus.Accepted,
            "partiallyfilled" => OrderStatus.PartiallyFilled,
            "filled" => OrderStatus.Filled,
            "pendingcancel" => OrderStatus.PendingCancel,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            "rejected" => OrderStatus.Rejected,
            "expired" => OrderStatus.Expired,
            _ => OrderStatus.PendingNew
        };

    private static string Required(string? value, string fieldName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"TradeStation payload missing required field '{fieldName}'.")
            : value.Trim();

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Trim()
                .ToLowerInvariant();
}

public sealed record TradeStationAccountPayload(
    string AccountId,
    string? DisplayName,
    string? Status,
    string? Currency,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record TradeStationPositionPayload(
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string? AssetClass,
    string? Description,
    string? PositionId,
    string? Currency,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record TradeStationOrderPayload(
    string OrderId,
    string? ClientOrderId,
    string Symbol,
    string Side,
    string Type,
    string Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);

public sealed record TradeStationFillPayload(
    string FillId,
    string? OrderId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTimeOffset FilledAt,
    string? Venue,
    decimal? Commission);
