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
    MarketEventPayload? Payload,
    long Sequence = 0,
    string Source = "IB",
    int SchemaVersion = 1,
    MarketEventTier Tier = MarketEventTier.Raw,
    DateTimeOffset? ExchangeTimestamp = null,
    DateTimeOffset ReceivedAtUtc = default,
    long ReceivedAtMonotonic = 0
);
```

### Envelope fields

- `Timestamp` – canonical event timestamp used by the pipeline.
- `Symbol` – raw instrument identifier as received from the provider (or `SYSTEM` for platform events like heartbeat). This field is never mutated by canonicalization.
- `Type` – discriminator for payload parsing and downstream routing.
- `Payload` – strongly typed `MarketEventPayload` instance (nullable for heartbeat/system events).
- `Sequence` – monotonic sequence when available from source or collector.
- `Source` – provider/source identifier (examples: `IB`, `ALPACA`, `stooq`).
- `SchemaVersion` – payload schema compatibility marker.
- `Tier` – event tier classification (`Raw`, `Enriched`, `Processed`). Canonicalized events are promoted to `Enriched`.
- `ExchangeTimestamp` – exchange/venue timestamp from the provider feed (best-effort, depends on provider).
- `ReceivedAtUtc` – wall-clock time when the event entered the collector. Set by `StampReceiveTime()`.
- `ReceivedAtMonotonic` – monotonic clock timestamp for latency measurement. Set by `StampReceiveTime()`.

### Timestamp semantics

The three timestamp fields serve distinct purposes:

| Field | Populated by | Semantics |
|-------|-------------|-----------|
| `Timestamp` | Factory methods (`MarketEvent.Trade()`, etc.) | When the event was created in the collector process |
| `ExchangeTimestamp` | `StampReceiveTime(exchangeTs)` in provider adapter | Exchange/venue timestamp from the provider feed |
| `ReceivedAtUtc` | `StampReceiveTime()` | Wall-clock time when event entered the collector |

`EstimatedLatencyMs` computes end-to-end latency from `ReceivedAtUtc - ExchangeTimestamp` when both are available.

### Canonicalization envelope fields

The [Deterministic Canonicalization](deterministic-canonicalization.md) design adds three fields to the envelope for cross-provider data comparison:

| Field | Type | Purpose |
|-------|------|---------|
| `CanonicalSymbol` | `string?` | Resolved canonical identity (e.g., `"AAPL"`) via `CanonicalSymbolRegistry` |
| `CanonicalizationVersion` | `int` | `0` = not canonicalized, `1+` = version of mapping tables applied |
| `CanonicalVenue` | `string?` | Normalized venue as ISO 10383 MIC code (e.g., `"XNAS"`) |

These fields are additive — existing consumers and storage paths continue to work unchanged. Events with `CanonicalizationVersion = 0` (or the field absent) have not been through canonicalization. See the [design document](deterministic-canonicalization.md) for the full specification.

### Current `MarketEventType` values

- `Unknown`
- `L2Snapshot`
- `BboQuote`
- `Trade`
- `OrderFlow`
- `Heartbeat`
- `ConnectionStatus`
- `Integrity`
- `HistoricalBar`
- `HistoricalQuote`
- `HistoricalTrade`
- `HistoricalAuction`
- `AggregateBar`
- `Quote`
- `Depth`
- `OptionQuote`
- `OptionTrade`
- `OptionGreeks`
- `OptionChain`
- `OpenInterest`

> Note: not every enum member is currently emitted by the three core collectors (`TradeDataCollector`, `MarketDepthCollector`, `QuoteCollector`); several are used by adapters/backfill paths.

---

## Collector Responsibilities

## Domain events

Domain events are the backbone of the platform. Every data point emitted by the collectors (trades, quotes, depth, integrity signals, or system notifications) is wrapped in a `MarketEvent` envelope with a typed `MarketEventPayload`. Consumer logic dispatches on the `MarketEventType` discriminator so side-by-side comparisons, canonicalization, and downstream routing stay predictable across providers.

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
}
```

Validation highlights:

- `Price > 0`
- `Size >= 0`
- non-empty `Symbol`

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

## `HistoricalBar`

Current backfill bar model is daily-session focused:

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

---

## Practical Mapping Summary

- **Trade tape + order-flow** → `TradeDataCollector`
- **Order book (L2)** → `MarketDepthCollector`
- **Order book (L3 / order-level)** → `L3OrderBookCollector`
- **Best bid/offer cache + events** → `QuoteCollector`
- **Option quotes, greeks, chain snapshots, open interest** → `OptionDataCollector`
- **Backfill bars** → `HistoricalBar` payloads on `MarketEventType.HistoricalBar`

This split keeps domain logic deterministic, testable, and independent from provider adapter implementation details.

---

## Canonicalization Layer

The canonicalization stage sits between provider adapters and the `EventPipeline`, enriching each `MarketEvent` with normalized identifiers before storage:

The canonicalization stage:

1. **Resolves symbols** via `CanonicalSymbolRegistry` to populate `CanonicalSymbol` on the envelope.
2. **Maps condition codes** from provider-specific formats (CTA plan codes, SEC numeric codes, IB field codes) to a canonical enum.
3. **Normalizes venues** to ISO 10383 MIC codes (e.g., Polygon exchange ID `4` → `"XNAS"`).
4. **Preserves raw data** — the original `Symbol`, condition codes, and venue strings are never mutated.

The canonicalization design is documented in [Deterministic Canonicalization](deterministic-canonicalization.md).

---

*Last Updated: 2026-03-18*
