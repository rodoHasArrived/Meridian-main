using System.Text.Json.Serialization;
using Meridian.Infrastructure.CppTrader.Options;

namespace Meridian.Infrastructure.CppTrader.Protocol;

public enum CppTraderSessionKind
{
    Execution,
    Replay,
    Ingest
}

public sealed record CppTraderEnvelope(
    string MessageType,
    string? RequestId = null,
    string? SessionId = null,
    JsonElement Payload = default,
    DateTimeOffset? Timestamp = null);

public sealed record CreateSessionRequest(
    CppTraderSessionKind SessionKind,
    string? SessionName = null);

public sealed record CreateSessionResponse(
    string SessionId,
    CppTraderSessionKind SessionKind,
    DateTimeOffset CreatedAt);

public sealed record RegisterSymbolRequest(
    string Symbol,
    int SymbolId,
    long TickSizeNanos,
    long QuantityIncrementNanos,
    int PriceScale,
    long LotSizeNanos,
    string? Venue,
    string SessionTimeZone);

public sealed record RegisterSymbolResponse(
    string Symbol,
    bool Registered,
    string? FailureReason = null);

public sealed record SubmitOrderRequest(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    string Side,
    string OrderType,
    string TimeInForce,
    long QuantityNanos,
    long? LimitPriceNanos,
    long? StopPriceNanos,
    bool IsDayOrder,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record SubmitOrderResponse(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    bool Accepted,
    string? FailureReason,
    DateTimeOffset Timestamp);

public sealed record CancelOrderRequest(
    string OrderId);

public sealed record CancelOrderResponse(
    string OrderId,
    bool Accepted,
    string? FailureReason,
    DateTimeOffset Timestamp);

public sealed record GetSnapshotRequest(
    string Symbol);

public sealed record CppTraderBookLevel(
    decimal Price,
    decimal Size);

public sealed record CppTraderBookSnapshot(
    string Symbol,
    IReadOnlyList<CppTraderBookLevel> Bids,
    IReadOnlyList<CppTraderBookLevel> Asks,
    decimal? MidPrice,
    decimal? MicroPrice,
    decimal? Imbalance,
    long SequenceNumber,
    string? Venue,
    DateTimeOffset Timestamp);

public sealed record GetSnapshotResponse(
    CppTraderBookSnapshot? Snapshot);

public sealed record HeartbeatRequest(
    string HostId);

public sealed record HeartbeatResponse(
    string HostId,
    DateTimeOffset Timestamp);

public sealed record FaultEvent(
    string Code,
    string Message,
    bool IsTerminal = false);

public sealed record SessionClosedEvent(
    string SessionId,
    string Reason);

public sealed record AcceptedEvent(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    DateTimeOffset Timestamp);

public sealed record RejectedEvent(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    string Reason,
    DateTimeOffset Timestamp);

public sealed record ExecutionEvent(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    long FilledQuantityNanos,
    long CumulativeFilledQuantityNanos,
    decimal AverageFillPrice,
    bool IsTerminal,
    DateTimeOffset Timestamp);

public sealed record CancelledEvent(
    string OrderId,
    string ClientOrderId,
    string Symbol,
    DateTimeOffset Timestamp);

public sealed record TradePrintEvent(
    string Symbol,
    decimal Price,
    long Size,
    AggressorSide Aggressor,
    long SequenceNumber,
    string? Venue,
    DateTimeOffset Timestamp);

public sealed record BookSnapshotEvent(
    CppTraderBookSnapshot Snapshot);

public sealed record HostHealthSnapshot(
    bool Enabled,
    bool HostHealthy,
    int ActiveSessions,
    int OutstandingOrders,
    long TotalFills,
    long TotalRejects,
    long TotalSnapshots,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset? LastFaultAt,
    string? LastFault);

public static class CppTraderProtocolNames
{
    public const string CreateSession = "createSession";
    public const string CreateSessionResponse = "createSessionResponse";
    public const string RegisterSymbol = "registerSymbol";
    public const string RegisterSymbolResponse = "registerSymbolResponse";
    public const string SubmitOrder = "submitOrder";
    public const string SubmitOrderResponse = "submitOrderResponse";
    public const string CancelOrder = "cancelOrder";
    public const string CancelOrderResponse = "cancelOrderResponse";
    public const string GetSnapshot = "getSnapshot";
    public const string GetSnapshotResponse = "getSnapshotResponse";
    public const string Heartbeat = "heartbeat";
    public const string HeartbeatResponse = "heartbeatResponse";

    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Execution = "execution";
    public const string Cancelled = "cancelled";
    public const string BookSnapshot = "bookSnapshot";
    public const string TradePrint = "tradePrint";
    public const string Fault = "fault";
    public const string SessionClosed = "sessionClosed";
}

[JsonSerializable(typeof(CppTraderEnvelope))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(CreateSessionResponse))]
[JsonSerializable(typeof(RegisterSymbolRequest))]
[JsonSerializable(typeof(RegisterSymbolResponse))]
[JsonSerializable(typeof(SubmitOrderRequest))]
[JsonSerializable(typeof(SubmitOrderResponse))]
[JsonSerializable(typeof(CancelOrderRequest))]
[JsonSerializable(typeof(CancelOrderResponse))]
[JsonSerializable(typeof(GetSnapshotRequest))]
[JsonSerializable(typeof(GetSnapshotResponse))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(HeartbeatResponse))]
[JsonSerializable(typeof(FaultEvent))]
[JsonSerializable(typeof(SessionClosedEvent))]
[JsonSerializable(typeof(AcceptedEvent))]
[JsonSerializable(typeof(RejectedEvent))]
[JsonSerializable(typeof(ExecutionEvent))]
[JsonSerializable(typeof(CancelledEvent))]
[JsonSerializable(typeof(TradePrintEvent))]
[JsonSerializable(typeof(BookSnapshotEvent))]
[JsonSerializable(typeof(CppTraderBookSnapshot))]
[JsonSerializable(typeof(CppTraderBookLevel))]
[JsonSerializable(typeof(HostHealthSnapshot))]
[JsonSerializable(typeof(Dictionary<string, CppTraderSymbolSpecification>))]
[JsonSerializable(typeof(CppTraderOptions))]
[JsonSerializable(typeof(CppTraderFeatureOptions))]
[JsonSerializable(typeof(CppTraderSymbolSpecification))]
internal sealed partial class CppTraderJsonContext : JsonSerializerContext
{
    public static JsonSerializerOptions ProtocolOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = Default
    };
}
