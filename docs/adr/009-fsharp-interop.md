# ADR-009: F# Type-Safe Domain with C# Interop Bridge

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

Market data domain logic involves complex calculations and validation that benefit from functional programming:

1. **Immutability** - Market events should never mutate
2. **Exhaustive pattern matching** - Handle all trade/quote scenarios
3. **Type safety** - Invalid states should be unrepresentable
4. **Pure functions** - Calculations (VWAP, spread, imbalance) should be testable

C# provides these features, but F# excels with:
- **Discriminated unions** - Natural event modeling
- **Pattern matching** - Exhaustive compile-time checks
- **Option types** - No nulls, explicit optional values
- **Units of measure** - Type-safe financial calculations

However, the application host and infrastructure are C# (80% of codebase). A hybrid approach is needed.

## Decision

Implement **F# domain modules with C# interop bridge**:

```fsharp
// F# domain (Meridian.FSharp)
module MarketEvents =
    type TradeEvent = { Symbol: string; Price: decimal; ... }
    type QuoteEvent = { Symbol: string; BidPrice: decimal; ... }
    
module Validation =
    type ValidationError = { Code: string; Description: string }
    type ValidationResult<'T> = Result<'T, ValidationError list>
    
    let validateTrade (trade: TradeEvent) : ValidationResult<TradeEvent> =
        if trade.Price <= 0m then Error [{ Code = "INVALID_PRICE"; ... }]
        else Ok trade
```

```csharp
// C# interop bridge (Interop.fs)
[<Sealed>]
type TradeValidator private () =
    static member Validate(trade: TradeEvent) =
        ValidationResultWrapper(Validation.validateTrade trade)
    
    static member IsValid(trade: TradeEvent) =
        Validation.isValidTrade trade
```

### Project Structure

- **Meridian.FSharp** - Pure F# domain logic (12 files)
  - `Domain/MarketEvents.fs` - Event types
  - `Domain/Sides.fs` - Buy/sell discriminated union
  - `Domain/Integrity.fs` - Sequence validation
  - `Validation/ValidationTypes.fs` - Result types
  - `Validation/TradeValidator.fs` - Trade validation
  - `Validation/QuoteValidator.fs` - Quote validation
  - `Calculations/Spread.fs` - Spread calculations
  - `Calculations/Imbalance.fs` - Order flow imbalance
  - `Calculations/Aggregations.fs` - VWAP, TWAP, volume
  - `Interop.fs` - C# interop wrappers

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| F# Project | `src/Meridian.FSharp/Meridian.FSharp.fsproj` | F# domain library |
| Domain Events | `src/Meridian.FSharp/Domain/MarketEvents.fs` | Trade/quote types |
| Validation Pipeline | `src/Meridian.FSharp/Validation/ValidationPipeline.fs` | Validation composition |
| Calculations | `src/Meridian.FSharp/Calculations/` | Pure math functions |
| Interop Bridge | `src/Meridian.FSharp/Interop.fs:3` | C# wrappers |
| Generated Interop | `src/Meridian.FSharp/Generated/Meridian.FSharp.Interop.g.cs` | Auto-generated C# |
| C# Integration | `src/Meridian.Application/Monitoring/DataQuality/` | Consumer code |
| F# Tests | `tests/Meridian.FSharp.Tests/` | F# unit tests |

## Rationale

### Domain Logic Expressiveness

F# discriminated unions model domain concepts naturally:

```fsharp
// F# - Invalid states are unrepresentable
type AggressorSide =
    | Buy
    | Sell
    | Unknown

type TradeEvent = {
    Symbol: string
    Price: decimal
    Quantity: int64
    Side: AggressorSide  // Type-safe
}

// C# equivalent requires validation
public record Trade(
    string Symbol,
    decimal Price,
    long Quantity,
    AggressorSide Side  // Enum, can have invalid values
)
```

F# pattern matching is exhaustive:

```fsharp
// Compiler error if Buy/Sell/Unknown not handled
let classifyTrade trade =
    match trade.Side with
    | Buy -> "Aggressive buy"
    | Sell -> "Aggressive sell"
    | Unknown -> "Passive"
```

### Pure Functional Calculations

Financial calculations benefit from immutability and pure functions:

```fsharp
// F# - Explicitly returns Option<decimal>
let vwap (trades: TradeEvent seq) : decimal option =
    match Seq.toList trades with
    | [] -> None
    | trades ->
        let totalValue = trades |> List.sumBy (fun t -> t.Price * decimal t.Quantity)
        let totalVolume = trades |> List.sumBy (fun t -> decimal t.Quantity)
        if totalVolume = 0m then None
        else Some (totalValue / totalVolume)
```

C# interop is seamless:

```csharp
// C# consumer
var vwap = AggregationFunctions.Vwap(trades);
if (vwap.HasValue)
{
    Console.WriteLine($"VWAP: {vwap.Value}");
}
```

### Interop Bridge Design

The interop layer provides:

1. **Type conversion** - `Option<T>` → `Nullable<T>` / `T?`
2. **Exception handling** - Result types → try/catch
3. **Static factory methods** - F# records → C# constructors
4. **Extension methods** - F# modules → C# classes

Example:

```fsharp
// F# module
module Spread =
    let calculate bidPrice askPrice =
        if askPrice > bidPrice then Some (askPrice - bidPrice)
        else None

// C# wrapper
[<Sealed>]
type SpreadCalculator private () =
    static member Calculate(bidPrice: decimal, askPrice: decimal) : Nullable<decimal> =
        Spread.calculate bidPrice askPrice |> toNullable
```

## Alternatives Considered

### Alternative 1: Pure C# Domain

Use C# 13 for all domain logic (pattern matching, records, LINQ).

**Pros:**
- Single language (simpler tooling)
- No interop overhead
- Familiar to more developers

**Cons:**
- **No discriminated unions** (requires enum + validation)
- **Non-exhaustive matching** (compiler doesn't enforce)
- **Null handling** (nullable reference types are opt-in)
- **Less expressive** for functional patterns

**Why rejected:** F# provides superior type safety for domain logic.

### Alternative 2: Full F# Application

Rewrite entire application in F#.

**Pros:**
- Maximum type safety
- Consistent codebase
- Full F# ecosystem

**Cons:**
- **Rewrite cost** (80% of 767 files are C#)
- **Ecosystem limitations** (WPF/UWP tooling is C#-first)
- **Team expertise** (most developers know C# better)

**Why rejected:** Pragmatic hybrid approach balances benefits and cost.

### Alternative 3: External F# Service

Run F# domain logic as separate microservice.

**Pros:**
- Language isolation
- Independent deployment

**Cons:**
- **Network latency** (unacceptable for 100k+ events/sec)
- **Operational complexity** (two services)
- **Violates ADR-003** (monolith preference)

**Why rejected:** Performance and complexity overhead.

## Consequences

### Positive

- **Type safety** - F# discriminated unions prevent invalid states
- **Exhaustive matching** - Compiler enforces all cases handled
- **Pure functions** - Calculations are testable and predictable
- **Interop transparency** - C# consumers don't see F# types
- **Incremental adoption** - Domain logic migrates gradually

### Negative

- **Dual language complexity** - Build tooling, debugging span both
- **Interop overhead** - Type conversions add small performance cost
- **Learning curve** - Developers need F# proficiency
- **Serialization** - F# types need `[<CLIMutable>]` for JSON

### Neutral

- F# modules compile to static classes in .NET
- Interop bridge must stay synchronized with F# signatures
- F# tests run separately from C# tests

## Compliance

### Code Contracts

```fsharp
// F# domain contract
module Validation =
    type ValidationError = { Code: string; Description: string }
    type ValidationResult<'T> = Result<'T, ValidationError list>
    
    // All validators return Result type
    val validateTrade: TradeEvent -> ValidationResult<TradeEvent>
    val validateQuote: QuoteEvent -> ValidationResult<QuoteEvent>

// C# interop contract
[<Sealed>]
type TradeValidator =
    static member Validate: TradeEvent -> ValidationResultWrapper<TradeEvent>
    static member IsValid: TradeEvent -> bool
```

### Runtime Verification

- No `[ImplementsAdr]` attribute (F# modules don't support attributes well)
- Build verification: F# project must compile before C# consumers
- Integration tests verify interop correctness (F# logic called from C#)
- Serialization tests verify `[<CLIMutable>]` types round-trip

## References

- [F# Integration Guide](../integrations/fsharp-integration.md)
- [Language Strategy](../integrations/language-strategy.md)
- [Domain Events ADR](006-domain-events-polymorphic-payload.md) (F# equivalent)
- [F# for Fun and Profit](https://fsharpforfunandprofit.com/) (domain modeling)

---

*Last Updated: 2026-02-12*
