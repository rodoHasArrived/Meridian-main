using System.Globalization;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using ExecutionOrderSide = Meridian.Execution.Sdk.OrderSide;

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

    private static readonly IReadOnlyDictionary<string, Meridian.Execution.Sdk.OrderSide> SideMap =
        new Dictionary<string, Meridian.Execution.Sdk.OrderSide>(StringComparer.OrdinalIgnoreCase)
        {
            ["BUY"] = Meridian.Execution.Sdk.OrderSide.Buy,
            ["BOT"] = Meridian.Execution.Sdk.OrderSide.Buy,
            ["SELL"] = Meridian.Execution.Sdk.OrderSide.Sell,
            ["SLD"] = Meridian.Execution.Sdk.OrderSide.Sell
        };

    // secType -> Meridian report code, translated through the provider code table.
    private static readonly IReadOnlyDictionary<string, string> AssetTypeMap =
        ProviderAssetClassCodeCatalog.InteractiveBrokersReportCodes;

    public static string MapAssetClass(string? securityType, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(AssetTypeMap, securityType, "asset_type", "equity", metadata);

    public static Meridian.Execution.Sdk.OrderSide ParseSide(string? action, IReadOnlyDictionary<string, string>? metadata = null)
        => TryMap(SideMap, action, "side", Meridian.Execution.Sdk.OrderSide.Buy, metadata);

    /// <summary>
    /// Parses an Interactive Brokers execution time. IB reports <c>yyyyMMdd-HH:mm:ss</c> in UTC,
    /// or <c>yyyyMMdd HH:mm:ss</c> followed by the TWS time zone name (for example
    /// <c>US/Eastern</c>). A time with no zone is in the TWS login's local time zone, which the API
    /// does not report, so it is not placed on the timeline and the caller keeps its fallback.
    /// </summary>
    internal static bool TryParseExecutionTime(string? value, out DateTimeOffset executedAt)
    {
        executedAt = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (DateTime.TryParseExact(text, "yyyyMMdd-HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
        {
            executedAt = new DateTimeOffset(utc, TimeSpan.Zero);
            return true;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !DateTime.TryParseExact($"{parts[0]} {parts[1]}", "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            return false;

        if (!TryFindTimeZone(parts[2], out var zone)
            && !(LegacyTimeZoneAliases.TryGetValue(parts[2], out var canonical) && TryFindTimeZone(canonical, out zone)))
            return false;

        executedAt = new DateTimeOffset(local, zone!.GetUtcOffset(local)).ToUniversalTime();
        return true;
    }

    // TWS still reports the legacy IANA aliases (US/Eastern and friends), which hosts without the
    // tz "backward" links cannot resolve.
    private static readonly IReadOnlyDictionary<string, string> LegacyTimeZoneAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["US/Eastern"] = "America/New_York",
            ["US/Central"] = "America/Chicago",
            ["US/Mountain"] = "America/Denver",
            ["US/Pacific"] = "America/Los_Angeles",
            ["US/Alaska"] = "America/Anchorage",
            ["US/Hawaii"] = "Pacific/Honolulu",
            ["US/Arizona"] = "America/Phoenix"
        };

    private static bool TryFindTimeZone(string id, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }

    internal static bool TryParseKnownSide(string? action, out ExecutionOrderSide side)
        => TryGetKnown(SideMap, action, out side);

    internal static bool TryParseKnownOrderType(string? orderType, out OrderType type)
        => TryGetKnown(OrderTypeMap, orderType, out type);

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
