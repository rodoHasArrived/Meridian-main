# Domain Model

This document describes the **runtime domain contracts** used by the collectors and event pipeline.
Primary source of truth lives in:

- `src/Meridian.Contracts/Domain/Events/`
- `src/Meridian.Contracts/Domain/Models/`
- `src/Meridian.Domain/Collectors/`

## MarketEvent Envelope

All emitted domain activity is wrapped in `MarketEvent`:

```csharp
public sealed record MarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventType Type,
    MarketEventPayload Payload,
    long Sequence = 0,
    string Source = "IB",
    byte SchemaVersion = 1,
    MarketEventTier Tier = MarketEventTier.Raw,
    DateTimeOffset? ExchangeTimestamp = null,
    DateTimeOffset ReceivedAtUtc = default,
    long ReceivedAtMonotonic = 0,
    // Canonicalization fields
    string? CanonicalSymbol = null,
    byte CanonicalizationVersion = 0,
    string? CanonicalVenue = null,
    // End-to-end trace context captured at pipeline ingress
    string? TraceId = null,
    string? ParentSpanId = null
);
```

### Envelope fields

- `Timestamp` – canonical event timestamp used by the pipeline.
- `Symbol` – raw instrument identifier as received from the provider (or `SYSTEM` for platform events like heartbeat). This field is never mutated by canonicalization.
- `Type` – discriminator for payload parsing and downstream routing.
- `Payload` – strongly typed `MarketEventPayload` instance (non-nullable; heartbeat events use the `MarketEventPayload.HeartbeatPayload` nested record to eliminate the null-payload special case and enable exhaustive pattern matching).
- `Sequence` – monotonic sequence when available from source or collector.
- `Source` – provider/source identifier (examples: `IB`, `ALPACA`, `stooq`).
- `SchemaVersion` – payload schema compatibility marker (`byte`).
- `Tier` – event tier classification (`Raw`, `Enriched`, `Processed`). Canonicalized events are promoted to `Enriched`.
- `ExchangeTimestamp` – exchange/venue timestamp from the provider feed (best-effort, depends on provider).
- `ReceivedAtUtc` – wall-clock time when the event entered the collector. Set by `StampReceiveTime()`.
- `ReceivedAtMonotonic` – monotonic clock timestamp for latency measurement. Set by `StampReceiveTime()`.
- `TraceId` / `ParentSpanId` – OpenTelemetry trace context stamped at pipeline ingress by `StampTraceContext(ActivityContext)`. Used to correlate records across sinks and dead-letter flows without relying on ambient `Activity` state.

### Computed properties

The following are actual computed properties on the `MarketEvent` record:

- `EffectiveSymbol` – returns `CanonicalSymbol` when available, otherwise falls back to the raw `Symbol`. Use this in storage paths, dedup keys, metrics labels, and quality monitoring to ensure consistent behavior regardless of canonicalization state.
- `EstimatedLatencyMs` – computes end-to-end latency from `ReceivedAtUtc − ExchangeTimestamp` when both are present; returns `null` otherwise.

### Timestamp semantics

The three timestamp fields serve distinct purposes:

| Field | Populated by | Semantics |
|-------|-------------|-----------|
| `Timestamp` | Factory methods (`MarketEvent.Trade()`, etc.) | When the event was created in the collector process |
| `ExchangeTimestamp` | `StampReceiveTime(exchangeTs)` in provider adapter | Exchange/venue timestamp from the provider feed |
| `ReceivedAtUtc` | `StampReceiveTime()` | Wall-clock time when event entered the collector |

### Canonicalization envelope fields

The [Deterministic Canonicalization](deterministic-canonicalization.md) design adds three fields to the envelope for cross-provider data comparison:

| Field | Type | Purpose |
|-------|------|---------|
| `CanonicalSymbol` | `string?` | Resolved canonical identity (e.g., `"AAPL"`) via `CanonicalSymbolRegistry` |
| `CanonicalizationVersion` | `byte` | `0` = not canonicalized, `1+` = version of mapping tables applied |
| `CanonicalVenue` | `string?` | Normalized venue as ISO 10383 MIC code (e.g., `"XNAS"`) |

These fields are additive — existing consumers and storage paths continue to work unchanged. Events with `CanonicalizationVersion = 0` (or the field absent) have not been through canonicalization. See the [design document](deterministic-canonicalization.md) for the full specification.

### Current `MarketEventType` values

| Value | Byte | Description |
|-------|------|-------------|
| `Unknown` | 0 | Fallback / unrecognized |
| `L2Snapshot` | 1 | Level 2 order book snapshot |
| `BboQuote` | 2 | Best bid/offer quote |
| `Trade` | 3 | Trade execution |
| `OrderFlow` | 4 | Order flow statistics |
| `Heartbeat` | 5 | Provider heartbeat |
| `ConnectionStatus` | 6 | Connection status change |
| `Integrity` | 7 | Data integrity / sequence anomaly |
| `HistoricalBar` | 8 | Daily backfill bar |
| `HistoricalQuote` | 9 | Historical NBBO quote |
| `HistoricalTrade` | 10 | Historical trade print |
| `HistoricalAuction` | 11 | Opening/closing auction data |
| `AggregateBar` | 12 | Real-time streaming OHLCV bar |
| `Quote` | 13 | Quote update |
| `Depth` | 14 | Order book depth update |
| `OptionQuote` | 15 | Option bid/ask snapshot |
| `OptionTrade` | 16 | Option trade execution |
| `OptionGreeks` | 17 | Greeks snapshot (delta, gamma, theta, vega, rho, IV) |
| `OptionChain` | 18 | Full option chain snapshot |
| `OpenInterest` | 19 | Open interest update |
| `OrderAdd` | 20 | New order added to matching engine book |
| `OrderModify` | 21 | Existing order modified (price/size/flags) |
| `OrderCancel` | 22 | Order full or partial cancellation |
| `OrderExecute` | 23 | Resting order executed (full or partial fill) |
| `OrderReplace` | 24 | Venue-side order replace with new order ID |

> Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters, backfill paths, or the `L3OrderBookCollector`.

---

## Collectors

Domain events are the backbone of the platform. Every data point emitted by the collectors (trades, quotes, depth, integrity signals, or system notifications) is wrapped in a `MarketEvent` envelope with a typed `MarketEventPayload`. Consumer logic dispatches on the `MarketEventType` discriminator so side-by-side comparisons, canonicalization, and downstream routing stay predictable across providers.

All concrete collectors extend `SymbolSubscriptionTracker` — a thread-safe base class that uses `ConcurrentDictionary` internally for per-symbol subscription state management. By default, collectors require explicit `RegisterSubscription(symbol)` calls before processing updates. Setting `requireExplicitSubscription = false` enables auto-subscribe mode for adapters that do not control their own subscription lifecycle.

---

## TradeDataCollector

Processes `MarketTradeUpdate` and emits:

- `Trade` events
- `OrderFlow` events (payload: `OrderFlowStatistics`)
- `Integrity` events (payload: `IntegrityEvent`) for sequence anomalies

Behavioral notes:

- Validates sequence continuity per symbol/stream.
- Infers aggressor side from quote context (`IQuoteStateStore`) when feed-side aggressor is unknown.
- Maintains rolling order-flow stats (VWAP + buy/sell/unknown volume splits).

### `Trade` payload

```csharp
public sealed record Trade : MarketEventPayload
{
    DateTimeOffset Timestamp;
    string Symbol;
    decimal Price;
    long Size;
    AggressorSide Aggressor;
    long SequenceNumber;
    string? StreamId;
    string? Venue;
    string[]? RawConditions;          // provider-specific codes, preserved for auditability
    CanonicalTradeCondition[]? CanonicalConditions; // populated by EventCanonicalizer
}
```

Validation highlights:

- `Price > 0`
- `Size >= 0`
- non-empty `Symbol`

`RawConditions` carries the condition codes exactly as received from the data source. `CanonicalConditions` is `null` until the event passes through the canonicalization stage; use it for cross-provider filtering (e.g., excluding odd-lot or extended-hours prints).

### `OrderFlowStatistics` payload

```csharp
public sealed record OrderFlowStatistics(
    DateTimeOffset Timestamp,
    string Symbol,
    long BuyVolume,
    long SellVolume,
    long UnknownVolume,
    decimal VWAP,
    decimal Imbalance,
    int TradeCount,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

Derived values include:

- VWAP over the collector window/state
- imbalance ratio from directional volume
- cumulative directional volume and trade count

### `IntegrityEvent` payload

Represents data continuity/quality issues (for example sequence gaps and out-of-order updates), including severity, description, error code, and sequence context.

---

## MarketDepthCollector

Processes `MarketDepthUpdate` and emits:

- `L2Snapshot` events (payload: `LOBSnapshot`)
- `Integrity` events (payload: `DepthIntegrityEvent`) for depth stream violations

Behavioral notes:

- Tracks per-symbol order book buffers.
- Applies insert/update/delete operations by side and position.
- Freezes symbol streams on integrity failures until reset.

### `LOBSnapshot` payload

```csharp
public sealed record LOBSnapshot(
    DateTimeOffset Timestamp,
    string Symbol,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks,
    decimal? MidPrice = null,
    decimal? MicroPrice = null,
    decimal? Imbalance = null,
    MarketState MarketState = MarketState.Normal,
    long SequenceNumber = 0,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

Common derived metrics:

- `MidPrice = (BestBid + BestAsk) / 2` when both sides are present
- top-of-book imbalance
- optional micro-price when available

### `DepthIntegrityEvent` payload

```csharp
public sealed record DepthIntegrityEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    DepthIntegrityKind Kind,
    string Description,
    int Position,
    DepthOperation Operation,
    OrderBookSide Side,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

Provides actionable context for operators and automated recovery logic.

---

## QuoteCollector (BBO)

Processes `MarketQuoteUpdate` and emits:

- `BboQuote` events (payload: `BboQuotePayload`)

Behavioral notes:

- Maintains latest per-symbol BBO snapshot cache.
- Maintains collector-local monotonically increasing quote sequence per symbol.
- Implements `IQuoteStateStore` for downstream consumers.

### `BboQuotePayload`

```csharp
public sealed record BboQuotePayload(
    DateTimeOffset Timestamp,
    string Symbol,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? MidPrice,
    decimal? Spread,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

`TradeDataCollector` uses quote state to improve aggressor classification when prints do not explicitly include side.

### `IQuoteStateStore`

```csharp
public interface IQuoteStateStore
{
    bool TryGet(string symbol, out BboQuotePayload? quote);
}
```

---

## OptionDataCollector

Captures option market data and emits:

- `OptionQuote` events – real-time option bid/ask snapshots
- `GreeksSnapshot` events – delta, gamma, theta, vega, rho, IV
- `OptionChainSnapshot` events – full chain state across all strikes and expiries
- `OpenInterestUpdate` events – daily open interest changes

Behavioral notes:

- Maintains per-contract latest quote, greeks, and chain state in concurrent dictionaries.
- Designed for providers that supply order-level option data (e.g., OPRA feeds).

---

## L3OrderBookCollector

Processes individual order lifecycle events (add, modify, cancel, execute) from ITCH/PITCH-style message feeds and emits:

- L3 order events (`MarketEventType` range 20–24) – raw order-level messages
- Derived `LOBSnapshot` (L2) events – synthesized from L3 state (dual-write)

Behavioral notes:

- Maintains per-symbol L3 order book with configurable depth cap (`MaxDepth = 50`).
- Dual-write contract: every `On*` call publishes exactly two events in order — the L3 event followed by a derived `LOBSnapshot` (L2).
- Downstream consumers needing only L2 can filter by `MarketEventType.L2Snapshot`; consumers needing order-level granularity subscribe to the L3 types.
- Providers that only support MBP/L2 deltas should use `MarketDepthCollector` instead.

---

## Historical Domain Payloads

### `HistoricalBar`

Daily-session OHLCV bar — the primary payload emitted by backfill providers:

```csharp
public sealed record HistoricalBar : MarketEventPayload
{
    string Symbol;
    DateOnly SessionDate;
    decimal Open;
    decimal High;
    decimal Low;
    decimal Close;
    long Volume;
    string Source;
    long SequenceNumber;
}
```

Key points:

- Includes strict OHLC validation (`Low <= Open/Close <= High`, all positive).
- Stores `SessionDate` rather than arbitrary timestamp.
- `ToTimestampUtc()` maps session date to midnight UTC when timestamp normalization is required.
- Computed convenience properties: `Range`, `BodySize`, `IsBullish`, `IsBearish`, `ChangePercent`, `TypicalPrice`, `Notional`.

### `AdjustedHistoricalBar`

Extended bar that carries split and dividend adjustment factors alongside raw OHLCV values. Used by providers that return corporate-action-adjusted data:

```csharp
public sealed record AdjustedHistoricalBar(
    string Symbol,
    DateOnly SessionDate,
    decimal Open, decimal High, decimal Low, decimal Close,
    long Volume,
    string Source = "unknown",
    long SequenceNumber = 0,
    decimal? AdjustedOpen = null,
    decimal? AdjustedHigh = null,
    decimal? AdjustedLow = null,
    decimal? AdjustedClose = null,
    long? AdjustedVolume = null,
    decimal? SplitFactor = null,
    decimal? DividendAmount = null
) : MarketEventPayload;
```

`AdjustedHistoricalBar.ToHistoricalBar(preferAdjusted: true)` (a method on `AdjustedHistoricalBar`) produces a `HistoricalBar` using adjusted values when present, clamping OHLC to maintain valid relationships in the face of rounding from split/dividend arithmetic. Pass `preferAdjusted: false` to get a `HistoricalBar` from the unadjusted raw fields instead.

### `HistoricalTrade`

Tick-precision historical trade from providers that support intraday tick history (e.g., Alpaca):

```csharp
public sealed record HistoricalTrade : MarketEventPayload
{
    string Symbol;
    DateTimeOffset Timestamp;
    string Exchange;
    decimal Price;
    long Size;
    string TradeId;
    string[]? Conditions;
    string? Tape;
    string Source;
    long SequenceNumber;
}
```

Computed property: `NotionalValue = Price × Size`. Validation: `Price > 0`, `Size > 0`, non-empty `Symbol` and `TradeId`.

### `HistoricalQuote`

Historical NBBO (National Best Bid and Offer) snapshot from providers that supply quote history:

```csharp
public sealed record HistoricalQuote : MarketEventPayload
{
    string Symbol;
    DateTimeOffset Timestamp;
    string AskExchange;
    decimal AskPrice;
    long AskSize;
    string BidExchange;
    decimal BidPrice;
    long BidSize;
    string[]? Conditions;
    string? Tape;
    string Source;
    long SequenceNumber;
}
```

Computed properties: `Spread = AskPrice − BidPrice`, `MidPrice`, `SpreadBps`.

### `HistoricalAuction`

Opening and closing auction data for a trading session (e.g., from Alpaca):

```csharp
public sealed record HistoricalAuction : MarketEventPayload
{
    string Symbol;
    DateOnly SessionDate;
    IReadOnlyList<AuctionPrice> OpeningAuctions;
    IReadOnlyList<AuctionPrice> ClosingAuctions;
    string Source;
    long SequenceNumber;
}
```

Each `AuctionPrice` record holds `Timestamp`, `Price`, `Size`, optional `Exchange`, and optional `Condition`. Computed aggregates: `PrimaryOpenPrice`, `PrimaryClosePrice`, `TotalOpeningVolume`, `TotalClosingVolume`.

### `AggregateBarPayload`

Real-time OHLCV bar emitted by streaming providers (e.g., Polygon per-second `A` and per-minute `AM` events). Unlike `HistoricalBar`, it uses `DateTimeOffset` to support sub-daily timeframes:

```csharp
public sealed record AggregateBarPayload(
    string Symbol,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal Vwap,
    int TradeCount,
    AggregateTimeframe Timeframe,   // Second | Minute | Hour | Day
    string Source,
    long SequenceNumber
) : MarketEventPayload;
```

Emitted as `MarketEventType.AggregateBar`.

---

## L3 Order Lifecycle Payloads

The `L3OrderBookCollector` ingests ITCH/PITCH-style feeds and emits five order lifecycle event types (values 20–24). These payloads all derive from `MarketEventPayload` and share a common `SequenceNumber`, optional `StreamId`, and optional `Venue` field.

### `OrderAdd` (type 20)

Arrival of a new order at the matching engine. Carries the full state needed to represent a book entry:

```csharp
public sealed record OrderAdd(
    string OrderId,
    string Symbol,
    OrderSide Side,
    decimal Price,
    long DisplayedSize,
    DateTimeOffset PriorityTimestamp,
    long SequenceNumber,
    long? HiddenSize = null,        // iceberg reserve
    string? ParticipantId = null,
    string? MarketMaker = null,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

### `OrderModify` (type 21)

Mutation of an existing order's price, displayed size, or hidden size. Only changed fields are populated; `null` means no change:

```csharp
public sealed record OrderModify(
    string OrderId,
    long SequenceNumber,
    decimal? NewPrice = null,
    long? NewDisplayedSize = null,
    long? NewHiddenSize = null,
    bool LosesPriority = false,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

`LosesPriority = true` when the modification resets the order's queue position at the price level.

### `OrderCancel` (type 22)

Full or partial removal of a resting order:

```csharp
public sealed record OrderCancel(
    string OrderId,
    long CanceledSize,
    long SequenceNumber,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

`CanceledSize` equals the original remaining size for a full cancel.

### `OrderExecute` (type 23)

Resting order executed (full or partial fill against an aggressor):

```csharp
public sealed record OrderExecute(
    string RestingOrderId,
    decimal ExecPrice,
    long ExecSize,
    AggressorSide AggressorSide,
    long SequenceNumber,
    string? TakerOrderId = null,    // aggressor order, when disclosed
    string? TradeId = null,         // links to trade print (time & sales)
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

### `OrderReplace` (type 24)

Venue-side order replace where the venue cancels the original order and re-enters it under a **new** order ID. Distinct from `OrderModify` because the ID changes break order lifecycle continuity:

```csharp
public sealed record OrderReplace(
    string OldOrderId,
    string NewOrderId,
    long SequenceNumber,
    decimal? NewPrice = null,
    long? NewDisplayedSize = null,
    long? NewHiddenSize = null,
    bool LosesPriority = true,
    string? StreamId = null,
    string? Venue = null
) : MarketEventPayload;
```

`LosesPriority` defaults to `true` because a venue ID reassignment typically resets queue position.

---

## Practical Mapping Summary

| Data need | Collector / payload |
|-----------|---------------------|
| Trade tape + order-flow | `TradeDataCollector` → `Trade`, `OrderFlowStatistics` |
| Order book (L2) | `MarketDepthCollector` → `LOBSnapshot` |
| Order book (L3 / order-level) | `L3OrderBookCollector` → `OrderAdd`–`OrderReplace` + derived `LOBSnapshot` |
| Best bid/offer cache + events | `QuoteCollector` → `BboQuotePayload` |
| Option quotes, greeks, chain snapshots, open interest | `OptionDataCollector` → `OptionQuote`, `GreeksSnapshot`, `OptionChainSnapshot`, `OpenInterestUpdate` |
| Daily backfill bars | `HistoricalBar` payloads on `MarketEventType.HistoricalBar` |
| Adjusted daily bars (splits/dividends) | `AdjustedHistoricalBar` → `.ToHistoricalBar()` for normalized consumption |
| Tick-level historical trades | `HistoricalTrade` on `MarketEventType.HistoricalTrade` |
| Historical NBBO quotes | `HistoricalQuote` on `MarketEventType.HistoricalQuote` |
| Opening/closing auction data | `HistoricalAuction` on `MarketEventType.HistoricalAuction` |
| Real-time streaming OHLCV bars | `AggregateBarPayload` on `MarketEventType.AggregateBar` |

This split keeps domain logic deterministic, testable, and independent from provider adapter implementation details.

---

## Canonicalization Layer

The canonicalization stage sits between provider adapters and the `EventPipeline`, enriching each `MarketEvent` with normalized identifiers before storage:

1. **Resolves symbols** via `CanonicalSymbolRegistry` to populate `CanonicalSymbol` on the envelope.
2. **Maps condition codes** from provider-specific formats (CTA plan codes, SEC numeric codes, IB field codes) to a canonical enum.
3. **Normalizes venues** to ISO 10383 MIC codes (e.g., Polygon exchange ID `4` → `"XNAS"`).
4. **Preserves raw data** — the original `Symbol`, condition codes, and venue strings are never mutated.

The canonicalization design is documented in [Deterministic Canonicalization](deterministic-canonicalization.md).

---

*Last Updated: 2026-03-25*
