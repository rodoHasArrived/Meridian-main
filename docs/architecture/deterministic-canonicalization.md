# Deterministic Canonicalization Across Providers

> **Status:** Implemented (Phase 1–3 complete)
> **Related ADRs:** ADR-001 (Provider Abstraction), ADR-006 (Domain Events Polymorphic Payload), ADR-009 (F# Type-Safe Domain)
> **Related:** [Data Uniformity Plan](../reference/data-uniformity.md), [Storage Design](storage-design.md)

## Problem Statement

Prior to implementation, the `MarketEvent.Symbol` field stored whatever string the provider emitted. The `EventPipeline` passed events through to storage sinks without symbol resolution, condition-code mapping, or timestamp alignment:

- The same instrument could appear as `"AAPL"` from Alpaca, `"AAPL"` from Polygon, but `"AAPL.US"` from StockSharp or `"AAPL.O"` from another feed — structurally different strings representing the same security.
- Trade condition codes were stored as raw `string[]?` with no normalization. Alpaca used CTA plan codes (`"@"`, `"T"`), Polygon used numeric codes (`"37"`, `"12"`), and IB used free-text descriptions.
- `Venue` was an optional freeform string that differed across providers for the same exchange.
- The `CanonicalSymbolRegistry` and `SymbolRegistry.ProviderMappings` infrastructure existed but was **not consulted** at event publish time.

All of these gaps are now closed. The sections below document the design, the actual implementation, and the remaining operational guidance.

## Goal

Equivalent market events from different providers for the same instrument should produce **structurally comparable canonical records** without losing the raw provider payload for auditability.

## Design Direction

### What changed

1. **Injected a canonicalization step between provider adapters and `EventPipeline`** that resolves symbols, maps condition codes, and normalizes venue identifiers.
2. **Extended `MarketEvent`** with `CanonicalSymbol`, `CanonicalizationVersion` (`byte`), `CanonicalVenue`, and `EffectiveSymbol` so consumers can distinguish raw vs. canonicalized events and pin to a specific transformation version.
3. **Introduced a deterministic condition-code mapping registry** (`ConditionCodeMapper`) keyed by `(provider, rawCode)` and loaded from `config/condition-codes.json`.
4. **Standardized venue identifiers** via `VenueMicMapper`, normalizing provider-specific strings to ISO 10383 MIC codes loaded from `config/venue-mapping.json`.

### What does not change

- The `EventPipeline` remains a passthrough bounded channel. Canonicalization happens **before** publish (via `CanonicalizingPublisher` decorator), not inside the consumer loop, to avoid adding latency to the high-throughput sink path.
- Raw provider payloads persist unchanged. Canonical fields are **additive** (new fields on the envelope), not mutations of existing fields.
- The WAL, storage sinks, and serialization pipeline are unaffected except for the new fields surfacing in JSON output.
- `SymbolNormalization.cs` continues to handle provider-specific format transforms (Tiingo dashes, Stooq lowercase, etc.). Canonicalization is a **higher-level identity resolution** that builds on normalization.

## Current State Assessment

### Implementation components

| Component | Location | Status |
|-----------|----------|--------|
| `CanonicalSymbolRegistry` | `Application/Services/CanonicalSymbolRegistry.cs` | ✅ Multi-identifier resolution (ISIN, FIGI, aliases, provider mappings). Wired into `EventCanonicalizer`. |
| `IEventCanonicalizer` / `EventCanonicalizer` | `Application/Canonicalization/` | ✅ Resolves symbols, maps venues, enriches trade condition codes using `with` expression pattern. |
| `CanonicalizingPublisher` | `Application/Canonicalization/CanonicalizingPublisher.cs` | ✅ Decorator over `IMarketEventPublisher` with dual-write, pilot symbol filtering, quarantine sink, and per-publisher metrics. |
| `ConditionCodeMapper` | `Application/Canonicalization/ConditionCodeMapper.cs` | ✅ Loaded from `config/condition-codes.json` into `FrozenDictionary`. Supports halt/resume detection helpers. |
| `VenueMicMapper` | `Application/Canonicalization/VenueMicMapper.cs` | ✅ Loaded from `config/venue-mapping.json` into `FrozenDictionary`. Case-insensitive provider and venue lookup. |
| `CanonicalizationMetrics` | `Application/Canonicalization/CanonicalizationMetrics.cs` | ✅ Static thread-safe counters with per-provider `ProviderParityStats` and immutable snapshot export. |
| `CanonicalizationConfig` | `Core/Config/AppConfig.cs` | ✅ `Enabled`, `PilotSymbols`, `DualWriteRawAndCanonical`, `ConditionCodesPath`, `VenueMappingPath`, `Version`. |
| `SymbolRegistry.ProviderMappings` | `Contracts/Catalog/SymbolRegistry.cs` | ✅ Populated by config tooling; queried by `CanonicalSymbolRegistry.ResolveToCanonical()`. |
| `SymbolNormalization` | `Infrastructure/Utilities/SymbolNormalization.cs` | ✅ Per-provider format normalization (uppercase, Tiingo dashes, etc.). Runs before canonicalization. |
| `MarketEventTier` enum | `Contracts/Domain/Enums/` | ✅ `Raw` → `Enriched` transition applied by `EventCanonicalizer`. |
| `EffectiveSymbol` property | `Domain/Events/MarketEvent.cs` | ✅ `CanonicalSymbol ?? Symbol` — used by storage sinks, dedup ledger, and audit trail. |
| `DataQualityMonitoringService` | `Application/Monitoring/DataQuality/` | ✅ Full quality pipeline including cross-provider comparison. |
| `CanonicalizationEndpoints` | `Ui.Shared/Endpoints/CanonicalizationEndpoints.cs` | ✅ REST API for status, per-provider parity breakdown, and config view. |
| `AddCanonicalizationServices()` | `Application/Composition/ServiceCompositionRoot.cs` | ✅ DI wiring: mappers → canonicalizer → publisher decorator. Enabled by default in all presets. |

### Convergence layer (collectors)

All providers converge through three collector classes that normalize the intermediate domain models:

- **`TradeDataCollector`** accepts `MarketTradeUpdate` from any provider, validates symbol format and sequence bounds, emits `Trade` payloads and `IntegrityEvent` for anomalies.
- **`QuoteCollector`** accepts `MarketQuoteUpdate`, maintains BBO state per symbol, auto-increments a local sequence number.
- **`MarketDepthCollector`** accepts `MarketDepthUpdate` (position-based deltas), maintains per-symbol order book buffers, emits `LOBSnapshot`.

These collectors handle **structural normalization** (consistent types, field validation). Canonicalization is a higher-level layer above the collectors, operating on the `MarketEvent` envelope after the collector produces a typed payload, and is responsible for **identity resolution** (canonical symbol), **semantic normalization** (condition codes), and **provenance tagging** (canonical venue).

### Closed gaps

| Gap | Resolution |
|-----|------------|
| No symbol resolution at event publish time | `EventCanonicalizer.Canonicalize()` calls `_symbols.ResolveToCanonical()` before the event enters the pipeline. |
| Condition codes stored as raw `string[]?` | `ConditionCodeMapper` maps to `CanonicalTradeCondition[]`; both raw and canonical arrays are preserved on the `Trade` payload. |
| `Venue` field is freeform | `VenueMicMapper` maps provider-specific strings to ISO 10383 MIC codes; stored in `MarketEvent.CanonicalVenue`. |
| No `CanonicalizationVersion` field | `byte CanonicalizationVersion` added to `MarketEvent`; `0` = not canonicalized, `1+` = version applied. |
| No `CanonicalSymbol` field on envelope | `string? CanonicalSymbol` added to `MarketEvent`; `EffectiveSymbol` property provides `CanonicalSymbol ?? Symbol`. |
| No dead-letter routing for unmapped events | `CanonicalizingPublisher` routes events with unresolved symbols to an optional `DeadLetterSink` (quarantine). |

## Provider Field Audit

The following tables document the concrete differences discovered by reading each provider's implementation. These drive the mapping tables in the detailed design.

### Timestamp Formats

| Provider | Source | Format | Unit | Fallback |
|----------|--------|--------|------|----------|
| Alpaca (`AlpacaMarketDataClient`) | `t` field in WebSocket JSON | ISO 8601 string | N/A | `DateTimeOffset.UtcNow` |
| Polygon (`PolygonMarketDataClient`) | `t` field in WebSocket JSON | Unix epoch long | Milliseconds | `DateTimeOffset.UtcNow` |
| IB (`IBCallbackRouter`) tick-by-tick | `time` parameter | Unix epoch long | **Seconds** | `DateTimeOffset.UtcNow` |
| IB (`IBCallbackRouter`) RTVolume | Embedded in `"price;size;time;..."` string | Unix epoch long | **Milliseconds** | N/A |
| IB (`IBCallbackRouter`) tick price/size | None (uses collector clock) | N/A | N/A | `DateTimeOffset.UtcNow` |
| StockSharp (`MessageConverter`) | `msg.ServerTime` | `DateTimeOffset` | N/A | Varies by connector |

**Key issue:** IB uses **seconds** for tick-by-tick but **milliseconds** in RTVolume. Mixing these without awareness produces timestamps off by 1000x.

### Aggressor Side Determination

| Provider | Method | Effective Coverage | Notes |
|----------|--------|--------------------|-------|
| Alpaca | None | 0% — always `Unknown` | Alpaca stock stream doesn't expose condition codes that indicate side |
| Polygon | Condition codes `c:[29-33]` → `Sell` | ~5% of trades | Only sell-side codes are definitive; no buyer-initiated codes in Polygon spec |
| IB | None | 0% — always `Unknown` | `tickType` could theoretically be inferred but isn't today |
| StockSharp | `msg.OriginSide` → `Sides.Buy`/`Sides.Sell` | Connector-dependent | Full coverage when underlying connector supports it (Rithmic: yes, IQFeed: no) |

**Implication for canonicalization:** Do not treat `AggressorSide.Unknown` as a mapping failure. For most providers, it is the truthful canonical value.

### Venue / Exchange Identifiers

| Provider | Format | Examples | Mapping needed |
|----------|--------|---------|----------------|
| Alpaca | Text strings | `"NASDAQ"`, `"V"`, `"P"`, `"NYSE_ARCA"` | Partial — some are already readable, single-char codes need lookup |
| Polygon | Numeric exchange ID | `1`→NYSE, `4`→NASDAQ, `8`→BATS, `9`→IEX, `16`→MEMX (19 codes) | Complete — all numeric, existing `MapExchangeCode()` in `PolygonMarketDataClient` |
| IB | TWS routing names | `"SMART"`, `"ISLAND"`, `"ARCA"`, `"NYSE"` | Partial — `"SMART"` is IB-specific (best-execution router), not an exchange |
| StockSharp | `SecurityId.BoardCode` | Varies by connector | Connector-dependent |

### Condition Codes

| Provider | System | Raw format | Scope |
|----------|--------|-----------|-------|
| Alpaca | CTA plan codes | Single-char strings: `"@"`, `"T"`, `"I"` | ~20 defined codes |
| Polygon | SEC numeric codes | Integer array: `[0, 12, 37]` | 54 codes (0–53), only 5 codes (29–33) are definitive for aggressor |
| IB | Field-code callbacks | Integer `tickType` values + `specialConditions` string | ~50 IB field codes, separate from trade conditions |
| StockSharp | Connector-specific | Varies | Unknown coverage |

### Sequence Numbers

| Provider | Source | Reliability | Gap detection possible |
|----------|--------|-------------|----------------------|
| Alpaca | Trade ID from `i` field | Sparse (not sequential) | No — IDs are not contiguous |
| Polygon | Local `Interlocked.Increment` counter | Sequential but collector-local | Only within a single collector process lifetime |
| IB | None (always `0`) | N/A | No |
| StockSharp | `msg.SeqNum` (optional) | Connector-dependent | Only when connector provides it |

### Field Name Mapping Across Providers

| Concept | Alpaca JSON | Polygon JSON | IB Callback | StockSharp |
|---------|-------------|-------------|-------------|------------|
| Trade price | `p` | `p` | `price` (double) | `msg.TradePrice` (decimal?) |
| Trade size | `s` | `s` | `size` (double) | `msg.TradeVolume` (decimal?) |
| Timestamp | `t` (ISO 8601) | `t` (epoch ms) | `time` (epoch s) | `msg.ServerTime` |
| Symbol | `S` | `sym` | reqId→symbol map | `symbol` parameter |
| Venue | `x` (text) | `x` (numeric) | `exchange` (text) | `msg.SecurityId.BoardCode` |
| Trade ID | `i` (long) | `i` (string) | N/A | `msg.TradeId` |
| Conditions | (implicit) | `c` (int array) | `specialConditions` | (connector-specific) |

## Detailed Design

### A. Extended MarketEvent Envelope

Three canonicalization fields were added to the existing `MarketEvent` sealed record using the `with` expression pattern. Both the Domain and Contracts `MarketEvent` records carry these fields:

```csharp
public sealed record MarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,                        // Raw provider symbol (unchanged)
    MarketEventType Type,
    MarketEventPayload Payload,           // Non-nullable; Heartbeat uses HeartbeatPayload
    long Sequence = 0,
    string Source = "IB",
    byte SchemaVersion = 1,
    MarketEventTier Tier = MarketEventTier.Raw,
    DateTimeOffset? ExchangeTimestamp = null,
    DateTimeOffset ReceivedAtUtc = default,
    long ReceivedAtMonotonic = 0,
    // Canonicalization fields
    string? CanonicalSymbol = null,       // Resolved canonical identity (e.g., "AAPL")
    byte CanonicalizationVersion = 0,     // 0 = not canonicalized, 1+ = version applied
    string? CanonicalVenue = null         // Normalized venue (e.g., "XNAS" ISO 10383 MIC)
)
{
    /// <summary>
    /// Returns CanonicalSymbol when available, otherwise falls back to the raw Symbol.
    /// Storage sinks, dedup ledger, and audit trail use this for consistent grouping.
    /// </summary>
    public string EffectiveSymbol => CanonicalSymbol ?? Symbol;
}
```

**Rationale for additive fields vs. mutating `Symbol`:**
- Existing consumers and storage paths continue to work unchanged via `EffectiveSymbol`.
- Cross-provider reconciliation groups by `CanonicalSymbol` while preserving the raw `Symbol` for debugging.
- `CanonicalizationVersion = 0` marks events not yet through the pipeline (backward compatible with all existing data).

**Impact on serialization:** New fields are registered in `MarketDataJsonContext` source generator attributes. `JsonIgnoreCondition.WhenWritingNull` and default-value omission keep fields absent from JSON output when not set, preserving backward compatibility with existing JSONL files.

### B. Canonicalization Stage

`IEventCanonicalizer` runs **before** `EventPipeline.PublishAsync()` via the `CanonicalizingPublisher` decorator:

```csharp
public interface IEventCanonicalizer
{
    MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default);
}
```

The `EventCanonicalizer` implementation:

```csharp
public sealed class EventCanonicalizer : IEventCanonicalizer
{
    private readonly ICanonicalSymbolRegistry _symbols;
    private readonly ConditionCodeMapper _conditions;
    private readonly VenueMicMapper _venues;
    private readonly byte _version;

    public MarketEvent Canonicalize(MarketEvent raw, CancellationToken ct = default)
    {
        // Skip heartbeats and already-canonicalized events
        if (raw.Type == MarketEventType.Heartbeat || raw.CanonicalizationVersion > 0)
            return raw;

        // Symbol resolution: use generic resolution (aliases, ISIN, FIGI, provider mappings)
        var canonicalSymbol = _symbols.ResolveToCanonical(raw.Symbol);

        // Venue normalization
        var rawVenue = ExtractVenue(raw.Payload);
        var canonicalVenue = _venues.TryMapVenue(rawVenue, raw.Source);

        var result = raw with
        {
            CanonicalSymbol = canonicalSymbol,
            CanonicalVenue = canonicalVenue,
            CanonicalizationVersion = _version,
            Tier = raw.Tier < MarketEventTier.Enriched ? MarketEventTier.Enriched : raw.Tier
        };

        // Condition code mapping: Trade payloads with raw conditions only
        if (raw.Payload is Trade trade && trade.RawConditions is { Length: > 0 })
        {
            var (canonical, _) = _conditions.MapConditions(raw.Source, trade.RawConditions);
            result = result with { Payload = trade with { CanonicalConditions = canonical } };
        }

        return result;
    }
}
```

**Venue extraction** covers all payload types with a `Venue` field:

```csharp
private static string? ExtractVenue(ContractPayload payload) => payload switch
{
    Trade trade => trade.Venue,
    BboQuotePayload bbo => bbo.Venue,
    LOBSnapshot lob => lob.Venue,
    L2SnapshotPayload l2 => l2.Venue,
    OrderFlowStatistics ofs => ofs.Venue,
    IntegrityEvent integrity => integrity.Venue,
    _ => null
};
```

**Placement in the pipeline:**

```
Provider WebSocket message
    |
    v
Provider Adapter (AlpacaMarketDataClient, PolygonMarketDataClient, etc.)
    |  Creates MarketEvent with raw Symbol, Source, optional ExchangeTimestamp
    |  Calls StampReceiveTime()
    v
CanonicalizingPublisher.TryPublish()      <--- Decorator wrapping IMarketEventPublisher
    |  [optional] Dual-write: publishes raw event first
    |  Calls EventCanonicalizer.Canonicalize()
    |    Resolves CanonicalSymbol via CanonicalSymbolRegistry
    |    Maps condition codes via ConditionCodeMapper
    |    Normalizes Venue to ISO 10383 MIC via VenueMicMapper
    |    Sets CanonicalizationVersion, Tier = Enriched
    |  [optional] Routes unresolved symbols to DeadLetterSink (quarantine)
    v
EventPipeline.PublishAsync()              <--- Existing, unchanged
    |
    v
Storage Sinks (JSONL, Parquet)
```

Storage sinks use `EffectiveSymbol` for path generation, buffer keys, and column writes.

**Why before the pipeline, not inside it:**
- The `EventPipeline` consumer loop is optimized for throughput with batched writes. Adding per-event lookups there would couple canonicalization latency to storage throughput.
- Canonicalization is synchronous (in-memory lookups). It does not need async I/O and fits naturally as a publisher decorator.
- If canonicalization fails, the raw event still enters the pipeline with `CanonicalizationVersion = 0`.

### C. Condition Code Mapping

Trade condition codes from different providers are stored on the `Trade` payload alongside their canonical equivalents. Raw codes are preserved for auditability.

```csharp
// Canonical condition codes (provider-agnostic), defined in Contracts/Domain/Enums/
public enum CanonicalTradeCondition
{
    Regular = 0,
    FormT_ExtendedHours = 1,
    OddLot = 2,
    AveragePrice = 3,
    Intermarket_Sweep = 4,
    OpeningPrint = 5,
    ClosingPrint = 6,
    DerivativelyPriced = 7,
    CrossTrade = 8,
    StockOption = 9,
    Halted = 10,
    CorrectedConsolidated = 11,
    CircuitBreakerLevel1 = 12,
    CircuitBreakerLevel2 = 13,
    CircuitBreakerLevel3 = 14,
    LuldPause = 15,
    RegulatoryHalt = 16,
    IpoHalt = 17,
    TradingResumed = 18,
    // ... additional codes
    Unknown = 255
}
```

`ConditionCodeMapper` uses a `FrozenDictionary` keyed by `(provider, rawCode)`, loaded from `config/condition-codes.json` at startup:

```csharp
public sealed class ConditionCodeMapper
{
    private readonly FrozenDictionary<(string Provider, string RawCode), CanonicalTradeCondition> _map;

    public (CanonicalTradeCondition[] Canonical, string[] Raw) MapConditions(
        string provider, string[]? rawConditions);

    public CanonicalTradeCondition MapSingle(string provider, string rawCode);

    // Halt detection helpers
    public static bool ContainsHaltCondition(CanonicalTradeCondition[] conditions);
    public static bool IsHaltCondition(CanonicalTradeCondition condition);
    public static bool IsResumedCondition(CanonicalTradeCondition condition);
}
```

**Mapping examples:**

| Provider | Raw Code | Canonical |
|----------|----------|-----------|
| ALPACA | `"@"` | `Regular` |
| ALPACA | `"T"` | `FormT_ExtendedHours` |
| ALPACA | `"I"` | `Intermarket_Sweep` |
| ALPACA | `"U"` | `OddLot` |
| ALPACA | `"H"` | `Halted` |
| POLYGON | `"0"` | `Regular` |
| POLYGON | `"12"` | `FormT_ExtendedHours` |
| POLYGON | `"15"` | `OddLot` |
| POLYGON | `"37"` | `OddLot` |
| IB | `"RegularTrade"` | `Regular` |
| IB | `"OddLot"` | `OddLot` |

**Polygon aggressor-side condition codes:**

| Polygon Code | Meaning | Aggressor Inference |
|-------------|---------|---------------------|
| 29 | Seller (`OriginatedBySeller`) | `AggressorSide.Sell` |
| 30 | Seller Down Exempt (`SellerDownExempt`) | `AggressorSide.Sell` |
| 31–33 | Additional seller codes | `AggressorSide.Sell` |
| 0–28, 34–53 | Informational/ambiguous | `AggressorSide.Unknown` |
| 14 | Intermarket Sweep | `AggressorSide.Unknown` (can be buy or sell) |

Note: Polygon does not define buyer-initiated codes. Only ~5% of trades carry definitive aggressor inference. The canonicalization layer preserves `Unknown` as a valid canonical value rather than attempting inference.

**Enriched payload contract:** Condition codes are added alongside raw conditions — both arrays travel together on the `Trade` payload:

```csharp
public string[]? RawConditions { get; init; }              // Original provider codes (preserved)
public CanonicalTradeCondition[]? CanonicalConditions { get; init; }  // Mapped canonical codes
```

### D. Venue Normalization

Freeform venue strings are normalized to [ISO 10383 MIC codes](https://www.iso20022.org/market-identifier-codes) stored in `MarketEvent.CanonicalVenue`. `VenueMicMapper` uses a `FrozenDictionary` keyed by `(provider, rawVenue)` with case-insensitive fallback:

| Provider | Raw Venue | Canonical MIC |
|----------|-----------|---------------|
| ALPACA | `"V"`, `"Q"` | `"XNAS"` |
| ALPACA | `"N"` | `"XNYS"` |
| ALPACA | `"P"` | `"ARCX"` |
| ALPACA | `"Z"`, `"Y"` | `"BATS"`, `"BATY"` |
| ALPACA | `"IEX"` | `"IEXG"` |
| POLYGON | `"4"` | `"XNAS"` |
| POLYGON | `"1"` | `"XNYS"` |
| IB | `"ISLAND"` | `"XNAS"` |
| IB | `"NYSE"` | `"XNYS"` |
| IB | `"ARCA"` | `"ARCX"` |
| IB | `"SMART"` | `null` (routing directive, not an exchange) |

**Polygon full exchange mapping** (all 19 codes from `config/venue-mapping.json`):

| Polygon ID | Name | ISO 10383 MIC |
|-----------|------|---------------|
| 1 | NYSE | `XNYS` |
| 2 | AMEX | `XASE` |
| 3 | ARCA | `ARCX` |
| 4 | NASDAQ | `XNAS` |
| 5 | NASDAQ BX | `XBOS` |
| 6 | NASDAQ PSX | `XPHL` |
| 7 | BATS Y | `BATY` |
| 8 | BATS | `BATS` |
| 9 | IEX | `IEXG` |
| 10 | EDGX | `EDGX` |
| 11 | EDGA | `EDGA` |
| 12 | CHX | `XCHI` |
| 14 | FINRA ADF | `FINN` |
| 15 | CBOE | `XCBO` |
| 16 | MEMX | `MEMX` |
| 17 | MIAX | `MIHI` |
| 19 | LTSE | `LTSE` |

Stored in `config/venue-mapping.json`, loaded at startup. `null` values in the JSON represent unmappable venues (e.g., IB `"SMART"` is a best-execution routing directive).

### E. Timestamp Semantics

Clarify the three timestamp fields and enforce population:

| Field | Semantics | Populated by | Required |
|-------|-----------|-------------|----------|
| `Timestamp` | When the event was created in the collector process | Factory methods (`MarketEvent.Trade()`, etc.) | Yes (always set) |
| `ExchangeTimestamp` | Exchange/venue timestamp from the provider feed | `StampReceiveTime(exchangeTs)` in provider adapter | Best-effort (depends on provider feed) |
| `ReceivedAtUtc` | Wall-clock time when event entered the collector | `StampReceiveTime()` | Yes (after stamping) |

**New field (future):**

| Field | Semantics | Purpose |
|-------|-----------|---------|
| `ClockQuality` | Enum: `ExchangeNtp`, `ProviderServer`, `CollectorLocal`, `Unknown` | Qualifies how trustworthy `ExchangeTimestamp` is for latency measurement |

Provider adapters should be updated to call `StampReceiveTime(exchangeTs)` with the exchange timestamp when the provider feed includes it (Alpaca and Polygon both provide it; IB provides it for most events).

**IB timestamp hazard:** The IB adapter uses Unix **seconds** for `tickByTickAllLast` callbacks but Unix **milliseconds** in the RTVolume string (`"price;size;time;..."` format). The canonicalization layer does not need to fix this (it's a provider adapter concern), but the `ClockQuality` tag should reflect the source: `ExchangeNtp` for tick-by-tick (exchange-stamped), `CollectorLocal` for `OnTickPrice`/`OnTickSize` (stamped with `DateTimeOffset.UtcNow` because IB doesn't provide timestamps for those callbacks).

**StockSharp variability:** `msg.ServerTime` comes from the underlying S# connector. For Rithmic, this is an exchange timestamp. For IQFeed, it may be the IQFeed server timestamp. The `ClockQuality` tag should be set per-connector, not per-provider.

### F. Symbol Identity Layer

`CanonicalSymbolRegistry` supports multi-identifier resolution via a fast reverse-lookup cache (`ConcurrentDictionary<string, string>` keyed case-insensitively):

```
CanonicalSymbolDefinition {
  Canonical: "AAPL"
  Aliases: ["AAPL.US", "AAPL.O", "US0378331005"]
  AssetClass: "equity"
  Exchange: "NASDAQ"
  ISIN, FIGI, CompositeFIGI, SEDOL, CUSIP
  ProviderSymbols: { "ALPACA": "AAPL", "POLYGON": "AAPL", "IB": "AAPL" }
}
```

`EventCanonicalizer` calls `_symbols.ResolveToCanonical(rawSymbol)`, which accepts any known identifier (canonical name, alias, ISIN, FIGI, SEDOL, CUSIP, or provider-specific ticker).

**Unresolved symbols:**
- Event persists with `CanonicalSymbol = null`, `CanonicalizationVersion = N`.
- `CanonicalizingPublisher` routes the event to the optional quarantine `DeadLetterSink` for explicit audit trail.
- `mdc_canonicalization_unresolved_total{provider,field}` counter is incremented.

### G. Failure Handling

| Severity | Condition | Action |
|----------|-----------|--------|
| **Hard-fail** | Missing required identity fields (`Symbol` empty or null) | Drop event, emit `IntegrityEvent` with `Severity.Error`, increment `mdc_canonicalization_hard_fail_total` |
| **Soft-fail** | Unknown condition code, unmapped venue, unresolved symbol | Persist with `CanonicalizationVersion = N` but `CanonicalSymbol = null` or partial mapping; route to quarantine sink if configured |
| **Degraded mode** | Unresolved mapping rate > 1% for 5+ minutes | Log alert, metric spike triggers PagerDuty/webhook if configured. No automatic fallback — events continue persisting with raw values |

Hard-fail events are routed to the existing `DroppedEventAuditTrail` (already wired into `EventPipeline`).

### H. Metrics

`CanonicalizationMetrics` is a static thread-safe class with `Interlocked`-based counters and per-provider `ProviderParityStats`:

```csharp
public static class CanonicalizationMetrics
{
    public static void RecordSuccess(string provider, string eventType);
    public static void RecordSoftFail(string provider, string eventType);
    public static void RecordHardFail(string provider, string eventType);
    public static void RecordUnresolved(string provider, string field); // "symbol" | "venue" | "condition"
    public static void RecordDualWrite();
    public static void SetActiveVersion(int version);
    public static CanonicalizationSnapshot GetSnapshot(); // immutable snapshot for Prometheus export
}
```

`CanonicalizingPublisher` additionally exposes per-publisher counters (lock-free `Interlocked`):

| Property | Meaning |
|----------|---------|
| `CanonicalizationCount` | Successfully canonicalized events |
| `SkippedCount` | Non-pilot or heartbeat events (pass-through) |
| `UnresolvedCount` | Events where `CanonicalSymbol` remained null |
| `DualWriteCount` | Raw + canonical publications |
| `QuarantinedCount` | Events sent to quarantine sink |
| `AverageDurationUs` | Mean canonicalization time in microseconds |

### I. Versioning and Schema Evolution

- `CanonicalizationVersion` (a `byte`) starts at `1` for the initial mapping tables.
- Any change to mapping tables (new condition codes, venue renames, symbol alias updates) bumps the version in the JSON file and the `EventCanonicalizer` constructor.
- Mapping table files (`config/condition-codes.json`, `config/venue-mapping.json`) are versioned in git alongside the source code.
- Backtests can pin to `CanonicalizationVersion = N` by replaying raw events through the canonicalizer at that version via `JsonlReplayer` + `EventCanonicalizer`.
- The `EventSchema.Version` field and `DataDictionary` already support this pattern.

**Backward compatibility:**
- All existing JSONL files have `CanonicalizationVersion = 0` (field absent due to `WhenWritingNull`/default omission = backward compatible).
- Consumers that don't read `CanonicalSymbol` continue using `Symbol` unchanged; `EffectiveSymbol` smooths the transition.
- No migration of existing files is required.

## Test Strategy

### Golden fixtures

Eight parameterized fixtures are in `tests/Meridian.Tests/Application/Canonicalization/Fixtures/`, one per provider-event-type combination:

| Fixture | Provider | Event | Venue | Expected MIC | Condition |
|---------|----------|-------|-------|-------------|-----------|
| `alpaca_trade_regular.json` | ALPACA | Trade | V | XNAS | `@` → Regular |
| `alpaca_trade_extended_hours.json` | ALPACA | Trade | P | ARCX | `T` → FormT_ExtendedHours |
| `alpaca_trade_odd_lot.json` | ALPACA | Trade | N | XNYS | `U` → OddLot |
| `alpaca_xnas_identity.json` | ALPACA | Trade | V | XNAS | `@` → Regular |
| `polygon_trade_regular.json` | POLYGON | Trade | 4 | XNAS | `0` → Regular |
| `polygon_trade_extended_hours.json` | POLYGON | Trade | 1 | XNYS | `12` → FormT_ExtendedHours |
| `polygon_trade_odd_lot.json` | POLYGON | Trade | 3 | ARCX | `15` → OddLot |
| `polygon_xnas_identity.json` | POLYGON | Trade | 4 | XNAS | `0` → Regular |

Each fixture has `"raw"` and `"expected"` sections. `CanonicalizationGoldenFixtureTests` loads the production mapping files and detects regressions immediately when mappings change.

### Property tests

`EventCanonicalizerTests` covers 27 test cases:

- **Idempotency:** `Canonicalize(Canonicalize(evt)) == Canonicalize(evt)` — applying twice produces the same result.
- **Determinism:** Same raw input always produces the same canonical output.
- **Preservation:** `canonicalized.Symbol == raw.Symbol` — raw symbol is never overwritten.
- **Tier progression:** `canonicalized.Tier >= raw.Tier` — tier only increases.
- **Skip conditions:** Heartbeats and already-canonicalized events pass through unchanged.
- **Cross-provider convergence:** Alpaca `"V"` and Polygon `"4"` both map to `"XNAS"` for the same symbol.

`CanonicalizingPublisherTests` covers dual-write mode, pilot symbol filtering, backpressure handling, quarantine routing, and all metrics counters.

### Integration with existing test infrastructure

- `CanonicalSymbolRegistryTests` covers `ResolveToCanonical()` via multi-identifier paths.
- `ConditionCodeMapperTests` and `VenueMicMapperTests` cover JSON loading, case-insensitive lookups, and edge cases.

### Drift canaries (CI)

- Nightly job fetches sample data from staging providers and runs canonicalization.
- Compares output against baseline snapshots.
- Alerts when a new unmapped condition code or venue appears.
- Integrates with existing `test-matrix.yml` workflow.

### Backward compatibility tests

- Replay archived JSONL files through the current canonicalizer.
- Verify no field is lost, no existing field value is mutated.
- Verify `CanonicalizationVersion = 0` files deserialize correctly with current schema.

## Operational Metrics

Exposed via `PrometheusMetrics` integration (`mdc_` prefix matches the existing service metric namespace):

| Metric | Labels | Type |
|--------|--------|------|
| `mdc_canonicalization_events_total` | `provider`, `event_type`, `status` (success/soft_fail/hard_fail) | Counter |
| `mdc_canonicalization_duration_seconds` | `provider` | Histogram |
| `mdc_canonicalization_unresolved_total` | `provider`, `field` (symbol/venue/condition) | Counter |
| `mdc_canonicalization_skipped_total` | `provider` | Counter |
| `mdc_canonicalization_dual_writes_total` | — | Counter |
| `mdc_canonicalization_version_active` | — | Gauge |

These integrate with the existing monitoring dashboard and `CrossProviderComparisonService`.

## REST API

`CanonicalizationEndpoints` in `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs` exposes four read-only routes:

| Route | Purpose |
|-------|---------|
| `GET /api/canonicalization/status` | Overall metrics snapshot: `enabled`, `version`, `eventsTotal`, `successTotal`, `softFailTotal`, `hardFailTotal`, `dualWriteTotal`, `matchRatePercent`, `unresolvedRate` |
| `GET /api/canonicalization/parity` | Per-provider parity breakdown |
| `GET /api/canonicalization/parity/{provider}` | Single provider detail including per-field unresolved counts |
| `GET /api/canonicalization/config` | Read-only view of `CanonicalizationConfig` |

## Acceptance Criteria

| Criterion | Target | How to measure |
|-----------|--------|----------------|
| Cross-provider canonical identity match | >= 99.5% of equivalent events map to the same `CanonicalSymbol` | `CrossProviderComparisonService` with canonical grouping |
| Unresolved mapping rate (liquid US equities) | < 0.1% | `mdc_canonicalization_unresolved_total / mdc_canonicalization_events_total` per provider |
| Ingest latency overhead | < 5% median increase | `mdc_canonicalization_duration_seconds` p50 vs. baseline |
| Condition code coverage (CTA plan) | >= 95% of observed codes mapped | `mdc_canonicalization_unresolved_total{field="condition"}` |
| Backward compatibility | Zero breaking changes to existing consumers | Backward compat test suite passes |
| Schema versioning | Every mapping change has version bump + changelog entry | CI check on `config/condition-codes.json` and `config/venue-mapping.json` |

## Rollout Plan

### Phase 1: Contract + Mapping Inventory *(Done)*

- ✅ Added `CanonicalSymbol`, `CanonicalizationVersion` (`byte`), `CanonicalVenue` fields to both Domain and Contracts `MarketEvent` records.
- ✅ Updated `MarketDataJsonContext` source generator attributes; fields absent from JSON when not set.
- ✅ Built `ConditionCodeMapper` with mapping tables for Alpaca, Polygon, and IB loaded from `config/condition-codes.json`.
- ✅ Built `VenueMicMapper` with ISO 10383 MIC lookup loaded from `config/venue-mapping.json`.
- ✅ Added `IEventCanonicalizer` interface and `EventCanonicalizer` implementation.
- ✅ `CanonicalSymbolRegistry.ResolveToCanonical()` accepts any known identifier (aliases, ISIN, FIGI, provider symbols).
- ✅ Added `EffectiveSymbol` computed property (`CanonicalSymbol ?? Symbol`) on `MarketEvent`.
- ✅ Golden fixture test suite: 8 fixtures covering Alpaca and Polygon trade events.
- **Gate:** ✅ All existing tests pass. New fields are absent from serialized output when not set.

### Phase 2: Dual-Write Validation *(Done)*

- ✅ `CanonicalizingPublisher` decorator wraps `IMarketEventPublisher` with pilot symbol filtering and dual-write support.
- ✅ `DualWriteRawAndCanonical` flag in `CanonicalizationConfig` controls dual-write behavior.
- ✅ `CanonicalizationConfig` added to `AppConfig` with `Enabled`, `PilotSymbols`, `DualWriteRawAndCanonical`, `ConditionCodesPath`, `VenueMappingPath`, and `Version` settings.
- ✅ `AddCanonicalizationServices()` in `ServiceCompositionRoot` registers mapping tables, canonicalizer, and publisher decorator via DI. Enabled by default in all presets.
- ✅ `CanonicalizationMetrics` static class with per-provider `ProviderParityStats` and Prometheus export.
- ✅ `CanonicalizationEndpoints` exposes `/api/canonicalization/status`, `/api/canonicalization/parity`, `/api/canonicalization/parity/{provider}`, and `/api/canonicalization/config`.
- **Gate:** >= 99% canonical identity match rate for pilot symbols. < 0.5% unresolved mapping rate.

### Phase 3: Default Canonical Read Path *(Done)*

- ✅ All symbols canonicalized by default: clear `PilotSymbols` in config and set `Enabled = true`.
- ✅ Critical consumers updated to use `EffectiveSymbol`:
  - `JsonlStoragePolicy.GetPath()` — storage path generation
  - `ParquetStorageSink` — buffer keys, file paths, and all symbol column writes
  - `PersistentDedupLedger` — dedup key composition
  - `CatalogSyncSink` — catalog metadata
  - `DroppedEventAuditTrail` — audit trail grouping
- ✅ `EventCanonicalizer.ExtractVenue()` covers `LOBSnapshot` and `L2SnapshotPayload` for venue extraction.
- ✅ Dual-write can be disabled by setting `DualWriteRawAndCanonical = false` once parity is confirmed.
- **Gate:** ✅ All acceptance criteria met.

### Remaining work

- 🔲 **Drift canaries in nightly CI** — expand golden fixtures to cover all event types and wire into nightly workflow.
- 🔲 **Schema evolution SOP document** — low priority; versioning already enforced via `CanonicalizationVersion` field.

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Mapping table incomplete for new provider | Medium | Soft-fail events with raw values | Drift canary CI alerts on unmapped codes; auto-create GitHub issue |
| Canonicalization adds measurable latency | Low | Pipeline throughput reduction | In-memory hash lookups only; benchmarked with BenchmarkDotNet |
| Source generator doesn't pick up new fields | Low | Serialization breaks | CI build verifies `MarketDataJsonContext` compiles cleanly |
| Corporate action renames break symbol mapping | Medium | Temporary unresolved symbols | `CanonicalSymbolRegistry` supports alias updates; registry hot-reload via `ConfigWatcher` |
| Backward incompatibility with existing JSONL | Low | Downstream consumers break | New fields use `WhenWritingNull`/default omission; absent = `CanonicalizationVersion = 0` |

## Appendix: Shipped Files

All items listed below were delivered as part of Phases 1–3.

| File | Change |
|------|--------|
| `src/Meridian.Domain/Events/MarketEvent.cs` | Added `CanonicalSymbol`, `CanonicalizationVersion` (`byte`), `CanonicalVenue`, `EffectiveSymbol` |
| `src/Meridian.Contracts/Domain/Events/MarketEvent.cs` | Mirrored new fields and `EffectiveSymbol` in contract record |
| `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` | Registered `CanonicalTradeCondition` enum and new types for source generation |
| `src/Meridian.Application/Canonicalization/IEventCanonicalizer.cs` | New interface |
| `src/Meridian.Application/Canonicalization/EventCanonicalizer.cs` | New implementation: symbol, venue, condition code enrichment |
| `src/Meridian.Application/Canonicalization/CanonicalizingPublisher.cs` | New decorator with dual-write, pilot filtering, quarantine, and metrics |
| `src/Meridian.Application/Canonicalization/ConditionCodeMapper.cs` | New: `FrozenDictionary`-based mapper with halt/resume helpers |
| `src/Meridian.Application/Canonicalization/VenueMicMapper.cs` | New: `FrozenDictionary`-based mapper with case-insensitive fallback |
| `src/Meridian.Application/Canonicalization/CanonicalizationMetrics.cs` | New: static thread-safe counters with per-provider parity tracking |
| `src/Meridian.Application/Services/CanonicalSymbolRegistry.cs` | Updated `ResolveToCanonical()` to accept aliases, ISIN, FIGI, provider symbols |
| `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` | Added `AddCanonicalizationServices()` and `EnableCanonicalizationServices` option |
| `src/Meridian.Application/Monitoring/PrometheusMetrics.cs` | Added `mdc_canonicalization_*` counters and histogram |
| `src/Meridian.Ui.Shared/Endpoints/CanonicalizationEndpoints.cs` | New: status, parity, and config endpoints |
| `src/Meridian.Core/Config/AppConfig.cs` | Added `CanonicalizationConfig` record with `Enabled`, `PilotSymbols`, `DualWriteRawAndCanonical`, etc. |
| `src/Meridian.Storage/Policies/JsonlStoragePolicy.cs` | Uses `EffectiveSymbol` for path generation |
| `src/Meridian.Storage/Sinks/ParquetStorageSink.cs` | Uses `EffectiveSymbol` for buffer keys, paths, and column writes |
| `src/Meridian.Storage/Sinks/CatalogSyncSink.cs` | Uses `EffectiveSymbol` for catalog metadata |
| `src/Meridian.Application/Pipeline/PersistentDedupLedger.cs` | Uses `EffectiveSymbol` for dedup key |
| `src/Meridian.Application/Pipeline/DroppedEventAuditTrail.cs` | Uses `EffectiveSymbol` for audit trail grouping |
| `config/condition-codes.json` | Provider condition code mapping table (version 1) |
| `config/venue-mapping.json` | Raw venue to ISO 10383 MIC mapping (version 1) |
| `config/appsettings.sample.json` | Added `Canonicalization` section |
| `tests/Meridian.Tests/Application/Services/EventCanonicalizerTests.cs` | 27 tests |
| `tests/Meridian.Tests/Application/Services/CanonicalizingPublisherTests.cs` | Dual-write, pilot filtering, backpressure, quarantine, and metrics tests |
| `tests/Meridian.Tests/Application/Canonicalization/CanonicalizationGoldenFixtureTests.cs` | 8 golden fixture regression tests |
| `tests/Meridian.Tests/Application/Canonicalization/Fixtures/*.json` | 8 fixture files (Alpaca + Polygon trades) |
| `tests/Meridian.Tests/Domain/Models/EffectiveSymbolTests.cs` | `EffectiveSymbol` property tests |
