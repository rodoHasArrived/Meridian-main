# CLAUDE.fsharp.md - F# Domain Library Guide

This document provides guidance for AI assistants working with the F# domain library in Meridian.

> **Domain naming standard:** All new F# types in `Meridian.FSharp.Domain` must conform to the
> [Domain Naming Standard](CLAUDE.domain-naming.md). Key rules at a glance:
> identifiers end in `Id`; definition records end in `Def`; trait records end in `Tr`;
> link records end in `Lnk`; boolean fields begin with `Is` or `Has`; date fields end with `Dt` in
> new code. See the full spec before creating any new domain types.

---

## Overview

The `Meridian.FSharp` library provides type-safe domain models, validation logic, and pure functional calculations. It leverages F#'s discriminated unions, pattern matching, and Railway-Oriented Programming to eliminate entire categories of bugs while maintaining seamless C# interoperability.

---

## Project Structure

```
src/Meridian.FSharp/
├── Domain/
│   ├── Sides.fs           # Side and AggressorSide types
│   ├── Integrity.fs       # Integrity event types
│   └── MarketEvents.fs    # Market event discriminated unions
├── Validation/
│   ├── ValidationTypes.fs     # Core validation types and operators
│   ├── TradeValidator.fs      # Trade validation logic
│   ├── QuoteValidator.fs      # Quote validation logic
│   └── ValidationPipeline.fs  # Pipeline composition
├── Calculations/
│   ├── Spread.fs          # Bid-ask spread calculations
│   ├── Imbalance.fs       # Order book imbalance metrics
│   └── Aggregations.fs    # VWAP, TWAP, volume analysis
├── Pipeline/
│   └── Transforms.fs      # Stream transformation functions
└── Interop.fs             # C# interoperability helpers
```

**Important:** F# requires explicit file ordering in `.fsproj`. Files are compiled in order, and each file can only reference types defined in files listed above it.

---

## Domain Naming Quick Reference

> **Full spec:** [CLAUDE.domain-naming.md](CLAUDE.domain-naming.md)

When adding new types to `Meridian.FSharp.Domain`, apply these rules:

### Type Role → Required Pattern

| Role | Pattern | F# Example |
|---|---|---|
| Identifier | `XxxId` single-case DU | `type SecurityId = SecurityId of Guid` |
| Entity | Short singular noun | `type CorpAct = { ... }` |
| Definition record (term sheet) | `XxxDef` | `type BondDef = { Coupon: CouponTerms; ... }` |
| Category union | `Class`/`Kind`/`Cat`/`Stat`/`Style`/`Right` suffix | `type CorpActStat = Announced \| Confirmed \| ...` |
| Trait record (cross-cutting) | `XxxTr` | `type ConvTr = { IsConvertible: bool; ConvRatio: decimal option }` |
| Link record (M:M relationship) | `LeftRightLnk` | `type SecExchLnk = { SecurityId: SecurityId; ExchId: ExchId; IsPrimary: bool }` |

### Field naming

| Field type | Convention | Examples |
|---|---|---|
| Boolean | `Is` or `Has` prefix | `IsCallable`, `HasVoting`, `IsPrimary`, `IsRestricted` |
| Date | `Dt` suffix | `MaturityDt`, `IssueDt`, `ExpiryDt`, `FirstCallDt` |
| Timestamp | `At` suffix | `RecordedAt`, `CreatedAt`, `AsOfUtc` |
| Amount | `Amt` suffix | `NotionalAmt`, `FaceAmt`, `GrossAmt` |
| Rate | `Rate` suffix | `CpnRate`, `DivRate`, `FloorRate`, `CapRate` |
| Price | `Px` suffix | `CallPx`, `ConvPx`, `RedPx`, `IssuePx` |
| Spread bps | `Bps` suffix | `SpreadBps`, `OasBps` |

### Approved abbreviation roots (compound names only)

`CorpAct`, `OptChain`, `OptDef`, `FutDef`, `FxDef`, `IssId`, `ExchId`, `CptyId`,
`PrefShr`, `ConvPref`, `OwnTr`, `IncTr`, `ConvTr`, `RedTr`, `SenTr`, `ListTr`

Never create an abbreviated synonym beside an established full-word type:
`SecurityId` is established — never introduce `SecId` next to it.

---

## Domain Types

### Sides.fs - Trade Sides

```fsharp
module Meridian.FSharp.Domain.Sides

/// Order book side
type Side = Buy | Sell

/// Trade aggressor (who initiated the trade)
type AggressorSide = Unknown | Buyer | Seller

/// Infer aggressor from trade price relative to BBO
let inferAggressor tradePrice bidPrice askPrice =
    match bidPrice, askPrice with
    | Some bid, Some ask when tradePrice >= ask -> Buyer
    | Some bid, Some ask when tradePrice <= bid -> Seller
    | _ -> Unknown

/// Convert to C# compatible int
let aggressorToInt = function
    | Unknown -> 0
    | Buyer -> 1
    | Seller -> 2

/// Convert from C# int
let aggressorFromInt = function
    | 1 -> Buyer
    | 2 -> Seller
    | _ -> Unknown
```

### MarketEvents.fs - Event Types

```fsharp
module Meridian.FSharp.Domain.MarketEvents

type TradeEvent = {
    Timestamp: DateTimeOffset
    Symbol: string
    Price: decimal
    Size: int64
    Aggressor: AggressorSide
    SequenceNumber: int64 option
    StreamId: string option
    Venue: string option
}

type QuoteEvent = {
    Timestamp: DateTimeOffset
    Symbol: string
    BidPrice: decimal
    BidSize: int64
    AskPrice: decimal
    AskSize: int64
    SequenceNumber: int64 option
}

type BarEvent = {
    Timestamp: DateTimeOffset
    Symbol: string
    Open: decimal
    High: decimal
    Low: decimal
    Close: decimal
    Volume: int64
    BarSize: string
}

/// Discriminated union for all market events
type MarketEvent =
    | Trade of TradeEvent
    | Quote of QuoteEvent
    | Bar of BarEvent
    | Depth of DepthEvent
    | OrderBook of OrderBookSnapshot
    | Integrity of IntegrityEvent
    | Heartbeat of timestamp: DateTimeOffset * source: string

/// Smart constructors
module MarketEvent =
    let createTrade symbol price size aggressor seq timestamp =
        Trade {
            Timestamp = timestamp
            Symbol = symbol
            Price = price
            Size = size
            Aggressor = aggressor
            SequenceNumber = Some seq
            StreamId = None
            Venue = None
        }

    let getSymbol = function
        | Trade t -> Some t.Symbol
        | Quote q -> Some q.Symbol
        | Bar b -> Some b.Symbol
        | Depth d -> Some d.Symbol
        | OrderBook ob -> Some ob.Symbol
        | Integrity i -> Some i.Symbol
        | Heartbeat _ -> None

    let getTimestamp = function
        | Trade t -> t.Timestamp
        | Quote q -> q.Timestamp
        | Bar b -> b.Timestamp
        | Depth d -> d.Timestamp
        | OrderBook ob -> ob.Timestamp
        | Integrity i -> i.Timestamp
        | Heartbeat (ts, _) -> ts
```

### Integrity.fs - Integrity Events

```fsharp
module Meridian.FSharp.Domain.Integrity

type IntegrityEventType =
    | SequenceGap of expected: int64 * received: int64
    | OutOfOrder of lastSeq: int64 * receivedSeq: int64
    | NegativeSpread of bid: decimal * ask: decimal
    | BookCrossed of level: int
    | StaleQuote of staleDuration: TimeSpan
    | PriceSpike of previous: decimal * current: decimal * threshold: decimal
    | MissingData of field: string
    | InvalidValue of field: string * value: obj

type IntegritySeverity = Info | Warning | Error | Critical

type IntegrityEvent = {
    Timestamp: DateTimeOffset
    Symbol: string
    EventType: IntegrityEventType
    Severity: IntegritySeverity
    Description: string
    SequenceNumber: int64
    StreamId: string option
    Venue: string option
}

/// Smart constructors with severity inference
module IntegrityEvent =
    let sequenceGap symbol ts expected received seq streamId venue =
        {
            Timestamp = ts
            Symbol = symbol
            EventType = SequenceGap(expected, received)
            Severity = if received - expected > 10L then Error else Warning
            Description = $"Sequence gap: expected {expected}, received {received}"
            SequenceNumber = seq
            StreamId = streamId
            Venue = venue
        }

    let outOfOrder symbol ts lastSeq receivedSeq seq streamId venue =
        {
            Timestamp = ts
            Symbol = symbol
            EventType = OutOfOrder(lastSeq, receivedSeq)
            Severity = Error
            Description = $"Out of order: last {lastSeq}, received {receivedSeq}"
            SequenceNumber = seq
            StreamId = streamId
            Venue = venue
        }
```

---

## Validation

### Railway-Oriented Programming

The validation system uses `Result<'T, 'TError list>` to accumulate errors instead of throwing exceptions:

```fsharp
module Meridian.FSharp.Validation.ValidationTypes

/// Validation error with context
type ValidationError =
    | InvalidPrice of value: decimal * reason: string
    | InvalidQuantity of value: int64 * reason: string
    | InvalidSymbol of value: string * reason: string
    | StaleTimestamp of age: TimeSpan * maxAge: TimeSpan
    | InvalidSequence of current: int64 * last: int64
    | InvalidSpread of bid: decimal * ask: decimal
    | CustomError of code: string * message: string

/// Get error description
let describeError = function
    | InvalidPrice (v, r) -> $"Invalid price {v}: {r}"
    | InvalidQuantity (v, r) -> $"Invalid quantity {v}: {r}"
    | InvalidSymbol (v, r) -> $"Invalid symbol '{v}': {r}"
    | StaleTimestamp (age, max) -> $"Timestamp too old: {age} > {max}"
    | InvalidSequence (cur, last) -> $"Invalid sequence: {cur} <= {last}"
    | InvalidSpread (bid, ask) -> $"Invalid spread: bid {bid} > ask {ask}"
    | CustomError (code, msg) -> $"[{code}] {msg}"

/// Applicative operators for composing validations
let (<!>) = Result.map
let (<*>) fRes xRes =
    match fRes, xRes with
    | Ok f, Ok x -> Ok (f x)
    | Error e1, Error e2 -> Error (e1 @ e2)
    | Error e, _ | _, Error e -> Error e
```

### TradeValidator.fs

```fsharp
module Meridian.FSharp.Validation.TradeValidator

open Meridian.FSharp.Validation.ValidationTypes

type TradeValidationConfig = {
    MaxPrice: decimal
    MinPrice: decimal
    MaxQuantity: int64
    MaxSymbolLength: int
    MaxTimestampAge: TimeSpan
}

let defaultConfig = {
    MaxPrice = 1_000_000m
    MinPrice = 0.0001m
    MaxQuantity = 1_000_000_000L
    MaxSymbolLength = 20
    MaxTimestampAge = TimeSpan.FromMinutes(5.0)
}

/// Validate price
let validatePrice config price =
    if price <= 0m then
        Error [InvalidPrice(price, "must be positive")]
    elif price > config.MaxPrice then
        Error [InvalidPrice(price, $"exceeds max {config.MaxPrice}")]
    elif price < config.MinPrice then
        Error [InvalidPrice(price, $"below min {config.MinPrice}")]
    else
        Ok price

/// Validate quantity
let validateQuantity config qty =
    if qty <= 0L then
        Error [InvalidQuantity(qty, "must be positive")]
    elif qty > config.MaxQuantity then
        Error [InvalidQuantity(qty, $"exceeds max {config.MaxQuantity}")]
    else
        Ok qty

/// Validate symbol
let validateSymbol config symbol =
    if String.IsNullOrWhiteSpace(symbol) then
        Error [InvalidSymbol(symbol, "cannot be empty")]
    elif symbol.Length > config.MaxSymbolLength then
        Error [InvalidSymbol(symbol, $"exceeds max length {config.MaxSymbolLength}")]
    elif symbol |> Seq.exists (fun c -> not (Char.IsLetterOrDigit(c) || c = '.' || c = '-')) then
        Error [InvalidSymbol(symbol, "contains invalid characters")]
    else
        Ok symbol

/// Validate complete trade (accumulates all errors)
let validateTrade config (trade: TradeEvent) =
    let create sym price qty =
        { trade with Symbol = sym; Price = price; Size = qty }

    create
    <!> validateSymbol config trade.Symbol
    <*> validatePrice config trade.Price
    <*> validateQuantity config trade.Size

/// Validate with default config
let validateTradeDefault = validateTrade defaultConfig
```

### QuoteValidator.fs

```fsharp
module Meridian.FSharp.Validation.QuoteValidator

let validateQuote config (quote: QuoteEvent) =
    let validateSpread () =
        if quote.AskPrice < quote.BidPrice then
            Error [InvalidSpread(quote.BidPrice, quote.AskPrice)]
        else
            Ok ()

    let create sym bidPrice askPrice () =
        { quote with
            Symbol = sym
            BidPrice = bidPrice
            AskPrice = askPrice }

    create
    <!> validateSymbol config quote.Symbol
    <*> validatePrice config quote.BidPrice
    <*> validatePrice config quote.AskPrice
    <*> validateSpread ()
```

---

## Calculations

### Spread.fs

```fsharp
module Meridian.FSharp.Calculations.Spread

/// Calculate absolute spread
let calculate bidPrice askPrice =
    if bidPrice > 0m && askPrice > 0m && askPrice >= bidPrice then
        Some (askPrice - bidPrice)
    else
        None

/// Calculate spread in basis points
let spreadBps bidPrice askPrice =
    calculate bidPrice askPrice
    |> Option.map (fun spread ->
        let mid = (bidPrice + askPrice) / 2m
        if mid > 0m then spread / mid * 10000m else 0m)

/// Calculate spread from quote event
let spreadBpsFromQuote (quote: QuoteEvent) =
    spreadBps quote.BidPrice quote.AskPrice

/// Calculate mid-price
let midPrice bidPrice askPrice =
    if bidPrice > 0m && askPrice > 0m then
        Some ((bidPrice + askPrice) / 2m)
    else
        None

/// Calculate effective spread (from trade price)
let effectiveSpread tradePrice bidPrice askPrice =
    midPrice bidPrice askPrice
    |> Option.map (fun mid -> 2m * abs(tradePrice - mid))

/// Calculate spread as percentage
let spreadPercent bidPrice askPrice =
    spreadBps bidPrice askPrice
    |> Option.map (fun bps -> bps / 100m)
```

### Imbalance.fs

```fsharp
module Meridian.FSharp.Calculations.Imbalance

/// Calculate order book imbalance (-1 to +1)
/// Positive = buying pressure, Negative = selling pressure
let calculate bidQty askQty =
    let total = bidQty + askQty
    if total > 0L then
        Some (decimal (bidQty - askQty) / decimal total)
    else
        None

/// Get imbalance direction
let getDirection imbalance =
    match imbalance with
    | Some i when i > 0m -> Some Buy
    | Some i when i < 0m -> Some Sell
    | _ -> None

/// Calculate microprice (volume-weighted mid)
let microprice bidPrice bidQty askPrice askQty =
    let totalQty = bidQty + askQty
    if totalQty > 0L && bidPrice > 0m && askPrice > 0m then
        let weighted = (bidPrice * decimal askQty + askPrice * decimal bidQty)
        Some (weighted / decimal totalQty)
    else
        None

/// Check if imbalance is significant
let isSignificant threshold imbalance =
    match imbalance with
    | Some i -> abs i >= threshold
    | None -> false

/// Calculate imbalance from quote
let fromQuote (quote: QuoteEvent) =
    calculate quote.BidSize quote.AskSize
```

### Aggregations.fs

```fsharp
module Meridian.FSharp.Calculations.Aggregations

/// Volume-Weighted Average Price
let vwap (trades: TradeEvent seq) =
    let trades = trades |> Seq.toList
    if trades.IsEmpty then None
    else
        let totalValue = trades |> List.sumBy (fun t -> t.Price * decimal t.Size)
        let totalVolume = trades |> List.sumBy (fun t -> t.Size)
        if totalVolume > 0L then Some (totalValue / decimal totalVolume)
        else None

/// Time-Weighted Average Price
let twap (trades: TradeEvent seq) =
    let trades = trades |> Seq.toList
    if trades.IsEmpty then None
    else
        let avgPrice = trades |> List.averageBy (fun t -> t.Price)
        Some avgPrice

/// Volume breakdown by aggressor
type VolumeBreakdown = {
    BuyVolume: int64
    SellVolume: int64
    UnknownVolume: int64
    TotalVolume: int64
    TradeCount: int
    BuyCount: int
    SellCount: int
}

let volumeBreakdown (trades: TradeEvent seq) =
    let trades = trades |> Seq.toList
    {
        BuyVolume = trades |> List.filter (fun t -> t.Aggressor = Buyer) |> List.sumBy (fun t -> t.Size)
        SellVolume = trades |> List.filter (fun t -> t.Aggressor = Seller) |> List.sumBy (fun t -> t.Size)
        UnknownVolume = trades |> List.filter (fun t -> t.Aggressor = Unknown) |> List.sumBy (fun t -> t.Size)
        TotalVolume = trades |> List.sumBy (fun t -> t.Size)
        TradeCount = trades.Length
        BuyCount = trades |> List.filter (fun t -> t.Aggressor = Buyer) |> List.length
        SellCount = trades |> List.filter (fun t -> t.Aggressor = Seller) |> List.length
    }

/// Order Flow Imbalance (signed volume sum)
let orderFlowImbalance (trades: TradeEvent seq) =
    trades |> Seq.sumBy (fun t ->
        match t.Aggressor with
        | Buyer -> t.Size
        | Seller -> -t.Size
        | Unknown -> 0L)
```

---

## Pipeline Transforms

### Transforms.fs

```fsharp
module Meridian.FSharp.Pipeline.Transforms

// Module aliases to avoid naming conflicts with record fields
module SpreadCalc = Meridian.FSharp.Calculations.Spread
module ImbalanceCalc = Meridian.FSharp.Calculations.Imbalance

/// Filter by symbol
let filterBySymbol symbol events =
    events |> Seq.filter (fun e ->
        MarketEvent.getSymbol e = Some symbol)

/// Filter by symbol set
let filterBySymbols symbols events =
    events |> Seq.filter (fun e ->
        match MarketEvent.getSymbol e with
        | Some s -> Set.contains s symbols
        | None -> false)

/// Filter by time range
let filterByTimeRange startTime endTime events =
    events |> Seq.filter (fun e ->
        let ts = MarketEvent.getTimestamp e
        ts >= startTime && ts <= endTime)

/// Filter trades only
let filterTrades events =
    events |> Seq.choose (function
        | Trade t -> Some t
        | _ -> None)

/// Filter quotes only
let filterQuotes events =
    events |> Seq.choose (function
        | Quote q -> Some q
        | _ -> None)

/// Enrich quote with calculated fields
type EnrichedQuote = {
    Quote: QuoteEvent
    Spread: decimal option
    SpreadBps: decimal option
    MidPrice: decimal option
    Imbalance: decimal option
}

let enrichQuote quote = {
    Quote = quote
    Spread = SpreadCalc.calculate quote.BidPrice quote.AskPrice
    SpreadBps = SpreadCalc.spreadBps quote.BidPrice quote.AskPrice
    MidPrice = SpreadCalc.midPrice quote.BidPrice quote.AskPrice
    Imbalance = ImbalanceCalc.fromQuote quote
}

let enrichQuotes quotes =
    quotes |> Seq.map enrichQuote

/// Deduplicate by sequence number
let deduplicate events =
    events
    |> Seq.distinctBy (fun e ->
        match e with
        | Trade t -> (t.Symbol, t.SequenceNumber)
        | Quote q -> (q.Symbol, q.SequenceNumber)
        | _ -> (MarketEvent.getSymbol e |> Option.defaultValue "", None))

/// Group by symbol
let groupBySymbol events =
    events
    |> Seq.groupBy (MarketEvent.getSymbol >> Option.defaultValue "")
    |> Map.ofSeq

/// Partition by event type
type PartitionedEvents = {
    Trades: TradeEvent list
    Quotes: QuoteEvent list
    Bars: BarEvent list
    Integrity: IntegrityEvent list
    Other: MarketEvent list
}

let partitionByType events =
    let mutable trades = []
    let mutable quotes = []
    let mutable bars = []
    let mutable integrity = []
    let mutable other = []

    for e in events do
        match e with
        | Trade t -> trades <- t :: trades
        | Quote q -> quotes <- q :: quotes
        | Bar b -> bars <- b :: bars
        | Integrity i -> integrity <- i :: integrity
        | _ -> other <- e :: other

    {
        Trades = List.rev trades
        Quotes = List.rev quotes
        Bars = List.rev bars
        Integrity = List.rev integrity
        Other = List.rev other
    }
```

---

## C# Interoperability

### Interop.fs

```fsharp
module Meridian.FSharp.Interop

open System
open System.Runtime.CompilerServices

// Private helper functions for F# internal use
let private toNullable (opt: 'T option) : Nullable<'T> =
    match opt with
    | Some v -> Nullable(v)
    | None -> Nullable()

let private toNullableRef (opt: 'T option) : 'T =
    match opt with
    | Some v -> v
    | None -> Unchecked.defaultof<'T>

/// Extension methods for C# consumption (marked with [<Extension>])
[<Extension>]
type OptionExtensions =
    /// Convert Option<T> to Nullable<T> for value types
    [<Extension>]
    static member ToNullable(opt: decimal option) = toNullable opt

    [<Extension>]
    static member ToNullable(opt: int64 option) = toNullable opt

    /// Convert Option<T> to T or null for reference types
    [<Extension>]
    static member ToNullableRef(opt: string option) = toNullableRef opt

/// C#-friendly validation wrapper
type TradeValidationResult = {
    IsSuccess: bool
    Value: TradeEvent option
    Errors: string list
}

[<AbstractClass; Sealed>]
type TradeValidator private () =
    static member Validate(trade: TradeEvent) =
        match TradeValidator.validateTradeDefault trade with
        | Ok t -> { IsSuccess = true; Value = Some t; Errors = [] }
        | Error errs ->
            { IsSuccess = false; Value = None; Errors = errs |> List.map describeError }

    static member IsValid(trade: TradeEvent) =
        match TradeValidator.validateTradeDefault trade with
        | Ok _ -> true
        | Error _ -> false

/// C#-friendly calculation helpers
[<AbstractClass; Sealed>]
type SpreadCalculator private () =
    static member Calculate(bid, ask) =
        Spread.calculate bid ask |> toNullable

    static member SpreadBps(bid, ask) =
        Spread.spreadBps bid ask |> toNullable

    static member MidPrice(bid, ask) =
        Spread.midPrice bid ask |> toNullable

[<AbstractClass; Sealed>]
type ImbalanceCalculator private () =
    static member Calculate(bidQty, askQty) =
        Imbalance.calculate bidQty askQty |> toNullable

    static member Microprice(bidPrice, bidQty, askPrice, askQty) =
        Imbalance.microprice bidPrice bidQty askPrice askQty |> toNullable

[<AbstractClass; Sealed>]
type AggregationFunctions private () =
    static member Vwap(trades: TradeEvent seq) =
        Aggregations.vwap trades |> toNullable

    static member Twap(trades: TradeEvent seq) =
        Aggregations.twap trades |> toNullable

    static member VolumeBreakdown(trades: TradeEvent seq) =
        Aggregations.volumeBreakdown trades

    static member TotalVolume(trades: TradeEvent seq) =
        trades |> Seq.sumBy (fun t -> t.Size)
```

---

## Using from C#

### Creating Events

```csharp
using Meridian.FSharp.Domain.MarketEvents;
using Meridian.FSharp.Domain.Sides;

// Using smart constructor
var trade = MarketEventModule.createTrade(
    "AAPL", 150.00m, 100L, AggressorSide.Buyer, 1L, DateTimeOffset.UtcNow);

// Direct record creation
var quote = new QuoteEvent(
    Timestamp: DateTimeOffset.UtcNow,
    Symbol: "AAPL",
    BidPrice: 149.50m,
    BidSize: 500L,
    AskPrice: 150.50m,
    AskSize: 300L,
    SequenceNumber: FSharpOption<long>.Some(1L));
```

### Validation

```csharp
using Meridian.FSharp.Interop;

var result = TradeValidator.Validate(trade);

if (result.IsSuccess)
{
    ProcessTrade(result.Value.Value);
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Validation error: {error}");
    }
}

// Quick check
if (TradeValidator.IsValid(trade))
{
    // Process...
}
```

### Calculations

```csharp
using Meridian.FSharp.Interop;

// Spread calculations
decimal? spread = SpreadCalculator.Calculate(149.50m, 150.50m);
decimal? spreadBps = SpreadCalculator.SpreadBps(149.50m, 150.50m);
decimal? mid = SpreadCalculator.MidPrice(149.50m, 150.50m);

// Imbalance calculations
decimal? imbalance = ImbalanceCalculator.Calculate(500L, 300L);
decimal? microprice = ImbalanceCalculator.Microprice(149.50m, 500L, 150.50m, 300L);

// Aggregations
var trades = GetTrades();
decimal? vwap = AggregationFunctions.Vwap(trades);
var breakdown = AggregationFunctions.VolumeBreakdown(trades);
Console.WriteLine($"Buy volume: {breakdown.BuyVolume}, Sell volume: {breakdown.SellVolume}");
```

---

## Running F# Tests

The F# test project contains 4 test files:
- `CalculationTests.fs` - Spread, imbalance, aggregation tests
- `ValidationTests.fs` - Railway-oriented validation tests
- `DomainTests.fs` - Domain model tests
- `PipelineTests.fs` - Transform pipeline tests

```bash
# Run all F# tests
dotnet test tests/Meridian.FSharp.Tests

# Run specific test category
dotnet test --filter "FullyQualifiedName~ValidationTests"

# Run with verbose output
dotnet test -v detailed
```

---

## F# Conventions

### Pattern Matching

Always use exhaustive pattern matching:

```fsharp
// Good - compiler ensures all cases handled
let processEvent event =
    match event with
    | Trade t -> handleTrade t
    | Quote q -> handleQuote q
    | Bar b -> handleBar b
    | Depth d -> handleDepth d
    | OrderBook ob -> handleOrderBook ob
    | Integrity i -> handleIntegrity i
    | Heartbeat (ts, src) -> handleHeartbeat ts src

// Bad - wildcard hides missing cases
let processEvent event =
    match event with
    | Trade t -> handleTrade t
    | _ -> ()  // What about other cases?
```

### Option Handling

Prefer `Option` over null:

```fsharp
// Good
let getSymbol event =
    match event with
    | Trade t -> Some t.Symbol
    | Quote q -> Some q.Symbol
    | _ -> None

// Using in pipeline
events
|> Seq.choose (fun e -> getSymbol e |> Option.map (fun s -> (s, e)))
|> Seq.groupBy fst

// Bad - using null
let getSymbol event =
    match event with
    | Trade t -> t.Symbol
    | _ -> null  // Avoid nulls in F#
```

### Railway-Oriented Validation

Accumulate all errors instead of failing fast:

```fsharp
// Good - accumulates all errors
let validate trade =
    createValidTrade
    <!> validateSymbol trade.Symbol
    <*> validatePrice trade.Price
    <*> validateQuantity trade.Size
// Returns: Error ["Invalid symbol", "Price too high"]

// Less ideal - fails on first error
let validate trade =
    trade
    |> validateSymbol
    |> Result.bind validatePrice
    |> Result.bind validateQuantity
// Returns: Error ["Invalid symbol"] (stops early)
```

---

## Related Documentation

- [docs/integrations/fsharp-integration.md](../../integrations/fsharp-integration.md) - Full integration guide
- [docs/integrations/language-strategy.md](../../integrations/language-strategy.md) - Language strategy
- [F# for Fun and Profit](https://fsharpforfunandprofit.com/) - F# learning resource
- [Railway-Oriented Programming](https://fsharpforfunandprofit.com/rop/) - Error handling pattern

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/docs/ai/README.md)
- **Root context:** [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/CLAUDE.md)
- **Code review (Lens 6 - F# interop):** [`.github/agents/code-review-agent.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents/code-review-agent.md)
- **Integration docs:** [`docs/integrations/fsharp-integration.md`](../../integrations/fsharp-integration.md)

---

*Last Updated: 2026-03-16*
