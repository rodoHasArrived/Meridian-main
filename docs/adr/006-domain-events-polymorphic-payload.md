# ADR-006: Domain Events Polymorphic Payload Pattern

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

Market data events come in many forms: trades, quotes, order book snapshots, aggregated bars, option data, and integrity events. The application needs to:

1. **Type-safe event handling** - Enable exhaustive pattern matching and compile-time verification
2. **Unified streaming** - Process all event types through a single pipeline (EventPipeline, storage sinks)
3. **Wire format efficiency** - Serialize to JSONL and Parquet without type erasure
4. **F# symmetry** - Provide equivalent ergonomics to F# discriminated unions while remaining C#-friendly

Traditional approaches have significant trade-offs:
- **Inheritance hierarchies** require runtime type checks and are awkward in F#
- **Marker interfaces** lose type information at serialization boundaries
- **Union types** aren't first-class in C# 13

## Decision

Implement a **sealed record wrapper with polymorphic payload** pattern:

```csharp
public sealed record MarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventType Type,  // enum discriminator
    MarketEventPayload? Payload,  // interface: Trade | LOBSnapshot | etc.
    long Sequence,
    string Source,
    int SchemaVersion,
    MarketEventTier Tier
)
{
    // Static factory methods for type-safe construction
    public static MarketEvent Trade(...)
        => new(..., MarketEventType.Trade, trade, ...);
    
    public static MarketEvent L2Snapshot(...)
        => new(..., MarketEventType.L2Snapshot, snapshot, ...);
}
```

All payload types implement `IMarketEventPayload` (marker interface) and serialize independently. The `Type` enum enables efficient switch expressions without runtime type checks.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Event Wrapper | `src/Meridian.Domain/Events/MarketEvent.cs:8` | Sealed record with static factories |
| Payload Interface | `src/Meridian.Contracts/Domain/Events/MarketEventPayload.cs` | Marker interface for payloads |
| Payload Types | `src/Meridian.Contracts/Domain/Models/` | Trade, LOBSnapshot, BboQuotePayload, etc. |
| Type Enum | `src/Meridian.Contracts/Domain/Enums/MarketEventType.cs` | Discriminator enum |
| Serialization | `src/Meridian.Core/Serialization/MarketDataJsonContext.cs:30` | Source-generated JSON |
| Pipeline Integration | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Unified streaming |
| F# Interop | `src/Meridian.FSharp/Domain/MarketEvents.fs` | Discriminated union equivalent |

## Rationale

### Type Safety Without Inheritance

Static factory methods (`MarketEvent.Trade()`, `MarketEvent.L2Snapshot()`) provide type-safe construction while keeping the event structure flat. The `Type` enum enables exhaustive switch expressions:

```csharp
var result = evt.Type switch
{
    MarketEventType.Trade => ProcessTrade((Trade)evt.Payload!),
    MarketEventType.L2Snapshot => ProcessSnapshot((LOBSnapshot)evt.Payload!),
    // Compiler warns on missing cases
};
```

### Serialization Efficiency

The payload interface allows System.Text.Json source generators to serialize each concrete type independently, preserving full type information in JSON:

```json
{
  "timestamp": "2026-02-12T10:30:00Z",
  "symbol": "SPY",
  "type": "Trade",
  "payload": {
    "price": 450.25,
    "size": 100,
    "aggressor": "Buy"
  }
}
```

### F# Symmetry

F# modules define equivalent discriminated unions that map directly to C# static factories:

```fsharp
type TradeEvent = { Symbol: string; Price: decimal; ... }
type MarketEvent =
    | Trade of TradeEvent
    | L2Snapshot of LOBSnapshot
    | BboQuote of QuoteEvent
```

The interop layer (ADR-009) bridges both representations seamlessly.

## Alternatives Considered

### Alternative 1: Inheritance Hierarchy

```csharp
abstract class MarketEvent { ... }
sealed class TradeEvent : MarketEvent { ... }
sealed class QuoteEvent : MarketEvent { ... }
```

**Pros:**
- Natural OOP design
- Polymorphic dispatch without type checks

**Cons:**
- Awkward in F# (no discriminated unions)
- Serialization requires custom converters
- No exhaustive pattern matching
- Difficult to extend without breaking changes

**Why rejected:** Poor F# interop and lack of compile-time exhaustiveness.

### Alternative 2: Generic Event Wrapper

```csharp
public sealed record MarketEvent<TPayload> { ... }
```

**Pros:**
- Full type safety
- No casting required

**Cons:**
- Cannot store in homogeneous collections (e.g., `List<MarketEvent>`)
- Breaks pipeline abstraction (EventPipeline must be generic)
- Storage sinks become unusably complex

**Why rejected:** Incompatible with unified streaming architecture.

### Alternative 3: C# 13 Union Types (Experimental)

Use proposed union syntax: `MarketEvent = Trade | Quote | ...`

**Pros:**
- First-class language support (eventual)

**Cons:**
- Not available in .NET 9 / C# 13
- Unknown serialization story
- No F# parity guarantees

**Why rejected:** Experimental feature not yet available.

## Consequences

### Positive

- **Compile-time safety** - Switch expressions enforce exhaustive handling
- **Single pipeline** - All events flow through one EventPipeline instance
- **Efficient serialization** - Source generators eliminate reflection
- **F# parity** - Discriminated unions map directly to static factories
- **Extensibility** - Adding event types requires minimal changes

### Negative

- **Manual casting** - Payload requires cast after switch on Type enum
- **Null handling** - Heartbeat events have `Payload = null`
- **Verbosity** - Static factories add boilerplate (11+ factory methods)

### Neutral

- Payload types must implement marker interface (IMarketEventPayload)
- Type enum must stay synchronized with factory methods
- Testing requires both wrapper and payload-level assertions

## Compliance

### Code Contracts

```csharp
// All payloads must implement this interface
public interface IMarketEventPayload
{
    long SequenceNumber { get; }
}

// Event wrapper contract
public sealed record MarketEvent(
    DateTimeOffset Timestamp,
    string Symbol,
    MarketEventType Type,
    MarketEventPayload? Payload,  // Null only for Heartbeat
    long Sequence,
    string Source,
    int SchemaVersion,
    MarketEventTier Tier
)
{
    // Static factories for each event type
    public static MarketEvent Trade(...) => ...;
    public static MarketEvent L2Snapshot(...) => ...;
    // ... (11 total factory methods)
}
```

### Runtime Verification

- No `[ImplementsAdr]` attribute required (pattern is structural)
- Build verification: All payload types must implement `IMarketEventPayload`
- Serialization tests verify round-trip fidelity for each payload type

## References

- [Domain Events Documentation](../architecture/domains.md)
- [F# Integration Guide](../integrations/fsharp-integration.md)
- [Event Pipeline Architecture](../architecture/storage-design.md)
- [ADR-004: Async Streaming Patterns](004-async-streaming-patterns.md) (complementary)
- [ADR-009: F# Type-Safe Domain](009-fsharp-interop.md) (complementary)

---

*Last Updated: 2026-02-12*
