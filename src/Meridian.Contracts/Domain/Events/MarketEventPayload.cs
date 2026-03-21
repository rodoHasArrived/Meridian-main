using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;

namespace Meridian.Contracts.Domain.Events;

/// <summary>
/// Discriminated-union base for all market event payloads.
/// Every <see cref="MarketEventDto"/> carries a non-null payload; heartbeat events use
/// <see cref="HeartbeatPayload"/> to eliminate the null-payload special case.
/// </summary>
/// <remarks>
/// Keep the <c>[JsonDerivedType]</c> list in sync with <c>Domain/Events/MarketEventPayload.cs</c>.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HeartbeatPayload), "heartbeat")]
[JsonDerivedType(typeof(Trade), "trade")]
[JsonDerivedType(typeof(LOBSnapshot), "l2")]
[JsonDerivedType(typeof(OrderFlowStatistics), "orderflow")]
[JsonDerivedType(typeof(IntegrityEvent), "integrity")]
[JsonDerivedType(typeof(DepthIntegrityEvent), "depth_integrity")]
[JsonDerivedType(typeof(L2SnapshotPayload), "l2payload")]
[JsonDerivedType(typeof(BboQuotePayload), "bbo")]
[JsonDerivedType(typeof(HistoricalBar), "historical_bar")]
[JsonDerivedType(typeof(HistoricalQuote), "historical_quote")]
[JsonDerivedType(typeof(HistoricalTrade), "historical_trade")]
[JsonDerivedType(typeof(HistoricalAuction), "historical_auction")]
[JsonDerivedType(typeof(AggregateBarPayload), "aggregate_bar")]
[JsonDerivedType(typeof(OptionQuote), "option_quote")]
[JsonDerivedType(typeof(OptionTrade), "option_trade")]
[JsonDerivedType(typeof(GreeksSnapshot), "greeks")]
[JsonDerivedType(typeof(OptionChainSnapshot), "option_chain")]
[JsonDerivedType(typeof(OpenInterestUpdate), "open_interest")]
[JsonDerivedType(typeof(OrderAdd), "order_add")]
[JsonDerivedType(typeof(OrderModify), "order_modify")]
[JsonDerivedType(typeof(OrderCancel), "order_cancel")]
[JsonDerivedType(typeof(OrderExecute), "order_execute")]
[JsonDerivedType(typeof(OrderReplace), "order_replace")]
public abstract record MarketEventPayload : IMarketEventPayload
{
    /// <summary>
    /// Payload for heartbeat events. Carries no market data; exists to eliminate the
    /// null-payload special case and enable exhaustive pattern matching over all event types.
    /// </summary>
    public sealed record HeartbeatPayload() : MarketEventPayload;
}
