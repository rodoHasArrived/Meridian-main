using Meridian.Execution.Sdk;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Maps Interactive Brokers payload primitives into Meridian canonical brokerage models.
/// Centralizes provider-enum translations and unknown-value handling so additive IB values
/// do not break shared API/read-model contracts.
/// </summary>
internal static class IBCanonicalPayloadMapper
{
    private const string UnknownMetadataPrefix = "ib.unknown.";

    private static readonly IReadOnlyDictionary<string, OrderStatus> OrderStatusMap =
        new Dictionary<string, OrderStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["Filled"] = OrderStatus.Filled,
            ["Cancelled"] = OrderStatus.Cancelled,
            ["ApiCancelled"] = OrderStatus.Cancelled,
            ["PendingCancel"] = OrderStatus.PendingCancel,
            ["Inactive"] = OrderStatus.Rejected,
            ["Submitted"] = OrderStatus.Accepted,
            ["PreSubmitted"] = OrderStatus.PendingNew,
            ["PendingSubmit"] = OrderStatus.PendingNew,
            ["ApiPending"] = OrderStatus.PendingNew
        };

    private static readonly IReadOnlyDictionary<string, OrderType> OrderTypeMap =
        new Dictionary<string, OrderType>(StringComparer.OrdinalIgnoreCase)
        {
            ["MKT"] = OrderType.Market,
            ["LMT"] = OrderType.Limit,
            ["STP"] = OrderType.StopMarket,
            ["STP LMT"] = OrderType.StopLimit
        };

    private static readonly IReadOnlyDictionary<string, TimeInForce> TimeInForceMap =
        new Dictionary<string, TimeInForce>(StringComparer.OrdinalIgnoreCase)
        {
            ["DAY"] = TimeInForce.Day,
            ["GTC"] = TimeInForce.GoodTilCancelled,
            ["IOC"] = TimeInForce.ImmediateOrCancel,
            ["FOK"] = TimeInForce.FillOrKill
        };

    private static readonly IReadOnlyDictionary<string, OrderSide> SideMap =
        new Dictionary<string, OrderSide>(StringComparer.OrdinalIgnoreCase)
        {
            ["BUY"] = OrderSide.Buy,
            ["BOT"] = OrderSide.Buy,
            ["SELL"] = OrderSide.Sell,
            ["SLD"] = OrderSide.Sell
        };

    private static readonly IReadOnlyDictionary<string, string> AssetTypeMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STK"] = "equity",
            ["ETF"] = "equity",
            ["OPT"] = "option",
            ["FUT"] = "futures",
            ["FOP"] = "option",
            ["CASH"] = "forex",
            ["BOND"] = "bond",
            ["GOVT"] = "bond"
        };

    public static string MapAssetClass(string? securityType, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(AssetTypeMap, securityType, "asset_type", "equity", metadata);

    public static OrderSide ParseSide(string? action, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(SideMap, action, "side", OrderSide.Buy, metadata);

    public static OrderType ParseOrderType(string? orderType, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(OrderTypeMap, orderType, "order_type", OrderType.Market, metadata);

    public static TimeInForce ParseTimeInForce(string? timeInForce, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(TimeInForceMap, timeInForce, "time_in_force", TimeInForce.Day, metadata);

    public static OrderStatus MapOrderStatus(string? status, decimal filled, decimal remaining, string? rejectReason, IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (!string.IsNullOrWhiteSpace(rejectReason))
            return OrderStatus.Rejected;

        if (TryGetKnown(OrderStatusMap, status, out var mapped))
            return mapped;

        if (filled > 0 && remaining > 0)
            return OrderStatus.PartiallyFilled;

        if (!string.IsNullOrWhiteSpace(status))
            RecordUnknown(metadata, "order_status", status);

        return filled > 0 ? OrderStatus.PartiallyFilled : OrderStatus.Accepted;
    }

    private static T TryMap<T>(IReadOnlyDictionary<string, T> map, string? raw, string field, T fallback, IReadOnlyDictionary<string, string>? metadata)
    {
        if (TryGetKnown(map, raw, out var mapped))
            return mapped;

        if (!string.IsNullOrWhiteSpace(raw))
            RecordUnknown(metadata, field, raw);

        return fallback;
    }

    private static bool TryGetKnown<T>(IReadOnlyDictionary<string, T> map, string? raw, out T value)
    {
        value = default!;
        var normalized = raw?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !map.TryGetValue(normalized, out var mapped))
            return false;

        value = mapped;
        return true;
    }

    private static void RecordUnknown(IReadOnlyDictionary<string, string>? metadata, string field, string raw)
    {
        if (metadata is null || metadata.ContainsKey($"{UnknownMetadataPrefix}{field}"))
            return;

        if (metadata is Dictionary<string, string> mutable)
        {
            mutable[$"{UnknownMetadataPrefix}{field}"] = raw.Trim();
        }
    }
}
