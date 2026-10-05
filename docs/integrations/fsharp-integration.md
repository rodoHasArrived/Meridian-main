# F# Domain Library Integration Guide

**Document Version:** 1.6.1
**Last Updated:** 2026-01-30
**Status:** Implemented

---

## Overview

The `Meridian.FSharp` library provides type-safe domain models, validation logic, and pure functional calculations for market data processing. It leverages F#'s discriminated unions, pattern matching, and Railway-Oriented Programming to eliminate entire categories of bugs while maintaining seamless C# interoperability.

The examples below focus on the original market-data slice. Current ownership, domain/operations
kernels, and validation slices are documented in the [F# source README](../../src/Meridian.FSharp/README.md).
The library and its tests target .NET 10; use the SDK selected by [`global.json`](../../global.json).
The project tree below is an excerpt, not a complete inventory of the current library.

## Project Structure

```
src/Meridian.FSharp/
├── Domain/
│   ├── Sides.fs          # Side and AggressorSide types
│   ├── Integrity.fs      # Integrity event types
│   └── MarketEvents.fs   # Market event discriminated unions
├── Validation/
│   ├── ValidationTypes.fs    # Core validation types and operators
│   ├── TradeValidator.fs     # Trade validation logic
│   ├── QuoteValidator.fs     # Quote validation logic
│   └── ValidationPipeline.fs # Pipeline composition
├── Calculations/
│   ├── Spread.fs         # Bid-ask spread calculations
│   ├── Imbalance.fs      # Order book imbalance metrics
│   └── Aggregations.fs   # VWAP, TWAP, volume analysis
├── Pipeline/
│   └── Transforms.fs     # Stream transformation functions
└── Interop.fs            # C# interoperability helpers
```

---

## Domain Types

### Side and AggressorSide

```fsharp
open Meridian.FSharp.Domain.Sides

// Order book side
type Side = Buy | Sell

// Trade aggressor (who initiated the trade)
type AggressorSide = Unknown | Buyer | Seller

// Infer aggressor from trade price relative to BBO
let inferAggressor tradePrice bidPrice askPrice =
    match bidPrice, askPrice with
    | Some bid, Some ask when tradePrice >= ask -> AggressorSide.Buyer
    | Some bid, Some ask when tradePrice <= bid -> AggressorSide.Seller
    | _ -> AggressorSide.Unknown
```

### Market Events

All market events are modeled as discriminated unions, ensuring exhaustive pattern matching:

```fsharp
open Meridian.FSharp.Domain.MarketEvents

type MarketEvent =
    | Trade of TradeEvent
    | Quote of QuoteEvent
    | Depth of DepthEvent
    | OrderBook of OrderBookSnapshot
    | Bar of BarEvent
    | Integrity of IntegrityEvent
    | Heartbeat of timestamp: DateTimeOffset * source: string

// Pattern matching forces handling of all cases
let processEvent event =
    match event with
    | Trade t -> handleTrade t
    | Quote q -> handleQuote q
    | Depth d -> handleDepth d
    | OrderBook ob -> handleOrderBook ob
    | Bar b -> handleBar b
    | Integrity i -> handleIntegrity i
    | Heartbeat (ts, src) -> handleHeartbeat ts src
```

### Integrity Events

Integrity events capture data quality issues with full context:

```fsharp
open Meridian.FSharp.Domain.Integrity

type IntegrityEventType =
    | SequenceGap of expected: int64 * received: int64
    | OutOfOrder of lastSeq: int64 * receivedSeq: int64
    | NegativeSpread of bid: decimal * ask: decimal
    | BookCrossed of level: int
    | StaleQuote of staleDuration: TimeSpan
    | PriceSpike of previousPrice: decimal * currentPrice: decimal * thresholdPercent: decimal
    // ... more event types

// Create events using smart constructors
let event = IntegrityEvent.sequenceGap "AAPL" DateTimeOffset.UtcNow 100L 105L 105L None None
```

---

## Validation

### Railway-Oriented Programming

The validation system uses Result types to accumulate errors instead of throwing exceptions:

```fsharp
open Meridian.FSharp.Validation.ValidationTypes
open Meridian.FSharp.Validation.TradeValidator

// Validate a trade - accumulates all errors
let result = validateTradeDefault trade

match result with
| Ok validTrade ->
    // Process valid trade
    processValidTrade validTrade
| Error errors ->
    // Handle all validation errors
    errors |> List.iter (fun e -> log.Warning(e.Description))
```

### Validation Errors

All validation errors are typed with full context:

```fsharp
type ValidationError =
    | InvalidPrice of value: decimal * reason: string
    | InvalidQuantity of value: int64 * reason: string
    | InvalidSymbol of value: string * reason: string
    | StaleTimestamp of age: TimeSpan * maxAge: TimeSpan
    | InvalidSequence of current: int64 * last: int64
    | InvalidSpread of bid: decimal * ask: decimal
    // ...
```

### Composing Validators

Use applicative style to combine multiple validations:

```fsharp
// Validate multiple fields, accumulating all errors
let validateTrade config trade =
    let createValidatedTrade sym price qty seq =
        { trade with Symbol = sym; Price = price; Quantity = qty; SequenceNumber = seq }

    createValidatedTrade
    <!> Validate.symbol config.MaxSymbolLength trade.Symbol
    <*> Validate.price config.MaxPrice trade.Price
    <*> Validate.quantity config.MaxQuantity trade.Quantity
    <*> validateSequence config trade.SequenceNumber
```

### Validation Pipeline Builder

For more complex validation scenarios:

```fsharp
let result =
    createTradeValidator()
        .WithPriceValidation(1_000_000m)
        .WithQuantityValidation(10_000_000L)
        .WithSymbolValidation(20)
        .WithCustomValidation(myCustomValidator)
        .Validate(trade)
```

---

## Calculations

### Spread Calculations

```fsharp
open Meridian.FSharp.Calculations.Spread

// Calculate absolute spread
let spread = calculate bidPrice askPrice  // Option<decimal>

// Calculate spread in basis points
let bps = spreadBps bidPrice askPrice  // Option<decimal>

// Calculate from quote event
let quoteBps = spreadBpsFromQuote quote

// Calculate mid-price
let mid = midPrice bidPrice askPrice

// Calculate effective spread (from trade price)
let effSpread = effectiveSpread tradePrice bidPrice askPrice
```

### Imbalance Calculations

```fsharp
open Meridian.FSharp.Calculations.Imbalance

// Calculate order book imbalance (-1 to +1)
let imbalance = calculate bidQty askQty  // Option<decimal>

// Positive = buying pressure, Negative = selling pressure
let direction = getImbalanceDirection imbalance  // Option<Side>

// Calculate microprice (volume-weighted mid)
let microprice = microprice orderBook  // Option<decimal>

// Check if imbalance is significant
let isSignificant = isSignificantImbalance 0.3m imbalance  // bool
```

### Aggregation Functions

```fsharp
open Meridian.FSharp.Calculations.Aggregations

// Volume-Weighted Average Price
let vwap = vwap trades  // Option<decimal>

// Time-Weighted Average Price
let twap = twap trades  // Option<decimal>

// Volume breakdown by aggressor
let breakdown = volumeBreakdown trades
// { BuyVolume = 1000L; SellVolume = 500L; UnknownVolume = 200L; ... }

// Order Flow Imbalance (signed volume sum)
let ofi = orderFlowImbalance trades  // int64

// Create OHLCV bars
let bars = createOhlcvBars 60 trades  // 60-second bars
```

---

## Pipeline Transforms

### Filtering

```fsharp
open Meridian.FSharp.Pipeline.Transforms

// Filter by symbol
let aaplEvents = events |> filterBySymbol "AAPL"

// Filter by symbol set
let techEvents = events |> filterBySymbols (Set.ofList ["AAPL"; "MSFT"; "GOOGL"])

// Filter by time range
let todayEvents = events |> filterByTimeRange startOfDay endOfDay

// Filter by event type
let trades = events |> filterTrades
let quotes = events |> filterQuotes
```

### Transformation

```fsharp
// Enrich trades with aggressor inference
let enriched = events |> enrichWithAggressor

// Enrich quotes with calculated fields
let enrichedQuotes = quotes |> enrichQuotes
// Returns: { Quote; Spread; SpreadBps; MidPrice; Imbalance }

// Deduplicate by sequence number
let unique = events |> deduplicate
```

### Aggregation

```fsharp
// Partition by event type
let partitioned = events |> partitionByType
// { Trades; Quotes; Depth; Integrity; Other }

// Group by symbol
let bySymbol = events |> groupBySymbol
// Map<string, MarketEvent list>

// Buffer by count
let batches = events |> bufferByCount 100

// Buffer by time window
let windows = events |> bufferByTime 1000  // 1 second windows
```

### Pipeline Composition

```fsharp
// Using the pipeline builder
let result =
    TransformPipeline.create()
    |> TransformPipeline.filterSymbol "AAPL"
    |> TransformPipeline.filterTime startTime endTime
    |> TransformPipeline.validate
    |> TransformPipeline.dedupe
    |> TransformPipeline.run
    <| events

// Using the |>> operator
let result =
    events
    |>> filterBySymbol "AAPL"
    |>> validateAndFilter
    |>> deduplicate
    |> Seq.toList
```

---

## C# Interoperability

### Architecture Notes

The `Interop.fs` module provides C#-friendly wrappers and extension methods for F# types. Key design decisions:

1. **Option Conversion Helpers**: F# extension methods (marked with `[<Extension>]`) are for C# consumption only. For F# internal use, private helper functions (`toNullable`, `toNullableRef`) convert F# `Option<T>` types to C# `Nullable<T>` or null.

2. **Module Aliases**: To avoid naming conflicts between record field names and module names (e.g., a field named `Spread` and the `Spread` calculation module), the codebase uses module aliases like `SpreadCalc` and `ImbalanceCalc` in pipeline transformations.

### Using F# Types from C#

Use the C#-friendly wrappers, following the production
[`FSharpEventValidator`](../../src/Meridian.Application/Pipeline/FSharpEventValidator.cs).
Alias the nested wrapper types explicitly rather than treating the F# `Interop` module as a
C# namespace:

```csharp
using TradeEventWrapper = Meridian.FSharp.Interop.TradeEventWrapper;

// Side codes: 0 = Unknown, 1 = Buyer, 2 = Seller.
var wrapper = TradeEventWrapper.Create(
    "AAPL", 150.00m, 100L, 1, 1L, DateTimeOffset.UtcNow);
var trade = wrapper.ToFSharpEvent();
```

### Using Wrapper Classes

For a more C#-idiomatic experience, use the Interop wrappers:

```csharp
using TradeValidator = Meridian.FSharp.Interop.TradeValidator;

// Validation with C#-friendly result
var result = TradeValidator.Validate(trade);

if (result.IsSuccess)
{
    var validTrade = result.Value;
    ProcessTrade(validTrade);
}
else
{
    foreach (var error in result.Errors)
        Console.WriteLine($"Validation error: {error}");
}

// Quick validation check
bool isValid = TradeValidator.IsValid(trade);
```

### Generated C# DTOs

The [`GenerateCSharpInterop` target](../../src/Meridian.FSharp/Meridian.FSharp.fsproj) creates
`src/Meridian.FSharp/Generated/Meridian.FSharp.Interop.g.cs` only when that file is missing.
It invokes [`build/dotnet/FSharpInteropGenerator`](../../build/dotnet/FSharpInteropGenerator/Program.cs);
an ordinary rebuild does not refresh an existing generated file. The F# project packages the C#
source as content rather than compiling it into the F# assembly. Runtime callers should use the
wrappers in [`Interop.fs`](../../src/Meridian.FSharp/Interop.fs) unless they explicitly compile the
generated C# content in their own project.

### Calculation Helpers

```csharp
using SpreadCalculator = Meridian.FSharp.Interop.SpreadCalculator;
using ImbalanceCalculator = Meridian.FSharp.Interop.ImbalanceCalculator;
using AggregationFunctions = Meridian.FSharp.Interop.AggregationFunctions;
using AggressorInference = Meridian.FSharp.Interop.AggressorInference;

// Spread calculations (return Nullable<decimal>)
decimal? spread = SpreadCalculator.Calculate(bidPrice, askPrice);
decimal? spreadBps = SpreadCalculator.SpreadBps(bidPrice, askPrice);
decimal? midPrice = SpreadCalculator.MidPrice(bidPrice, askPrice);

// Imbalance calculations
decimal? imbalance = ImbalanceCalculator.Calculate(bidQty, askQty);
decimal? microprice = ImbalanceCalculator.Microprice(orderBook);

// Aggregation functions
decimal? vwap = AggregationFunctions.Vwap(trades);
long totalVolume = AggregationFunctions.TotalVolume(trades);
var breakdown = AggregationFunctions.VolumeBreakdown(trades);

// Aggressor inference
int aggressorSide = AggressorInference.InferFromQuote(tradePrice, quote);
```

### Working with F# Options

The interop wrappers explicitly convert F# `Option<T>` values to `Nullable<T>` or null for
reference types; direct F# module calls still return F# options where declared:

```csharp
using SpreadCalculator = Meridian.FSharp.Interop.SpreadCalculator;
using OptionExtensions = Meridian.FSharp.Interop.OptionExtensions;

// Option extension methods
var spread = SpreadCalculator.Calculate(bid, ask);

// Check if has value
if (spread.HasValue)
{
    Console.WriteLine($"Spread: {spread.Value}");
}

// Get value or default
decimal spreadValue = spread.GetValueOrDefault(0m);

// Convert Option<string> to string (or null)
string? symbol = OptionExtensions.ToNullableRef(fsharpSymbol);
```

---

## Testing

### Running F# Tests

```bash
# Domain and validation rules, from the repository root
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -p:FSharpTestSlice=Domain

# Narrow to validation tests within that compiled slice
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -p:FSharpTestSlice=Domain --filter "FullyQualifiedName~ValidationTests"

# Calculations and stream transformations
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -p:FSharpTestSlice=MarketData
```

Use `FSharpTestSlice=Operations` for workflow kernels. Reserve `FSharpTestSlice=All` for changes
that cross slices; the [source README](../../src/Meridian.FSharp/README.md#validation) owns the
current test routing. Omitting the project path can invoke unrelated solution tests.

### Test Coverage

The F# test suite includes:
- **Domain Tests**: Side/AggressorSide conversions, event creation, pattern matching
- **Validation Tests**: Price/quantity/symbol validation, error accumulation, pipeline composition
- **Calculation Tests**: Spread, imbalance, VWAP, TWAP, OHLCV bar creation
- **Pipeline Tests**: Filtering, transformation, aggregation, pipeline composition

---

## Migration Guide

### From C# Validation to F# Validation

**Before (C# with exceptions):**
```csharp
public Trade ValidateTrade(Trade trade)
{
    if (trade.Price <= 0)
        throw new ArgumentException("Price must be positive");
    if (string.IsNullOrEmpty(trade.Symbol))
        throw new ArgumentException("Symbol required");
    return trade;
}
```

**After (F# with Railway-Oriented Programming):**
```fsharp
let validateTrade trade =
    let create sym price = { trade with Symbol = sym; Price = price }
    create
    <!> Validate.symbolDefault trade.Symbol
    <*> Validate.priceDefault trade.Price
```

### From C# Domain Models to F# Discriminated Unions

**Before (C# with inheritance):**
```csharp
public abstract class MarketEvent { }
public class TradeEvent : MarketEvent { }
public class QuoteEvent : MarketEvent { }
// Missing case handling possible at runtime
```

**After (F# with exhaustive pattern matching):**
```fsharp
type MarketEvent =
    | Trade of TradeEvent
    | Quote of QuoteEvent
// Compiler enforces handling all cases
```

---

## Performance Considerations

1. **Immutability**: F# types are immutable by default, enabling thread-safe sharing
2. **Tail Recursion**: Recursive functions use tail-call optimization
3. **Lazy Evaluation**: Use `Seq` for lazy processing of large datasets
4. **Struct Records**: Use `[<Struct>]` for small value types to avoid allocation

```fsharp
// Use struct for small, frequently-created types
[<Struct>]
type PriceLevel = { Price: decimal; Quantity: int64 }

// Use lazy sequences for large data
let processLargeFile path =
    File.ReadLines(path)
    |> Seq.map parseLine
    |> Seq.filter isValid
    |> Seq.take 1000
```

---

## References

- [F# for Fun and Profit](https://fsharpforfunandprofit.com/) - Comprehensive F# learning resource
- [Railway-Oriented Programming](https://fsharpforfunandprofit.com/rop/) - Error handling pattern
- [Domain Modeling Made Functional](https://pragprog.com/titles/swdddf/domain-modeling-made-functional/) - F# domain modeling
- [language-strategy.md](language-strategy.md) - Overall language strategy document

---

**Document Owner:** Architecture Team
**Review Cycle:** Quarterly
