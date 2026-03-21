using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;

namespace Meridian.Domain.Events;

/// <summary>
/// Discriminated-union base for domain-layer market event payloads.
/// Models are consolidated in the Contracts project as single source of truth.
/// Keep the <c>[JsonDerivedType]</c> list in sync with <c>Contracts/Domain/Events/MarketEventPayload.cs</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload), "heartbeat")]
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
public abstract record MarketEventPayload : Contracts.Domain.Events.IMarketEventPayload;
